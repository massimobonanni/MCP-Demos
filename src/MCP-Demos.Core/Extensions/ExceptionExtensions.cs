using System.Net.Http;
using ModelContextProtocol;

namespace System;

/// <summary>
/// Provides extension methods for classifying exceptions.
/// </summary>
public static class ExceptionExtensions
{
    /// <summary>
    /// Identifies expected failures returned by the MCP protocol or its HTTP transport.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>True if the exception represents an MCP communication failure; otherwise, false.</returns>
    public static bool IsMcpCommunicationError(this Exception exception)
    {
        return exception is HttpRequestException or McpException or IOException or TimeoutException;
    }
}
