using PizzaOps.Core.Domain;

namespace PizzaOps.UnitTests;

public sealed class BakePolicyTests
{
    [Theory]
    [InlineData("margherita", 304, BakeOutcome.Undercooked)]
    [InlineData("margherita", 305, BakeOutcome.Pass)]
    [InlineData("margherita", 335, BakeOutcome.Pass)]
    [InlineData("margherita", 336, BakeOutcome.Burnt)]
    [InlineData("margherita", 450, BakeOutcome.Burnt)]
    [InlineData("marinara", 284, BakeOutcome.Undercooked)]
    [InlineData("marinara", 285, BakeOutcome.Pass)]
    [InlineData("marinara", 315, BakeOutcome.Pass)]
    [InlineData("marinara", 316, BakeOutcome.Burnt)]
    [InlineData("diavola", 294, BakeOutcome.Undercooked)]
    [InlineData("diavola", 295, BakeOutcome.Pass)]
    [InlineData("diavola", 325, BakeOutcome.Pass)]
    [InlineData("diavola", 326, BakeOutcome.Burnt)]
    public void BakeUsesInclusiveRecipeBounds(
        string pizzaType,
        int temperatureC,
        BakeOutcome expectedOutcome)
    {
        var recipe = PizzaRecipeCatalog.Instance.FindByType(pizzaType);

        Assert.NotNull(recipe);
        Assert.Equal(expectedOutcome, BakePolicy.Evaluate(recipe, temperatureC));
    }

    [Fact]
    public void BakePolicyRejectsMissingRecipe()
    {
        Assert.Throws<ArgumentNullException>(() => BakePolicy.Evaluate(null!, 320));
    }
}
