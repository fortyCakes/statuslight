using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StatusLight.Core.Composition;
using StatusLight.Core.Devices;

namespace StatusLight.Core.Rendering;

public sealed class RendererOptions
{
    public const string Section = "Renderer";

    /// <summary>Global brightness, 0–1. The BlinkStick is very bright at full power.</summary>
    public double Brightness { get; set; } = 0.25;

    public int FramesPerSecond { get; set; } = 30;
}

/// <summary>Turns the current signals into LED colours on a fixed tick and sends them to the device.</summary>
public sealed class Renderer(
    SignalStore store,
    IFrameCompositor compositor,
    ILedDevice device,
    IOptionsMonitor<RendererOptions> options,
    TimeProvider time,
    ILogger<Renderer> logger) : BackgroundService
{
    private string? _lastError;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var start = time.GetTimestamp();
        var frame = new Rgb[device.LedCount];
        var interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, options.CurrentValue.FramesPerSecond));
        using var timer = new PeriodicTimer(interval, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                RenderFrame(frame, time.GetElapsedTime(start));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        device.Write(new Rgb[device.LedCount]);
    }

    private void RenderFrame(Rgb[] frame, TimeSpan elapsed)
    {
        try
        {
            var effects = compositor.Compose(store.GetActive(), frame.Length);
            var brightness = options.CurrentValue.Brightness;
            for (var i = 0; i < frame.Length; i++)
                frame[i] = effects[i].Evaluate(elapsed).Scale(brightness);

            device.Write(frame);
            _lastError = null;
        }
        catch (Exception ex)
        {
            // Log each distinct failure once rather than every tick.
            if (ex.Message != _lastError)
                logger.LogError(ex, "Rendering a frame failed");
            _lastError = ex.Message;
        }
    }
}
