using FluentAssertions;
using GestionCaja.API.Services;
using Xunit;

namespace GestionCaja.API.Tests;

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
