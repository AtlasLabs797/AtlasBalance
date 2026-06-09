using System.Text;
using FluentAssertions;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

public class AuditoriaControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Listar_Should_Include_Account_Audits_When_Filtering_By_CuentaId()
    {
        await using var db = BuildDbContext();
        var titular = new Titular
        {
            Id = Guid.NewGuid(),
            Nombre = "Titular QA",
            Tipo = TipoTitular.EMPRESA
        };
        var cuenta = new Cuenta
        {
            Id = Guid.NewGuid(),
            TitularId = titular.Id,
            Nombre = "Cuenta QA",
            Divisa = "EUR"
        };
        var extracto = new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = new DateOnly(2026, 4, 14),
            Concepto = "Movimiento QA",
            Monto = 25,
            Saldo = 250,
            FilaNumero = 1
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.Extractos.Add(extracto);
        db.Auditorias.AddRange(
            new Auditoria
            {
                Id = Guid.NewGuid(),
                TipoAccion = "cuenta_actualizada",
                EntidadTipo = "CUENTAS",
                EntidadId = cuenta.Id,
                Timestamp = new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc)
            },
            new Auditoria
            {
                Id = Guid.NewGuid(),
                TipoAccion = "extracto_celda_actualizada",
                EntidadTipo = "EXTRACTOS",
                EntidadId = extracto.Id,
                CeldaReferencia = "A1",
                ColumnaNombre = "fecha",
                ValorAnterior = "2026-04-13",
                ValorNuevo = "2026-04-14",
                Timestamp = new DateTime(2026, 4, 14, 11, 0, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();

        var controller = new AuditoriaController(db);

        var result = await controller.Listar(
            page: 1,
            pageSize: 50,
            cuentaId: cuenta.Id,
            ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<PaginatedResponse<AuditoriaListItemResponse>>().Subject;

        payload.Total.Should().Be(2);
        payload.Data.Should().HaveCount(2);
        payload.Data.Should().ContainSingle(row =>
            row.EntidadTipo == "CUENTAS" &&
            row.EntidadId == cuenta.Id &&
            row.CuentaId == cuenta.Id &&
            row.CuentaNombre == cuenta.Nombre &&
            row.TitularId == titular.Id &&
            row.TitularNombre == titular.Nombre);
    }

    [Fact]
    public async Task AuditoriaEndpoints_Should_Filter_By_PaisId()
    {
        await using var db = BuildDbContext();
        var paisAId = Guid.NewGuid();
        var paisBId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaA = new Cuenta { Id = Guid.NewGuid(), TitularId = titularId, Nombre = "Cuenta ES", Divisa = "EUR", PaisId = paisAId };
        var cuentaB = new Cuenta { Id = Guid.NewGuid(), TitularId = titularId, Nombre = "Cuenta MX", Divisa = "MXN", PaisId = paisBId };
        var extractoA = new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaA.Id, Fecha = new DateOnly(2026, 5, 1), Concepto = "ES", Monto = 1m, Saldo = 1m, FilaNumero = 1 };
        var extractoB = new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaB.Id, Fecha = new DateOnly(2026, 5, 1), Concepto = "MX", Monto = 1m, Saldo = 1m, FilaNumero = 1 };

        db.Paises.AddRange(
            new Pais { Id = paisAId, Nombre = "Espana", CodigoIso2 = "ES", Activo = true },
            new Pais { Id = paisBId, Nombre = "Mexico", CodigoIso2 = "MX", Activo = true });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Pais", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(cuentaA, cuentaB);
        db.Extractos.AddRange(extractoA, extractoB);
        db.Auditorias.AddRange(
            new Auditoria { Id = Guid.NewGuid(), TipoAccion = "cuenta_actualizada", EntidadTipo = "CUENTAS", EntidadId = cuentaA.Id, Timestamp = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc) },
            new Auditoria { Id = Guid.NewGuid(), TipoAccion = "extracto_celda_actualizada", EntidadTipo = "EXTRACTOS", EntidadId = extractoB.Id, Timestamp = new DateTime(2026, 5, 1, 11, 0, 0, DateTimeKind.Utc) });
        await db.SaveChangesAsync();

        var controller = new AuditoriaController(db);

        var listar = await controller.Listar(paisId: paisAId, ct: CancellationToken.None);
        var filtros = await controller.GetFiltros(paisAId, CancellationToken.None);
        var csvResult = await controller.ExportarCsv(paisId: paisAId, ct: CancellationToken.None);

        var page = listar.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<AuditoriaListItemResponse>>().Subject;
        page.Total.Should().Be(1);
        page.Data.Single().CuentaNombre.Should().Be("Cuenta ES");

        var filterPayload = filtros.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<AuditoriaFiltrosResponse>().Subject;
        filterPayload.Cuentas.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(cuentaA.Id);
        filterPayload.TiposAccion.Should().ContainSingle().Which.Should().Be("cuenta_actualizada");

        var csv = Encoding.UTF8.GetString(csvResult.Should().BeOfType<FileContentResult>().Subject.FileContents);
        csv.Should().Contain("Cuenta ES");
        csv.Should().NotContain("Cuenta MX");
        csv.Should().NotContain("extracto_celda_actualizada");
    }

    [Fact]
    public async Task ExportarCsv_Should_Export_All_Filtered_Rows_Without_Truncation()
    {
        await using var db = BuildDbContext();
        var titular = new Titular
        {
            Id = Guid.NewGuid(),
            Nombre = "Titular CSV",
            Tipo = TipoTitular.EMPRESA
        };
        var cuenta = new Cuenta
        {
            Id = Guid.NewGuid(),
            TitularId = titular.Id,
            Nombre = "Cuenta CSV",
            Divisa = "USD"
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);

        var baseTimestamp = new DateTime(2026, 4, 14, 10, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 10005; i++)
        {
            db.Auditorias.Add(new Auditoria
            {
                Id = Guid.NewGuid(),
                TipoAccion = "cuenta_actualizada",
                EntidadTipo = "CUENTAS",
                EntidadId = cuenta.Id,
                Timestamp = baseTimestamp.AddSeconds(i),
                DetallesJson = $"{{\"indice\":{i}}}"
            });
        }

        await db.SaveChangesAsync();

        var controller = new AuditoriaController(db);

        var result = await controller.ExportarCsv(cuentaId: cuenta.Id, ct: CancellationToken.None);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        var csv = Encoding.UTF8.GetString(file.FileContents);
        var rowCount = csv
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Length;

        rowCount.Should().Be(10006);
    }

    [Fact]
    public async Task ExportarCsv_Should_Escape_Spreadsheet_Formulas()
    {
        await using var db = BuildDbContext();
        db.Auditorias.Add(new Auditoria
        {
            Id = Guid.NewGuid(),
            TipoAccion = "extracto_celda_actualizada",
            EntidadTipo = "EXTRACTOS",
            EntidadId = Guid.NewGuid(),
            ColumnaNombre = "concepto",
            ValorAnterior = "Normal",
            ValorNuevo = "=HYPERLINK(\"http://evil.local\",\"click\")",
            Timestamp = new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var controller = new AuditoriaController(db);

        var result = await controller.ExportarCsv(ct: CancellationToken.None);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        var csv = Encoding.UTF8.GetString(file.FileContents);
        csv.Should().Contain("\"'=HYPERLINK(\"\"http://evil.local\"\",\"\"click\"\")\"");
        csv.Should().NotContain("\",\"=HYPERLINK(");
    }
}
