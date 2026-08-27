using Meridian.Core.Text;

namespace Meridian.Tests;

// A structural merge must not rewrite lines nobody edited. Most structured adapters cannot
// round-trip CRLF, so the driver restores the ours-side convention on write; these pin that.
public sealed class LineEndingsTests
{
    [Theory]
    [InlineData("a\r\nb\r\nc", "\r\n")]
    [InlineData("a\nb\nc", "\n")]
    [InlineData("no line breaks at all", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DetectsTheDominantConvention(string? text, string? expected) =>
        Assert.Equal(expected, LineEndings.Detect(text));

    // Mixed files have no right answer; CRLF wins because that is the platform that produces them.
    [Theory]
    [InlineData("a\r\nb\r\nc\nd", "\r\n")]   // 2 crlf, 1 lf
    [InlineData("a\r\nb\nc\nd", "\n")]       // 1 crlf, 2 lf
    [InlineData("a\r\nb\n", "\r\n")]         // tie
    public void MixedFilesResolveToTheDominantConvention(string text, string expected) =>
        Assert.Equal(expected, LineEndings.Detect(text));

    [Fact]
    public void NormalizeConvergesMixedInputIncludingLoneCarriageReturns()
    {
        Assert.Equal("a\r\nb\r\nc", LineEndings.Normalize("a\nb\rc", "\r\n"));
        Assert.Equal("a\nb\nc", LineEndings.Normalize("a\r\nb\rc", "\n"));
    }

    [Fact]
    public void NormalizeDoesNotDoubleUpAlreadyCorrectEndings()
    {
        var crlf = "a\r\nb\r\n";
        Assert.Equal(crlf, LineEndings.Normalize(crlf, "\r\n"));
    }

    [Fact]
    public void ANullReferenceLeavesTheTextAlone()
    {
        const string rendered = "a\nb\n";
        Assert.Equal(rendered, LineEndings.MatchStyleOf(rendered, "no newlines here"));
    }

    // The bug this exists for: adapters render LF, and the ours side was CRLF.
    [Fact]
    public void RenderedLfIsRestoredToTheOursSideCrlf()
    {
        const string ours = "{\r\n  \"a\": 1\r\n}";
        const string rendered = "{\n  \"a\": 2\n}";
        Assert.Equal("{\r\n  \"a\": 2\r\n}", LineEndings.MatchStyleOf(rendered, ours));
    }
}
