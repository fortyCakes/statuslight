using StatusLight.Sources.ClaudeCode;

namespace StatusLight.Core.Tests;

public sealed class ViewedTabsTests
{
    [Theory]
    [InlineData("Fix the light", "Fix the light", true)]
    [InlineData("StatusLight VS Code view…", "StatusLight VS Code view detection", true)]
    [InlineData("StatusLight VS Code view…", "StatusLight VS Code view", true)]
    [InlineData("StatusLight VS Code view…", "StatusLight VS Code panel", false)]
    [InlineData("Fix the light", "Fix the light switch", false)]
    public void Matches_full_and_truncated_labels(string label, string title, bool expected) =>
        Assert.Equal(expected, ViewedTabs.LabelMatches(label, title));
}
