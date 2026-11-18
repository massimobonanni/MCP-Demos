using StageDirector.Mcp;
using StageDirector.Services;
using StageDirector.Utilities;

namespace Microsoft.Extensions.DependencyInjection;

internal static class ServiceCollectionExtension
{
    /// <summary>
    /// Registers the three Stage Director MCP tools with their explicit input schemas.
    /// </summary>
    /// <param name="services">The service collection containing the shared stage service.</param>
    internal static void RegisterMCPTools(this IServiceCollection services)
    {
        services.AddSingleton(provider => new StageTools(provider.GetRequiredService<StageService>()));
        services.AddSingleton(provider =>
            {
                var stageTools = provider.GetRequiredService<StageTools>();
                return McpToolsUtility.CreateMcpTool(stageTools.GetStageStatus, StageTools.noArgumentsSchema);
            });
        services.AddSingleton(provider =>
            {
                var stageTools = provider.GetRequiredService<StageTools>();
                return McpToolsUtility.CreateMcpTool(stageTools.ActivateScene, StageTools.activateSchema);
            });
        services.AddSingleton(provider =>
            {
                var stageTools = provider.GetRequiredService<StageTools>();
                return McpToolsUtility.CreateMcpTool(stageTools.ResetStage, StageTools.noArgumentsSchema);
            });
    }
}
