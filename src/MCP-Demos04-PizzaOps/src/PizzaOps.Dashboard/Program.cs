using System.Text.Json;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Components.Server.Circuits;
using PizzaOps.Core.Application;
using PizzaOps.Dashboard.Components;
using PizzaOps.Dashboard.Endpoints;
using PizzaOps.Dashboard.Presentation;
using PizzaOps.Storage.DependencyInjection;
using PizzaOps.Telemetry;

var builder = WebApplication.CreateBuilder(args);
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddApplicationInsightsTelemetry(options => options.EnableAdaptiveSampling = false);
    builder.Services.AddSingleton<ITelemetryInitializer>(new RoleNameInitializer("PizzaOps.Dashboard"));
    builder.Services.AddApplicationInsightsTelemetryProcessor<SafeTelemetryProcessor>();
}
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<DashboardCircuit>();
builder.Services.AddScoped<CircuitHandler>(services => services.GetRequiredService<DashboardCircuit>());
builder.Services.AddTransient<DashboardSession>();
builder.Services.AddPizzaOpsReadOnlyStorage(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddSingleton<IKitchenReadService>(services => new ObservedKitchenReadService(
    new KitchenReadService(services.GetRequiredService<IOvenReader>(),
        services.GetRequiredService<IKitchenEventReader>(), services.GetRequiredService<TimeProvider>()),
    services.GetRequiredService<ILogger<ObservedKitchenReadService>>(),
    services.GetService<Microsoft.ApplicationInsights.TelemetryClient>()));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api") ||
    context.Request.Path.StartsWithSegments("/health"), api =>
{
    api.Use(async (context, next) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        await next(context);
    });
    api.Use(async (context, next) =>
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499;
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PizzaOps.Dashboard");
            ObservedKitchenReadService.LogRequestFailed(logger,
                exception.GetType().Name, System.Diagnostics.Activity.Current?.TraceId.ToHexString());
            context.Response.Clear();
            context.Response.Headers.CacheControl = "no-store";
            await Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred.",
                extensions: new Dictionary<string, object?>
                {
                    ["correlation_id"] = System.Diagnostics.Activity.Current?.TraceId.ToHexString()
                }).ExecuteAsync(context);
        }
    });
});
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api") &&
    !context.Request.Path.StartsWithSegments("/health"), pages =>
    pages.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseAntiforgery();

app.MapKitchenEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Dashboard application entry point for integration hosting.</summary>
public partial class Program;
