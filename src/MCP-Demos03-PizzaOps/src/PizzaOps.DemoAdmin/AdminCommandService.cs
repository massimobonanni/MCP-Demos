using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;
using PizzaOps.Core.Interfaces;
using PizzaOps.Core.Utilities;
using PizzaOps.Storage;
using PizzaOps.Storage.Persistence;

namespace PizzaOps.DemoAdmin;

internal sealed class AdminCommandService : IAdminCommandService
{
    public Task<int> StatusAsync(string environmentName, CancellationToken cancellationToken) =>
        RunAsync("status", environmentName, cancellationToken);

    public Task<int> ResetAsync(string environmentName, CancellationToken cancellationToken) =>
        RunAsync("reset", environmentName, cancellationToken);

    private static async Task<int> RunAsync(
        string commandName,
        string environmentName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var host = CreateHost(environmentName);
            var options = host.Services.GetRequiredService<IOptions<PizzaOpsTableOptions>>().Value;
            var dashboardUrl = host.Services.GetRequiredService<PizzaOpsAdminOptions>().DashboardUrl;
            var scope = AdminTarget.Create(environmentName, options, dashboardUrl);

            if (commandName == "status")
            {
                return await ShowStatusAsync(host.Services, scope, cancellationToken);
            }

            return await ResetAsync(host.Services, scope, cancellationToken);
        }
        catch (OptionsValidationException exception)
        {
            ConsoleUtility.WriteErrorLine($"Configuration is invalid: {string.Join("; ", exception.Failures)}");
            return 2;
        }
        catch (KitchenResetException exception)
        {
            ConsoleUtility.WriteErrorLine(
                $"Reset failed during {exception.Stage}; target is NOT READY. {exception.Message} " +
                "Correct the cause and rerun reset to converge.");
            return 1;
        }
        catch (KitchenStorageException exception)
        {
            ConsoleUtility.WriteErrorLine($"Storage operation failed ({exception.FailureKind}); target is NOT READY.");
            return 1;
        }
        catch (AuthenticationFailedException)
        {
            ConsoleUtility.WriteErrorLine(
                "Azure operator authentication failed. Run az login with an identity authorized for PizzaOps Table Storage.");
            return 1;
        }
        catch (OperationCanceledException)
        {
            ConsoleUtility.WriteErrorLine("Operation cancelled; target readiness was not confirmed.");
            return 1;
        }
        catch (FormatException exception)
        {
            ConsoleUtility.WriteErrorLine($"Configuration could not be read: {exception.Message}");
            return 2;
        }
        catch (IOException exception)
        {
            ConsoleUtility.WriteErrorLine($"Configuration could not be read: {exception.Message}");
            return 2;
        }
        catch (ArgumentException exception)
        {
            ConsoleUtility.WriteErrorLine($"Configuration is invalid: {exception.Message}");
            return 2;
        }
    }

    private static IHost CreateHost(string environmentName)
    {
        var hostBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            EnvironmentName = environmentName
        });

        var sources = hostBuilder.Configuration.Sources;
        foreach (var source in sources
            .Where(source => source is JsonConfigurationSource or EnvironmentVariablesConfigurationSource)
            .ToArray())
        {
            sources.Remove(source);
        }

        hostBuilder.Configuration
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables($"PIZZAOPS_{environmentName.ToUpperInvariant()}_");

        var tableOptions = new PizzaOpsTableOptions();
        hostBuilder.Configuration.GetSection(PizzaOpsTableOptions.SectionName).Bind(tableOptions);
        var validation = new PizzaOpsTableOptionsValidator(environmentName).Validate(
            Options.DefaultName,
            tableOptions);
        if (validation.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(PizzaOpsTableOptions),
                validation.Failures);
        }

        if (string.Equals(tableOptions.Mode, "Azure", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(tableOptions.ManagedIdentityClientId))
        {
            throw new ArgumentException(
                "DemoAdmin uses the signed-in Azure CLI operator identity; ManagedIdentityClientId is not allowed.");
        }

        if (string.Equals(tableOptions.Mode, "Azure", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = new Uri(tableOptions.TableEndpoint!, UriKind.Absolute);
            if (!string.IsNullOrEmpty(endpoint.UserInfo) ||
                !string.IsNullOrEmpty(endpoint.Query) ||
                !string.IsNullOrEmpty(endpoint.Fragment) ||
                endpoint.AbsolutePath != "/")
            {
                throw new ArgumentException(
                    "The Azure TableEndpoint must be a service URL without credentials, path, query, or fragment.");
            }
        }

        var adminOptions = new PizzaOpsAdminOptions();
        hostBuilder.Configuration.GetSection(PizzaOpsAdminOptions.SectionName).Bind(adminOptions);
        adminOptions.Validate();

        hostBuilder.Services.AddSingleton(Options.Create(tableOptions));
        hostBuilder.Services.AddSingleton(adminOptions);
        hostBuilder.Services.AddSingleton(serviceProvider =>
        {
            if (string.Equals(tableOptions.Mode, "Azurite", StringComparison.OrdinalIgnoreCase))
            {
                return new TableStorageClients(
                    new TableServiceClient(
                        tableOptions.AzuriteConnectionString,
                        CreateClientOptions(maxRetries: 2)),
                    new TableServiceClient(
                        tableOptions.AzuriteConnectionString,
                        CreateClientOptions(maxRetries: 0)));
            }

            var credential = new AzureCliCredential();
            return new TableStorageClients(
                new TableServiceClient(
                    new Uri(tableOptions.TableEndpoint!, UriKind.Absolute),
                    credential,
                    CreateClientOptions(maxRetries: 2)),
                new TableServiceClient(
                    new Uri(tableOptions.TableEndpoint!, UriKind.Absolute),
                    credential,
                    CreateClientOptions(maxRetries: 0)));
        });
        hostBuilder.Services.AddSingleton(serviceProvider => new TableKitchenRepository(
            serviceProvider.GetRequiredService<TableStorageClients>(),
            serviceProvider.GetRequiredService<IOptions<PizzaOpsTableOptions>>()));
        hostBuilder.Services.AddSingleton<IOvenRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<TableKitchenRepository>());
        hostBuilder.Services.AddSingleton<IKitchenEventRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<TableKitchenRepository>());
        hostBuilder.Services.AddSingleton<IKitchenTableMaintenance>(serviceProvider =>
            serviceProvider.GetRequiredService<TableKitchenRepository>());
        hostBuilder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        hostBuilder.Services.AddSingleton<IKitchenReadService, KitchenReadService>();
        hostBuilder.Services.AddSingleton<IKitchenResetService, KitchenResetService>();

        return hostBuilder.Build();
    }

    private static async Task<int> ShowStatusAsync(
        IServiceProvider services,
        AdminTarget target,
        CancellationToken cancellationToken)
    {
        PrintTarget(target);
        PrintDashboardUrl(target);
        OperationResult<KitchenSnapshot> result;
        using (StartProgress("Reading kitchen status"))
        {
            result = await services
                .GetRequiredService<IKitchenReadService>()
                .GetStatusAsync(cancellationToken);
        }

        if (!result.Success || result.Data is null || !result.Data.EventFeedAvailable)
        {
            ConsoleUtility.WriteErrorLine($"Readiness: NOT READY ({result.Code}: {result.Message})");
            return 1;
        }

        ConsoleUtility.WriteLine("Readiness: READY", ConsoleColor.Green);
        foreach (var oven in result.Data.Ovens)
        {
            ConsoleUtility.WriteLine($"{oven.DisplayName}: {oven.TemperatureC} C ({oven.Health})");
        }

        ConsoleUtility.WriteLine($"Recent events available: {result.Data.RecentEvents.Count}");
        return 0;
    }

    private static async Task<int> ResetAsync(
        IServiceProvider services,
        AdminTarget target,
        CancellationToken cancellationToken)
    {
        PrintTarget(target);
        if (!ConfirmTarget(target))
        {
            ConsoleUtility.WriteErrorLine("Scope confirmation did not match; reset was not started.");
            return 2;
        }

        using (StartProgress("Resetting and verifying kitchen"))
        {
            await services.GetRequiredService<IKitchenResetService>().ResetAsync(cancellationToken);
        }
        ConsoleUtility.WriteLine(
            "Reset completed and verified: three canonical ovens and exactly one RESET event.",
            ConsoleColor.Green);
        ConsoleUtility.WriteLine("Readiness: READY", ConsoleColor.Green);
        PrintDashboardUrl(target);
        return 0;
    }

    private static void PrintTarget(AdminTarget target)
    {
        ConsoleUtility.WriteLine($"Environment: {target.EnvironmentName}");
        ConsoleUtility.WriteLine($"Mode: {target.Mode}");
        ConsoleUtility.WriteLine($"Table endpoint: {target.DisplayEndpoint}");
        ConsoleUtility.WriteLine($"Ovens table: {target.OvensTableName}");
        ConsoleUtility.WriteLine($"Events table: {target.EventsTableName}");
        ConsoleUtility.WriteLine($"Kitchen partition: {target.PartitionKey}");
    }

    private static bool ConfirmTarget(AdminTarget target)
    {
        var expected = target.ConfirmationText;
        ConsoleUtility.WriteLine("Reset requires tool calls to be paused and all in-flight calls drained.");
        ConsoleUtility.WriteLine($"Type this exact target confirmation to proceed: {expected}");
        return string.Equals(Console.ReadLine(), expected, StringComparison.Ordinal);
    }

    private static void PrintDashboardUrl(AdminTarget target)
    {
        if (target.DashboardUrl is not null)
        {
            ConsoleUtility.WriteLine($"Dashboard: {target.DashboardUrl}");
        }
    }

    private static LoadingIndicator? StartProgress(string message)
    {
        ConsoleUtility.WriteLine($"{message}...", ConsoleColor.Cyan);
        return Console.IsOutputRedirected ? null : new LoadingIndicator(message);
    }

    private static TableClientOptions CreateClientOptions(int maxRetries)
    {
        var options = new TableClientOptions();
        options.Retry.MaxRetries = maxRetries;
        return options;
    }
}
