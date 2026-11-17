using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PizzaOps.Core.Domain;
using PizzaOps.DemoAdmin;
using PizzaOps.Storage;
using PizzaOps.Storage.Persistence;

namespace PizzaOps.IntegrationTests;

[Collection("MCP endpoint")]
public sealed class McpTemperatureToolTests
{
    [McpFact]
    public async Task RealEndpointBakesDeterministicallyWithoutChangingOvensAndReportsEventFailure()
    {
        var reset = CreateResetService();
        await reset.ResetAsync(CancellationToken.None);
        var tableService = new TableServiceClient("UseDevelopmentStorage=true");
        var ovensTable = tableService.GetTableClient("PizzaOpsOvens");
        var eventsTable = tableService.GetTableClient("PizzaOpsEvents");

        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://127.0.0.1:7071/runtime/webhooks/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp
            });
            await using var client = await McpClient.CreateAsync(transport);

            using var recipe = await CallAsync(client, "get_pizza_recipe", new() { ["pizza_type"] = "margherita" });
            Assert.Equal(320, recipe.RootElement.GetProperty("data").GetProperty("target_temperature_c").GetInt32());
            var initial = (await ovensTable.GetEntityAsync<TableEntity>("main-kitchen", "oven-02")).Value;
            using var burnt = await CallBakeAsync(client, "BURNT", 450);
            AssertOvenUnchanged(initial, (await ovensTable.GetEntityAsync<TableEntity>("main-kitchen", "oven-02")).Value);

            foreach (var (temperature, outcome) in new[]
            {
                (304, "UNDERCOOKED"), (305, "PASS"), (335, "PASS"), (336, "BURNT"), (320, "PASS")
            })
            {
                using var changed = await CallAsync(
                    client,
                    "set_oven_temperature",
                    new() { ["oven_id"] = "oven-02", ["target_temperature_c"] = temperature });
                Assert.True(changed.RootElement.GetProperty("success").GetBoolean());
                var before = (await ovensTable.GetEntityAsync<TableEntity>("main-kitchen", "oven-02")).Value;
                using var bake = await CallBakeAsync(client, outcome, temperature);
                AssertOvenUnchanged(before, (await ovensTable.GetEntityAsync<TableEntity>("main-kitchen", "oven-02")).Value);
            }

            var repaired = (await ovensTable.GetEntityAsync<TableEntity>("main-kitchen", "oven-02")).Value;
            using var repeated = await CallBakeAsync(client, "PASS", 320);

            using var missingOven = await CallAsync(
                client, "bake_test_pizza", new() { ["oven_id"] = "oven-99", ["pizza_type"] = "margherita" });
            Assert.False(missingOven.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("OVEN_NOT_FOUND", missingOven.RootElement.GetProperty("code").GetString());
            using var missingRecipe = await CallAsync(
                client, "bake_test_pizza", new() { ["oven_id"] = "oven-02", ["pizza_type"] = "unknown" });
            Assert.False(missingRecipe.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("RECIPE_NOT_FOUND", missingRecipe.RootElement.GetProperty("code").GetString());

            foreach (var arguments in new Dictionary<string, object?>[]
            {
                [],
                new() { ["oven_id"] = "oven-02" },
                new() { ["pizza_type"] = "margherita" },
                new() { ["oven_id"] = null, ["pizza_type"] = "margherita" },
                new() { ["oven_id"] = "oven-02", ["pizza_type"] = null },
                new() { ["oven_id"] = " ", ["pizza_type"] = "margherita" },
                new() { ["oven_id"] = "oven-02", ["pizza_type"] = " " },
                new() { ["oven_id"] = 2, ["pizza_type"] = "margherita" },
                new() { ["oven_id"] = "oven-02", ["pizza_type"] = 123 },
                new() { ["oven_id"] = "oven-02", ["pizza_type"] = "margherita", ["temperature_c"] = 450 }
            })
            {
                await AssertCallFailsSafelyAsync(client, "bake_test_pizza", arguments);
            }

            var events = await CreateRepository().GetLatestAsync(20, CancellationToken.None);
            var bakes = events.Where(item => item.EventType == KitchenEventType.TestBake).ToArray();
            Assert.Equal(7, bakes.Length);
            Assert.Equal(7, bakes.Select(item => item.EventId).Distinct(StringComparer.Ordinal).Count());
            Assert.Contains(bakes, item =>
                item.Outcome == KitchenEventOutcome.Fail &&
                item.Summary.Contains("BURNT", StringComparison.Ordinal) &&
                item.Summary.Contains("450 C", StringComparison.Ordinal) &&
                item.Summary.Contains("305-335 C", StringComparison.Ordinal));
            Assert.Contains(bakes, item => item.Outcome == KitchenEventOutcome.Pass);

            using var status = await CallAsync(client, "get_ovens_status", []);
            Assert.Equal(320, status.RootElement.GetProperty("data").GetProperty("ovens")
                .EnumerateArray().Single(item => item.GetProperty("oven_id").GetString() == "oven-02")
                .GetProperty("temperature_c").GetInt32());

            await eventsTable.DeleteAsync();
            using var eventFailure = await CallAsync(
                client, "bake_test_pizza", new() { ["oven_id"] = "oven-02", ["pizza_type"] = "margherita" });
            var failureRoot = eventFailure.RootElement;
            Assert.True(failureRoot.GetProperty("success").GetBoolean());
            Assert.Equal("TEST_COMPLETED", failureRoot.GetProperty("code").GetString());
            Assert.Equal("PASS", failureRoot.GetProperty("data").GetProperty("result").GetString());
            Assert.False(failureRoot.GetProperty("event_recorded").GetBoolean());
            Assert.StartsWith(
                "EVENT_WRITE_FAILED:",
                Assert.Single(failureRoot.GetProperty("warnings").EnumerateArray()).GetString(),
                StringComparison.Ordinal);
            AssertOvenUnchanged(repaired, (await ovensTable.GetEntityAsync<TableEntity>("main-kitchen", "oven-02")).Value);
        }
        finally
        {
            await reset.ResetAsync(CancellationToken.None);
        }
    }

    [McpFact]
    public async Task RealEndpointChangesRejectsNoOpsAndSafelyRejectsMalformedArguments()
    {
        var reset = CreateResetService();
        await reset.ResetAsync(CancellationToken.None);

        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://127.0.0.1:7071/runtime/webhooks/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp
            });
            await using var client = await McpClient.CreateAsync(transport);
            Assert.Equal("2025-06-18", client.NegotiatedProtocolVersion);

            var tools = await client.ListToolsAsync();
            Assert.Contains(tools, tool => tool.Name == "set_oven_temperature");

            using var changed = await CallAsync(
                client,
                "set_oven_temperature",
                new()
                {
                    ["oven_id"] = "oven-02",
                    ["target_temperature_c"] = 320,
                    ["reason"] = "operator repair"
                });
            var changedRoot = changed.RootElement;
            Assert.True(changedRoot.GetProperty("success").GetBoolean());
            Assert.Equal("TEMPERATURE_UPDATED", changedRoot.GetProperty("code").GetString());
            Assert.True(changedRoot.GetProperty("event_recorded").GetBoolean());
            var changedData = changedRoot.GetProperty("data");
            Assert.Equal(450, changedData.GetProperty("previous_temperature_c").GetInt32());
            Assert.Equal(320, changedData.GetProperty("current_temperature_c").GetInt32());
            Assert.Equal("HEALTHY", changedData.GetProperty("health").GetString());
            Assert.True(changedData.GetProperty("state_changed").GetBoolean());
            Assert.True(changedData.GetProperty("updated_at_utc").GetDateTimeOffset() > DateTimeOffset.UnixEpoch);

            using var rejected = await CallAsync(
                client,
                "set_oven_temperature",
                new()
                {
                    ["oven_id"] = "oven-02",
                    ["target_temperature_c"] = 600
                });
            var rejectedRoot = rejected.RootElement;
            Assert.False(rejectedRoot.GetProperty("success").GetBoolean());
            Assert.Equal("TEMPERATURE_OUT_OF_RANGE", rejectedRoot.GetProperty("code").GetString());
            Assert.True(rejectedRoot.GetProperty("event_recorded").GetBoolean());
            Assert.Equal(320, rejectedRoot.GetProperty("data").GetProperty("current_temperature_c").GetInt32());
            Assert.False(rejectedRoot.GetProperty("data").GetProperty("state_changed").GetBoolean());

            using var noChange = await CallAsync(
                client,
                "set_oven_temperature",
                new()
                {
                    ["oven_id"] = "oven-02",
                    ["target_temperature_c"] = 320
                });
            Assert.Equal("NO_CHANGE", noChange.RootElement.GetProperty("code").GetString());
            Assert.False(noChange.RootElement.GetProperty("data").GetProperty("state_changed").GetBoolean());
            Assert.False(noChange.RootElement.TryGetProperty("event_recorded", out _));

            await AssertCallFailsSafelyAsync(
                client,
                "set_oven_temperature",
                new() { ["oven_id"] = " ", ["target_temperature_c"] = 320 });
            await AssertCallFailsSafelyAsync(
                client,
                "set_oven_temperature",
                new() { ["oven_id"] = "oven-99", ["target_temperature_c"] = 320 });
            await AssertCallFailsSafelyAsync(
                client,
                "set_oven_temperature",
                new() { ["oven_id"] = "oven-02", ["target_temperature_c"] = 320.5 });
            await AssertCallFailsSafelyAsync(
                client,
                "set_oven_temperature",
                new() { ["oven_id"] = "oven-02", ["target_temperature_c"] = 2147483648L });
            await AssertCallFailsSafelyAsync(
                client,
                "set_oven_temperature",
                new() { ["oven_id"] = "oven-02" });
            await AssertCallFailsSafelyAsync(
                client,
                "set_oven_temperature",
                new() { ["target_temperature_c"] = 320 });
            await AssertCallFailsSafelyAsync(
                client,
                "set_oven_temperature",
                new()
                {
                    ["oven_id"] = "oven-02",
                    ["target_temperature_c"] = 320,
                    ["display_name"] = "unexpected mutation"
                });
            await AssertCallFailsSafelyAsync(
                client,
                "set_oven_temperature",
                new()
                {
                    ["oven_id"] = "oven-02",
                    ["target_temperature_c"] = 320,
                    ["reason"] = new string('x', 121)
                });

            using var status = await CallAsync(client, "get_ovens_status", []);
            var oven = status.RootElement.GetProperty("data").GetProperty("ovens")
                .EnumerateArray()
                .Single(item => item.GetProperty("oven_id").GetString() == "oven-02");
            Assert.Equal(320, oven.GetProperty("temperature_c").GetInt32());
            Assert.Equal("HEALTHY", oven.GetProperty("health").GetString());

            var events = await CreateRepository().GetLatestAsync(20, CancellationToken.None);
            Assert.Single(events, item => item.EventType == KitchenEventType.TemperatureChanged);
            Assert.Single(events, item => item.EventType == KitchenEventType.TemperatureRejected);
        }
        finally
        {
            await reset.ResetAsync(CancellationToken.None);
        }
    }

    private static async Task<JsonDocument> CallBakeAsync(McpClient client, string outcome, int observedTemperatureC)
    {
        var result = await CallAsync(
            client, "bake_test_pizza", new() { ["oven_id"] = " OVEN-02 ", ["pizza_type"] = " MarGhEriTa " });
        var root = result.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("TEST_COMPLETED", root.GetProperty("code").GetString());
        Assert.True(root.GetProperty("event_recorded").GetBoolean());
        Assert.Empty(root.GetProperty("warnings").EnumerateArray());
        var data = root.GetProperty("data");
        Assert.Equal(outcome, data.GetProperty("result").GetString());
        Assert.Equal(observedTemperatureC, data.GetProperty("oven_temperature_c").GetInt32());
        Assert.Equal("oven-02", data.GetProperty("oven_id").GetString());
        Assert.Equal("margherita", data.GetProperty("pizza_type").GetString());
        Assert.Equal(320, data.GetProperty("target_temperature_c").GetInt32());
        Assert.Equal(15, data.GetProperty("tolerance_c").GetInt32());
        Assert.Equal(305, data.GetProperty("minimum_passing_temperature_c").GetInt32());
        Assert.Equal(335, data.GetProperty("maximum_passing_temperature_c").GetInt32());
        return result;
    }

    private static void AssertOvenUnchanged(TableEntity before, TableEntity after)
    {
        Assert.Equal(before.ETag, after.ETag);
        Assert.Equal(before.Timestamp, after.Timestamp);
        Assert.Equal(
            before.OrderBy(item => item.Key, StringComparer.Ordinal),
            after.OrderBy(item => item.Key, StringComparer.Ordinal));
    }

    private static KitchenResetService CreateResetService()
    {
        var repository = CreateRepository();
        return new KitchenResetService(repository, repository, repository, TimeProvider.System);
    }

    private static TableKitchenRepository CreateRepository()
    {
        var tableOptions = new PizzaOpsTableOptions
        {
            Mode = "Azurite",
            AzuriteConnectionString = "UseDevelopmentStorage=true"
        };
        var tableServiceClient = new TableServiceClient("UseDevelopmentStorage=true");
        return new TableKitchenRepository(tableServiceClient, Options.Create(tableOptions));
    }

    private static async Task<JsonDocument> CallAsync(
        McpClient client,
        string name,
        Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(name, arguments);
        Assert.NotEqual(true, result.IsError);
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        return JsonDocument.Parse(content.Text);
    }

    private static async Task AssertCallFailsSafelyAsync(
        McpClient client,
        string name,
        Dictionary<string, object?> arguments)
    {
        try
        {
            var result = await client.CallToolAsync(name, arguments);
            if (result.IsError == true)
            {
                return;
            }

            var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
            using var document = JsonDocument.Parse(content.Text);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        }
        catch (McpProtocolException)
        {
        }
    }

    private sealed class McpFactAttribute : FactAttribute
    {
        public McpFactAttribute()
        {
            if (!string.Equals(
                Environment.GetEnvironmentVariable("PIZZAOPS_MCP_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
            {
                Skip = "Set PIZZAOPS_MCP_TESTS=true after starting and seeding the local MCP host.";
            }
        }
    }
}
