using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PizzaOps.Core.Application;
using PizzaOps.Storage.Persistence;

namespace PizzaOps.Storage.DependencyInjection;

/// <summary>Registers the Azure Table Storage adapter and shared application read projection.</summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Table Storage adapter. Azure uses managed identity; Development must
    /// explicitly select Azurite. This registration never creates tables.
    /// </summary>
    public static IServiceCollection AddPizzaOpsStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        services.AddSingleton<IValidateOptions<PizzaOpsTableOptions>>(
            new PizzaOpsTableOptionsValidator(environmentName));
        services.AddOptions<PizzaOpsTableOptions>()
            .Bind(configuration.GetSection(PizzaOpsTableOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<PizzaOpsTableOptions>>().Value;

            if (string.Equals(options.Mode, "Azurite", StringComparison.OrdinalIgnoreCase))
            {
                return new TableStorageClients(
                    new TableServiceClient(options.AzuriteConnectionString, CreateClientOptions(maxRetries: 2)),
                    new TableServiceClient(options.AzuriteConnectionString, CreateClientOptions(maxRetries: 0)));
            }

            var endpoint = new Uri(options.TableEndpoint!, UriKind.Absolute);
            var identity = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
                ? ManagedIdentityId.SystemAssigned
                : ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId);
            var credential = new ManagedIdentityCredential(identity);

            return new TableStorageClients(
                new TableServiceClient(endpoint, credential, CreateClientOptions(maxRetries: 2)),
                new TableServiceClient(endpoint, credential, CreateClientOptions(maxRetries: 0)));
        });
        services.AddSingleton(serviceProvider => new TableKitchenRepository(
            serviceProvider.GetRequiredService<TableStorageClients>(),
            serviceProvider.GetRequiredService<IOptions<PizzaOpsTableOptions>>()));
        services.AddSingleton<IOvenRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<TableKitchenRepository>());
        services.AddSingleton<IKitchenEventRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<TableKitchenRepository>());
        services.AddSingleton<IKitchenTableMaintenance>(serviceProvider =>
            serviceProvider.GetRequiredService<TableKitchenRepository>());
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IKitchenReadService, KitchenReadService>();

        return services;
    }

    private static TableClientOptions CreateClientOptions(int maxRetries)
    {
        var options = new TableClientOptions();
        options.Retry.MaxRetries = maxRetries;
        return options;
    }
}
