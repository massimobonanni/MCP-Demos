using PizzaOps.Core.Domain;

namespace PizzaOps.Core.Contracts;

/// <summary>A point-in-time best-effort projection of oven state and recent events.</summary>
public sealed class KitchenSnapshot
{
    /// <summary>Creates a snapshot with defensive copies of its projected collections.</summary>
    public KitchenSnapshot(
        DateTimeOffset retrievedAtUtc,
        IEnumerable<Oven> ovens,
        IEnumerable<KitchenEvent> recentEvents,
        bool eventFeedAvailable,
        string? eventFeedWarning)
    {
        if (retrievedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The retrieval timestamp must be UTC.", nameof(retrievedAtUtc));
        }

        ArgumentNullException.ThrowIfNull(ovens);
        ArgumentNullException.ThrowIfNull(recentEvents);

        if (eventFeedAvailable && eventFeedWarning is not null)
        {
            throw new ArgumentException("An available event feed cannot have a degradation warning.", nameof(eventFeedWarning));
        }

        RetrievedAtUtc = retrievedAtUtc;
        Ovens = Array.AsReadOnly(ovens.OrderBy(oven => oven.OvenId, StringComparer.Ordinal).ToArray());
        RecentEvents = Array.AsReadOnly(recentEvents.ToArray());
        EventFeedAvailable = eventFeedAvailable;
        EventFeedWarning = eventFeedWarning;
    }

    /// <summary>The UTC time the snapshot was assembled.</summary>
    public DateTimeOffset RetrievedAtUtc { get; }

    /// <summary>Ovens in deterministic identifier order.</summary>
    public IReadOnlyList<Oven> Ovens { get; }

    /// <summary>Recent events in the order supplied by the read projection.</summary>
    public IReadOnlyList<KitchenEvent> RecentEvents { get; }

    /// <summary>Whether recent events could be read; oven state can remain authoritative if false.</summary>
    public bool EventFeedAvailable { get; }

    /// <summary>A safe warning when the event feed is degraded.</summary>
    public string? EventFeedWarning { get; }
}
