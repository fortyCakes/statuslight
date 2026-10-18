using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using StatusLight.Sources.ClaudeCode;

namespace StatusLight.Core.Tests;

public class ClaudeHookMapperTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly SignalStore _store;
    private readonly ClaudeHookMapper _mapper;

    public ClaudeHookMapperTests()
    {
        _store = new SignalStore(_time);
        _mapper = new ClaudeHookMapper(_store, new TestOptionsMonitor<ClaudeOptions>(new ClaudeOptions()));
    }

    private bool Apply(string eventName, string? notificationType = null, bool fromCloud = false) =>
        _mapper.Apply(new ClaudeHookEvent(eventName, "s1", @"C:\src\my-repo", notificationType), _time.GetUtcNow(), fromCloud);

    private SignalState? State => _store.GetActive().SingleOrDefault()?.State;

    [Theory]
    [InlineData("SessionStart", SignalState.Idle)]
    [InlineData("UserPromptSubmit", SignalState.Working)]
    [InlineData("PreToolUse", SignalState.Working)]
    [InlineData("PostToolUse", SignalState.Working)]
    [InlineData("Notification", SignalState.NeedsAttention)]
    [InlineData("Stop", SignalState.Done)]
    public void Maps_events_to_states(string eventName, SignalState expected)
    {
        Assert.True(Apply(eventName));
        Assert.Equal(expected, State);
    }

    [Fact]
    public void Idle_prompt_notification_leaves_done_alone()
    {
        Apply("Stop");

        Assert.False(Apply("Notification", "idle_prompt"));
        Assert.Equal(SignalState.Done, State);
    }

    [Fact]
    public void Permission_prompt_needs_attention()
    {
        Apply("PreToolUse");

        Apply("Notification", "permission_prompt");

        Assert.Equal(SignalState.NeedsAttention, State);
    }

    [Fact]
    public void Session_end_removes_the_signal()
    {
        Apply("UserPromptSubmit");

        Apply("SessionEnd");

        Assert.Null(State);
    }

    [Fact]
    public void Cloud_sessions_get_a_separate_key_and_label_from_cwd()
    {
        Apply("UserPromptSubmit", fromCloud: true);

        var signal = Assert.Single(_store.GetActive());
        Assert.Equal("cloud:s1", signal.Key);
        Assert.Equal("my-repo", signal.Label);
    }

    [Fact]
    public void Parses_a_full_hook_payload()
    {
        const string json = """
            {"session_id":"abc","transcript_path":"x","cwd":"/repo","hook_event_name":"Notification",
             "message":"Claude needs your permission","notification_type":"permission_prompt"}
            """;

        var e = JsonSerializer.Deserialize<ClaudeHookEvent>(json)!;

        Assert.Equal(new ClaudeHookEvent("Notification", "abc", "/repo", "permission_prompt"), e);
    }
}
