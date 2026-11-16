using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;

namespace PizzaOps.Functions;

/// <summary>MCP adapter for deterministic test bakes.</summary>
public sealed class BakeTestPizzaTool(IBakeTestService bakes)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Tests the observed oven temperature without modifying the oven.</summary>
    [Function(nameof(BakeTestPizza))]
    public async Task<string> BakeTestPizza(
        [McpToolTrigger(
            "bake_test_pizza",
            "Runs an immediate deterministic simulated bake using the current persisted oven temperature and an immutable PizzaOps recipe. Returns PASS, UNDERCOOKED, or BURNT as a completed test, not a tool failure. Does not change the oven or create an order, but appends a TEST_BAKE event; repeated calls may create separate events.")]
        ToolInvocationContext context,
        [McpToolProperty("oven_id", "Existing oven ID, such as oven-02.", true)]
        string ovenId,
        [McpToolProperty("pizza_type", "Recipe to test: margherita, marinara, or diavola.", true)]
        string pizzaType,
        CancellationToken cancellationToken = default)
    {
        if (context.Arguments is not { Count: 2 } arguments ||
            !arguments.TryGetValue("oven_id", out var rawOvenId) ||
            rawOvenId is not string originalOvenId ||
            !string.Equals(originalOvenId, ovenId, StringComparison.Ordinal) ||
            !arguments.TryGetValue("pizza_type", out var rawPizzaType) ||
            rawPizzaType is not string originalPizzaType ||
            !string.Equals(originalPizzaType, pizzaType, StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(
                OperationResultFactory.Failed<BakeResult>(
                    ResultCodes.InvalidArgument,
                    "Arguments must contain only string oven_id and pizza_type values."),
                JsonOptions);
        }

        var result = await bakes.BakeAsync(ovenId, pizzaType, correlationId: null, cancellationToken);
        return JsonSerializer.Serialize(result, JsonOptions);
    }
}
