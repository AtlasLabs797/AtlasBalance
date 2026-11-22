using System.Security.Claims;
using System.Text;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// S-7: la exportacion CSV de la auditoria (hasta 50.000 filas) tiene que dejar
/// rastro de quien la extrajo. El rango de auditoria es un bien forense: si
/// nadie deja constancia de quien se lo lleva, el historico deja de servir
/// para saber que ha pasado.
///
/// El patron es el mismo que usa GET /api/auditoria/integridad: fila en
/// AUDITORIAS con el actor, la accion y el detalle de lo extraido.
/// </summary>
public sealed class AuditoriaControllerExportacionCsvAuditoriaTests
{
    // S-7: accion nueva en AuditActions (mismo patron que AUDITORIA_INTEGRIDAD_FALLIDA).
    private const string TipoAccionExportacion = AuditActions.AuditoriaExportacionCsv;

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static AuditoriaController BuildController(AppDbContext db, Guid actorId)
    {
        var signer = TestAuditService.Signer();
        var controller = new AuditoriaController(
            db,
            new AuditIntegrityService(db, signer),
            TestAuditService.Create(db),
            signer);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
            new Claim(ClaimTypes.Role, nameof(AtlasBalance.API.Models.RolUsuario.ADMIN))
        ], "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    [Fact]
    public async Task ExportarCsv_Should_Leave_Audit_Trail_Identifying_The_Actor()
    {
        await using var db = BuildDbContext();
        var actorId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Export", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Export", Divisa = "EUR" });
        db.Auditorias.Add(new Auditoria
        {
            Id = Guid.NewGuid(),
            TipoAccion = "cuenta_actualizada",
            EntidadTipo = "CUENTAS",
            EntidadId = cuentaId,
            Timestamp = new DateTime(2026, 4, 14, 10, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, actorId);

        var result = await controller.ExportarCsv(cuentaId: cuentaId, ct: CancellationToken.None);

        result.Should().BeOfType<FileContentResult>();
        Encoding.UTF8.GetString(result.Should().BeOfType<FileContentResult>().Subject.FileContents)
            .Should().Contain("cuenta_actualizada");

        var fila = await db.Auditorias.SingleAsync(a => a.TipoAccion == TipoAccionExportacion);
        fila.UsuarioId.Should().Be(actorId);
        fila.EntidadTipo.Should().Be("AUDITORIAS");
        fila.EntidadId.Should().BeNull();
        fila.DetallesJson.Should().Contain("filas_exportadas");
        fila.DetallesJson.Should().Contain("cuenta_id");
    }
}
