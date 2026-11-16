using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PizzaOps.IntegrationTests;

[Collection("MCP endpoint")]
public sealed class McpReadToolsTests
{
    [McpFact]
    public async Task RealEndpointExposesReadToolsAndStableResults()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://127.0.0.1:7071/runtime/webhooks/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        });
        await using var client = await McpClient.CreateAsync(transport);
        Assert.Equal("2025-06-18", client.NegotiatedProtocolVersion);

        var tools = await client.ListToolsAsync();
        Assert.Equal(
            ["bake_test_pizza", "get_ovens_status", "get_pizza_recipe", "set_oven_temperature"],
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description)));
        Assert.Empty(await client.ListResourcesAsync());
        Assert.Empty(await client.ListResourceTemplatesAsync());
        Assert.Empty(await client.ListPromptsAsync());

        var bakeSchema = tools.Single(tool => tool.Name == "bake_test_pizza").JsonSchema;
        Assert.Equal("object", bakeSchema.GetProperty("type").GetString());
        var bakeProperties = bakeSchema.GetProperty("properties");
        Assert.Equal(2, bakeProperties.EnumerateObject().Count());
        Assert.Equal("string", bakeProperties.GetProperty("oven_id").GetProperty("type").GetString());
        Assert.Equal("string", bakeProperties.GetProperty("pizza_type").GetProperty("type").GetString());
        Assert.Equal(
            ["oven_id", "pizza_type"],
            bakeSchema.GetProperty("required").EnumerateArray()
                .Select(argument => argument.GetString()).Order(StringComparer.Ordinal));
        var statusSchema = tools.Single(tool => tool.Name == "get_ovens_status").JsonSchema;
        Assert.Equal("object", statusSchema.GetProperty("type").GetString());
        Assert.Empty(statusSchema.GetProperty("properties").EnumerateObject());
        Assert.Empty(statusSchema.GetProperty("required").EnumerateArray());

        var recipeSchema = tools.Single(tool => tool.Name == "get_pizza_recipe").JsonSchema;
        Assert.Equal("object", recipeSchema.GetProperty("type").GetString());
        Assert.Single(recipeSchema.GetProperty("properties").EnumerateObject());
        Assert.Equal("string", recipeSchema.GetProperty("properties").GetProperty("pizza_type").GetProperty("type").GetString());
        Assert.Equal("pizza_type", Assert.Single(recipeSchema.GetProperty("required").EnumerateArray()).GetString());

        var temperatureSchema = tools.Single(tool => tool.Name == "set_oven_temperature").JsonSchema;
        var temperatureProperties = temperatureSchema.GetProperty("properties");
        Assert.Equal(3, temperatureProperties.EnumerateObject().Count());
        Assert.Equal("string", temperatureProperties.GetProperty("oven_id").GetProperty("type").GetString());
        Assert.Equal("integer", temperatureProperties.GetProperty("target_temperature_c").GetProperty("type").GetString());
        Assert.False(temperatureProperties.GetProperty("target_temperature_c").TryGetProperty("minimum", out _));
        Assert.False(temperatureProperties.GetProperty("target_temperature_c").TryGetProperty("maximum", out _));
        Assert.Equal(
            ["oven_id", "target_temperature_c"],
            temperatureSchema.GetProperty("required").EnumerateArray()
                .Select(argument => argument.GetString())
                .Order(StringComparer.Ordinal));

        using var status = await CallAsync(client, "get_ovens_status", []);
        Assert.True(status.RootElement.GetProperty("success").GetBoolean());
        var ovens = status.RootElement.GetProperty("data").GetProperty("ovens").EnumerateArray().ToArray();
        Assert.Equal(["oven-01", "oven-02", "oven-03"], ovens.Select(oven => oven.GetProperty("oven_id").GetString()));
        Assert.Equal([320, 450, 300], ovens.Select(oven => oven.GetProperty("temperature_c").GetInt32()));
        Assert.Equal("CRITICAL", ovens[1].GetProperty("health").GetString());
        Assert.Equal(1, status.RootElement.GetProperty("data").GetProperty("summary").GetProperty("critical").GetInt32());

        using var recipe = await CallAsync(client, "get_pizza_recipe", new() { ["pizza_type"] = " MarGhEriTa " });
        var data = recipe.RootElement.GetProperty("data");
        Assert.Equal("margherita", data.GetProperty("pizza_type").GetString());
        Assert.Equal(320, data.GetProperty("target_temperature_c").GetInt32());
        Assert.Equal(15, data.GetProperty("tolerance_c").GetInt32());
        Assert.Equal(305, data.GetProperty("minimum_passing_temperature_c").GetInt32());
        Assert.Equal(335, data.GetProperty("maximum_passing_temperature_c").GetInt32());

        foreach (var (pizzaType, target) in new[] { ("marinara", 300), ("diavola", 310) })
        {
            using var otherRecipe = await CallAsync(client, "get_pizza_recipe", new() { ["pizza_type"] = pizzaType });
            Assert.Equal(target, otherRecipe.RootElement.GetProperty("data").GetProperty("target_temperature_c").GetInt32());
            Assert.Equal(15, otherRecipe.RootElement.GetProperty("data").GetProperty("tolerance_c").GetInt32());
        }

        using var unknown = await CallAsync(client, "get_pizza_recipe", new() { ["pizza_type"] = "unknown" });
        Assert.Equal("RECIPE_NOT_FOUND", unknown.RootElement.GetProperty("code").GetString());
        using var blank = await CallAsync(client, "get_pizza_recipe", new() { ["pizza_type"] = "  " });
        Assert.Equal("INVALID_ARGUMENT", blank.RootElement.GetProperty("code").GetString());
        using var wrongType = await CallAsync(client, "get_pizza_recipe", new() { ["pizza_type"] = 123 });
        Assert.Equal("INVALID_ARGUMENT", wrongType.RootElement.GetProperty("code").GetString());
        await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync("get_pizza_recipe", new Dictionary<string, object?>()));
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

    private sealed class McpFactAttribute : FactAttribute
    {
        public McpFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("PIZZAOPS_MCP_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Skip = "Set PIZZAOPS_MCP_TESTS=true after starting and seeding the local MCP host.";
            }
        }
    }
}
