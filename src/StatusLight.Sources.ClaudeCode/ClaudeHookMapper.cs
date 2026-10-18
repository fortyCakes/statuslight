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

    /// <summary>Applies to Idle and Done. SessionEnd removes the signal sooner; this catches sessions that crashed.</summary>
    public TimeSpan IdleTtl { get; set; } = TimeSpan.FromHours(8);
}

/// <summary>Turns Claude Code hook events into signals. Shared by the local hook endpoint and the cloud relay poller.</summary>
public sealed class ClaudeHookMapper(SignalStore store, IOptionsMonitor<ClaudeOptions> options)
{
    public const string Source = "claude";

    /// <returns><c>false</c> if the event doesn't affect the light.</returns>
    public bool Apply(ClaudeHookEvent e, DateTimeOffset at, bool fromCloud)
    {
        var key = fromCloud ? $"cloud:{e.SessionId}" : e.SessionId;

        if (e.EventName == "SessionEnd")
        {
            store.Remove(Source, key);
            return true;
        }

        SignalState? state = e.EventName switch
        {
            "SessionStart" => SignalState.Idle,
            "UserPromptSubmit" or "PreToolUse" or "PostToolUse" => SignalState.Working,
            // idle_prompt fires a minute after every finished turn; Done already covers that.
            "Notification" when e.NotificationType is not ("idle_prompt" or "auth_success") => SignalState.NeedsAttention,
            "Stop" => SignalState.Done,
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
}
