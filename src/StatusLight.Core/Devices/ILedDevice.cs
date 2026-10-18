namespace StatusLight.Core.Devices;

public interface ILedDevice
{
    int LedCount { get; }

    bool IsConnected { get; }

    /// <summary>
    /// Shows a full frame. Called every render tick, so implementations should skip frames identical to the last one
    /// sent, drop frames while disconnected, and always send the next frame after (re)connecting.
    /// </summary>
    void Write(ReadOnlySpan<Rgb> frame);
}
