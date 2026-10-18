using System.Text;
using System.Text.Json;

namespace StatusLight.Sources.ClaudeCode;

/// <summary>
/// Reads a session's tab title from its transcript (JSONL): the latest <c>custom-title</c> if it was renamed, otherwise
/// the latest <c>ai-title</c>. Transcripts only grow, so each read resumes where the previous one stopped.
/// </summary>
public sealed class TranscriptTitles
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _cache = [];

    public string? Read(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        lock (_lock)
        {
            try
            {
                var entry = _cache.GetValueOrDefault(path) ?? new Entry();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                if (stream.Length < entry.Offset)
                    entry = new Entry(); // File was replaced; start over.

                if (stream.Length > entry.Offset)
                {
                    stream.Seek(entry.Offset, SeekOrigin.Begin);
                    var bytes = new byte[stream.Length - entry.Offset];
                    stream.ReadExactly(bytes);

                    // Stop at the last complete line; a partly written one is picked up next time.
                    var end = Array.LastIndexOf(bytes, (byte)'\n');
                    if (end >= 0)
                    {
                        foreach (var line in Encoding.UTF8.GetString(bytes, 0, end).Split('\n'))
                            Scan(line, entry);
                        entry.Offset += end + 1;
                    }
                }

                _cache[path] = entry;
                return entry.CustomTitle ?? entry.AiTitle;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    private static void Scan(string line, Entry entry)
    {
        // Cheap filter first: only title records need parsing.
        if (!line.Contains("-title\"", StringComparison.Ordinal))
            return;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type))
                return;

            switch (type.GetString())
            {
                case "ai-title" when root.TryGetProperty("aiTitle", out var ai) && ai.GetString() is { Length: > 0 } a:
                    entry.AiTitle = a;
                    break;
                case "custom-title" when root.TryGetProperty("customTitle", out var custom) && custom.GetString() is { Length: > 0 } c:
                    entry.CustomTitle = c;
                    break;
            }
        }
        catch (JsonException)
        {
        }
    }

    private sealed class Entry
    {
        public long Offset;
        public string? AiTitle;
        public string? CustomTitle;
    }
}
