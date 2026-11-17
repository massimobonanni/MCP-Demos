using PizzaOps.Core.Utilities;

namespace PizzaOps.UnitTests;

public sealed class LoadingIndicatorTests
{
    [Fact]
    public void StoppingImmediatelyAndRepeatedlyIsSafe()
    {
        using var indicator = new LoadingIndicator();

        indicator.Stop();
        indicator.Stop();
    }
}
