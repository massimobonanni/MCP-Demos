namespace MCP_Demos._02_MCPChat;

internal sealed class FoundryConfiguration(Uri endpoint, string apiKey)
{
    internal Uri Endpoint { get; } = endpoint;

    internal string ApiKey { get; } = apiKey;
}
