using System.Text.Json;
using ModelContextProtocol.Server;

namespace StageDirector.Utilities;

internal static class McpToolsUtility
{
    /// <summary>
    /// Serializer options shared by the MCP tools and <c>GET /api/stage</c> so both emit the same snapshot shape.
    /// </summary>
    internal static readonly JsonSerializerOptions SerializerOptions = JsonSerializerOptions.Web;

    /// <summary>
    /// Wraps a handler as an MCP tool with the supplied input schema.
    /// </summary>
    /// <param name="handler">The delegate invoked by the tool.</param>
    /// <param name="inputSchema">The schema advertised to MCP clients.</param>
    /// <returns>The configured MCP tool.</returns>
    internal static McpServerTool CreateMcpTool(Delegate handler, JsonElement inputSchema)
    {
        McpServerTool tool = McpServerTool.Create(handler, new() { SerializerOptions = SerializerOptions });
        tool.ProtocolTool.InputSchema = inputSchema;
        return tool;
    }
}
