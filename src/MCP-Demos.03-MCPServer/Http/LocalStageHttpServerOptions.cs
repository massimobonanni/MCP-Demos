using System.Globalization;

namespace StageDirector.Http;

/// <summary>
/// Startup settings for the loopback-only Stage Director dashboard.
/// </summary>
/// <param name="Port">The validated TCP port bound on <c>127.0.0.1</c>.</param>
internal sealed record LocalStageHttpServerOptions(int Port)
{
    internal const string PortEnvironmentVariable = "STAGE_DIRECTOR_PORT";
    internal const int DefaultPort = 5187;
    internal const int MinimumPort = 1024;
    internal const int MaximumPort = 65535;

    /// <summary>
    /// The only prefix registered with the listener; the host is a loopback IP literal, never a wildcard.
    /// </summary>
    internal string Prefix => $"http://127.0.0.1:{Port.ToString(CultureInfo.InvariantCulture)}/";

    /// <summary>
    /// Reads and validates <c>STAGE_DIRECTOR_PORT</c>, falling back to the default port when it is unset.
    /// </summary>
    /// <param name="options">The validated options when the method returns <see langword="true"/>.</param>
    /// <param name="error">A presenter-readable diagnostic when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the configured port is usable.</returns>
    internal static bool TryFromEnvironment(out LocalStageHttpServerOptions options, out string? error) =>
        TryCreate(Environment.GetEnvironmentVariable(PortEnvironmentVariable), out options, out error);

    /// <summary>
    /// Validates a raw port value as a plain decimal integer from 1024 through 65535.
    /// </summary>
    /// <param name="rawPort">The raw value, or <see langword="null"/>/empty to use the default port.</param>
    /// <param name="options">The validated options when the method returns <see langword="true"/>.</param>
    /// <param name="error">A presenter-readable diagnostic when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the port is usable.</returns>
    internal static bool TryCreate(string? rawPort, out LocalStageHttpServerOptions options, out string? error)
    {
        options = new(DefaultPort);
        error = null;
        if (string.IsNullOrEmpty(rawPort))
        {
            return true;
        }

        // NumberStyles.None rejects signs, whitespace, and separators so only plain digits are accepted.
        if (!int.TryParse(rawPort, NumberStyles.None, CultureInfo.InvariantCulture, out int port) ||
            port is < MinimumPort or > MaximumPort)
        {
            error = $"{PortEnvironmentVariable} must be an integer from {MinimumPort} through {MaximumPort}; received '{rawPort}'.";
            return false;
        }

        options = new(port);
        return true;
    }
}
