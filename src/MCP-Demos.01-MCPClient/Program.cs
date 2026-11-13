using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

const string serverUrl = "https://learn.microsoft.com/api/mcp";
const string searchToolName = "microsoft_docs_search";

try
{
    await using var transport = new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(serverUrl),
        TransportMode = HttpTransportMode.StreamableHttp,
        Name = "Microsoft Learn MCP Server"
    });
    await using var client = await McpClient.CreateAsync(transport);

    var tools = await client.ListToolsAsync();
    Console.WriteLine("Available tools:");
    foreach (var tool in tools)
    {
        Console.WriteLine($"- {tool.Name}: {tool.Description}");
    }

    var searchTool = tools.FirstOrDefault(tool => tool.Name == searchToolName);
    if (searchTool is null)
    {
        Console.Error.WriteLine($"The MCP server does not expose the required tool '{searchToolName}'.");
        return 1;
    }

    await RunSearchLoopAsync(client, searchTool.Name);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Could not connect to the Microsoft Learn MCP server or list its tools: {exception.Message}");
    return 1;
}

// Reads search topics until the user enters /quit or the input stream ends.
static async Task RunSearchLoopAsync(McpClient client, string searchToolName)
{
    while (true)
    {
        Console.Write("\nSearch topic (or /quit): ");
        var input = Console.ReadLine();
        if (input is null)
        {
            return;
        }

        var query = input.Trim();
        if (query.Equals("/quit", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            continue;
        }

        try
        {
            var result = await client.CallToolAsync(
                searchToolName,
                new Dictionary<string, object?> { ["query"] = query });

            if (result.IsError is true)
            {
                WriteToolError(result);
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
                try
                {
                    using var document = JsonDocument.Parse(textBlock.Text);
                    resultCount += PrintSearchResults(document.RootElement, printedResults);
                }
                catch (JsonException)
                {
                    // Text content isn't necessarily JSON; structured results are preferred.
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
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Search failed: {exception.Message}");
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

    var title = GetStringProperty(element, "title");
    var contentUrl = GetStringProperty(element, "contentUrl");
    if (title is not null && contentUrl is not null)
    {
        if (!printedResults.Add($"{title}\0{contentUrl}"))
        {
            return 0;
        }

        Console.WriteLine($"Title: {title}");
        Console.WriteLine($"URL: {contentUrl}");
        Console.WriteLine();
        return 1;
    }

    return element.EnumerateObject().Sum(property => PrintSearchResults(property.Value, printedResults));
}

// Gets a string-valued JSON property without depending on property-name casing.
static string? GetStringProperty(JsonElement element, string propertyName)
{
    foreach (var property in element.EnumerateObject())
    {
        if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
            property.Value.ValueKind == JsonValueKind.String)
        {
            return property.Value.GetString();
        }
    }

    return null;
}

// Reports tool-level errors separately from protocol or connection failures.
static void WriteToolError(CallToolResult result)
{
    Console.Error.WriteLine("The search tool reported an error.");
    foreach (var textBlock in result.Content.OfType<TextContentBlock>())
    {
        Console.Error.WriteLine(textBlock.Text);
    }
}
