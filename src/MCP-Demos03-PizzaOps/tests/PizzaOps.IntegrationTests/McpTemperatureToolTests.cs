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
