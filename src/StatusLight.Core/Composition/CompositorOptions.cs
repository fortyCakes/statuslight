using StatusLight.Core.Effects;

namespace StatusLight.Core.Composition;

public sealed class CompositorOptions
{
    public const string Section = "Compositor";

    /// <summary>Which LEDs each source owns. LEDs not in any zone stay off.</summary>
    public List<ZoneOptions> Zones { get; set; } = [];

    /// <summary>How each state is drawn. A state with no style is drawn as off.</summary>
    public Dictionary<SignalState, StateStyle> Styles { get; set; } = [];

    /// <summary>How long a <see cref="SignalState.Done"/> signal shows its own style before switching to the Idle style.</summary>
    public TimeSpan DoneFadeAfter { get; set; } = TimeSpan.FromMinutes(10);
}

public sealed class ZoneOptions
{
    public string Source { get; set; } = "";

    /// <summary>LED indexes in slot order. The last one doubles as the overflow slot.</summary>
    public List<int> Leds { get; set; } = [];
}

public enum EffectKind
{
    Solid,
    Pulse,
    Blink,
}

public sealed class StateStyle
{
    public string Color { get; set; } = "#FFFFFF";
    public EffectKind Effect { get; set; } = EffectKind.Solid;
    public int PeriodMs { get; set; } = 1000;

    /// <summary>Lowest brightness of a <see cref="EffectKind.Pulse"/>, 0–1. Closer to 1 is subtler.</summary>
    public double MinLevel { get; set; } = 0.15;

    public LedEffect ToEffect()
    {
        var color = Rgb.Parse(Color);
        var period = TimeSpan.FromMilliseconds(Math.Max(PeriodMs, 1));
        return Effect switch
        {
            EffectKind.Pulse => new PulseEffect(color, period, Math.Clamp(MinLevel, 0, 1)),
            EffectKind.Blink => new BlinkEffect(color, period),
            _ => new SolidEffect(color),
        };
    }
}
