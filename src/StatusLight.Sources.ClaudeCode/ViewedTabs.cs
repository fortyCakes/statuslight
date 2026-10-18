namespace StatusLight.Sources.ClaudeCode;

/// <summary>
/// Which Claude Code tabs you're looking at, as reported by the companion VS Code extension: the active Claude tab
/// in each editor group of a focused window. Each window reports separately and heartbeats, so a window that closes
/// or crashes without saying so drops out once its report goes stale.
/// </summary>
public sealed class ViewedTabs(TimeProvider time)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (string[] Titles, DateTimeOffset At)> _windows = [];

    public void Update(string windowId, IEnumerable<string> titles)
    {
        lock (_lock)
            _windows[windowId] = ([.. titles], time.GetUtcNow());
    }

    /// <summary>Every window's latest report, including stale ones; for diagnostics.</summary>
    public IReadOnlyList<(string WindowId, string[] Titles, DateTimeOffset At)> Snapshot()
    {
        lock (_lock)
            return [.. _windows.Select(w => (w.Key, w.Value.Titles, w.Value.At))];
    }

    public bool IsViewed(string title)
    {
        var cutoff = time.GetUtcNow() - StaleAfter;
        lock (_lock)
            return _windows.Values.Any(w => w.At > cutoff && w.Titles.Any(label => LabelMatches(label, title)));
    }

    /// <summary>
    /// VS Code shortens long tab labels to a prefix plus "…", so a label ending in "…" matches any title it's a prefix
    /// of. Two sessions whose titles share that prefix are indistinguishable; both count as viewed.
    /// </summary>
    public static bool LabelMatches(string label, string title) =>
        label == title || (label.EndsWith('…') && title.StartsWith(label[..^1].TrimEnd(), StringComparison.Ordinal));
}
