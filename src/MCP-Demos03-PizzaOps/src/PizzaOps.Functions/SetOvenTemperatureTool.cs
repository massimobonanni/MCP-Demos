using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;

namespace PizzaOps.Functions;

/// <summary>MCP adapter for the validated oven temperature command.</summary>
public sealed class SetOvenTemperatureTool(ITemperatureChangeService temperatureChanges, ToolExecution execution)
{
    /// <summary>Changes an existing oven temperature within the server-enforced policy.</summary>
    [Function(nameof(SetOvenTemperature))]
    public async Task<string> SetOvenTemperature(
        [McpToolTrigger(
            "set_oven_temperature",
            "Changes an existing PizzaOps oven temperature. The server enforces the 200–350°C policy; out-of-range requests are rejected. This tool modifies shared kitchen state.")]
        ToolInvocationContext context,
        [McpToolProperty("oven_id", "Existing oven ID, such as oven-02.", true)]
        string ovenId,
        [McpToolProperty("target_temperature_c", "Requested oven temperature in °C; the server enforces 200–350.", true)]
        int targetTemperatureC,
        [McpToolProperty("reason", "Optional brief reason for the change; at most 120 characters.", false)]
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        await execution.RunAsync("set_oven_temperature", mutation: true, async correlationId =>
        {
            if (!HasValidArgumentShape(
                context.Arguments,
                ovenId,
                targetTemperatureC,
                reason))
            {
                return OperationResultFactory.Failed<TemperatureChangeResult>(
                        ResultCodes.InvalidArgument,
                        "Arguments must contain only oven_id, target_temperature_c, and an optional string reason.");
            }

            return await temperatureChanges.SetTemperatureAsync(
                ovenId,
                targetTemperatureC,
                reason,
                correlationId,
                cancellationToken);
        }, cancellationToken);

    private static bool HasValidArgumentShape(
        IReadOnlyDictionary<string, object>? arguments,
        string? boundOvenId,
        int boundTemperatureC,
        string? boundReason)
    {
        if (arguments is null ||
            arguments.Keys.Any(key => key is not ("oven_id" or "target_temperature_c" or "reason")) ||
            !arguments.TryGetValue("oven_id", out var rawOvenId) ||
            rawOvenId is not string ovenId ||
            !string.Equals(ovenId, boundOvenId, StringComparison.Ordinal) ||
            !arguments.TryGetValue("target_temperature_c", out var rawTemperature) ||
            !TryGetInt32(rawTemperature, out var temperatureC) ||
            temperatureC != boundTemperatureC)
        {
            return false;
        }

        if (!arguments.TryGetValue("reason", out var rawReason))
        {
            return boundReason is null;
        }

        return rawReason is string reason &&
            string.Equals(reason, boundReason, StringComparison.Ordinal);
    }

    private static bool TryGetInt32(object? value, out int result)
    {
        switch (value)
        {
            case int integer:
                result = integer;
                return true;
            case long integer when integer is >= int.MinValue and <= int.MaxValue:
                result = (int)integer;
                return true;
            case uint integer when integer <= int.MaxValue:
                result = (int)integer;
                return true;
            case ulong integer when integer <= int.MaxValue:
                result = (int)integer;
                return true;
            case short integer:
                result = integer;
                return true;
            case ushort integer:
                result = integer;
                return true;
            case byte integer:
                result = integer;
                return true;
            case sbyte integer:
                result = integer;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } number when number.TryGetInt32(out result):
                return true;
            default:
                result = default;
                return false;
        }
    }
}
