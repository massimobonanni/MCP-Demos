using PizzaOps.Core.Domain;

namespace PizzaOps.UnitTests;

public sealed class OvenTemperaturePolicyTests
{
    [Theory]
    [InlineData(199, false, OvenHealth.Critical)]
    [InlineData(200, true, OvenHealth.Healthy)]
    [InlineData(350, true, OvenHealth.Healthy)]
    [InlineData(351, false, OvenHealth.Critical)]
    [InlineData(450, false, OvenHealth.Critical)]
    [InlineData(600, false, OvenHealth.Critical)]
    public void CommandAndHealthBoundariesAreIndependent(
        int temperatureC,
        bool commandAllowed,
        OvenHealth expectedHealth)
    {
        Assert.Equal(commandAllowed, OvenTemperaturePolicy.IsCommandAllowed(temperatureC));
        Assert.Equal(expectedHealth, OvenTemperaturePolicy.GetHealth(temperatureC));
    }

    [Fact]
    public void OvenCanRepresentThe450DegreeIncidentAsCritical()
    {
        var oven = new Oven(
            " OVEN-02 ",
            "Oven 02",
            450,
            DateTimeOffset.UnixEpoch,
            null,
            new ConcurrencyToken("opaque-version"));

        Assert.Equal("oven-02", oven.OvenId);
        Assert.Equal(450, oven.TemperatureC);
        Assert.Equal(OvenHealth.Critical, oven.Health);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void OvenRejectsMissingIdentifiers(string? ovenId)
    {
        Assert.Throws<ArgumentException>(() => new Oven(
            ovenId!,
            "Oven",
            450,
            DateTimeOffset.UnixEpoch,
            null,
            new ConcurrencyToken("version")));
    }

    [Fact]
    public void OvenRequiresUtcTimestamps()
    {
        var nonUtc = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.FromHours(1));

        Assert.Throws<ArgumentException>(() => new Oven(
            "oven-01",
            "Oven 01",
            320,
            nonUtc,
            null,
            new ConcurrencyToken("version")));
    }
}
