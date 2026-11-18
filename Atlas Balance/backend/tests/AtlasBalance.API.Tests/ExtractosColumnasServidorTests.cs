using System.Security.Claims;
using System.Text.Json;
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

/// <summary>
/// S-2 (LOTE 6): el filtro de columnas visibles se aplica EN EL SERVIDOR.
/// La regla del administrador (ColumnasVisiblesAdmin) es una restriccion y la
/// preferencia del usuario (ColumnasVisibles) solo puede estrechar la vista.
/// El conjunto efectivo = admin == null ? usuario : usuario == null ? admin :
/// admin ∩ usuario.
///
/// S-8 (LOTE 6): cuando el recurso existe pero el actor no tiene permiso, la
/// respuesta es 403; el 404 queda para "no existe".
/// </summary>
public sealed class ExtractosColumnasServidorTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static ExtractosController BuildController(AppDbContext db, Guid userId, RolUsuario role)
    {
        var controller = new ExtractosController(
            db,
            new NoOpAlertaService(),
            NullLogger<ExtractosController>.Instance,
            TestUserAccess.Create(db));
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString())
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

    private sealed class NoOpAlertaService : IAlertaService
    {
        public Task EvaluateSaldoPostAsync(Guid cuentaId, Guid? actorUserId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AlertaActivaItemResponse>> GetAlertasActivasAsync(UserAccessScope scope, Guid? paisId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AlertaActivaItemResponse>>([]);
    }

    /// <summary>
    /// Usuario GERENTE con permiso de ver una cuenta, un extracto con columna
    /// extra y la fila de preferencia del alcance (donde el administrador
    /// escribe su whitelist y el usuario escribe su preferencia).
    /// </summary>
    private static async Task<(Guid UserId, Guid CuentaId, Guid ExtractoId)> SeedAsync(
        AppDbContext db,
        RolUsuario rol,
        string? columnasVisiblesAdmin,
        string? columnasVisibles)
    {
        var userId = Guid.NewGuid();
        var paisId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var extractoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.columnas.servidor@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Columnas Servidor",
            Rol = rol,
            Activo = true,
            PrimerLogin = false
        });
        db.Paises.Add(new Pais { Id = paisId, Nombre = "Espana", CodigoIso2 = "ES", Activo = true });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Columnas Servidor", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            PaisId = paisId,
            Nombre = "Cuenta Columnas Servidor",
            BancoNombre = "CaixaBank",
            Divisa = "EUR",
            Activa = true
        });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            PaisId = paisId,
            TitularId = titularId,
            CuentaId = cuentaId,
            PuedeVerCuentas = true
        });
        db.PreferenciasUsuarioCuenta.Add(new PreferenciaUsuarioCuenta
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            PaisId = paisId,
            TitularId = titularId,
            CuentaId = cuentaId,
            ColumnasVisiblesAdmin = columnasVisiblesAdmin,
            ColumnasVisibles = columnasVisibles
        });
        db.Extractos.Add(new Extracto
        {
            Id = extractoId,
            CuentaId = cuentaId,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Concepto = "Compra con tarjeta",
            Comentarios = "Comentario interno",
            Monto = 10m,
            Saldo = 10m,
            FilaNumero = 1
        });
        db.ExtractosColumnasExtra.Add(new ExtractoColumnaExtra
        {
            Id = Guid.NewGuid(),
            ExtractoId = extractoId,
            NombreColumna = "canal",
            Valor = "Banca online"
        });
        await db.SaveChangesAsync();

        return (userId, cuentaId, extractoId);
    }

    [Fact]
    public async Task Listar_Should_Not_Return_Columns_Outside_Admin_Whitelist()
    {
        // S-2: whitelist del administrador ["fecha","monto"] y sin preferencia
        // del usuario: ni las columnas extra ni su valor salen del servidor.
        await using var db = BuildDbContext();
        var (userId, cuentaId, _) = await SeedAsync(db, RolUsuario.GERENTE, """["fecha","monto"]""", null);

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(cuentaId: cuentaId, ct: CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Data.Should().ContainSingle();
        page.Data.Single().ColumnasExtra.Should().BeEmpty();
        page.ColumnasDisponibles.Should().BeEmpty();
        // Las columnas base nullable llegan a null; las no nullable (fecha,
        // monto, saldo) se quedan por contrato de la fila.
        page.Data.Single().Concepto.Should().BeNull();
        page.Data.Single().Comentarios.Should().BeNull();
        page.Data.Single().Monto.Should().Be(10m);
    }

    [Fact]
    public async Task GetColumnasVisibles_Should_Return_Effective_Set_And_Admin_Whitelist()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId, _) = await SeedAsync(db, RolUsuario.GERENTE, """["fecha","monto"]""", null);

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.GetColumnasVisibles(cuentaId: cuentaId, ct: CancellationToken.None);

        var payload = result.Should().BeOfType<OkObjectResult>().Subject.Value;
        payload.Should().NotBeNull();
        payload!.GetType().GetProperty("columnas_visibles")!.GetValue(payload)
            .Should().BeEquivalentTo(new[] { "fecha", "monto" });
        // La whitelist del administrador viaja aparte para que el cliente no
        // ofrezca columnas prohibidas en su interruptor.
        payload.GetType().GetProperty("columnas_restringidas")!.GetValue(payload)
            .Should().BeEquivalentTo(new[] { "fecha", "monto" });
    }

    [Fact]
    public async Task Listar_Should_Intersect_Admin_Whitelist_With_User_Preference()
    {
        // S-2: la preferencia del usuario pide "concepto", pero el admin solo
        // permite fecha y monto: el conjunto efectivo es la interseccion.
        await using var db = BuildDbContext();
        var (userId, cuentaId, _) = await SeedAsync(db, RolUsuario.GERENTE, """["fecha","monto"]""", """["concepto"]""");

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(cuentaId: cuentaId, ct: CancellationToken.None);
        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Data.Single().Concepto.Should().BeNull();
        page.Data.Single().ColumnasExtra.Should().BeEmpty();

        var visibles = await controller.GetColumnasVisibles(cuentaId: cuentaId, ct: CancellationToken.None);
        var payload = visibles.Should().BeOfType<OkObjectResult>().Subject.Value;
        payload!.GetType().GetProperty("columnas_visibles")!.GetValue(payload)
            .Should().BeEquivalentTo(Array.Empty<string>());
    }

    [Fact]
    public async Task SaveColumnasVisibles_Should_Store_Intersection_With_Admin_Whitelist()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId, _) = await SeedAsync(db, RolUsuario.GERENTE, """["fecha","monto"]""", """["concepto"]""");

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.SaveColumnasVisibles(
            new SaveColumnasVisiblesRequest
            {
                CuentaId = cuentaId,
                ColumnasVisibles = ["concepto", "monto", "saldo"]
            },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var pref = await db.PreferenciasUsuarioCuenta.SingleAsync();
        // Lo guardado nunca puede ser mas ancho que la regla del administrador.
        JsonSerializer.Deserialize<string[]>(pref.ColumnasVisibles!).Should().BeEquivalentTo(new[] { "monto" });
        // La regla del administrador no se toca en este PUT.
        pref.ColumnasVisiblesAdmin.Should().Be("""["fecha","monto"]""");
    }

    [Fact]
    public async Task Listar_Should_Apply_User_Preference_When_No_Admin_Whitelist()
    {
        // Sin whitelist del administrador, la preferencia del usuario se aplica
        // tal cual (sin regresion): solo se ven las columnas que pidio.
        await using var db = BuildDbContext();
        var (userId, cuentaId, _) = await SeedAsync(db, RolUsuario.GERENTE, null, """["fecha","monto","canal"]""");

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(cuentaId: cuentaId, ct: CancellationToken.None);
        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Data.Single().Concepto.Should().BeNull();
        page.Data.Single().ColumnasExtra.Should().ContainKey("canal");
        page.ColumnasDisponibles.Should().BeEquivalentTo(new[] { "canal" });
    }

    [Fact]
    public async Task Listar_Should_Not_Filter_Columns_For_Admin()
    {
        // El admin no tiene restriccion: se ve todo, como antes.
        await using var db = BuildDbContext();
        var (userId, cuentaId, _) = await SeedAsync(db, RolUsuario.ADMIN, null, null);

        var controller = BuildController(db, userId, RolUsuario.ADMIN);

        var result = await controller.Listar(cuentaId: cuentaId, ct: CancellationToken.None);
        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Data.Single().Concepto.Should().Be("Compra con tarjeta");
        page.Data.Single().Comentarios.Should().Be("Comentario interno");
        page.Data.Single().ColumnasExtra.Should().ContainKey("canal");
        page.ColumnasDisponibles.Should().BeEquivalentTo(new[] { "canal" });
    }

    [Fact]
    public async Task Listar_Should_Apply_Admin_Whitelist_Stored_In_Broader_Scope()
    {
        // S-2: la regla del administrador puede vivir en una fila de alcance
        // MAS AMPLIO (pais) que la vista (cuenta).
        await using var db = BuildDbContext();
        var (userId, cuentaId, _) = await SeedAsync(db, RolUsuario.GERENTE, null, null);

        var paisId = await db.Cuentas.Select(c => c.PaisId).FirstAsync();
        db.PreferenciasUsuarioCuenta.Add(new PreferenciaUsuarioCuenta
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            PaisId = paisId,
            ColumnasVisiblesAdmin = """["fecha","monto"]"""
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(cuentaId: cuentaId, ct: CancellationToken.None);
        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Data.Single().Concepto.Should().BeNull();
        page.Data.Single().ColumnasExtra.Should().BeEmpty();
        page.ColumnasDisponibles.Should().BeEmpty();
    }

    [Fact]
    public async Task Actualizar_Should_Return_Forbidden_When_Extracto_Account_Is_Deleted()
    {
        // S-8: el extracto existe pero su cuenta esta borrada: el actor no
        // tiene permiso sobre el -> 403 (no 404, que queda para "no existe").
        await using var db = BuildDbContext();
        var (userId, cuentaId, extractoId) = await SeedAsync(db, RolUsuario.GERENTE, null, null);

        var cuenta = await db.Cuentas.SingleAsync(c => c.Id == cuentaId);
        cuenta.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Actualizar(extractoId, new UpdateExtractoRequest { Monto = 20m }, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Actualizar_Should_Return_NotFound_When_Extracto_Does_Not_Exist()
    {
        await using var db = BuildDbContext();
        var (userId, _, _) = await SeedAsync(db, RolUsuario.GERENTE, null, null);

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Actualizar(Guid.NewGuid(), new UpdateExtractoRequest { Monto = 20m }, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
