using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PizzaOps.Core.Application;
using PizzaOps.Core.Domain;
using PizzaOps.Functions;
using PizzaOps.Storage.DependencyInjection;
using PizzaOps.Telemetry;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        if (!string.IsNullOrWhiteSpace(context.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            services.AddApplicationInsightsTelemetryWorkerService(options => options.EnableAdaptiveSampling = false);
            services.ConfigureFunctionsApplicationInsights();
            services.AddSingleton<ITelemetryInitializer>(new RoleNameInitializer("PizzaOps.Functions"));
            services.AddApplicationInsightsTelemetryProcessor<SafeTelemetryProcessor>();
            services.Configure<LoggerFilterOptions>(options =>
            {
                var defaultRule = options.Rules.FirstOrDefault(rule =>
                    rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
                if (defaultRule is not null)
                {
                    options.Rules.Remove(defaultRule);
                }
            });
        }
        services.AddSingleton<ToolExecution>();
        services.AddPizzaOpsStorage(context.Configuration, context.HostingEnvironment.EnvironmentName);
        services.AddSingleton<IRecipeCatalog>(PizzaRecipeCatalog.Instance);
        services.AddSingleton<RecipeReadService>();
        services.AddSingleton<ITemperatureChangeService, TemperatureChangeService>();
        services.AddSingleton<IBakeTestService, BakeTestService>();
    })
    .ConfigureLogging(logging =>
    {
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        logging.AddJsonConsole(options => options.IncludeScopes = true);
    })
    .Build();

await host.RunAsync();
