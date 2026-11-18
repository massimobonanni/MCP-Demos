using System.Collections.Frozen;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StageDirector.Services;
using StageDirector.Utilities;

namespace StageDirector.Http;

/// <summary>
/// Read-only dashboard server hosted in the MCP process with <see cref="HttpListener"/>.
/// </summary>
/// <remarks>
/// <see cref="HttpListener"/> is used instead of ASP.NET Core to keep Demo 03 a single console project with no web
/// framework. It binds only the <c>127.0.0.1</c> prefix, serves a fixed GET allowlist of embedded assets plus the stage
/// snapshot, and never exposes a state-changing route. Request failures are contained per request so a browser
/// disconnect cannot stop the MCP stdio server.
/// </remarks>
internal sealed partial class LocalStageHttpServer : IHostedService, IDisposable
{
    private const string ApiStagePath = "/api/stage";
    private const int MaxConcurrentRequests = 16;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    // 'self' is sufficient because the page loads only same-origin CSS/JS and polls the same-origin API.
    private const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; " +
        "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    private static readonly (string Path, string ResourceName, string ContentType)[] StaticAssets =
    [
        ("/", "StageDirector.Web.index.html", "text/html; charset=utf-8"),
        ("/styles.css", "StageDirector.Web.styles.css", "text/css; charset=utf-8"),
        ("/stage.js", "StageDirector.Web.stage.js", "text/javascript; charset=utf-8"),
    ];

    private static readonly byte[] NotFoundBody = Encoding.UTF8.GetBytes("Not Found");
    private static readonly byte[] MethodNotAllowedBody = Encoding.UTF8.GetBytes("Method Not Allowed");
    private static readonly byte[] ForbiddenBody = Encoding.UTF8.GetBytes("Forbidden");

    private readonly StageService stageService;
    private readonly LocalStageHttpServerOptions options;
    private readonly ILogger<LocalStageHttpServer> logger;
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim requestSlots = new(MaxConcurrentRequests, MaxConcurrentRequests);
    private FrozenDictionary<string, StaticAsset> assets = FrozenDictionary<string, StaticAsset>.Empty;
    private Task acceptLoop = Task.CompletedTask;

    /// <summary>
    /// Creates the dashboard server around the process-wide <see cref="StageService"/> singleton.
    /// </summary>
    /// <param name="stageService">The same service instance used by the MCP tools.</param>
    /// <param name="options">The validated loopback port settings.</param>
    /// <param name="logger">Logger whose output is routed to stderr.</param>
    public LocalStageHttpServer(StageService stageService, LocalStageHttpServerOptions options, ILogger<LocalStageHttpServer> logger)
    {
        this.stageService = stageService;
        this.options = options;
        this.logger = logger;
    }

    /// <summary>
    /// Loads the embedded assets and binds the loopback listener.
    /// </summary>
    /// <param name="cancellationToken">Host startup cancellation.</param>
    /// <returns>A completed task once the listener accepts requests.</returns>
    /// <exception cref="StageHttpServerStartupException">An asset is missing or the port cannot be bound.</exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        assets = LoadAssets();

        if (!HttpListener.IsSupported)
        {
            throw new StageHttpServerStartupException("System.Net.HttpListener is not supported on this platform.");
        }

        listener.Prefixes.Add(options.Prefix);
        if (OperatingSystem.IsWindows())
        {
            // http.sys enforces these before the request reaches managed code, bounding slow or idle clients.
            listener.TimeoutManager.HeaderWait = RequestTimeout;
            listener.TimeoutManager.EntityBody = RequestTimeout;
            listener.TimeoutManager.DrainEntityBody = RequestTimeout;
            listener.TimeoutManager.IdleConnection = TimeSpan.FromSeconds(30);
        }

        try
        {
            listener.Start();
        }
        catch (Exception exception) when (exception is HttpListenerException or SocketException or InvalidOperationException)
        {
            throw new StageHttpServerStartupException(
                $"Cannot start the Stage Director dashboard on {options.Prefix}: {exception.Message} " +
                $"Make sure no other process (or another Stage Director instance) uses port {options.Port}, " +
                $"or set {LocalStageHttpServerOptions.PortEnvironmentVariable} to a free port from " +
                $"{LocalStageHttpServerOptions.MinimumPort} through {LocalStageHttpServerOptions.MaximumPort}.",
                exception);
        }

        acceptLoop = Task.Run(() => AcceptLoopAsync(stopping.Token), CancellationToken.None);
        LogListening(options.Prefix);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops accepting requests, aborts in-flight responses, and releases the port.
    /// </summary>
    /// <param name="cancellationToken">Host shutdown timeout.</param>
    /// <returns>A task that completes when the accept loop has exited or shutdown times out.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        listener.Close();
        await acceptLoop.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        LogStopped(options.Prefix);
    }

    /// <summary>
    /// Releases the listener and synchronization resources; safe after <see cref="StopAsync"/>.
    /// </summary>
    public void Dispose()
    {
        stopping.Cancel();
        listener.Close();
        stopping.Dispose();
        requestSlots.Dispose();
    }

    private static FrozenDictionary<string, StaticAsset> LoadAssets()
    {
        var assembly = typeof(LocalStageHttpServer).Assembly;
        var loaded = new Dictionary<string, StaticAsset>(StringComparer.Ordinal);
        foreach ((string path, string resourceName, string contentType) in StaticAssets)
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new StageHttpServerStartupException(
                    $"Embedded dashboard asset '{resourceName}' is missing from the Stage Director assembly. Rebuild the project.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            loaded[path] = new(buffer.ToArray(), contentType);
        }

        return loaded.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private async Task AcceptLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                if (stoppingToken.IsCancellationRequested || !listener.IsListening)
                {
                    break;
                }

                LogAcceptFailed(exception);
                continue;
            }

            // Bound concurrency so a burst of browser tabs cannot exhaust threads shared with the MCP server.
            if (!requestSlots.Wait(0, CancellationToken.None))
            {
                context.Response.Abort();
                continue;
            }

            _ = HandleRequestAsync(context, stoppingToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken stoppingToken)
    {
        HttpListenerResponse response = context.Response;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(RequestTimeout);
            // Aborting the response is the only reliable way to interrupt a write to a stalled client.
            await using CancellationTokenRegistration abortOnTimeout = timeout.Token.Register(response.Abort);

            (int statusCode, byte[] body, string contentType) = Route(context.Request, response);
            // HEAD responses must not carry a body; writing one makes HttpListener reset the connection.
            if (string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.Ordinal))
            {
                body = [];
            }

            response.StatusCode = statusCode;
            response.ContentType = contentType;
            response.ContentLength64 = body.Length;
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
            response.Headers["Referrer-Policy"] = "no-referrer";

            await response.OutputStream.WriteAsync(body, timeout.Token).ConfigureAwait(false);
            response.Close();
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or ObjectDisposedException or
            OperationCanceledException or InvalidOperationException)
        {
            // Client disconnects, timeouts, and shutdown are expected; only this request is affected.
            LogRequestAborted(exception);
            AbortQuietly(response);
        }
        catch (Exception exception)
        {
            // Last-resort guard: an unexpected bug in one request must never surface to the MCP host.
            LogRequestFailed(exception);
            AbortQuietly(response);
        }
        finally
        {
            requestSlots.Release();
        }
    }

    private (int StatusCode, byte[] Body, string ContentType) Route(HttpListenerRequest request, HttpListenerResponse response)
    {
        const string plainText = "text/plain; charset=utf-8";

        // Defense in depth: the prefix already binds 127.0.0.1, but never answer a non-loopback peer.
        if (request.RemoteEndPoint is not { } remote || !IPAddress.IsLoopback(remote.Address))
        {
            return (StatusCodes.Forbidden, ForbiddenBody, plainText);
        }

        string path = request.Url?.AbsolutePath ?? string.Empty;
        bool isApi = string.Equals(path, ApiStagePath, StringComparison.Ordinal);
        if (!isApi && !assets.ContainsKey(path))
        {
            return (StatusCodes.NotFound, NotFoundBody, plainText);
        }

        if (!string.Equals(request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            response.Headers["Allow"] = "GET";
            return (StatusCodes.MethodNotAllowed, MethodNotAllowedBody, plainText);
        }

        if (isApi)
        {
            byte[] snapshot = JsonSerializer.SerializeToUtf8Bytes(stageService.GetSnapshot(), McpToolsUtility.SerializerOptions);
            return (StatusCodes.Ok, snapshot, "application/json; charset=utf-8");
        }

        StaticAsset asset = assets[path];
        return (StatusCodes.Ok, asset.Content, asset.ContentType);
    }

    private static void AbortQuietly(HttpListenerResponse response)
    {
        try
        {
            response.Abort();
        }
        catch (ObjectDisposedException)
        {
            // The response or listener is already closed; nothing remains to release.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Stage Director dashboard listening on {Prefix}")]
    private partial void LogListening(string prefix);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stage Director dashboard stopped; {Prefix} released")]
    private partial void LogStopped(string prefix);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dashboard listener failed to accept a request")]
    private partial void LogAcceptFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dashboard request aborted")]
    private partial void LogRequestAborted(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dashboard request failed unexpectedly")]
    private partial void LogRequestFailed(Exception exception);

    private sealed record StaticAsset(byte[] Content, string ContentType);

    private static class StatusCodes
    {
        internal const int Ok = 200;
        internal const int Forbidden = 403;
        internal const int NotFound = 404;
        internal const int MethodNotAllowed = 405;
    }
}

/// <summary>
/// Signals that the dashboard could not start, so the process must fail fast instead of running MCP without it.
/// </summary>
internal sealed class StageHttpServerStartupException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public StageHttpServerStartupException()
        : base("The Stage Director dashboard could not start.")
    {
    }

    /// <summary>Creates the exception with a presenter-readable diagnostic.</summary>
    /// <param name="message">The diagnostic written to stderr.</param>
    public StageHttpServerStartupException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a diagnostic and the underlying listener failure.</summary>
    /// <param name="message">The diagnostic written to stderr.</param>
    /// <param name="innerException">The original listener or socket failure.</param>
    public StageHttpServerStartupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
