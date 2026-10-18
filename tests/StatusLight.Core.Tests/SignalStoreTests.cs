using Microsoft.Extensions.Time.Testing;

namespace StatusLight.Core.Tests;

public class SignalStoreTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private StatusSignal Signal(SignalState state, TimeSpan ttl, TimeSpan? updatedOffset = null)
    {
        var updated = _time.GetUtcNow() + (updatedOffset ?? TimeSpan.Zero);
        return new("claude", "s1", state, updated, updated + ttl);
    }

    [Fact]
    public void Drops_signals_once_they_expire()
    {
        var store = new SignalStore(_time);
        store.Upsert(Signal(SignalState.Working, TimeSpan.FromMinutes(5)));

        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Single(store.GetActive());

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(store.GetActive());
    }

    [Fact]
    public void Ignores_updates_older_than_the_current_signal()
    {
        var store = new SignalStore(_time);
        store.Upsert(Signal(SignalState.Done, TimeSpan.FromHours(1)));

        store.Upsert(Signal(SignalState.Working, TimeSpan.FromHours(1), updatedOffset: TimeSpan.FromSeconds(-10)));

        Assert.Equal(SignalState.Done, Assert.Single(store.GetActive()).State);
    }

    [Fact]
    public void Remove_deletes_the_signal()
    {
        var store = new SignalStore(_time);
        store.Upsert(Signal(SignalState.Working, TimeSpan.FromHours(1)));

        Assert.True(store.Remove("claude", "s1"));
        Assert.Empty(store.GetActive());
    }
}
