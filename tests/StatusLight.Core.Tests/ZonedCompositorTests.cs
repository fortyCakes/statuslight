using Microsoft.Extensions.Time.Testing;
using StatusLight.Core.Composition;
using StatusLight.Core.Effects;

namespace StatusLight.Core.Tests;

public class ZonedCompositorTests
{
    private static readonly Rgb Green = Rgb.Parse("#00FF00");
    private static readonly Rgb Grey = Rgb.Parse("#202020");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly ZonedCompositor _compositor;

    public ZonedCompositorTests()
    {
        var options = new CompositorOptions
        {
            DoneFadeAfter = TimeSpan.FromMinutes(10),
            Zones = [new ZoneOptions { Source = "claude", Leds = [0, 1, 2, 3, 4] }],
            Styles = new()
            {
                [SignalState.Idle] = new StateStyle { Color = Grey.ToString() },
                [SignalState.Done] = new StateStyle { Color = Green.ToString() },
            },
        };
        _compositor = new ZonedCompositor(new TestOptionsMonitor<CompositorOptions>(options), _time);
    }

    private StatusSignal Signal(string source, string key, SignalState state) =>
        new(source, key, state, _time.GetUtcNow(), _time.GetUtcNow().AddHours(1));

    [Fact]
    public void Leds_outside_every_zone_stay_off()
    {
        var frame = _compositor.Compose([Signal("claude", "a", SignalState.Done), Signal("teams", "x", SignalState.Done)], 8);

        Assert.Equal(new SolidEffect(Green), frame[0]);
        Assert.All(frame[1..], e => Assert.Equal(LedEffect.Off, e));
    }

    [Fact]
    public void Done_switches_to_idle_style_after_the_fade_delay()
    {
        var signals = new[] { Signal("claude", "a", SignalState.Done) };

        _time.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(new SolidEffect(Green), _compositor.Compose(signals, 8)[0]);

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new SolidEffect(Grey), _compositor.Compose(signals, 8)[0]);
    }

    [Fact]
    public void State_without_a_style_is_drawn_off()
    {
        var frame = _compositor.Compose([Signal("claude", "a", SignalState.Error)], 8);

        Assert.Equal(LedEffect.Off, frame[0]);
    }
}
