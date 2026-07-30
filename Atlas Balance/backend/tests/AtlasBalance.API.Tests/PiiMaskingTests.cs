using AtlasBalance.API.Services;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class PiiMaskingTests
{
    [Fact]
    public void MaskIban_Should_Keep_Last_Four_Chars_And_Mask_Rest()
    {
        var result = PiiMasking.MaskIban("ES9121000418450200051332");

        result.Should().Be("********************1332");
    }

    [Fact]
    public void MaskIban_Should_Return_Null_When_Value_Is_Null()
    {
        PiiMasking.MaskIban(null).Should().BeNull();
    }

    [Fact]
    public void MaskIban_Should_Return_Empty_When_Value_Is_Empty()
    {
        PiiMasking.MaskIban(string.Empty).Should().Be(string.Empty);
    }

    [Fact]
    public void MaskIban_Should_Mask_Everything_When_Value_Has_Three_Chars()
    {
        PiiMasking.MaskIban("ABC").Should().Be("***");
    }

    [Fact]
    public void MaskIban_Should_Mask_Everything_When_Value_Has_Exactly_Four_Chars()
    {
        PiiMasking.MaskIban("ABCD").Should().Be("****");
    }

    [Fact]
    public void MaskIdentificacion_Should_Keep_Last_Three_Chars_And_Mask_Rest()
    {
        PiiMasking.MaskIdentificacion("12345678Z").Should().Be("******78Z");
    }

    [Fact]
    public void MaskIdentificacion_Should_Mask_Everything_When_Value_Has_Three_Chars()
    {
        PiiMasking.MaskIdentificacion("78Z").Should().Be("***");
    }

    [Fact]
    public void MaskIdentificacion_Should_Return_Null_When_Value_Is_Null()
    {
        PiiMasking.MaskIdentificacion(null).Should().BeNull();
    }
}
