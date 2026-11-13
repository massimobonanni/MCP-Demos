using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using MCP_Demos.Core.Utilities;

const string serverUrl = "https://learn.microsoft.com/api/mcp";
const string searchToolName = "microsoft_docs_search";

// Step 1: Configure Streamable HTTP for the remote Microsoft Learn server.
// await using ensures asynchronous cleanup when the demo exits, including on early returns.
await using var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(serverUrl),
    TransportMode = HttpTransportMode.StreamableHttp,
    Name = "Microsoft Learn MCP Server"
});

// Step 2: Connect and complete MCP initialization before making requests.
await using var client = await CreateMcpClientAsync(transport);
if (client is null)
{
    return 1;
}

// Step 3: Discover the server's tools through MCP tools/list rather than assuming availability.
var tools = await GetToolsAsync(client);
if (tools is null)
{
    return 1;
}

// Step 4: Display the names and descriptions advertised by the server.
ConsoleUtility.WriteLine("Available tools:", ConsoleColor.Green);
foreach (var tool in tools)
{
    ConsoleUtility.Write($"- {tool.Name}: ", ConsoleColor.Yellow);
    ConsoleUtility.WriteLine($"{tool.Description.TruncateWithEllipsis(200)}", ConsoleColor.White);
}

// Step 5: Check that the documentation search tool is available before entering the loop.
var searchTool = tools.FirstOrDefault(tool => tool.Name == searchToolName);
if (searchTool is null)
{
    ConsoleUtility.WriteLine($"The MCP server does not expose the required tool '{searchToolName}'.", ConsoleColor.Red);
    return 1;
}

// Step 6: Read topics, call the search tool with query, and display titles and URLs until /quit.
await RunSearchLoopAsync(client, searchTool.Name);
return 0;

// Connects to the MCP server; the caller owns the returned client's lifetime.
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

// Retrieves tools, returning null only after reporting a communication failure.
static async Task<IList<McpClientTool>?> GetToolsAsync(McpClient client)
{
    try
    {
        return await client.ListToolsAsync();
    }
    catch (Exception exception) when (exception.IsMcpCommunicationError())
    {
        ConsoleUtility.WriteLine($"Could not list tools from the Microsoft Learn MCP server: {exception.Message}", ConsoleColor.Red);
        return null;
    }
}

// Reads search topics until the user enters /quit or the input stream ends.
static async Task RunSearchLoopAsync(McpClient client, string searchToolName)
{
    while (true)
    {
        ConsoleUtility.Write("\nSearch topic (or /quit): ", ConsoleColor.Cyan);
        var query = Console.ReadLine()?.Trim();
        if (query is null || query.Equals("/quit", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            continue;
        }

        CallToolResult result;
        try
        {
            result = await client.CallToolAsync(
                searchToolName,
                new Dictionary<string, object?> { ["query"] = query });
        }
        catch (Exception exception) when (exception.IsMcpCommunicationError())
        {
            ConsoleUtility.WriteLine($"Search failed: {exception.Message}", ConsoleColor.Red);
            continue;
        }

        if (result.IsError is true)
        {
            result.WriteToolError();
            continue;
        }

        var resultCount = 0;
        var printedResults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (result.StructuredContent is { } structuredContent)
        {
            resultCount += PrintSearchResults(structuredContent, printedResults);
        }

        foreach (var textBlock in result.Content.OfType<TextContentBlock>())
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(textBlock.Text);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                resultCount += PrintSearchResults(document.RootElement, printedResults);
            }
        }

        if (resultCount == 0)
        {
            Console.Error.WriteLine("The search response did not contain results with title and contentUrl fields.");
            foreach (var textBlock in result.Content.OfType<TextContentBlock>())
            {
                Console.Error.WriteLine(textBlock.Text);
            }
        }
    }
}

// Prints every result object containing both title and contentUrl fields.
static int PrintSearchResults(JsonElement element, ISet<string> printedResults)
{
    if (element.ValueKind == JsonValueKind.Array)
    {
        return element.EnumerateArray().Sum(item => PrintSearchResults(item, printedResults));
    }

    if (element.ValueKind != JsonValueKind.Object)
    {
        return 0;
    }

    var title = element.GetStringProperty("title");
    var contentUrl = element.GetStringProperty("contentUrl");
    if (title is not null && contentUrl is not null)
    {
        if (!printedResults.Add($"{title}\0{contentUrl}"))
        {
            return 0;
        }

        ConsoleUtility.Write($"Title: {title} - ", ConsoleColor.Green);
        ConsoleUtility.WriteLine($"URL: {contentUrl}", ConsoleColor.White);
        ConsoleUtility.WriteLine();
        return 1;
    }

    return element.EnumerateObject().Sum(property => PrintSearchResults(property.Value, printedResults));
}
