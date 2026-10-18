namespace StatusLight.Core;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Off = new(0, 0, 0);

    public Rgb Scale(double factor)
    {
        factor = Math.Clamp(factor, 0, 1);
        return new((byte)Math.Round(R * factor), (byte)Math.Round(G * factor), (byte)Math.Round(B * factor));
    }

    /// <summary>Parses <c>#RRGGBB</c> (the leading <c>#</c> is optional).</summary>
    public static Rgb Parse(string hex)
    {
        var digits = hex.TrimStart('#');
        if (digits.Length != 6)
            throw new FormatException($"Expected a colour like #RRGGBB, got '{hex}'.");

        var value = Convert.ToInt32(digits, 16);
        return new((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
