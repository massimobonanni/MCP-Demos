using PizzaOps.Storage;

namespace PizzaOps.UnitTests;

public sealed class PizzaOpsTableOptionsValidatorTests
{
    [Fact]
    public void DevelopmentAcceptsOnlyTheExplicitAzuriteProfile()
    {
        var options = new PizzaOpsTableOptions
        {
            Mode = "Azurite",
            AzuriteConnectionString = "UseDevelopmentStorage=true"
        };

        var result = new PizzaOpsTableOptionsValidator("Development").Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AzureRequiresHttpsEndpointAndRejectsConnectionStrings()
    {
        var options = new PizzaOpsTableOptions
        {
            Mode = "Azure",
            TableEndpoint = "https://pizzaops.table.core.windows.net",
            AzuriteConnectionString = "UseDevelopmentStorage=true"
        };

        var result = new PizzaOpsTableOptionsValidator("Production").Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains("does not accept a connection string", result.Failures.Single());
    }

    [Fact]
    public void AzuriteIsRejectedOutsideDevelopment()
    {
        var options = new PizzaOpsTableOptions
        {
            Mode = "Azurite",
            AzuriteConnectionString = "UseDevelopmentStorage=true"
        };

        var result = new PizzaOpsTableOptionsValidator("Production").Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains("only in the Development environment", result.Failures.Single());
    }
}
