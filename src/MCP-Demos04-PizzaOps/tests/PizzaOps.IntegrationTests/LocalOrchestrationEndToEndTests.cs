using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PizzaOps.Core.Domain;
using PizzaOps.DemoAdmin;
using PizzaOps.Storage;
using PizzaOps.Storage.Persistence;
using Xunit.Abstractions;

namespace PizzaOps.IntegrationTests;

[Collection("MCP endpoint")]
public sealed class LocalOrchestrationEndToEndTests(ITestOutputHelper output)
{
    [LocalE2EFact]
    public async Task RealMcpStorageAndDashboardCompleteStoryTwiceAndReportStorageFaults()
    {
        var settings = LocalE2ESettings.Read();
        var options = new PizzaOpsTableOptions
        {
            Mode = "Azurite",
            AzuriteConnectionString = settings.ConnectionString,
            OvensTableName = settings.OvensTableName,
            EventsTableName = settings.EventsTableName,
            PartitionKey = settings.PartitionKey
        };
        var tableService = new TableServiceClient(settings.ConnectionString);
        var ovensTable = tableService.GetTableClient(settings.OvensTableName);
        var eventsTable = tableService.GetTableClient(settings.EventsTableName);
        var repository = new TableKitchenRepository(tableService, Options.Create(options));
        var reset = new KitchenResetService(repository, repository, repository, TimeProvider.System);
        using var dashboard = new HttpClient
        {
            BaseAddress = new Uri(settings.DashboardUrl),
            Timeout = TimeSpan.FromSeconds(8)
        };
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "msedge",
            Headless = true,
            Args =
            [
                "--disable-background-networking",
                "--disable-component-update",
                "--disable-default-apps",
                "--disable-extensions",
                "--no-first-run"
            ]
        });
        var page = await browser.NewPageAsync(new()
        {
            ViewportSize = new() { Width = 1920, Height = 1080 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(settings.McpEndpoint),
            TransportMode = HttpTransportMode.StreamableHttp
        });
        await using var mcp = await McpClient.CreateAsync(transport);

        for (var iteration = 1; iteration <= 2; iteration++)
        {
            await reset.ResetAsync(CancellationToken.None);
            await AssertDiscoveryAsync(mcp);
            await AssertResetStatusAsync(mcp, dashboard);
            if (iteration == 1)
            {
                await page.GotoAsync(settings.DashboardUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });
                await WaitForDashboardTextAsync(page, "LIVE / CONNECTED");
            }
            await AssertResetDisplayAsync(page);

            using var recipe = await CallAsync(
                mcp,
                "get_pizza_recipe",
                new() { ["pizza_type"] = " MarGhEriTa " });
            var recipeData = recipe.RootElement.GetProperty("data");
            Assert.Equal("margherita", recipeData.GetProperty("pizza_type").GetString());
            Assert.Equal(320, recipeData.GetProperty("target_temperature_c").GetInt32());
            Assert.Equal(15, recipeData.GetProperty("tolerance_c").GetInt32());
            Assert.Equal(305, recipeData.GetProperty("minimum_passing_temperature_c").GetInt32());
            Assert.Equal(335, recipeData.GetProperty("maximum_passing_temperature_c").GetInt32());

            var repairUiTimer = Stopwatch.StartNew();
            using var repair = await CallAsync(
                mcp,
                "set_oven_temperature",
                new()
                {
                    ["oven_id"] = "oven-02",
                    ["target_temperature_c"] = 320,
                    ["reason"] = "operator repair"
                });
            var repairRoot = repair.RootElement;
            Assert.True(repairRoot.GetProperty("success").GetBoolean());
            Assert.Equal("TEMPERATURE_UPDATED", repairRoot.GetProperty("code").GetString());
            Assert.True(repairRoot.GetProperty("event_recorded").GetBoolean());
            Assert.Equal(450, repairRoot.GetProperty("data").GetProperty("previous_temperature_c").GetInt32());
            Assert.Equal(320, repairRoot.GetProperty("data").GetProperty("current_temperature_c").GetInt32());
            await AssertPersistedCorrelationAsync(
                repository,
                KitchenEventType.TemperatureChanged,
                repairRoot.GetProperty("correlation_id").GetString()!);
            await AssertHostJsonLogAsync(
                settings.FunctionsLogDirectory,
                repairRoot.GetProperty("correlation_id").GetString()!,
                "TEMPERATURE_UPDATED");

            var repairLatency = await WaitForDashboardAsync(
                dashboard,
                state => OvenTemperature(state, "oven-02") == 320 &&
                    HasEvent(state, "TEMPERATURE_CHANGED"),
                TimeSpan.FromSeconds(3));
            output.WriteLine(
                $"Iteration {iteration}: post-repair API observation took {repairLatency.TotalMilliseconds:F0} ms.");
            await WaitForDashboardTextAsync(page, "TEMPERATURE_CHANGED");
            Assert.Equal("320°C", await page.Locator(".oven").Nth(1).Locator(".temperature").InnerTextAsync());
            var repairUiLatency = repairUiTimer.Elapsed;
            Assert.InRange(repairUiLatency, TimeSpan.Zero, TimeSpan.FromSeconds(3));
            output.WriteLine(
                $"Iteration {iteration}: repair-to-Blazor DOM latency was {repairUiLatency.TotalMilliseconds:F0} ms.");

            var bakeUiTimer = Stopwatch.StartNew();
            using var bake = await CallAsync(
                mcp,
                "bake_test_pizza",
                new() { ["oven_id"] = "oven-02", ["pizza_type"] = "margherita" });
            var bakeRoot = bake.RootElement;
            Assert.True(bakeRoot.GetProperty("success").GetBoolean());
            Assert.Equal("TEST_COMPLETED", bakeRoot.GetProperty("code").GetString());
            Assert.True(bakeRoot.GetProperty("event_recorded").GetBoolean());
            Assert.Equal("PASS", bakeRoot.GetProperty("data").GetProperty("result").GetString());
            Assert.Equal(320, bakeRoot.GetProperty("data").GetProperty("oven_temperature_c").GetInt32());
            await AssertPersistedCorrelationAsync(
                repository,
                KitchenEventType.TestBake,
                bakeRoot.GetProperty("correlation_id").GetString()!);
            await AssertHostJsonLogAsync(
                settings.FunctionsLogDirectory,
                bakeRoot.GetProperty("correlation_id").GetString()!,
                "TEST_COMPLETED");
            await WaitForDashboardTextAsync(page, "TEST_BAKE");
            await WaitForDashboardTextAsync(page, "PASS");
            Assert.InRange(bakeUiTimer.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(3));
            output.WriteLine($"Iteration {iteration}: bake-to-Blazor DOM latency was {bakeUiTimer.Elapsed.TotalMilliseconds:F0} ms.");

            var beforeReject = await ovensTable.GetEntityAsync<TableEntity>(
                settings.PartitionKey,
                "oven-02");
            var rejectUiTimer = Stopwatch.StartNew();
            using var rejected = await CallAsync(
                mcp,
                "set_oven_temperature",
                new() { ["oven_id"] = "oven-02", ["target_temperature_c"] = 600 });
            var rejectedRoot = rejected.RootElement;
            Assert.False(rejectedRoot.GetProperty("success").GetBoolean());
            Assert.Equal("TEMPERATURE_OUT_OF_RANGE", rejectedRoot.GetProperty("code").GetString());
            Assert.True(rejectedRoot.GetProperty("event_recorded").GetBoolean());
            Assert.Equal(320, rejectedRoot.GetProperty("data").GetProperty("current_temperature_c").GetInt32());
            Assert.False(rejectedRoot.GetProperty("data").GetProperty("state_changed").GetBoolean());
            await AssertPersistedCorrelationAsync(
                repository,
                KitchenEventType.TemperatureRejected,
                rejectedRoot.GetProperty("correlation_id").GetString()!);
            await AssertHostJsonLogAsync(
                settings.FunctionsLogDirectory,
                rejectedRoot.GetProperty("correlation_id").GetString()!,
                "TEMPERATURE_OUT_OF_RANGE");

            var afterReject = await ovensTable.GetEntityAsync<TableEntity>(
                settings.PartitionKey,
                "oven-02");
            AssertEntityUnchanged(beforeReject.Value, afterReject.Value);
            await WaitForDashboardTextAsync(page, "TEMPERATURE_REJECTED");
            Assert.Equal("320°C", await page.Locator(".oven").Nth(1).Locator(".temperature").InnerTextAsync());
            var rejectUiLatency = rejectUiTimer.Elapsed;
            Assert.InRange(rejectUiLatency, TimeSpan.Zero, TimeSpan.FromSeconds(3));
            output.WriteLine(
                $"Iteration {iteration}: rejection-to-Blazor DOM latency was {rejectUiLatency.TotalMilliseconds:F0} ms.");

            var storyLatency = await WaitForDashboardAsync(
                dashboard,
                state => OvenTemperature(state, "oven-02") == 320 &&
                    HasEvent(state, "TEST_BAKE") &&
                    HasEvent(state, "TEMPERATURE_REJECTED"),
                TimeSpan.FromSeconds(3));
            output.WriteLine(
                $"Iteration {iteration}: post-story API observation took {storyLatency.TotalMilliseconds:F0} ms.");
        }

        var beforeInvalid = (await ovensTable.GetEntityAsync<TableEntity>(settings.PartitionKey, "oven-02")).Value;
        const string untrustedMarker = "UNTRUSTED_ARGUMENT_NOT_FOR_LOGGING";
        foreach (var arguments in new Dictionary<string, object?>[]
        {
            new() { ["oven_id"] = " ", ["target_temperature_c"] = 320 },
            new() { ["oven_id"] = "oven-02", ["target_temperature_c"] = 320, ["reason"] = new string('x', 121) },
            new() { ["oven_id"] = "oven-02", ["target_temperature_c"] = 320, ["display_name"] = untrustedMarker }
        })
        {
            using var invalid = await CallAsync(mcp, "set_oven_temperature", arguments);
            Assert.False(invalid.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("INVALID_ARGUMENT", invalid.RootElement.GetProperty("code").GetString());
        }
        AssertEntityUnchanged(beforeInvalid,
            (await ovensTable.GetEntityAsync<TableEntity>(settings.PartitionKey, "oven-02")).Value);
        foreach (var logPath in Directory.EnumerateFiles(settings.FunctionsLogDirectory, "functions.*.log"))
        {
            using var reader = new StreamReader(new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            Assert.DoesNotContain(untrustedMarker, await reader.ReadToEndAsync(), StringComparison.Ordinal);
        }

        var degradedTimer = Stopwatch.StartNew();
        await eventsTable.DeleteAsync();
        using var eventFailure = await CallAsync(
            mcp,
            "set_oven_temperature",
            new() { ["oven_id"] = "oven-02", ["target_temperature_c"] = 330 });
        Assert.True(eventFailure.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("TEMPERATURE_UPDATED", eventFailure.RootElement.GetProperty("code").GetString());
        Assert.True(eventFailure.RootElement.GetProperty("data").GetProperty("state_changed").GetBoolean());
        Assert.Equal(330, (await ovensTable.GetEntityAsync<TableEntity>(settings.PartitionKey, "oven-02")).Value.GetInt32("TemperatureC"));
        Assert.False(eventFailure.RootElement.GetProperty("event_recorded").GetBoolean());
        Assert.StartsWith(
            "EVENT_WRITE_FAILED:",
            Assert.Single(eventFailure.RootElement.GetProperty("warnings").EnumerateArray()).GetString(),
            StringComparison.Ordinal);
        await AssertHostJsonLogAsync(settings.FunctionsLogDirectory,
            eventFailure.RootElement.GetProperty("correlation_id").GetString()!, "TEMPERATURE_UPDATED", "EVENT_WRITE_FAILED");

        using (var degraded = await dashboard.GetAsync("/api/kitchen/state"))
        {
            Assert.Equal(HttpStatusCode.OK, degraded.StatusCode);
            using var body = await degraded.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.NotNull(body);
            Assert.False(body.RootElement.GetProperty("event_feed_available").GetBoolean());
            Assert.Equal(3, body.RootElement.GetProperty("ovens").GetArrayLength());
            AssertDashboardCorrelation(body.RootElement);
        }

        await WaitForDashboardTextAsync(page, "CONNECTED / DEGRADED");
        await WaitForDashboardTextAsync(page, "DEGRADED / EVENTS UNAVAILABLE");
        Assert.Equal("330°C", await page.Locator(".oven").Nth(1).Locator(".temperature").InnerTextAsync());
        var degradedLatency = degradedTimer.Elapsed;
        Assert.InRange(degradedLatency, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        output.WriteLine(
            $"Event-feed failure-to-Blazor degraded display latency was {degradedLatency.TotalMilliseconds:F0} ms.");

        await reset.ResetAsync(CancellationToken.None);
        await AssertResetDisplayAsync(page);
        var staleTimer = Stopwatch.StartNew();
        await ovensTable.DeleteAsync();
        using var toolOutage = await CallAsync(mcp, "get_ovens_status", []);
        Assert.False(toolOutage.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("STORAGE_UNAVAILABLE", toolOutage.RootElement.GetProperty("code").GetString());
        using (var unavailable = await dashboard.GetAsync("/api/kitchen/state"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            using var body = await unavailable.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.NotNull(body);
            Assert.Equal("STORAGE_UNAVAILABLE", body.RootElement.GetProperty("code").GetString());
        }

        await WaitForDashboardTextAsync(page, "STALE / NOT LIVE");
        await WaitForDashboardTextAsync(page, "STALE / READ FAILED");
        Assert.Equal("450°C", await page.Locator(".oven").Nth(1).Locator(".temperature").InnerTextAsync());
        var staleLatency = staleTimer.Elapsed;
        Assert.InRange(staleLatency, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        output.WriteLine(
            $"Oven-state outage-to-Blazor stale display latency was {staleLatency.TotalMilliseconds:F0} ms.");

        await reset.ResetAsync(CancellationToken.None);
        await AssertResetStatusAsync(mcp, dashboard);
        await AssertResetDisplayAsync(page);
    }

    private static async Task AssertResetDisplayAsync(IPage page)
    {
        await WaitForDashboardTextAsync(page, "LIVE / CONNECTED");
        await Expect(page.Locator(".oven").Nth(1).Locator(".temperature"))
            .ToHaveTextAsync("450°C", new() { Timeout = 3000 });
        await Expect(page.Locator(".event-feed li")).ToHaveCountAsync(1, new() { Timeout = 3000 });
        await WaitForDashboardTextAsync(page, "RESET");
    }

    private static async Task AssertDiscoveryAsync(McpClient client)
    {
        Assert.Equal("2025-06-18", client.NegotiatedProtocolVersion);
        var tools = await client.ListToolsAsync();
        Assert.Equal(
            ["bake_test_pizza", "get_ovens_status", "get_pizza_recipe", "set_oven_temperature"],
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description)));
    }

    private static async Task AssertResetStatusAsync(McpClient client, HttpClient dashboard)
    {
        using var status = await CallAsync(client, "get_ovens_status", []);
        Assert.True(status.RootElement.GetProperty("success").GetBoolean());
        var ovens = status.RootElement.GetProperty("data").GetProperty("ovens").EnumerateArray().ToArray();
        Assert.Equal(["oven-01", "oven-02", "oven-03"], ovens.Select(oven => oven.GetProperty("oven_id").GetString()));
        Assert.Equal([320, 450, 300], ovens.Select(oven => oven.GetProperty("temperature_c").GetInt32()));

        using var state = await dashboard.GetAsync("/api/kitchen/state");
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
        using var body = await state.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(body);
        Assert.True(body.RootElement.GetProperty("event_feed_available").GetBoolean());
        Assert.Equal([320, 450, 300], body.RootElement.GetProperty("ovens").EnumerateArray()
            .Select(oven => oven.GetProperty("temperature_c").GetInt32()));
        Assert.Equal("RESET", Assert.Single(body.RootElement.GetProperty("events").EnumerateArray())
            .GetProperty("event_type").GetString());
        AssertDashboardCorrelation(body.RootElement);
    }

    private static async Task<TimeSpan> WaitForDashboardAsync(
        HttpClient dashboard,
        Func<JsonElement, bool> condition,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            using var response = await dashboard.GetAsync("/api/kitchen/state", cancellation.Token);
            if (response.IsSuccessStatusCode)
            {
                using var state = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellation.Token);
                if (state is not null && condition(state.RootElement))
                {
                    AssertDashboardCorrelation(state.RootElement);
                    return timer.Elapsed;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellation.Token);
        }
    }

    private static Task WaitForDashboardTextAsync(IPage page, string text) =>
        page.GetByText(text, new PageGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = (float)TimeSpan.FromSeconds(3).TotalMilliseconds
            });

    private static int OvenTemperature(JsonElement state, string ovenId) =>
        state.GetProperty("ovens").EnumerateArray()
            .Single(oven => oven.GetProperty("oven_id").GetString() == ovenId)
            .GetProperty("temperature_c").GetInt32();

    private static bool HasEvent(JsonElement state, string eventType) =>
        state.GetProperty("events").EnumerateArray()
            .Any(item => item.GetProperty("event_type").GetString() == eventType);

    private static void AssertEntityUnchanged(TableEntity before, TableEntity after)
    {
        Assert.Equal(before.ETag, after.ETag);
        Assert.Equal(before.Timestamp, after.Timestamp);
        Assert.Equal(
            before.Keys.Order(StringComparer.Ordinal),
            after.Keys.Order(StringComparer.Ordinal));
        foreach (var (property, value) in before)
        {
            Assert.Equal(value, after[property]);
        }
    }

    private static async Task AssertPersistedCorrelationAsync(
        TableKitchenRepository repository,
        KitchenEventType eventType,
        string correlationId)
    {
        var storedEvent = Assert.Single(
            await repository.GetLatestAsync(20, CancellationToken.None),
            item => item.EventType == eventType);
        Assert.Equal(correlationId, storedEvent.CorrelationId);
    }

    private static void AssertDashboardCorrelation(JsonElement state)
    {
        var correlationId = state.GetProperty("correlation_id").GetString();
        Assert.NotNull(correlationId);
        Assert.Equal(32, correlationId.Length);
        Assert.All(correlationId, character => Assert.True(Uri.IsHexDigit(character)));
        Assert.All(state.GetProperty("events").EnumerateArray(), item =>
            Assert.False(item.TryGetProperty("correlation_id", out _)));
    }

    private static async Task AssertHostJsonLogAsync(
        string logDirectory,
        string correlationId,
        string expectedCode,
        string? warningCode = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observedOutcomes = new HashSet<string>(StringComparer.Ordinal);
        var observedReadErrors = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            if (timeout.IsCancellationRequested)
            {
                throw new Xunit.Sdk.XunitException(
                    $"No owned Functions JSON log matched code '{expectedCode}' and correlation '{correlationId}'. " +
                    $"Observed structured outcomes: {string.Join(", ", observedOutcomes)}. " +
                    $"Log read errors: {string.Join(", ", observedReadErrors)}");
            }

            try
            {
                if (Directory.Exists(logDirectory))
                {
                    foreach (var logPath in Directory.EnumerateFiles(logDirectory, "functions.*.log"))
                    {
                        await using var logFile = new FileStream(
                            logPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        using var reader = new StreamReader(logFile);
                        string? line;
                        while ((line = await reader.ReadLineAsync(timeout.Token)) is not null)
                        {
                            var jsonStart = line.IndexOf('{');
                            if (jsonStart < 0)
                            {
                                continue;
                            }

                            try
                            {
                                using var entry = JsonDocument.Parse(line.AsMemory(jsonStart));
                                var loggedCorrelationId = FindStringField(entry.RootElement, "correlation_id");
                                var loggedCode = FindStringField(entry.RootElement, "code");
                                if (loggedCorrelationId is not null && loggedCode is not null)
                                {
                                    observedOutcomes.Add($"{loggedCode}/{loggedCorrelationId}");
                                    if (loggedCorrelationId == correlationId && loggedCode == expectedCode &&
                                        (warningCode is null || FindStringField(entry.RootElement, "warning_codes") == warningCode))
                                    {
                                        return;
                                    }
                                }
                            }
                            catch (JsonException)
                            {
                            }
                        }
                    }
                }
            }
            catch (IOException exception)
            {
                observedReadErrors.Add(exception.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
            }
        }
    }

    private static string? FindStringField(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (NormalizeFieldName(property.Name) == NormalizeFieldName(fieldName) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = FindStringField(property.Value, fieldName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindStringField(item, fieldName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string NormalizeFieldName(string fieldName) =>
        fieldName.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static async Task<JsonDocument> CallAsync(
        McpClient client,
        string name,
        Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(name, arguments);
        Assert.NotEqual(true, result.IsError);
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        var response = JsonDocument.Parse(content.Text);
        var correlationId = response.RootElement.GetProperty("correlation_id").GetString();
        Assert.NotNull(correlationId);
        Assert.Equal(32, correlationId.Length);
        Assert.All(correlationId, character => Assert.True(Uri.IsHexDigit(character)));
        return response;
    }

    private sealed class LocalE2EFactAttribute : FactAttribute
    {
        public LocalE2EFactAttribute()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("PIZZAOPS_LOCAL_E2E_TESTS"),
                    "true",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    Environment.GetEnvironmentVariable("PIZZAOPS_LOCAL_E2E_ENVIRONMENT"),
                    "PizzaOps-Local-E2E",
                    StringComparison.Ordinal))
            {
                Skip = "Run scripts/Test-Local.ps1 to opt into the isolated PizzaOps-Local-E2E environment.";
            }
        }
    }

    private sealed record LocalE2ESettings(
        string ConnectionString,
        string McpEndpoint,
        string DashboardUrl,
        string FunctionsLogDirectory,
        string OvensTableName,
        string EventsTableName,
        string PartitionKey)
    {
        public static LocalE2ESettings Read()
        {
            var connectionString = GetRequired("PIZZAOPS_AZURITE_CONNECTION_STRING");
            var endpoint = GetRequired("PIZZAOPS_MCP_ENDPOINT");
            var dashboardUrl = GetRequired("PIZZAOPS_DASHBOARD_URL");
            var functionsLogDirectory = GetRequired("PIZZAOPS_E2E_FUNCTIONS_LOG_DIRECTORY");
            var ovensTableName = GetRequired("PIZZAOPS_E2E_OVENS_TABLE");
            var eventsTableName = GetRequired("PIZZAOPS_E2E_EVENTS_TABLE");
            var partitionKey = GetRequired("PIZZAOPS_E2E_PARTITION");

            if (connectionString != "UseDevelopmentStorage=true" ||
                !IsLoopbackHttp(endpoint) ||
                !IsLoopbackHttp(dashboardUrl) ||
                !ovensTableName.StartsWith("PizzaOvens", StringComparison.Ordinal) ||
                !eventsTableName.StartsWith("PizzaEvents", StringComparison.Ordinal) ||
                !partitionKey.StartsWith("local-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Local E2E settings must use Azurite, loopback HTTP endpoints, and run-unique PizzaOps tables.");
            }

            return new LocalE2ESettings(
                connectionString,
                endpoint,
                dashboardUrl,
                functionsLogDirectory,
                ovensTableName,
                eventsTableName,
                partitionKey);
        }

        private static string GetRequired(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Required local E2E setting '{name}' is missing.");

        private static bool IsLoopbackHttp(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttp &&
            uri.Host == "127.0.0.1";
    }
}
