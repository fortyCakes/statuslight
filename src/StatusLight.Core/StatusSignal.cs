namespace StatusLight.Core;

/// <summary>What a status source is reporting. Ordered by urgency: higher values win when signals compete for one LED.</summary>
public enum SignalState
{
    Idle,
    Done,
    Working,
    Error,
    NeedsAttention,
}

/// <param name="Source">Which source produced the signal, e.g. <c>claude</c>. Zones in the layout are keyed by this.</param>
/// <param name="Key">Identifies the signal within its source, e.g. a session id.</param>
/// <param name="ExpiresAt">When the signal is dropped if nothing refreshes it, so a crashed source can't leave a light stuck on.</param>
/// <param name="Label">Optional human-readable name, e.g. the repo a session is working in.</param>
public sealed record StatusSignal(
    string Source,
    string Key,
    SignalState State,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    string? Label = null);
