using Microsoft.Extensions.Options;
using StatusLight.Core.Effects;
using StatusLight.Core.Layout;

namespace StatusLight.Core.Composition;

/// <summary>Gives each source its own zone of LEDs, with one LED per signal (see <see cref="SlotAllocator"/>).</summary>
public sealed class ZonedCompositor(IOptionsMonitor<CompositorOptions> options, TimeProvider time) : IFrameCompositor
{
    private readonly Dictionary<string, SlotAllocator> _allocators = [];

    public LedEffect[] Compose(IReadOnlyList<StatusSignal> signals, int ledCount)
    {
        var opts = options.CurrentValue;
        var now = time.GetUtcNow();
        var frame = Enumerable.Repeat(LedEffect.Off, ledCount).ToArray();

        foreach (var zone in opts.Zones)
        {
            var slots = GetAllocator(zone).Allocate([.. signals.Where(s => s.Source == zone.Source)]);
            for (var i = 0; i < slots.Length; i++)
            {
                var led = zone.Leds[i];
                if (led >= 0 && led < ledCount && slots[i] is { } signal)
                    frame[led] = StyleFor(signal, opts, now);
            }
        }

        return frame;
    }

    private SlotAllocator GetAllocator(ZoneOptions zone)
    {
        // Recreated if a config reload changes the zone's size.
        if (!_allocators.TryGetValue(zone.Source, out var allocator) || allocator.SlotCount != zone.Leds.Count)
            _allocators[zone.Source] = allocator = new SlotAllocator(zone.Leds.Count);

        return allocator;
    }

    private static LedEffect StyleFor(StatusSignal signal, CompositorOptions opts, DateTimeOffset now)
    {
        var state = signal.State == SignalState.Done && now - signal.UpdatedAt >= opts.DoneFadeAfter
            ? SignalState.Idle
            : signal.State;

        return opts.Styles.TryGetValue(state, out var style) ? style.ToEffect() : LedEffect.Off;
    }
}
