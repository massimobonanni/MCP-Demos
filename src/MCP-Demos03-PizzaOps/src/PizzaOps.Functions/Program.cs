using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PizzaOps.Core.Application;
using PizzaOps.Core.Domain;
using PizzaOps.Storage.DependencyInjection;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddPizzaOpsStorage(context.Configuration, context.HostingEnvironment.EnvironmentName);
        services.AddSingleton<IRecipeCatalog>(PizzaRecipeCatalog.Instance);
        services.AddSingleton<RecipeReadService>();
        services.AddSingleton<ITemperatureChangeService, TemperatureChangeService>();
    })
    .Build();

await host.RunAsync();
