using StatusLight.Sources.ClaudeCode;

namespace StatusLight.Core.Tests;

public sealed class TranscriptTitlesTests : IDisposable
{
    private readonly string _path = Path.GetTempFileName();
    private readonly TranscriptTitles _titles = new();

    public void Dispose() => File.Delete(_path);

    private void Append(string text) => File.AppendAllText(_path, text);

    [Fact]
    public void Uses_the_latest_ai_title()
    {
        Append("""
            {"type":"user","message":"hi"}
            {"type":"ai-title","aiTitle":"First","sessionId":"s1"}
            {"type":"ai-title","aiTitle":"Second","sessionId":"s1"}

            """);

        Assert.Equal("Second", _titles.Read(_path));
    }

    [Fact]
    public void A_custom_title_wins_over_ai_titles()
    {
        Append("""
            {"type":"custom-title","customTitle":"Renamed","sessionId":"s1"}
            {"type":"ai-title","aiTitle":"Generated","sessionId":"s1"}

            """);

        Assert.Equal("Renamed", _titles.Read(_path));
    }

    [Fact]
    public void Picks_up_titles_appended_after_the_first_read()
    {
        Append("""{"type":"ai-title","aiTitle":"First"}""" + "\n");
        Assert.Equal("First", _titles.Read(_path));

        Append("""{"type":"ai-title","aiTitle":"Second"}""" + "\n");
        Assert.Equal("Second", _titles.Read(_path));
    }

    [Fact]
    public void Waits_for_a_partly_written_line_to_finish()
    {
        Append("""{"type":"ai-title","aiTitle":"First"}""" + "\n" + """{"type":"ai-title","aiTi""");
        Assert.Equal("First", _titles.Read(_path));

        Append("""tle":"Second"}""" + "\n");
        Assert.Equal("Second", _titles.Read(_path));
    }

    [Fact]
    public void Ignores_title_text_quoted_inside_other_messages()
    {
        Append("""{"type":"user","message":"{\"type\":\"ai-title\",\"aiTitle\":\"Fake\"}"}""" + "\n");

        Assert.Null(_titles.Read(_path));
    }

    [Fact]
    public void Missing_file_has_no_title() =>
        Assert.Null(_titles.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl")));
}
