using PizzaOps.Core.Contracts;
using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Application;

/// <summary>Evaluates the observed oven temperature and records a best-effort test event.</summary>
public sealed class BakeTestService(
    IOvenRepository ovens,
    IKitchenEventRepository events,
    IRecipeCatalog recipes,
    TimeProvider timeProvider) : IBakeTestService
{
    /// <inheritdoc />
    public async Task<OperationResult<BakeResult>> BakeAsync(
        string? ovenId,
        string? pizzaType,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IdentifierNormalization.TryNormalize(ovenId, out var normalizedOvenId) ||
            !IdentifierNormalization.TryNormalize(pizzaType, out var normalizedPizzaType))
        {
            return OperationResultFactory.Failed<BakeResult>(
                ResultCodes.InvalidArgument,
                "An oven ID and pizza type are required.",
                correlationId);
        }

        var recipe = recipes.FindByType(normalizedPizzaType);
        if (recipe is null)
        {
            return OperationResultFactory.Failed<BakeResult>(
                ResultCodes.RecipeNotFound,
                "The pizza recipe was not found.",
                correlationId);
        }

        Oven? oven;
        try
        {
            oven = await ovens.GetByIdAsync(normalizedOvenId, cancellationToken);
        }
        catch (KitchenStorageException exception)
        {
            return OperationResultFactory.Failed<BakeResult>(
                exception.FailureKind == KitchenStorageFailureKind.InvalidData
                    ? ResultCodes.KitchenStateInvalid
                    : ResultCodes.StorageUnavailable,
                exception.FailureKind == KitchenStorageFailureKind.InvalidData
                    ? "The requested oven state is invalid."
                    : "The requested oven could not be read from storage.",
                correlationId);
        }

        if (oven is null)
        {
            return OperationResultFactory.Failed<BakeResult>(
                ResultCodes.OvenNotFound,
                "The requested oven was not found.",
                correlationId);
        }

        var result = new BakeResult(
            oven.OvenId,
            recipe.PizzaType,
            oven.TemperatureC,
            recipe.TargetTemperatureC,
            recipe.ToleranceC,
            BakePolicy.Evaluate(recipe, oven.TemperatureC));
        var kitchenEvent = new KitchenEvent(
            Guid.NewGuid().ToString("N"),
            timeProvider.GetUtcNow(),
            KitchenEventType.TestBake,
            oven.OvenId,
            $"Test bake {result.Result}: {recipe.DisplayName} at {oven.TemperatureC} C; target {recipe.TargetTemperatureC} C, passing {recipe.MinimumPassingTemperatureC}-{recipe.MaximumPassingTemperatureC} C.",
            result.Outcome == BakeOutcome.Pass ? KitchenEventOutcome.Pass : KitchenEventOutcome.Fail,
            correlationId);
        var eventWrite = await BestEffortEventWriter.AppendAsync(events, kitchenEvent, cancellationToken);

        var message = result.Outcome switch
        {
            BakeOutcome.Pass => $"{recipe.DisplayName} passed: dinner is saved!",
            BakeOutcome.Undercooked => $"{recipe.DisplayName} is undercooked: dinner needs more heat.",
            _ => $"{recipe.DisplayName} is burnt: charcoal is not a topping."
        };
        return OperationResultFactory.Completed(
            ResultCodes.TestCompleted,
            message,
            result,
            correlationId,
            eventWrite.Recorded,
            eventWrite.Warnings);
    }
}
