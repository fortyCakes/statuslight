using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using StatusLight.Sources.ClaudeCode;

namespace StatusLight.Core.Tests;

public sealed class ClaudeHookMapperTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly string _transcript = Path.GetTempFileName();
    private readonly SignalStore _store;
    private readonly ViewedTabs _viewedTabs;
    private readonly ClaudeHookMapper _mapper;

    public ClaudeHookMapperTests()
    {
        File.WriteAllText(_transcript, """{"type":"ai-title","aiTitle":"Fix the light","sessionId":"s1"}""" + "\n");
        _store = new SignalStore(_time);
        _viewedTabs = new ViewedTabs(_time);
        _mapper = new ClaudeHookMapper(_store, _viewedTabs, new TranscriptTitles(), new TestOptionsMonitor<ClaudeOptions>(new ClaudeOptions()));
    }

    public void Dispose() => File.Delete(_transcript);

    private bool Apply(string eventName, string? notificationType = null, bool fromCloud = false) =>
        _mapper.Apply(
            new ClaudeHookEvent(eventName, "s1", @"C:\src\my-repo", notificationType, TranscriptPath: fromCloud ? null : _transcript),
            _time.GetUtcNow(),
            fromCloud);

    private SignalState? State => _store.GetActive().SingleOrDefault()?.State;

    [Theory]
    [InlineData("SessionStart", SignalState.Idle)]
    [InlineData("UserPromptSubmit", SignalState.Working)]
    [InlineData("PreToolUse", SignalState.Working)]
    [InlineData("PostToolUse", SignalState.Working)]
    [InlineData("Notification", SignalState.NeedsAttention)]
    [InlineData("Stop", SignalState.Unread)]
    public void Maps_events_to_states(string eventName, SignalState expected)
    {
        Assert.True(Apply(eventName));
        Assert.Equal(expected, State);
    }

    [Fact]
    public void Stop_is_done_when_the_tab_is_in_view()
    {
        _viewedTabs.Update("window-1", ["Fix the light"]);

        Apply("Stop");

        Assert.Equal(SignalState.Done, State);
    }

    [Fact]
    public void Cloud_stop_is_done()
    {
        Apply("Stop", fromCloud: true);

        Assert.Equal(SignalState.Done, State);
    }

    [Fact]
    public void Unread_becomes_done_when_the_tab_comes_into_view()
    {
        Apply("Stop");
        _time.Advance(TimeSpan.FromMinutes(5));

        _viewedTabs.Update("window-1", ["Some other session", "Fix the light"]);
        _mapper.MarkViewedSessionsSeen(_time.GetUtcNow());

        var signal = Assert.Single(_store.GetActive());
        Assert.Equal(SignalState.Done, signal.State);
        Assert.Equal(_time.GetUtcNow(), signal.UpdatedAt);
    }

    [Fact]
    public void Viewing_a_different_tab_leaves_unread_alone()
    {
        Apply("Stop");

        _viewedTabs.Update("window-1", ["Some other session"]);
        _mapper.MarkViewedSessionsSeen(_time.GetUtcNow());

        Assert.Equal(SignalState.Unread, State);
    }

    [Fact]
    public void A_stale_window_report_does_not_count_as_viewing()
    {
        _viewedTabs.Update("window-1", ["Fix the light"]);
        _time.Advance(ViewedTabs.StaleAfter);

        Apply("Stop");

        Assert.Equal(SignalState.Unread, State);
    }

    [Fact]
    public void Idle_prompt_notification_leaves_unread_alone()
    {
        Apply("Stop");

        Assert.False(Apply("Notification", "idle_prompt"));
        Assert.Equal(SignalState.Unread, State);
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
            {"session_id":"abc","transcript_path":"C:\\t.jsonl","cwd":"/repo","hook_event_name":"Notification",
             "message":"Claude needs your permission","notification_type":"permission_prompt"}
            """;

        var e = JsonSerializer.Deserialize<ClaudeHookEvent>(json)!;

        Assert.Equal(new ClaudeHookEvent("Notification", "abc", "/repo", "permission_prompt", TranscriptPath: @"C:\t.jsonl"), e);
    }
}
