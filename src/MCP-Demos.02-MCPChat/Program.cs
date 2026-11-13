using System.ClientModel;
using System.Net.Http;
using Azure;
using Azure.AI.OpenAI;
using MCP_Demos.Core.Utilities;
using MCP_Demos._02_MCPChat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

const string deploymentName = "gpt-6-luna";
const string systemMessage = "You are a helpful assistant. Respond clearly and accurately. If you are unsure, say so.";

var foundryConfiguration = LoadConfiguration();
if (foundryConfiguration is null)
{
    return 1;
}

var foundryClient = new AzureOpenAIClient(
    foundryConfiguration.Endpoint,
    new AzureKeyCredential(foundryConfiguration.ApiKey));
using var chatClient = foundryClient.GetChatClient(deploymentName).AsIChatClient();

var conversation = new List<ChatMessage>
{
    new(ChatRole.System, systemMessage)
};

ConsoleUtility.WriteLine("Chat with Microsoft Foundry. Enter /quit to exit.", ConsoleColor.Green);

while (true)
{
    ConsoleUtility.Write("\nUser: ", ConsoleColor.Cyan);
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
        var response = await chatClient.GetResponseAsync(conversation);
        conversation.AddRange(response.Messages);
        ConsoleUtility.WriteLine($"Assistant: {response.Text}", ConsoleColor.Green);
    }
    catch (ClientResultException exception)
    {
        conversation.RemoveAt(conversation.Count - 1);
        ConsoleUtility.WriteLine(
            $"The Foundry request failed (HTTP {exception.Status}). Check the endpoint, API key, and deployment configuration.",
            ConsoleColor.Red);
    }
    catch (HttpRequestException exception)
    {
        conversation.RemoveAt(conversation.Count - 1);
        var status = exception.StatusCode is { } statusCode ? $" (HTTP {(int)statusCode})" : string.Empty;
        ConsoleUtility.WriteLine(
            $"Could not connect to Microsoft Foundry{status}. Check the endpoint and network connection.",
            ConsoleColor.Red);
    }
}

return 0;

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

    return new FoundryConfiguration(foundryEndpoint, apiKey);
}
