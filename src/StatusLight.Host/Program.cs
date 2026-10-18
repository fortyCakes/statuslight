using System.Text.Json.Serialization;
using StatusLight.Core;
using StatusLight.Core.Composition;
using StatusLight.Core.Devices;
using StatusLight.Core.Rendering;
using StatusLight.Devices.BlinkStick;
using StatusLight.Host;
using StatusLight.Sources.ClaudeCode;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var services = builder.Services;

// Machine-specific settings (e.g. the ntfy topic) live here and are git-ignored.
config.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

services.Configure<CompositorOptions>(config.GetSection(CompositorOptions.Section));
services.Configure<RendererOptions>(config.GetSection(RendererOptions.Section));
services.Configure<BlinkStickOptions>(config.GetSection(BlinkStickOptions.Section));
services.Configure<ClaudeOptions>(config.GetSection(ClaudeOptions.Section));
services.Configure<NtfyOptions>(config.GetSection(NtfyOptions.Section));
services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

services.AddSingleton(TimeProvider.System);
services.AddSingleton<SignalStore>();
services.AddSingleton<IFrameCompositor, ZonedCompositor>();
services.AddSingleton<ClaudeHookMapper>();

if (string.Equals(config["Device"], "Console", StringComparison.OrdinalIgnoreCase))
    services.AddSingleton<ILedDevice, ConsoleDevice>();
else
    services.AddSingleton<ILedDevice, BlinkStickDevice>();

services.AddHttpClient();
services.AddHostedService<Renderer>();
services.AddHostedService<NtfyPollingSource>();

var app = builder.Build();

// Claude Code hooks pipe their stdin JSON straight here. The response body must stay empty: for some hook events,
// whatever the hook prints is added to Claude's context.
app.MapPost("/claude/hook", (ClaudeHookEvent e, ClaudeHookMapper mapper, TimeProvider time) =>
{
    if (!e.IsValid)
        return Results.BadRequest();

    mapper.Apply(e, time.GetUtcNow(), fromCloud: false);
    return Results.NoContent();
});

// Generic API for any other script or source.
app.MapGet("/signals", (SignalStore store) => store.GetActive());

app.MapPut("/signals/{source}/{key}", (string source, string key, SignalUpdate body, SignalStore store, TimeProvider time) =>
{
    var now = time.GetUtcNow();
    store.Upsert(new StatusSignal(source, key, body.State, now, now + TimeSpan.FromSeconds(body.TtlSeconds ?? 300), body.Label));
    return Results.NoContent();
});

app.MapDelete("/signals/{source}/{key}", (string source, string key, SignalStore store) =>
    store.Remove(source, key) ? Results.NoContent() : Results.NotFound());

app.MapGet("/device", (ILedDevice device) => new { device.IsConnected, device.LedCount });

app.Run();

internal sealed record SignalUpdate(SignalState State, int? TtlSeconds = null, string? Label = null);
