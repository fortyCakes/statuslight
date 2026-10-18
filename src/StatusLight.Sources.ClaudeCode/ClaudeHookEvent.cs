using System.Text.Json.Serialization;

namespace StatusLight.Sources.ClaudeCode;

/// <summary>
/// The parts of a Claude Code hook payload we use. Local hooks POST the full payload (extra fields are ignored);
/// the cloud hook script sends only these fields, plus <see cref="Repo"/>.
/// </summary>
public sealed record ClaudeHookEvent(
    [property: JsonPropertyName("hook_event_name")] string EventName,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("cwd")] string? Cwd = null,
    [property: JsonPropertyName("notification_type")] string? NotificationType = null,
    [property: JsonPropertyName("repo")] string? Repo = null,
    [property: JsonPropertyName("transcript_path")] string? TranscriptPath = null)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(EventName) && !string.IsNullOrWhiteSpace(SessionId);
}
