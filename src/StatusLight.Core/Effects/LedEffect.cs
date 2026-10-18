namespace StatusLight.Core.Effects;

/// <summary>A colour as a function of time. Every LED is evaluated against the same clock, so animations stay in phase.</summary>
public abstract record LedEffect
{
    public static readonly LedEffect Off = new SolidEffect(Rgb.Off);

    public abstract Rgb Evaluate(TimeSpan time);
}

public sealed record SolidEffect(Rgb Color) : LedEffect
{
    public override Rgb Evaluate(TimeSpan time) => Color;
}

/// <summary>Smoothly breathes between <paramref name="MinLevel"/> and full brightness.</summary>
public sealed record PulseEffect(Rgb Color, TimeSpan Period, double MinLevel = 0.15) : LedEffect
{
    public override Rgb Evaluate(TimeSpan time)
    {
        var phase = time.TotalMilliseconds % Period.TotalMilliseconds / Period.TotalMilliseconds;
        var wave = (1 - Math.Cos(phase * 2 * Math.PI)) / 2;
        return Color.Scale(MinLevel + (1 - MinLevel) * wave);
    }
}

/// <summary>On for the first half of each period, off for the second.</summary>
public sealed record BlinkEffect(Rgb Color, TimeSpan Period) : LedEffect
{
    public override Rgb Evaluate(TimeSpan time) =>
        time.TotalMilliseconds % Period.TotalMilliseconds < Period.TotalMilliseconds / 2 ? Color : Rgb.Off;
}
