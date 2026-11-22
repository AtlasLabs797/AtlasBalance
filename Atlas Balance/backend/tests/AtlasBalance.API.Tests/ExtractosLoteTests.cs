using System.Security.Claims;
using FluentAssertions;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

// F-7: borrado y marcado en lote como UNA peticion atomica. Si una fila no
// existe, no es accesible o el actor no puede tocarla, no se aplica ninguna.
public sealed class ExtractosLoteTests
{
    private sealed record Escenario(AppDbContext Db, Guid UserId, List<Guid> ExtractosPermitidos, Guid ExtractoSinPermiso);

    private static async Task<Escenario> CrearEscenarioAsync(bool puedeEliminar = true, bool puedeEditar = true, string? columnasEditables = null)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaA = Guid.NewGuid();
        var cuentaB = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "lote@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Lote",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Lote", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaA, TitularId = titularId, Nombre = "Permitida", Divisa = "EUR", Activa = true });
        db.Cuentas.Add(new Cuenta { Id = cuentaB, TitularId = titularId, Nombre = "Sin permiso", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaA,
            PuedeEditarLineas = puedeEditar,
            PuedeEliminarLineas = puedeEliminar
        });
        if (columnasEditables is not null)
        {
            db.PreferenciasUsuarioCuenta.Add(new PreferenciaUsuarioCuenta
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                CuentaId = cuentaA,
                ColumnasEditables = columnasEditables
            });
        }

        var permitidos = new List<Guid>();
        for (var i = 1; i <= 3; i++)
        {
            var id = Guid.NewGuid();
            permitidos.Add(id);
            db.Extractos.Add(new Extracto { Id = id, CuentaId = cuentaA, Fecha = new DateOnly(2026, 1, i), Concepto = $"A{i}", Monto = 10m, Saldo = 10m, FilaNumero = i });
        }
        var ajeno = Guid.NewGuid();
        db.Extractos.Add(new Extracto { Id = ajeno, CuentaId = cuentaB, Fecha = new DateOnly(2026, 1, 1), Concepto = "B1", Monto = 10m, Saldo = 10m, FilaNumero = 1 });
        await db.SaveChangesAsync();

        return new Escenario(db, userId, permitidos, ajeno);
    }

    private static ExtractosController BuildController(AppDbContext db, Guid userId)
    {
        var controller = new ExtractosController(db, new NoOpAlertaService(), NullLogger<ExtractosController>.Instance, TestUserAccess.Create(db));
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, RolUsuario.GERENTE.ToString())
        ], "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    [Fact]
    public async Task EliminarLote_Should_Delete_All_And_Audit_Each_Row()
    {
        var e = await CrearEscenarioAsync();
        await using var db = e.Db;
        var controller = BuildController(db, e.UserId);

        var result = await controller.EliminarLote(new LoteEliminarExtractosRequest { Ids = [.. e.ExtractosPermitidos] }, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<LoteExtractosResponse>().Which.Procesados.Should().Be(3);
        (await db.Extractos.CountAsync(x => e.ExtractosPermitidos.Contains(x.Id))).Should().Be(0);
        var auditorias = await db.Auditorias.Where(a => a.TipoAccion == "extracto_eliminado").ToListAsync();
        auditorias.Select(a => a.EntidadId).Should().BeEquivalentTo(e.ExtractosPermitidos);
    }

    [Fact]
    public async Task EliminarLote_Should_Deduplicate_Ids()
    {
        var e = await CrearEscenarioAsync();
        await using var db = e.Db;
        var controller = BuildController(db, e.UserId);
        var id = e.ExtractosPermitidos[0];

        var result = await controller.EliminarLote(new LoteEliminarExtractosRequest { Ids = [id, id, id] }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<LoteExtractosResponse>().Which.Procesados.Should().Be(1);
        (await db.Auditorias.CountAsync(a => a.TipoAccion == "extracto_eliminado")).Should().Be(1);
    }

    [Fact]
    public async Task EliminarLote_Should_Apply_Nothing_When_One_Row_Has_No_Permission()
    {
        var e = await CrearEscenarioAsync();
        await using var db = e.Db;
        var controller = BuildController(db, e.UserId);

        var result = await controller.EliminarLote(new LoteEliminarExtractosRequest { Ids = [e.ExtractosPermitidos[0], e.ExtractoSinPermiso] }, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await db.Extractos.CountAsync()).Should().Be(4);
        (await db.Auditorias.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task EliminarLote_Should_Apply_Nothing_When_One_Row_Does_Not_Exist()
    {
        var e = await CrearEscenarioAsync();
        await using var db = e.Db;
        var controller = BuildController(db, e.UserId);

        var result = await controller.EliminarLote(new LoteEliminarExtractosRequest { Ids = [e.ExtractosPermitidos[0], Guid.NewGuid()] }, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        (await db.Extractos.CountAsync()).Should().Be(4);
        (await db.Auditorias.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task EliminarLote_Should_Forbid_Without_Delete_Permission()
    {
        var e = await CrearEscenarioAsync(puedeEliminar: false);
        await using var db = e.Db;
        var controller = BuildController(db, e.UserId);

        var result = await controller.EliminarLote(new LoteEliminarExtractosRequest { Ids = [.. e.ExtractosPermitidos] }, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await db.Extractos.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task EliminarLote_Should_Reject_Empty_And_Oversized_Lists()
    {
        var e = await CrearEscenarioAsync();
        await using var db = e.Db;
        var controller = BuildController(db, e.UserId);

        var vacio = await controller.EliminarLote(new LoteEliminarExtractosRequest { Ids = [] }, CancellationToken.None);
        vacio.Should().BeOfType<BadRequestObjectResult>();

        var grande = await controller.EliminarLote(
            new LoteEliminarExtractosRequest { Ids = [.. Enumerable.Range(0, ExtractosController.MaxIdsLote + 1).Select(_ => Guid.NewGuid())] },
            CancellationToken.None);
        grande.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task FlagLote_Should_Flag_All_With_Note_And_Audit_Each_Row()
    {
        var e = await CrearEscenarioAsync();
        await using var db = e.Db;
        var controller = BuildController(db, e.UserId);

        var result = await controller.FlagLote(new LoteFlagExtractosRequest
        {
            Flagged = true,
            Items = [.. e.ExtractosPermitidos.Select(id => new LoteFlagExtractoItem { Id = id, Nota = " revisar " })]
        }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<LoteExtractosResponse>().Which.Procesados.Should().Be(3);
        var filas = await db.Extractos.Where(x => e.ExtractosPermitidos.Contains(x.Id)).ToListAsync();
        filas.Should().OnlyContain(x => x.Flagged && x.FlaggedNota == "revisar" && x.FlaggedById == e.UserId);
        (await db.Auditorias.CountAsync(a => a.TipoAccion == "extracto_toggle_flag" && a.ColumnaNombre == "flagged")).Should().Be(3);
        (await db.Auditorias.CountAsync(a => a.TipoAccion == "extracto_toggle_flag" && a.ColumnaNombre == "flagged_nota")).Should().Be(3);
    }

    [Fact]
    public async Task FlagLote_Should_Apply_Nothing_When_One_Row_Has_No_Permission()
    {
        var e = await CrearEscenarioAsync();
        await using var db = e.Db;
        var controller = BuildController(db, e.UserId);

        var result = await controller.FlagLote(new LoteFlagExtractosRequest
        {
            Flagged = true,
            Items = [new() { Id = e.ExtractosPermitidos[0] }, new() { Id = e.ExtractoSinPermiso }]
        }, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await db.Extractos.CountAsync(x => x.Flagged)).Should().Be(0);
        (await db.Auditorias.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task FlagLote_Should_Apply_Nothing_When_One_Row_Does_Not_Exist()
    {
        var e = await CrearEscenarioAsync();
        await using var db = e.Db;
        var controller = BuildController(db, e.UserId);

        var result = await controller.FlagLote(new LoteFlagExtractosRequest
        {
            Flagged = true,
            Items = [new() { Id = e.ExtractosPermitidos[0] }, new() { Id = Guid.NewGuid() }]
        }, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        (await db.Extractos.CountAsync(x => x.Flagged)).Should().Be(0);
    }

    [Fact]
    public async Task FlagLote_Should_Respect_Editable_Columns_Like_The_Single_Endpoint()
    {
        // Solo puede editar la nota, no el flag: marcar (cambia flagged) esta prohibido.
        var e = await CrearEscenarioAsync(columnasEditables: """["flagged_nota"]""");
        await using var db = e.Db;
        var controller = BuildController(db, e.UserId);

        var result = await controller.FlagLote(new LoteFlagExtractosRequest
        {
            Flagged = true,
            Items = [.. e.ExtractosPermitidos.Select(id => new LoteFlagExtractoItem { Id = id, Nota = "x" })]
        }, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await db.Extractos.CountAsync(x => x.Flagged)).Should().Be(0);
    }

    [Fact]
    public async Task FlagLote_Should_Unflag_And_Clear_Note()
    {
        var e = await CrearEscenarioAsync();
        await using var db = e.Db;
        foreach (var x in await db.Extractos.Where(x => e.ExtractosPermitidos.Contains(x.Id)).ToListAsync())
        {
            x.Flagged = true;
            x.FlaggedNota = "n";
            x.FlaggedById = e.UserId;
            x.FlaggedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        var controller = BuildController(db, e.UserId);

        var result = await controller.FlagLote(new LoteFlagExtractosRequest
        {
            Flagged = false,
            Items = [.. e.ExtractosPermitidos.Select(id => new LoteFlagExtractoItem { Id = id })]
        }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        (await db.Extractos.Where(x => e.ExtractosPermitidos.Contains(x.Id)).ToListAsync())
            .Should().OnlyContain(x => !x.Flagged && x.FlaggedNota == null && x.FlaggedAt == null && x.FlaggedById == null);
    }

    [Fact]
    public async Task FlagLote_Should_Reject_Empty_And_Oversized_Lists()
    {
        var e = await CrearEscenarioAsync();
        await using var db = e.Db;
        var controller = BuildController(db, e.UserId);

        (await controller.FlagLote(new LoteFlagExtractosRequest { Flagged = true, Items = [] }, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();
        (await controller.FlagLote(new LoteFlagExtractosRequest
        {
            Flagged = true,
            Items = [.. Enumerable.Range(0, ExtractosController.MaxIdsLote + 1).Select(_ => new LoteFlagExtractoItem { Id = Guid.NewGuid() })]
        }, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
    }

    private sealed class NoOpAlertaService : IAlertaService
    {
        public Task EvaluateSaldoPostAsync(Guid cuentaId, Guid? actorUserId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AlertaActivaItemResponse>> GetAlertasActivasAsync(UserAccessScope scope, Guid? paisId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AlertaActivaItemResponse>>([]);
    }
}
