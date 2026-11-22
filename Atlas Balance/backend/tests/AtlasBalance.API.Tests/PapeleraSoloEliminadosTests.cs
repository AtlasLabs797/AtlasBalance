using System.Security.Claims;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// F-2: la papelera pedia 500 filas de cada listado y filtraba en cliente por
/// deleted_at. Como la paginacion del backend cuenta TODAS las filas (vivas +
/// eliminadas), los eliminados mas antiguos quedaban fuera de la pagina y no
/// se podian restaurar.
///
/// El parametro soloEliminados se aplica ANTES de contar y paginar, de modo
/// que "total" y las paginas cubren unicamente las filas eliminadas.
/// </summary>
public sealed class PapeleraSoloEliminadosTests
{
    [Fact]
    public async Task Titulares_Listar_Con_SoloEliminados_Debe_Contar_Solo_Las_Filas_Eliminadas()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        db.Usuarios.Add(Admin(userId, "admin.papelera.titulares@test.local"));
        var titularVivo = Guid.NewGuid();
        var titularEliminado1 = Guid.NewGuid();
        var titularEliminado2 = Guid.NewGuid();
        db.Titulares.AddRange(
            new Titular { Id = titularVivo, Nombre = "Titular Vivo", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = titularEliminado1, Nombre = "Titular Eliminado 1", Tipo = TipoTitular.EMPRESA, DeletedAt = DateTime.UtcNow, DeletedById = userId },
            new Titular { Id = titularEliminado2, Nombre = "Titular Eliminado 2", Tipo = TipoTitular.EMPRESA, DeletedAt = DateTime.UtcNow, DeletedById = userId });
        await db.SaveChangesAsync();

        var controller = BuildTitularesController(db, userId, RolUsuario.ADMIN);

        var result = await controller.Listar(page: 1, pageSize: 50, sortBy: "nombre", sortDir: "asc", search: null, tipoTitular: null, paisId: null, incluirEliminados: false, soloEliminados: true, CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<TitularListItemResponse>>().Subject;
        page.Total.Should().Be(2, "el total solo puede contar las filas eliminadas");
        page.TotalPages.Should().Be(1);
        page.Data.Should().HaveCount(2);
        page.Data.Should().OnlyContain(t => t.DeletedAt != null);
    }

    [Fact]
    public async Task Cuentas_Listar_Con_SoloEliminados_Debe_Contar_Solo_Las_Filas_Eliminadas()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        db.Usuarios.Add(Admin(userId, "admin.papelera.cuentas@test.local"));
        var titularId = Guid.NewGuid();
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Cuentas", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularId, Nombre = "Cuenta Viva", Divisa = "EUR", Activa = true },
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularId, Nombre = "Cuenta Eliminada 1", Divisa = "EUR", Activa = true, DeletedAt = DateTime.UtcNow, DeletedById = userId },
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularId, Nombre = "Cuenta Eliminada 2", Divisa = "EUR", Activa = true, DeletedAt = DateTime.UtcNow, DeletedById = userId });
        await db.SaveChangesAsync();

        var controller = BuildCuentasController(db, userId, RolUsuario.ADMIN);

        var result = await controller.Listar(page: 1, pageSize: 50, sortBy: "fecha_creacion", sortDir: "desc", search: null, titularId: null, paisId: null, tipoTitular: null, tipoCuenta: null, incluirEliminados: false, soloEliminados: true, CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<CuentaListItemResponse>>().Subject;
        page.Total.Should().Be(2, "el total solo puede contar las filas eliminadas");
        page.TotalPages.Should().Be(1);
        page.Data.Should().HaveCount(2);
        page.Data.Should().OnlyContain(c => c.DeletedAt != null);
    }

    [Fact]
    public async Task Extractos_Listar_Con_SoloEliminados_Debe_Contar_Solo_Las_Filas_Eliminadas()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        db.Usuarios.Add(Admin(userId, "admin.papelera.extractos@test.local"));
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Extractos", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Extractos", Divisa = "USD", Activa = true });
        db.Extractos.AddRange(
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaId, Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date), Monto = 10m, Saldo = 10m, FilaNumero = 1 },
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaId, Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date), Monto = 20m, Saldo = 30m, FilaNumero = 2, DeletedAt = DateTime.UtcNow, DeletedById = userId },
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaId, Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date), Monto = 30m, Saldo = 60m, FilaNumero = 3, DeletedAt = DateTime.UtcNow, DeletedById = userId });
        await db.SaveChangesAsync();

        var controller = BuildExtractosController(db, userId, RolUsuario.ADMIN);

        var result = await controller.Listar(incluirEliminados: false, soloEliminados: true, ct: CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Total.Should().Be(2, "el total solo puede contar las filas eliminadas");
        page.TotalPages.Should().Be(1);
        page.Data.Should().HaveCount(2);
        page.Data.Should().OnlyContain(e => e.DeletedAt != null);
        // La papelera muestra el importe en la divisa real de la cuenta.
        page.Data.Should().OnlyContain(e => e.Divisa == "USD");
    }

    [Fact]
    public async Task Usuarios_Listar_Con_SoloEliminados_Debe_Contar_Solo_Las_Filas_Eliminadas()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        db.Usuarios.Add(Admin(userId, "admin.papelera.usuarios@test.local"));
        db.Usuarios.Add(new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "eliminado.papelera@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Usuario Eliminado",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            PrimerLogin = false,
            DeletedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = BuildUsuariosController(db, userId);

        var result = await controller.Listar(page: 1, pageSize: 50, sortBy: "fecha_creacion", sortDir: "desc", search: null, incluirEliminados: false, soloEliminados: true, CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<UsuarioListItemResponse>>().Subject;
        page.Total.Should().Be(1, "el total solo puede contar las filas eliminadas");
        page.TotalPages.Should().Be(1);
        page.Data.Should().ContainSingle();
        page.Data.Single().DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Titulares_Listar_Con_SoloEliminados_No_Aplica_A_Un_Usuario_Sin_Permisos_De_Administrador()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.papelera@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Papelera",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        var titularId = Guid.NewGuid();
        db.Titulares.AddRange(
            new Titular { Id = titularId, Nombre = "Titular Vivo", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = Guid.NewGuid(), Nombre = "Titular Eliminado", Tipo = TipoTitular.EMPRESA, DeletedAt = DateTime.UtcNow });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildTitularesController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(page: 1, pageSize: 50, sortBy: "nombre", sortDir: "asc", search: null, tipoTitular: null, paisId: null, incluirEliminados: false, soloEliminados: true, CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<TitularListItemResponse>>().Subject;
        page.Total.Should().Be(1, "sin permisos de administrador no se ven las filas eliminadas");
        page.Data.Should().ContainSingle();
        page.Data.Single().DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Titular_Eliminado_Serializa_Deleted_At_Con_El_Nombre_Que_Lee_La_Papelera()
    {
        // El frontend (PapeleraPage) lee row.deleted_at: con la politica snake_case y
        // WhenWritingNull de Program.cs el campo debe viajar con ese nombre.
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        var json = System.Text.Json.JsonSerializer.Serialize(
            new TitularListItemResponse { Nombre = "T", Tipo = "EMPRESA", DeletedAt = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc) },
            options);

        json.Should().Contain("\"deleted_at\":\"2026-11-01T10:00:00Z\"");
    }

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Usuario Admin(Guid id, string email) => new()
    {
        Id = id,
        Email = email,
        PasswordHash = "hash",
        NombreCompleto = "Admin Papelera",
        Rol = RolUsuario.ADMIN,
        Activo = true,
        PrimerLogin = false
    };

    private static void SetUser(ControllerBase controller, Guid userId, RolUsuario role)
    {
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
    }

    private static UserAccessService BuildUserAccessService(AppDbContext db) =>
        new(db, new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance), Options.Create(new CachingOptions()));

    private static TitularesController BuildTitularesController(AppDbContext db, Guid userId, RolUsuario role)
    {
        var controller = new TitularesController(db, BuildUserAccessService(db), TestAuditService.Create(db));
        SetUser(controller, userId, role);
        return controller;
    }

    private static CuentasController BuildCuentasController(AppDbContext db, Guid userId, RolUsuario role)
    {
        var controller = new CuentasController(db, BuildUserAccessService(db), TestAuditService.Create(db), new NoOpPlazoFijoService());
        SetUser(controller, userId, role);
        return controller;
    }

    private static ExtractosController BuildExtractosController(AppDbContext db, Guid userId, RolUsuario role)
    {
        var controller = new ExtractosController(db, new NoOpAlertaService(), NullLogger<ExtractosController>.Instance, TestUserAccess.Create(db));
        SetUser(controller, userId, role);
        return controller;
    }

    private static UsuariosController BuildUsuariosController(AppDbContext db, Guid userId)
    {
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        SetUser(controller, userId, RolUsuario.ADMIN);
        return controller;
    }

    private sealed class NoOpPlazoFijoService : IPlazoFijoService
    {
        public Task<int> ProcesarVencimientosAsync(DateOnly hoy, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<PlazoFijoResponse> RenovarAsync(Guid cuentaId, RenovarPlazoFijoRequest request, Guid? actorUserId, HttpContext httpContext, CancellationToken cancellationToken)
            => Task.FromResult(new PlazoFijoResponse { CuentaId = cuentaId });
    }

    private sealed class NoOpAlertaService : IAlertaService
    {
        public Task EvaluateSaldoPostAsync(Guid cuentaId, Guid? actorUserId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AlertaActivaItemResponse>> GetAlertasActivasAsync(UserAccessScope scope, Guid? paisId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AlertaActivaItemResponse>>([]);
    }
}
