using MCP_Demos.Core.Utilities;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides extension methods for displaying MCP tool results.
/// </summary>
public static class CallToolResultExtensions
{
    /// <summary>
    /// Writes the search tool error message and its text content to the console in red.
    /// </summary>
    /// <param name="result">The tool result containing error details.</param>
    public static void WriteToolError(this CallToolResult result)
    {
        ConsoleUtility.WriteLine("The search tool reported an error.", ConsoleColor.Red);
        foreach (var textBlock in result.Content.OfType<TextContentBlock>())
        {
            ConsoleUtility.WriteLine(textBlock.Text, ConsoleColor.Red);
        }
    }
}
