using AtlasBalance.API.Logging;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests.Logging;

public sealed class LogScrubberTests
{
    [Fact]
    public void Scrub_Should_ReturnNull_When_Null()
    {
        LogScrubber.Scrub(null).Should().BeNull();
    }

    [Fact]
    public void Scrub_Should_ReturnEmpty_When_Empty()
    {
        LogScrubber.Scrub(string.Empty).Should().Be(string.Empty);
    }

    [Fact]
    public void Scrub_Should_Strip_CrLf()
    {
        var input = "Mozilla/5.0\r\n2026-01-01 FAKE LOG ENTRY\r\n";
        var output = LogScrubber.Scrub(input);

        output.Should().NotContain("\r");
        output.Should().NotContain("\n");
        output.Should().Be("Mozilla/5.0  2026-01-01 FAKE LOG ENTRY  ");
    }

    [Fact]
    public void Scrub_Should_Strip_Tabs()
    {
        var input = "col1\tcol2\tcol3";
        LogScrubber.Scrub(input).Should().Be("col1 col2 col3");
    }

    [Fact]
    public void Scrub_Should_Truncate_At_256()
    {
        var input = new string('a', 500);
        var output = LogScrubber.Scrub(input);

        output.Should().NotBeNull();
        output!.Length.Should().Be(256);
    }

    [Fact]
    public void Scrub_Should_Keep_Clean_Ascii_Intact()
    {
        var input = "192.168.1.42 - GET /api/usuarios";
        LogScrubber.Scrub(input).Should().Be(input);
    }
}