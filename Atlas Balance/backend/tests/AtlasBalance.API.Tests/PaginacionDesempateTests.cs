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
/// B-15: los listados paginados necesitan un desempate estable por clave
/// primaria DESPUES del campo de ordenacion que pide el usuario. Sin el, dos
/// filas con el mismo valor de orden pueden caer en paginas distintas o
/// desaparecer entre peticiones.
///
/// En cada test las filas empatadas se insertan en orden de clave primaria
/// DESCENDENTE: ese es el orden que devuelve la base de datos cuando falta el
/// desempate, asi que la asercion (empates ordenados por clave primaria
/// ASCENDENTE) solo puede cumplirse con el desempate aplicado.
/// </summary>
public sealed class PaginacionDesempateTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Listar_Extractos_Should_Desempatar_Por_Id_Cuando_El_Importe_Es_Igual()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var fecha = new DateOnly(2026, 5, 1);

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.desempate@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Desempate",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Desempate", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Desempate", Divisa = "EUR", Activa = true });

        var ids = IdsDescendientes(4);
        foreach (var id in ids)
        {
            db.Extractos.Add(new Extracto
            {
                Id = id,
                CuentaId = cuentaId,
                Fecha = fecha,
                Concepto = "Mismo importe",
                Monto = 25m,
                Saldo = 25m,
                FilaNumero = 1
            });
        }
        await db.SaveChangesAsync();

        var controller = BuildExtractosController(db, userId);

        var pagina1 = await controller.Listar(page: 1, pageSize: 2, sortBy: "monto", sortDir: "asc", ct: CancellationToken.None);
        var pagina2 = await controller.Listar(page: 2, pageSize: 2, sortBy: "monto", sortDir: "asc", ct: CancellationToken.None);

        var ordenados = IdsDeExtractos(pagina1).Concat(IdsDeExtractos(pagina2)).ToArray();

        ordenados.Should().Equal(ids.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Listar_Auditoria_Should_Desempatar_Por_Id_Cuando_El_Timestamp_Es_Igual()
    {
        await using var db = BuildDbContext();
        var timestamp = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);

        var ids = IdsDescendientes(4);
        foreach (var id in ids)
        {
            db.Auditorias.Add(new Auditoria
            {
                Id = id,
                TipoAccion = "cuenta_actualizada",
                EntidadTipo = "CUENTAS",
                EntidadId = Guid.NewGuid(),
                Timestamp = timestamp
            });
        }
        await db.SaveChangesAsync();

        var controller = BuildAuditoriaController(db);

        var pagina1 = await controller.Listar(page: 1, pageSize: 2, ct: CancellationToken.None);
        var pagina2 = await controller.Listar(page: 2, pageSize: 2, ct: CancellationToken.None);

        var ordenados = IdsDeAuditoria(pagina1).Concat(IdsDeAuditoria(pagina2)).ToArray();

        ordenados.Should().Equal(ids.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task GetComisionesAsync_Should_Desempatar_Por_ExtractoId_Cuando_Fecha_E_Importe_Son_Iguales()
    {
        await using var db = BuildDbContext();
        var cuentaId = await SeedCuentaAsync(db);

        var ids = IdsDescendientes(4);
        foreach (var id in ids)
        {
            db.Extractos.Add(new Extracto
            {
                Id = id,
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 1),
                Concepto = "Comision mantenimiento",
                Monto = -60m,
                Saldo = 100m,
                FilaNumero = 1
            });
        }
        await db.SaveChangesAsync();

        var sut = BuildRevisionService(db);

        var pagina1 = await sut.GetComisionesAsync(AdminScope(), new RevisionQueryRequest { Page = 1, PageSize = 2 }, CancellationToken.None);
        var pagina2 = await sut.GetComisionesAsync(AdminScope(), new RevisionQueryRequest { Page = 2, PageSize = 2 }, CancellationToken.None);

        var ordenados = pagina1.Data.Select(x => x.ExtractoId).Concat(pagina2.Data.Select(x => x.ExtractoId)).ToArray();

        pagina1.Total.Should().Be(4);
        ordenados.Should().Equal(ids.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task GetSegurosAsync_Should_Desempatar_Por_ExtractoId_Cuando_Fecha_E_Importe_Son_Iguales()
    {
        await using var db = BuildDbContext();
        var cuentaId = await SeedCuentaAsync(db);

        var ids = IdsDescendientes(4);
        foreach (var id in ids)
        {
            db.Extractos.Add(new Extracto
            {
                Id = id,
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 1),
                Concepto = "Recibo poliza MAPFRE hogar",
                Monto = -180m,
                Saldo = 100m,
                FilaNumero = 1
            });
        }
        await db.SaveChangesAsync();

        var sut = BuildRevisionService(db);

        var pagina1 = await sut.GetSegurosAsync(AdminScope(), new RevisionQueryRequest { Page = 1, PageSize = 2 }, CancellationToken.None);
        var pagina2 = await sut.GetSegurosAsync(AdminScope(), new RevisionQueryRequest { Page = 2, PageSize = 2 }, CancellationToken.None);

        var ordenados = pagina1.Data.Select(x => x.ExtractoId).Concat(pagina2.Data.Select(x => x.ExtractoId)).ToArray();

        pagina1.Total.Should().Be(4);
        ordenados.Should().Equal(ids.OrderBy(x => x).ToArray());
    }

    private static Guid[] IdsDescendientes(int cantidad)
        => Enumerable.Range(0, cantidad).Select(_ => Guid.NewGuid()).OrderByDescending(x => x).ToArray();

    private static Guid[] IdsDeExtractos(IActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var page = ok.Value.Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        return page.Data.Select(x => x.Id).ToArray();
    }

    private static Guid[] IdsDeAuditoria(IActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var page = ok.Value.Should().BeOfType<PaginatedResponse<AuditoriaListItemResponse>>().Subject;
        return page.Data.Select(x => x.Id).ToArray();
    }

    private static async Task<Guid> SeedCuentaAsync(AppDbContext db)
    {
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Revision", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Revision", Divisa = "EUR", Activa = true });
        await db.SaveChangesAsync();

        return cuentaId;
    }

    private static RevisionService BuildRevisionService(AppDbContext db)
        => new(db, new UserAccessService(db, new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance), Options.Create(new CachingOptions())));

    private static UserAccessScope AdminScope() => new()
    {
        UserId = Guid.NewGuid(),
        IsAdmin = true,
        HasPermissions = true,
        HasGlobalAccess = true
    };

    private static ExtractosController BuildExtractosController(AppDbContext db, Guid userId)
    {
        var controller = new ExtractosController(db, new NoOpAlertaService(), NullLogger<ExtractosController>.Instance, TestUserAccess.Create(db));
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, RolUsuario.ADMIN.ToString())
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

    private static AuditoriaController BuildAuditoriaController(AppDbContext db)
    {
        var signer = TestAuditService.Signer();
        return new AuditoriaController(
            db,
            new AuditIntegrityService(db, signer),
            TestAuditService.Create(db),
            signer)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private sealed class NoOpAlertaService : IAlertaService
    {
        public Task EvaluateSaldoPostAsync(Guid cuentaId, Guid? actorUserId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AlertaActivaItemResponse>> GetAlertasActivasAsync(UserAccessScope scope, Guid? paisId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AlertaActivaItemResponse>>([]);
    }
}
