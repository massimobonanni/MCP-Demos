using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PizzaOps.Core.Application;
using PizzaOps.Core.Domain;
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

        AddOptions(services, configuration, environmentName);
        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<PizzaOpsTableOptions>>().Value;
            return new TableStorageClients(CreateClient(options, maxRetries: 2), CreateClient(options, maxRetries: 0));
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
        services.AddSingleton<IOvenReader>(serviceProvider => serviceProvider.GetRequiredService<IOvenRepository>());
        services.AddSingleton<IKitchenEventReader>(serviceProvider => serviceProvider.GetRequiredService<IKitchenEventRepository>());
        AddReadProjection(services);

        return services;
    }

    /// <summary>Registers only read ports and the shared projection; no tables are created.</summary>
    public static IServiceCollection AddPizzaOpsReadOnlyStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        AddOptions(services, configuration, environmentName);
        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<PizzaOpsTableOptions>>();
            return new ReadRepository(new TableKitchenRepository(CreateClient(options.Value, maxRetries: 2), options));
        });
        services.AddSingleton<IOvenReader>(serviceProvider => serviceProvider.GetRequiredService<ReadRepository>());
        services.AddSingleton<IKitchenEventReader>(serviceProvider => serviceProvider.GetRequiredService<ReadRepository>());
        AddReadProjection(services);
        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration, string environmentName)
    {
        services.AddSingleton<IValidateOptions<PizzaOpsTableOptions>>(
            new PizzaOpsTableOptionsValidator(environmentName));
        services.AddOptions<PizzaOpsTableOptions>()
            .Bind(configuration.GetSection(PizzaOpsTableOptions.SectionName))
            .ValidateOnStart();

    }

    private static void AddReadProjection(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IKitchenReadService, KitchenReadService>();
    }

    private static TableServiceClient CreateClient(PizzaOpsTableOptions options, int maxRetries)
    {
        if (string.Equals(options.Mode, "Azurite", StringComparison.OrdinalIgnoreCase))
        {
            return new TableServiceClient(options.AzuriteConnectionString, CreateClientOptions(maxRetries));
        }

        var endpoint = new Uri(options.TableEndpoint!, UriKind.Absolute);
        var identity = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId);
        return new TableServiceClient(endpoint, new ManagedIdentityCredential(identity), CreateClientOptions(maxRetries));
    }

    private sealed class ReadRepository(TableKitchenRepository repository) : IOvenReader, IKitchenEventReader
    {
        public Task<IReadOnlyList<Oven>> GetAllAsync(CancellationToken cancellationToken) =>
            repository.GetAllAsync(cancellationToken);

        public Task<IReadOnlyList<KitchenEvent>> GetLatestAsync(int count, CancellationToken cancellationToken) =>
            repository.GetLatestAsync(count, cancellationToken);
    }

    private static TableClientOptions CreateClientOptions(int maxRetries)
    {
        var options = new TableClientOptions();
        options.Retry.MaxRetries = maxRetries;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
        options.Retry.MaxDelay = TimeSpan.FromSeconds(1);
        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(2);
        options.Diagnostics.IsLoggingContentEnabled = false;
        return options;
    }
}
