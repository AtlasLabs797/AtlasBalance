using System.Net;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

// -----------------------------------------------------------------------
// V-02.07: la verificacion combina dos senales independientes porque detectan
// cosas distintas. La firma detecta contenido alterado o filas inventadas; la
// continuidad de la secuencia detecta filas borradas, que la firma no puede
// ver por definicion (una fila que ya no esta no tiene firma que validar).
// -----------------------------------------------------------------------
public sealed class AuditIntegrityServiceTests
{
    private static readonly DateTime Base = new(2026, 7, 30, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Should_Report_Integrity_When_Everything_Is_Signed_And_Contiguous()
    {
        await using var db = BuildDbContext();
        var firmador = Firmador();
        for (var i = 1; i <= 5; i++)
        {
            db.Auditorias.Add(FilaFirmada(firmador, i));
        }
        await db.SaveChangesAsync();

        var resultado = await new AuditIntegrityService(db, firmador).VerificarAsync(null, null, default);

        resultado.Integra.Should().BeTrue();
        resultado.FilasExaminadas.Should().Be(5);
        resultado.FirmasValidas.Should().Be(5);
        resultado.FirmasInvalidas.Should().Be(0);
        resultado.FilasFaltantes.Should().Be(0);
    }

    [Fact]
    public async Task Should_Detect_A_Tampered_Row()
    {
        await using var db = BuildDbContext();
        var firmador = Firmador();
        for (var i = 1; i <= 3; i++)
        {
            db.Auditorias.Add(FilaFirmada(firmador, i));
        }
        await db.SaveChangesAsync();

        // Alguien cambia el actor de una fila directamente en la BD, sin poder
        // recalcular la firma porque no tiene la clave.
        var fila = await db.Auditorias.FirstAsync(a => a.Secuencia == 2);
        fila.UsuarioId = Guid.NewGuid();
        await db.SaveChangesAsync();

        var resultado = await new AuditIntegrityService(db, firmador).VerificarAsync(null, null, default);

        resultado.Integra.Should().BeFalse();
        resultado.FirmasInvalidas.Should().Be(1);
        resultado.IdsFirmaInvalida.Should().Contain(fila.Id);
    }

    [Fact]
    public async Task Should_Detect_Deleted_Rows_As_A_Sequence_Gap()
    {
        await using var db = BuildDbContext();
        var firmador = Firmador();
        foreach (var secuencia in new[] { 1L, 2L, 7L, 8L })
        {
            db.Auditorias.Add(FilaFirmada(firmador, secuencia));
        }
        await db.SaveChangesAsync();

        var resultado = await new AuditIntegrityService(db, firmador).VerificarAsync(null, null, default);

        resultado.Integra.Should().BeFalse();
        // Faltan las secuencias 3, 4, 5 y 6.
        resultado.FilasFaltantes.Should().Be(4);
        resultado.Huecos.Should().ContainSingle();
        resultado.Huecos[0].DesdeSecuencia.Should().Be(3);
        resultado.Huecos[0].HastaSecuencia.Should().Be(6);
    }

    [Fact]
    public async Task Should_Treat_Pre_V0207_Rows_As_Unverifiable_Not_Tampered()
    {
        // Las filas anteriores a V-02.07 no llevan firma. Contarlas como
        // invalidas dispararia una alerta de integridad falsa en cada
        // instalacion que se actualice, y el operador aprenderia a ignorarla.
        await using var db = BuildDbContext();
        var firmador = Firmador();
        var antigua = FilaFirmada(firmador, 1);
        antigua.Firma = null;
        db.Auditorias.Add(antigua);
        db.Auditorias.Add(FilaFirmada(firmador, 2));
        await db.SaveChangesAsync();

        var resultado = await new AuditIntegrityService(db, firmador).VerificarAsync(null, null, default);

        resultado.Integra.Should().BeTrue();
        resultado.SinFirma.Should().Be(1);
        resultado.FirmasInvalidas.Should().Be(0);
    }

    [Fact]
    public async Task Should_Report_All_Rows_As_Invalid_When_The_Key_Rotates()
    {
        // Rotar Security:AuditSigningKey invalida la verificacion de todo lo ya
        // firmado. No es manipulacion, pero se ve igual, y por eso el job lo dice
        // explicitamente en su log. Este test fija ese comportamiento para que
        // nadie lo confunda con un bug.
        await using var db = BuildDbContext();
        db.Auditorias.Add(FilaFirmada(Firmador(), 1));
        await db.SaveChangesAsync();

        var resultado = await new AuditIntegrityService(db, Firmador("otra-clave-completamente-distinta-de-32+"))
            .VerificarAsync(null, null, default);

        resultado.Integra.Should().BeFalse();
        resultado.FirmasInvalidas.Should().Be(1);
    }

    [Fact]
    public async Task Should_Respect_The_Date_Range()
    {
        await using var db = BuildDbContext();
        var firmador = Firmador();
        var vieja = FilaFirmada(firmador, 1);
        vieja.Timestamp = Base.AddDays(-30);
        vieja.Firma = firmador.Firmar(vieja);
        db.Auditorias.Add(vieja);
        db.Auditorias.Add(FilaFirmada(firmador, 2));
        await db.SaveChangesAsync();

        var resultado = await new AuditIntegrityService(db, firmador)
            .VerificarAsync(Base.AddDays(-1), null, default);

        resultado.FilasExaminadas.Should().Be(1);
    }

    // --- helpers -----------------------------------------------------------

    private static AuditSigner Firmador(string clave = "clave-de-firma-de-integridad-de-pruebas-32")
        => new(new AuditSigningKey(clave));

    private static Auditoria FilaFirmada(AuditSigner firmador, long secuencia)
    {
        var fila = new Auditoria
        {
            Id = Guid.NewGuid(),
            Secuencia = secuencia,
            TipoAccion = AuditActions.Login,
            EntidadTipo = "USUARIOS",
            UsuarioId = Guid.NewGuid(),
            Timestamp = Base.AddMinutes(secuencia),
            IpAddress = IPAddress.Parse("10.0.0.1"),
            Origen = AuditOrigenes.Ui
        };
        fila.Firma = firmador.Firmar(fila);
        return fila;
    }

    private static AppDbContext BuildDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
