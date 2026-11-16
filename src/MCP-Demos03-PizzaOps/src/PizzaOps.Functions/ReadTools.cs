using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;
using PizzaOps.Core.Domain;

namespace PizzaOps.Functions;

/// <summary>Read-only MCP adapters for kitchen status and recipes.</summary>
public sealed class ReadTools(IKitchenReadService kitchen, RecipeReadService recipes)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Reads the current state of all ovens.</summary>
    [Function(nameof(GetOvensStatus))]
    public async Task<string> GetOvensStatus(
        [McpToolTrigger("get_ovens_status", "Returns the temperatures and health of all PizzaOps ovens. Use before diagnosing or changing an oven. Read-only.")]
        ToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var result = await kitchen.GetStatusAsync(cancellationToken);
        if (!result.Success || result.Data is null)
        {
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        var ovens = result.Data.Ovens.Select(oven => new
        {
            oven.OvenId,
            oven.TemperatureC,
            Health = oven.Health switch
            {
                OvenHealth.Healthy => "HEALTHY",
                OvenHealth.Critical => "CRITICAL",
                _ => throw new InvalidOperationException("Unknown oven health.")
            }
        }).ToArray();

        var data = new
        {
            Ovens = ovens,
            Summary = new
            {
                Total = ovens.Length,
                Healthy = ovens.Count(oven => oven.Health == "HEALTHY"),
                Critical = ovens.Count(oven => oven.Health == "CRITICAL")
            }
        };
        return JsonSerializer.Serialize(
            OperationResultFactory.Completed(result.Code, result.Message, data, warnings: result.Warnings),
            JsonOptions);
    }

    /// <summary>Reads the target and tolerance of a named pizza recipe.</summary>
    [Function(nameof(GetPizzaRecipe))]
    public string GetPizzaRecipe(
        [McpToolTrigger("get_pizza_recipe", "Looks up the target temperature and tolerance for a PizzaOps recipe. Use before choosing a repair temperature or test bake. Read-only.")]
        ToolInvocationContext context,
        [McpToolProperty("pizza_type", "Pizza type: margherita, marinara, or diavola.", true)]
        string pizzaType)
    {
        if (context.Arguments is null ||
            !context.Arguments.TryGetValue("pizza_type", out var rawValue) ||
            rawValue is not string)
        {
            return JsonSerializer.Serialize(
                OperationResultFactory.Failed<Recipe>(ResultCodes.InvalidArgument, "Pizza type must be a string."),
                JsonOptions);
        }

        var result = recipes.GetRecipe(pizzaType);
        return JsonSerializer.Serialize(result, JsonOptions);
    }
}
