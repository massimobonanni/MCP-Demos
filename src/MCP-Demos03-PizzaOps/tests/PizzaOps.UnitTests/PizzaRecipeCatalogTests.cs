using PizzaOps.Core.Domain;

namespace PizzaOps.UnitTests;

public sealed class PizzaRecipeCatalogTests
{
    [Fact]
    public void CatalogContainsExactlyTheCanonicalImmutableRecipes()
    {
        Assert.Collection(
            PizzaRecipeCatalog.Recipes,
            recipe => AssertRecipe(recipe, "margherita", "Margherita", 320),
            recipe => AssertRecipe(recipe, "marinara", "Marinara", 300),
            recipe => AssertRecipe(recipe, "diavola", "Diavola", 310));
        Assert.Throws<NotSupportedException>(() => ((IList<Recipe>)PizzaRecipeCatalog.Recipes).Add(
            new Recipe("extra", "Extra", 300, 15)));
    }

    [Theory]
    [InlineData(" MarGhEriTa ", "margherita")]
    [InlineData("\tDIAVOLA\n", "diavola")]
    public void LookupTrimsAndInvariantLowercasesRecipeIds(string input, string canonicalType)
    {
        Assert.Equal(canonicalType, PizzaRecipeCatalog.Instance.FindByType(input)?.PizzaType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("hawaiian")]
    public void LookupReturnsNullForInvalidOrUnknownRecipeIds(string? input)
    {
        Assert.Null(PizzaRecipeCatalog.Instance.FindByType(input));
    }

    [Fact]
    public void RecipeRejectsNegativeOrOverflowingTolerance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Recipe("test", "Test", 300, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Recipe("test", "Test", int.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Recipe("test", "Test", int.MinValue, 1));
    }

    private static void AssertRecipe(Recipe recipe, string type, string displayName, int targetC)
    {
        Assert.Equal(type, recipe.PizzaType);
        Assert.Equal(displayName, recipe.DisplayName);
        Assert.Equal(targetC, recipe.TargetTemperatureC);
        Assert.Equal(15, recipe.ToleranceC);
    }
}
