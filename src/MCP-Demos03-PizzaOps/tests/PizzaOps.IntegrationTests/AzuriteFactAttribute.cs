namespace PizzaOps.IntegrationTests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AzuriteFactAttribute : FactAttribute
{
    public AzuriteFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("PIZZAOPS_AZURITE_TESTS"),
                "true",
                StringComparison.Ordinal) ||
            !string.Equals(
                Environment.GetEnvironmentVariable("PIZZAOPS_AZURITE_CONNECTION_STRING"),
                "UseDevelopmentStorage=true",
                StringComparison.Ordinal))
        {
            Skip = "Start Azurite and explicitly set PIZZAOPS_AZURITE_TESTS=true and PIZZAOPS_AZURITE_CONNECTION_STRING=UseDevelopmentStorage=true.";
        }
    }
}
