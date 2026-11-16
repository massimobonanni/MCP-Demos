namespace PizzaOps.Core.Domain;

/// <summary>A safe, concise record of a PizzaOps kitchen action.</summary>
public sealed class KitchenEvent
{
    /// <summary>Creates an event with a UTC timestamp and normalized optional oven identifier.</summary>
    public KitchenEvent(
        string eventId,
        DateTimeOffset timestampUtc,
        KitchenEventType eventType,
        string? ovenId,
        string summary,
        KitchenEventOutcome outcome,
        string? correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        if (timestampUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The event timestamp must be UTC.", nameof(timestampUtc));
        }

        if (ovenId is not null && !IdentifierNormalization.TryNormalize(ovenId, out _))
        {
            throw new ArgumentException("The oven identifier cannot be empty.", nameof(ovenId));
        }

        EventId = eventId;
        TimestampUtc = timestampUtc;
        EventType = eventType;
        OvenId = ovenId is null ? null : ovenId.Trim().ToLowerInvariant();
        Summary = summary;
        Outcome = outcome;
        CorrelationId = correlationId;
    }

    /// <summary>The unique event identifier.</summary>
    public string EventId { get; }

    /// <summary>The UTC event time.</summary>
    public DateTimeOffset TimestampUtc { get; }

    /// <summary>The event category.</summary>
    public KitchenEventType EventType { get; }

    /// <summary>The canonical oven identifier, if the event concerns one oven.</summary>
    public string? OvenId { get; }

    /// <summary>A short safe summary suitable for the dashboard.</summary>
    public string Summary { get; }

    /// <summary>The coarse event outcome.</summary>
    public KitchenEventOutcome Outcome { get; }

    /// <summary>The originating operation correlation identifier, if available.</summary>
    public string? CorrelationId { get; }
}
