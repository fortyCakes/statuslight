using StatusLight.Core.Layout;

namespace StatusLight.Core.Tests;

public class SlotAllocatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static StatusSignal Signal(string key, int secondsAfterT0 = 0, SignalState state = SignalState.Working) =>
        new("claude", key, state, T0.AddSeconds(secondsAfterT0), T0.AddHours(1));

    private static string?[] Keys(StatusSignal?[] slots) => [.. slots.Select(s => s?.Key)];

    [Fact]
    public void Fills_slots_in_arrival_order()
    {
        var allocator = new SlotAllocator(3);

        var slots = allocator.Allocate([Signal("b", 2), Signal("a", 1)]);

        Assert.Equal(["a", "b", null], Keys(slots));
    }

    [Fact]
    public void Keeps_existing_sessions_in_place_when_one_ends()
    {
        var allocator = new SlotAllocator(3);
        allocator.Allocate([Signal("a", 1), Signal("b", 2), Signal("c", 3)]);

        var slots = allocator.Allocate([Signal("a", 1), Signal("c", 3)]);

        Assert.Equal(["a", null, "c"], Keys(slots));
    }

    [Fact]
    public void New_session_takes_the_lowest_free_slot()
    {
        var allocator = new SlotAllocator(3);
        allocator.Allocate([Signal("a", 1), Signal("b", 2), Signal("c", 3)]);
        allocator.Allocate([Signal("a", 1), Signal("c", 3)]);

        var slots = allocator.Allocate([Signal("a", 1), Signal("c", 3), Signal("d", 4)]);

        Assert.Equal(["a", "d", "c"], Keys(slots));
    }

    [Fact]
    public void Overflow_slot_shows_the_most_urgent_of_the_overflow()
    {
        var allocator = new SlotAllocator(2);

        var slots = allocator.Allocate(
        [
            Signal("a", 1),
            Signal("b", 2, SignalState.Done),
            Signal("c", 3, SignalState.NeedsAttention),
            Signal("d", 4),
        ]);

        Assert.Equal(["a", "c"], Keys(slots));
    }

    [Fact]
    public void Last_slot_owner_gets_its_slot_back_when_overflow_clears()
    {
        var allocator = new SlotAllocator(2);
        allocator.Allocate([Signal("a", 1), Signal("b", 2)]);
        allocator.Allocate([Signal("a", 1), Signal("b", 2), Signal("c", 3, SignalState.NeedsAttention)]);

        var slots = allocator.Allocate([Signal("a", 1), Signal("b", 2)]);

        Assert.Equal(["a", "b"], Keys(slots));
    }
}
