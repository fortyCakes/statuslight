using StatusLight.Core;
using StatusLight.Core.Devices;

namespace StatusLight.Host;

/// <summary>Stand-in device for development without the hardware: logs the frame, at most once a second.</summary>
internal sealed class ConsoleDevice(ILogger<ConsoleDevice> logger, TimeProvider time) : ILedDevice
{
    private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(1);

    private Rgb[] _last = [];
    private DateTimeOffset _lastLogged;

    public int LedCount => 8;

    public bool IsConnected => true;

    public void Write(ReadOnlySpan<Rgb> frame)
    {
        var now = time.GetUtcNow();
        if (frame.SequenceEqual(_last) || now - _lastLogged < LogInterval)
            return;

        _last = frame.ToArray();
        _lastLogged = now;
        logger.LogInformation("Frame: {Frame}", string.Join(" ", _last));
    }
}
