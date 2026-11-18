using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using StageDirector.Domain;
using StageDirector.Services;

namespace StageDirector.Mcp;

/// <summary>
/// Adapts the shared <see cref="StageService"/> to the Stage Director MCP tools.
/// </summary>
/// <remarks>
/// Validates incoming arguments against the advertised tool contracts before delegating to the service.
/// </remarks>
/// <param name="stageService">The shared service that owns the current stage state.</param>
[McpServerToolType]
internal sealed class StageTools(StageService stageService)
{

    /// <summary>
    /// Defines an empty argument object for <c>get_stage_status</c> and <c>reset_stage</c>.
    /// </summary>
    internal static readonly JsonElement noArgumentsSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    });

    /// <summary>
    /// Requires <c>activate_scene</c> to receive exactly one <c>scene</c> string from the scene catalog.
    /// </summary>
    internal static readonly JsonElement activateSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { scene = new { type = "string", @enum = StageSceneCatalog.AvailableScenes } },
        required = new[] { "scene" },
        additionalProperties = false
    });


    /// <summary>
    /// Reads the current stage snapshot without changing it.
    /// </summary>
    /// <param name="request">The MCP request, which must not supply tool arguments.</param>
    /// <param name="cancellationToken">The token checked before reading the stage.</param>
    /// <returns>The current stage snapshot.</returns>
    /// <exception cref="McpException">The request supplies tool arguments.</exception>
    [McpServerTool(Name = "get_stage_status", ReadOnly = true, Destructive = false, Idempotent = true,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Returns the active scene, its visual effects, and the available scenes on the Stage Director virtual stage. Does not modify state.")]
    internal StageState GetStageStatus(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireNoArguments(request);
        return stageService.GetSnapshot();
    }

    /// <summary>
    /// Activates a predefined scene. Activating the current scene leaves the stage unchanged.
    /// </summary>
    /// <param name="request">The MCP request containing only a <c>scene</c> string with a canonical identifier.</param>
    /// <param name="cancellationToken">The token checked before changing the stage.</param>
    /// <returns>The result of the activation, including the resulting stage snapshot.</returns>
    /// <exception cref="McpException">The arguments do not specify exactly one valid scene.</exception>
    [McpServerTool(Name = "activate_scene", Destructive = false, Idempotent = true,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Activates a predefined scene on the Stage Director virtual stage. Choose one of: welcome, focus, energy, celebration. The call changes local graphical effects only; it does not control lights or physical devices.")]
    internal StageChangeResult ActivateScene(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Params?.Arguments is not { Count: 1 } arguments ||
            !arguments.TryGetValue("scene", out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw InvalidScene();
        }

        string? scene = value.GetString();
        try
        {
            return stageService.ActivateScene(scene);
        }
        catch (ArgumentException exception) when (exception.ParamName == nameof(scene))
        {
            throw InvalidScene();
        }
    }

    /// <summary>
    /// Restores the welcome scene. If it is already active, the stage remains unchanged.
    /// </summary>
    /// <param name="request">The MCP request, which must not supply tool arguments.</param>
    /// <param name="cancellationToken">The token checked before resetting the stage.</param>
    /// <returns>The result of the reset, including the resulting stage snapshot.</returns>
    /// <exception cref="McpException">The request supplies tool arguments.</exception>
    [McpServerTool(Name = "reset_stage", Destructive = false, Idempotent = true,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Resets the virtual stage to the welcome scene. Acts on in-memory state only; does not delete files or modify devices.")]
    internal StageChangeResult ResetStage(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireNoArguments(request);
        return stageService.Reset();
    }

    private static void RequireNoArguments(RequestContext<CallToolRequestParams> request)
    {
        if (request.Params?.Arguments is { Count: > 0 })
        {
            throw new McpException("INVALID_ARGUMENTS: This tool takes no arguments.");
        }
    }

    private static McpException InvalidScene() =>
        new($"INVALID_SCENE: Supply only a scene string. Allowed values: {string.Join(", ", StageSceneCatalog.AvailableScenes)}.");
}
