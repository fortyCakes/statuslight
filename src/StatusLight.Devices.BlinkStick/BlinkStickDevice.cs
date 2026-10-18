using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StatusLight.Core;
using StatusLight.Core.Devices;

namespace StatusLight.Devices.BlinkStick;

public sealed class BlinkStickOptions
{
    public const string Section = "BlinkStick";

    /// <summary>Serial number to use when more than one BlinkStick is attached; <c>null</c> uses the first one found.</summary>
    public string? Serial { get; set; }
}

/// <summary>
/// BlinkStick Square driver. The device may come and go at any time (it sits on a USB switcher shared between two
/// computers), so writes are dropped while it is absent and the full frame is re-sent whenever it reappears.
/// </summary>
public sealed class BlinkStickDevice : ILedDevice, IDisposable
{
    private const int VendorId = 0x20A0;
    private const int ProductId = 0x41E5;
    private const int Leds = 8;

    // Feature report 6 sets 8 LEDs at once: [report id, channel, G, R, B × 8].
    private const byte FrameReportId = 6;
    private const byte Channel = 0;

    private readonly Lock _lock = new();
    private readonly ILogger<BlinkStickDevice> _logger;
    private readonly string? _serial;
    private readonly byte[] _report = new byte[2 + Leds * 3];
    private byte[]? _lastSent;
    private HidStream? _stream;

    public BlinkStickDevice(IOptions<BlinkStickOptions> options, ILogger<BlinkStickDevice> logger)
    {
        _serial = options.Value.Serial;
        _logger = logger;

        DeviceList.Local.Changed += OnDeviceListChanged;
        lock (_lock)
            Reconnect();
    }

    public int LedCount => Leds;

    public bool IsConnected
    {
        get
        {
            lock (_lock)
                return _stream is not null;
        }
    }

    public void Write(ReadOnlySpan<Rgb> frame)
    {
        lock (_lock)
        {
            if (_stream is null)
                return;

            _report[0] = FrameReportId;
            _report[1] = Channel;
            for (var i = 0; i < Leds; i++)
            {
                var color = i < frame.Length ? frame[i] : Rgb.Off;
                _report[2 + i * 3] = color.G;
                _report[3 + i * 3] = color.R;
                _report[4 + i * 3] = color.B;
            }

            if (_lastSent is not null && _report.AsSpan().SequenceEqual(_lastSent))
                return;

            try
            {
                _stream.SetFeature(_report);
                _lastSent = [.. _report];
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException)
            {
                _logger.LogInformation("BlinkStick disconnected ({Reason})", ex.Message);
                CloseStream();
            }
        }
    }

    public void Dispose()
    {
        DeviceList.Local.Changed -= OnDeviceListChanged;
        lock (_lock)
            CloseStream();
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        // Fires for any USB change on the machine. Reopening every time is cheap, and it guarantees that a device
        // that was switched away and back gets a fresh handle and a full repaint, even if the two events coalesce.
        lock (_lock)
            Reconnect();
    }

    private void Reconnect()
    {
        var wasConnected = _stream is not null;
        CloseStream();

        var device = DeviceList.Local
            .GetHidDevices(VendorId, ProductId)
            .FirstOrDefault(d => _serial is null || TryGetSerial(d) == _serial);

        if (device is not null && device.TryOpen(out var stream))
            _stream = stream;

        if (_stream is not null && !wasConnected)
            _logger.LogInformation("BlinkStick connected ({Serial})", TryGetSerial(device!) ?? "unknown serial");
        else if (_stream is null && wasConnected)
            _logger.LogInformation("BlinkStick disconnected");
    }

    private void CloseStream()
    {
        _stream?.Dispose();
        _stream = null;
        _lastSent = null;
    }

    private static string? TryGetSerial(HidDevice device)
    {
        try
        {
            return device.GetSerialNumber();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
