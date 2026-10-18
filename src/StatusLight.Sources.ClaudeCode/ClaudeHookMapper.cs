using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using StatusLight.Core;

namespace StatusLight.Sources.ClaudeCode;

public sealed class ClaudeOptions
{
    public const string Section = "Claude";

    /// <summary>Local sessions refresh this on every tool call, so it only needs to cover a long stretch of thinking.</summary>
    public TimeSpan WorkingTtl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Cloud sessions don't forward tool events, so nothing refreshes Working until the turn ends.</summary>
    public TimeSpan CloudWorkingTtl { get; set; } = TimeSpan.FromHours(2);

    public TimeSpan AttentionTtl { get; set; } = TimeSpan.FromHours(4);

    /// <summary>Applies to Idle, Unread and Done. SessionEnd removes the signal sooner; this catches sessions that crashed.</summary>
    public TimeSpan IdleTtl { get; set; } = TimeSpan.FromHours(8);
}

/// <summary>
/// Turns Claude Code hook events into signals. Shared by the local hook endpoint and the cloud relay poller.
/// When a turn finishes it's <see cref="SignalState.Unread"/> unless you're looking at the session's tab, and it
/// becomes <see cref="SignalState.Done"/> once you do (see <see cref="ViewedTabs"/>).
/// </summary>
public sealed class ClaudeHookMapper(
    SignalStore store,
    ViewedTabs viewedTabs,
    TranscriptTitles titles,
    IOptionsMonitor<ClaudeOptions> options)
{
    public const string Source = "claude";

    private readonly ConcurrentDictionary<string, string> _transcriptPaths = [];

    /// <returns><c>false</c> if the event doesn't affect the light.</returns>
    public bool Apply(ClaudeHookEvent e, DateTimeOffset at, bool fromCloud)
    {
        var key = fromCloud ? $"cloud:{e.SessionId}" : e.SessionId;

        if (e.EventName == "SessionEnd")
        {
            _transcriptPaths.TryRemove(key, out _);
            store.Remove(Source, key);
            return true;
        }

        if (e.TranscriptPath is { Length: > 0 } path)
            _transcriptPaths[key] = path;

        SignalState? state = e.EventName switch
        {
            "SessionStart" => SignalState.Idle,
            "UserPromptSubmit" or "PreToolUse" or "PostToolUse" => SignalState.Working,
            // idle_prompt fires a minute after every finished turn; Unread/Done already covers that.
            "Notification" when e.NotificationType is not ("idle_prompt" or "auth_success") => SignalState.NeedsAttention,
            "Stop" => IsViewed(key) ? SignalState.Done : SignalState.Unread,
            _ => null,
        };

        if (state is not { } s)
            return false;

        var opts = options.CurrentValue;
        var ttl = s switch
        {
            SignalState.Working => fromCloud ? opts.CloudWorkingTtl : opts.WorkingTtl,
            SignalState.NeedsAttention => opts.AttentionTtl,
            _ => opts.IdleTtl,
        };

        var label = e.Repo ?? (e.Cwd is null ? null : Path.GetFileName(e.Cwd.TrimEnd('/', '\\')));
        store.Upsert(new StatusSignal(Source, key, s, at, at + ttl, label));
        return true;
    }

    /// <summary>Marks Unread sessions whose tab is now in view as Done. Call whenever the viewed tabs change.</summary>
    public void MarkViewedSessionsSeen(DateTimeOffset now)
    {
        foreach (var signal in store.GetActive())
        {
            if (signal.Source != Source || signal.State != SignalState.Unread || !IsViewed(signal.Key))
                continue;

            // Re-checked under the store's lock in case a new event arrived meanwhile. UpdatedAt moves to now so the
            // Done style's fade timer starts from when you looked.
            store.Update(Source, signal.Key, current =>
                current.State == SignalState.Unread ? current with { State = SignalState.Done, UpdatedAt = now } : null);
        }
    }

    private bool IsViewed(string key) =>
        _transcriptPaths.TryGetValue(key, out var path) && titles.Read(path) is { } title && viewedTabs.IsViewed(title);
}
