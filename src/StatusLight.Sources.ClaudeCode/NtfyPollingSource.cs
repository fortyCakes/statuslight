using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StatusLight.Sources.ClaudeCode;

public sealed class NtfyOptions
{
    public const string Section = "CloudRelay";

    public string BaseUrl { get; set; } = "https://ntfy.sh";

    /// <summary>The topic the cloud hook publishes to. Polling is off while this is empty. Keep it out of source control.</summary>
    public string? Topic { get; set; }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How far back to read on startup, so sessions that were already running show up.</summary>
    public TimeSpan InitialLookback { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Polls an ntfy topic for Claude Code hook events published by cloud sessions.</summary>
public sealed class NtfyPollingSource(
    IHttpClientFactory httpClientFactory,
    ClaudeHookMapper mapper,
    IOptionsMonitor<NtfyOptions> options,
    TimeProvider time,
    ILogger<NtfyPollingSource> logger) : BackgroundService
{
    private string? _topic;
    private string? _since;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = options.CurrentValue;
            if (!string.IsNullOrWhiteSpace(opts.Topic))
            {
                try
                {
                    await PollAsync(opts, stoppingToken);
                }
                catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !stoppingToken.IsCancellationRequested))
                {
                    logger.LogWarning("Polling the cloud relay failed: {Reason}", ex.Message);
                }
            }

            try
            {
                await Task.Delay(opts.PollInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollAsync(NtfyOptions opts, CancellationToken ct)
    {
        if (opts.Topic != _topic)
        {
            _topic = opts.Topic;
            _since = null;
        }

        var since = _since ?? (time.GetUtcNow() - opts.InitialLookback).ToUnixTimeSeconds().ToString();
        var url = $"{opts.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(_topic!)}/json?poll=1&since={Uri.EscapeDataString(since)}";

        using var http = httpClientFactory.CreateClient(nameof(NtfyPollingSource));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length > 0)
                HandleLine(line);
        }
    }

    private void HandleLine(string line)
    {
        try
        {
            var message = JsonSerializer.Deserialize<NtfyMessage>(line);
            if (message is not { Event: "message", Id: { } id })
                return;

            _since = id;

            var hookEvent = message.Body is null ? null : JsonSerializer.Deserialize<ClaudeHookEvent>(message.Body);
            if (hookEvent is { IsValid: true })
                mapper.Apply(hookEvent, DateTimeOffset.FromUnixTimeSeconds(message.Time), fromCloud: true);
        }
        catch (JsonException ex)
        {
            logger.LogDebug("Ignoring malformed relay message: {Reason}", ex.Message);
        }
    }

    private sealed record NtfyMessage(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("time")] long Time,
        [property: JsonPropertyName("event")] string? Event,
        [property: JsonPropertyName("message")] string? Body);
}
