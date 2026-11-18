using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using StageDirector.Http;
using StageDirector.Services;

// Validate the dashboard port before anything starts so a bad value never yields a partial demo.
if (!LocalStageHttpServerOptions.TryFromEnvironment(out LocalStageHttpServerOptions httpOptions, out string? portError))
{
    await Console.Error.WriteLineAsync($"Stage Director startup failed: {portError}");
    return 1;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton(new StageService());
builder.Services.AddSingleton(httpOptions);
// Registered before the MCP stdio transport so the listener binds first; a bind failure aborts startup.
builder.Services.AddHostedService<LocalStageHttpServer>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport();
builder.Services.RegisterMCPTools();

using IHost host = builder.Build();
try
{
    await host.RunAsync();
}
catch (StageHttpServerStartupException exception)
{
    await Console.Error.WriteLineAsync($"Stage Director startup failed: {exception.Message}");
    return 1;
}

return 0;
