using System.ClientModel;
using System.Text.Json;
using System.Net.Http;
using MCP_Demos.Core.Utilities;
using MCP_Demos._02_MCPChat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using OpenAI;

const string deploymentName = "gpt-6-luna";
const string learnServerUrl = "https://learn.microsoft.com/api/mcp";
const string systemMessage = "You are a helpful assistant. Respond clearly and accurately. If you use an MCP tool, base your answer only on its results. If a tool fails or returns an error, say so and do not claim that it succeeded. If you are unsure, say so.";

// Load the Foundry endpoint and key from this project's .NET user secrets.
var foundryConfiguration = LoadConfiguration();
if (foundryConfiguration is null)
{
    return 1;
}

// Connect to Microsoft Learn using the server's documented Streamable HTTP transport.
await using var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(learnServerUrl),
    TransportMode = HttpTransportMode.StreamableHttp,
    Name = "Microsoft Learn MCP Server"
});

await using var mcpClient = await CreateMcpClientAsync(transport);
if (mcpClient is null)
{
    return 1;
}

// Discover the server's current tools; the server can change its tool list over time.
var tools = await GetToolsAsync(mcpClient);
if (tools is null)
{
    return 1;
}

// Build the Foundry Responses API client and initialize history with system instructions.
using var chatClient = CreateResponsesChatClient(foundryConfiguration, deploymentName);
var conversation = new List<ChatMessage>
{
    new(ChatRole.System, systemMessage)
};

ConsoleUtility.WriteLine("Chat with Microsoft Foundry. Enter /quit to exit.", ConsoleColor.Green);

// Keep accepting turns until the user exits; each turn uses the same conversation history.
while (true)
{
    ConsoleUtility.Write("\n[USER] ", ConsoleColor.Cyan);
    var userInput = Console.ReadLine();

    if (userInput is null || userInput.Trim().Equals("/quit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    if (string.IsNullOrWhiteSpace(userInput))
    {
        continue;
    }

    conversation.Add(new ChatMessage(ChatRole.User, userInput));

    try
    {
        // Supply discovered MCP tools on every request; MEAI runs requested tools and returns their results.
        var response = await chatClient.GetResponseAsync(
            conversation,
            new ChatOptions { Tools = [.. tools] });
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            conversation.RemoveAt(conversation.Count - 1);
            ConsoleUtility.WriteLine("[MODEL RESPONSE ERROR] The model returned no text response.", ConsoleColor.Red);
            continue;
        }

        // MEAI includes intermediate tool calls and results in Messages, preserving them for the next turn.
        conversation.AddRange(response.Messages);
        ConsoleUtility.WriteLine($"[MODEL RESPONSE] {response.Text}", ConsoleColor.Green);
    }
    catch (McpToolInvocationException exception)
    {
        // A failed turn is removed so the next request doesn't inherit an incomplete exchange.
        conversation.RemoveAt(conversation.Count - 1);
        ConsoleUtility.WriteLine(
            $"[MCP INVOCATION ERROR] The turn was stopped because tool '{exception.ToolName}' failed. No model response was generated for this turn.",
            ConsoleColor.Red);
    }
    catch (ClientResultException exception)
    {
        conversation.RemoveAt(conversation.Count - 1);
        ConsoleUtility.WriteLine(
            $"[MODEL RESPONSE ERROR] The Foundry request failed (HTTP {exception.Status}). {exception.Message}",
            ConsoleColor.Red);
    }
    catch (HttpRequestException exception)
    {
        conversation.RemoveAt(conversation.Count - 1);
        var status = exception.StatusCode is { } statusCode ? $" (HTTP {(int)statusCode})" : string.Empty;
        ConsoleUtility.WriteLine(
            $"[MODEL RESPONSE ERROR] Could not connect to Microsoft Foundry{status}.  {exception.Message}",
            ConsoleColor.Red);
    }
}

return 0;

// Connects to the Microsoft Learn MCP server; the caller owns the returned client's lifetime.
static async Task<McpClient?> CreateMcpClientAsync(IClientTransport transport)
{
    try
    {
        return await McpClient.CreateAsync(transport);
    }
    catch (Exception exception) when (exception.IsMcpCommunicationError())
    {
        ConsoleUtility.WriteLine($"Could not connect to the Microsoft Learn MCP server: {exception.Message}", ConsoleColor.Red);
        return null;
    }
}

// Retrieves the tools advertised by the Microsoft Learn MCP server.
static async Task<IList<McpClientTool>?> GetToolsAsync(McpClient client)
{
    try
    {
        var tools = await client.ListToolsAsync();
        //ConsoleUtility.WriteLine($"[MCP] Discovered {tools.Count} Microsoft Learn tool(s).", ConsoleColor.DarkCyan);
        //foreach (var tool in tools)
        //{
        //    ConsoleUtility.WriteLine($"  {tool.Name}: {tool.Description}", ConsoleColor.DarkCyan);
        //}

        return tools;
    }
    catch (Exception exception) when (exception.IsMcpCommunicationError())
    {
        ConsoleUtility.WriteLine($"Could not discover tools from the Microsoft Learn MCP server: {exception.Message}", ConsoleColor.Red);
        return null;
    }
}

// Creates the Foundry Responses API client and wraps it with the MEAI MCP tool invocation pipeline.
#pragma warning disable OPENAI001 // The Responses API adapter is required for tool calls with this model.
static IChatClient CreateResponsesChatClient(FoundryConfiguration configuration, string deploymentName)
{
    var clientOptions = new OpenAIClientOptions
    {
        Endpoint = new Uri(configuration.Endpoint, "openai/v1/")
    };
    var foundryClient = new OpenAIClient(
        new ApiKeyCredential(configuration.ApiKey),
        clientOptions);

    // Adapt the Responses SDK client to the common Microsoft.Extensions.AI chat interface.
    var responsesClient = foundryClient.GetResponsesClient();
    var modelClient = responsesClient.AsIChatClient(deploymentName);
    var chatClientBuilder = new ChatClientBuilder(modelClient);

    // The model requests a tool; this middleware executes it and sends its result back to the model.
    chatClientBuilder.UseFunctionInvocation(configure: functionInvoker =>
    {
        // Stop the turn on the first tool exception instead of asking the model to recover from it.
        functionInvoker.MaximumConsecutiveErrorsPerRequest = 0;
        functionInvoker.FunctionInvoker = InvokeMcpToolAsync;
    });

    return chatClientBuilder.Build();
}
#pragma warning restore OPENAI001

// Executes the requested MCP tool and displays its error before stopping the turn if it fails.
static async ValueTask<object?> InvokeMcpToolAsync(
    FunctionInvocationContext context,
    CancellationToken cancellationToken)
{
    ConsoleUtility.WriteLine($"[MCP TOOL CALL] {context.Function.Name}", ConsoleColor.Magenta);
    ConsoleUtility.WriteLine($"\tArguments: {JsonSerializer.Serialize(context.Arguments)}", ConsoleColor.DarkMagenta);

    object? result;
    try
    {
        // MCP tools are exposed as AIFunctions; invoking one sends the call to the MCP server.
        result = await context.Function.InvokeAsync(context.Arguments, cancellationToken);
    }
    catch (Exception exception)
    {
        ConsoleUtility.WriteLine($"[MCP RESULT - ERROR] {exception.Message}", ConsoleColor.Red);
        throw new McpToolInvocationException(context.Function.Name, exception);
    }

    // An MCP server can report failure through isError without throwing an exception.
    var toolReportedError = IsToolError(result);
    if (toolReportedError)
    {
        ConsoleUtility.WriteLine("[MCP RESULT - ERROR]", ConsoleColor.Red);
        ConsoleUtility.WriteLine(FormatToolResult(result), ConsoleColor.Red);
        throw new McpToolInvocationException(context.Function.Name);
    }

    // The middleware passes this result to the model so it can produce the final answer.
    return result;
}

// Checks whether an MCP tool returned an error result instead of successful content.
static bool IsToolError(object? result)
{
    return result is JsonElement { ValueKind: JsonValueKind.Object } json &&
        json.TryGetProperty("isError", out var isError) &&
        isError.ValueKind == JsonValueKind.True;
}

// Formats MCP tool results without replacing missing or failed results with generated text.
static string FormatToolResult(object? result)
{
    return result switch
    {
        null => "null",
        string text => text,
        JsonElement json => JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }),
        TextContent textContent => textContent.Text,
        IEnumerable<AIContent> content => string.Join(
            Environment.NewLine,
            content.Select(item => item is TextContent textItem
                ? textItem.Text
                : JsonSerializer.Serialize(item, item.GetType(), new JsonSerializerOptions { WriteIndented = true }))),
        AIContent content => JsonSerializer.Serialize(content, content.GetType(), new JsonSerializerOptions { WriteIndented = true }),
        _ => JsonSerializer.Serialize(result, result.GetType(), new JsonSerializerOptions { WriteIndented = true })
    };
}

// Loads and validates the Foundry endpoint and API key from the project's .NET user secrets.
static FoundryConfiguration? LoadConfiguration()
{
    const string endpointSetting = "Foundry:Endpoint";
    const string apiKeySetting = "Foundry:ApiKey";

    var configuration = new ConfigurationBuilder()
        .AddUserSecrets<Program>(optional: true)
        .Build();

    var endpoint = configuration[endpointSetting];
    var apiKey = configuration[apiKeySetting];
    if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
    {
        var missingSettings = new List<string>();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            missingSettings.Add(endpointSetting);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            missingSettings.Add(apiKeySetting);
        }

        ConsoleUtility.WriteLine($"Missing local settings: {string.Join(", ", missingSettings)}", ConsoleColor.Red);
        ConsoleUtility.WriteLine("Configure them with .NET user secrets from the repository root:", ConsoleColor.Yellow);
        ConsoleUtility.WriteLine("dotnet user-secrets set \"Foundry:Endpoint\" \"<your-foundry-endpoint>\" --project src\\MCP-Demos.02-MCPChat");
        ConsoleUtility.WriteLine("dotnet user-secrets set \"Foundry:ApiKey\" \"<your-api-key>\" --project src\\MCP-Demos.02-MCPChat");
        return null;
    }

    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var foundryEndpoint) ||
        !string.Equals(foundryEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        ConsoleUtility.WriteLine($"The {endpointSetting} setting must be an absolute HTTPS URL.", ConsoleColor.Red);
        return null;
    }

    ConsoleUtility.WriteLine("Configuration loaded.", ConsoleColor.Green);
    ConsoleUtility.WriteLine($"\tFoundry endpoint: {foundryEndpoint}", ConsoleColor.Green);
    ConsoleUtility.WriteLine($"\tFoundry API key: {apiKey.MaskStringBothEnds()}", ConsoleColor.Green);
    ConsoleUtility.WriteLine();

    return new FoundryConfiguration(foundryEndpoint, apiKey);
}

internal sealed class McpToolInvocationException(string toolName, Exception? innerException = null)
    : Exception($"The MCP tool '{toolName}' failed.", innerException)
{
    internal string ToolName { get; } = toolName;
}
