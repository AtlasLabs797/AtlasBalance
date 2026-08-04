using AtlasBalance.API.Validation;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// V-02.07: el rango invertido solo se rechazaba en los endpoints de OpenClaw.
/// En auditoria y en el listado de extractos las fechas iban directas a la query
/// y un rango al reves devolvia cero filas con un 200, indistinguible de "no hay
/// datos".
/// </summary>
public class DateRangeValidatorTests
{
    [Fact]
    public void Should_Reject_InvertedRange()
    {
        var ok = DateRangeValidator.TryValidate(
            new DateOnly(2026, 3, 31),
            new DateOnly(2026, 3, 1),
            out var error);

        ok.Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    [Fact]
    public void Should_Accept_SameDay()
    {
        // Filtrar un unico dia es lo normal, no un rango invalido.
        var dia = new DateOnly(2026, 3, 15);

        DateRangeValidator.TryValidate(dia, dia, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Should_Accept_OpenEndedRanges(bool conDesde, bool conHasta)
    {
        // Un extremo sin fecha significa "sin limite por ese lado", no un error.
        var desde = conDesde ? new DateOnly(2026, 1, 1) : (DateOnly?)null;
        var hasta = conHasta ? new DateOnly(2026, 1, 1) : (DateOnly?)null;

        DateRangeValidator.TryValidate(desde, hasta, out _).Should().BeTrue();
    }

    [Fact]
    public void Should_Accept_NormalRange()
    {
        var ok = DateRangeValidator.TryValidate(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            out var error);

        ok.Should().BeTrue();
        error.Should().BeEmpty();
    }
}
