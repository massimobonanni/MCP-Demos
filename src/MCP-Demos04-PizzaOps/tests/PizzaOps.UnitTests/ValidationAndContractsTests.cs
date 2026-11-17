using System.Text.Json;
using PizzaOps.Core.Application;
using PizzaOps.Core.Contracts;
using PizzaOps.Core.Domain;

namespace PizzaOps.UnitTests;

public sealed class ValidationAndContractsTests
{
    [Theory]
    [InlineData(" Oven-02 ", "oven-02")]
    [InlineData("OVEN-03", "oven-03")]
    public void IdentifierNormalizationTrimsAndUsesInvariantCase(string input, string expected)
    {
        Assert.True(IdentifierNormalization.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void IdentifierNormalizationRejectsMissingValues(string? input)
    {
        Assert.False(IdentifierNormalization.TryNormalize(input, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \t ", null)]
    [InlineData("  operator requested  ", "operator requested")]
    [InlineData("  123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890  ",
        "123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890")]
    public void ReasonValidationTrimsAndAllowsAtMost120Characters(string? input, string? expected)
    {
        Assert.True(ReasonValidation.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void ReasonValidationRejectsMoreThan120NormalizedCharacters()
    {
        Assert.False(ReasonValidation.TryNormalize($"  {new string('x', 121)}  ", out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void OperationResultKeepsBakeBusinessOutcomeSeparateFromExecutionSuccess()
    {
        var result = OperationResultFactory.Completed(
            ResultCodes.TestCompleted,
            "The pizza was burnt.",
            new BakeResult("oven-02", "margherita", 450, 320, 15, BakeOutcome.Burnt),
            eventRecorded: false,
            warnings: ["Event could not be recorded."]);

        Assert.True(result.Success);
        Assert.Equal(ResultCodes.TestCompleted, result.Code);
        Assert.Equal(BakeOutcome.Burnt, result.Data?.Outcome);
        Assert.False(result.EventRecorded);
        Assert.Single(result.Warnings);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.Warnings).Add("mutated"));
    }

    [Fact]
    public void FailedOperationsHaveNoBakeDataAndNoEventAttemptIsNull()
    {
        var result = OperationResultFactory.Failed<BakeResult>(ResultCodes.OvenNotFound, "Oven not found.");

        Assert.False(result.Success);
        Assert.Equal(ResultCodes.OvenNotFound, result.Code);
        Assert.Null(result.Data);
        Assert.Null(result.EventRecorded);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void BakeEnvelopeSerializesStableSnakeCaseAndUppercaseBusinessOutcome()
    {
        var result = OperationResultFactory.Completed(
            ResultCodes.TestCompleted,
            "The pizza was burnt.",
            new BakeResult("oven-02", "margherita", 450, 320, 15, BakeOutcome.Burnt),
            eventRecorded: false,
            warnings: ["Event could not be recorded."]);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var root = document.RootElement;
        var data = root.GetProperty("data");

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("TEST_COMPLETED", root.GetProperty("code").GetString());
        Assert.Equal("oven-02", data.GetProperty("oven_id").GetString());
        Assert.Equal("BURNT", data.GetProperty("result").GetString());
        Assert.False(root.GetProperty("event_recorded").GetBoolean());
        Assert.Equal("Event could not be recorded.", root.GetProperty("warnings")[0].GetString());
        Assert.False(root.TryGetProperty("Outcome", out _));
    }

    [Fact]
    public void ConcurrencyTokenIsOpaqueAndComparesOnlyForExactEquality()
    {
        var first = new ConcurrencyToken("opaque:etag");
        var same = new ConcurrencyToken("opaque:etag");
        var different = new ConcurrencyToken("opaque:etag-2");

        Assert.Equal(first, same);
        Assert.NotEqual(first, different);
        Assert.Equal(nameof(ConcurrencyToken), first.ToString());
    }

    [Fact]
    public void KitchenSnapshotSortsOvensAndReportsEventFeedDegradationSeparately()
    {
        var snapshot = new KitchenSnapshot(
            DateTimeOffset.UnixEpoch,
            [
                MakeOven("oven-03"),
                MakeOven("oven-01")
            ],
            [],
            eventFeedAvailable: false,
            eventFeedWarning: "Recent events are unavailable.");

        Assert.Equal(["oven-01", "oven-03"], snapshot.Ovens.Select(oven => oven.OvenId));
        Assert.False(snapshot.EventFeedAvailable);
        Assert.Equal("Recent events are unavailable.", snapshot.EventFeedWarning);
    }

    [Fact]
    public void KitchenSnapshotRequiresWarningOnlyForAnUnavailableEventFeed()
    {
        Assert.Throws<ArgumentException>(() => new KitchenSnapshot(
            DateTimeOffset.UnixEpoch,
            [],
            [],
            eventFeedAvailable: true,
            eventFeedWarning: "degraded"));
    }

    private static Oven MakeOven(string ovenId) =>
        new(ovenId, ovenId, 320, DateTimeOffset.UnixEpoch, null, new ConcurrencyToken(ovenId));
}
