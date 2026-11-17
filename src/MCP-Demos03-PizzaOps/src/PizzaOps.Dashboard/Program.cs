using System.Text.Json;
using Microsoft.AspNetCore.Components.Server.Circuits;
using PizzaOps.Dashboard.Components;
using PizzaOps.Dashboard.Endpoints;
using PizzaOps.Dashboard.Presentation;
using PizzaOps.Storage.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<DashboardCircuit>();
builder.Services.AddScoped<CircuitHandler>(services => services.GetRequiredService<DashboardCircuit>());
builder.Services.AddTransient<DashboardSession>();
builder.Services.AddPizzaOpsReadOnlyStorage(builder.Configuration, builder.Environment.EnvironmentName);
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
    api.UseExceptionHandler(handler => handler.Run(async context =>
    {
        await Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred.").ExecuteAsync(context);
    }));
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
