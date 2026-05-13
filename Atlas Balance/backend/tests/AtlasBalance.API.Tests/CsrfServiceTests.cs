using FluentAssertions;
using AtlasBalance.API.Services;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class CsrfServiceTests
{
    [Fact]
    public void IsValid_Should_Handle_Length_Mismatch_And_Exact_Match()
    {
        var service = new CsrfService();

        service.IsValid("short", "muchisimo mas largo").Should().BeFalse();
        service.IsValid("abcd", "abce").Should().BeFalse();
        service.IsValid("same", "same").Should().BeTrue();
    }
}
