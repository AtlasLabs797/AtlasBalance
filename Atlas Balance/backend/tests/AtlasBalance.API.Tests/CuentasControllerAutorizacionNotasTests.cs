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
/// S-8: PATCH /api/cuentas/{id}/notas tiene que distinguir "no existe" (404)
/// de "existe pero el actor no tiene permiso" (403).
///
/// Escenarios cubiertos:
/// - GERENTE con PuedeVerCuentas + PuedeEditarLineas sobre la cuenta:
///   200 y la nota se guarda.
/// - GERENTE con permisos, pero solo sobre otro titular: 403 y la nota NO se
///   modifica (la cuenta existe, el permiso no).
/// - ADMIN con un id de cuenta que no existe: 404.
/// </summary>
public sealed class CuentasControllerAutorizacionNotasTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task ActualizarNotas_Should_Return_403_And_Not_Modify_Notas_When_Account_Exists_But_User_Has_No_Permission()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularPermitidoId = Guid.NewGuid();
        var titularBloqueadoId = Guid.NewGuid();
        var cuentaBloqueadaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.s8.notas.bloqueado@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente S8 Notas Bloqueado",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.AddRange(
            new Titular { Id = titularPermitidoId, Nombre = "Titular Permitido", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = titularBloqueadoId, Nombre = "Titular Bloqueado", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaBloqueadaId,
            TitularId = titularBloqueadoId,
            Nombre = "Cuenta Bloqueada",
            Divisa = "EUR",
            Activa = true,
            Notas = "Notas originales"
        });
        // El gerente tiene permisos, pero no sobre esta cuenta.
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            TitularId = titularPermitidoId,
            PuedeVerCuentas = true,
            PuedeEditarLineas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.ActualizarNotas(
            cuentaBloqueadaId,
            new UpdateCuentaNotasRequest { Notas = "Nota inyectada" },
            CancellationToken.None);

        // 403, no 404: la cuenta existe, lo que falta es el permiso.
        result.Should().BeOfType<ForbidResult>();

        var cuenta = await db.Cuentas.SingleAsync(c => c.Id == cuentaBloqueadaId);
        cuenta.Notas.Should().Be("Notas originales");
    }

    [Fact]
    public async Task ActualizarNotas_Should_Return_Ok_And_Save_Notas_When_User_Has_Permission()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.s8.notas.permitido@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente S8 Notas Permitido",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Permitido", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta Permitida",
            Divisa = "EUR",
            Activa = true,
            Notas = "Notas originales"
        });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeVerCuentas = true,
            PuedeEditarLineas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.ActualizarNotas(
            cuentaId,
            new UpdateCuentaNotasRequest { Notas = "Nota nueva" },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var cuenta = await db.Cuentas.SingleAsync(c => c.Id == cuentaId);
        cuenta.Notas.Should().Be("Nota nueva");
    }

    [Fact]
    public async Task ActualizarNotas_Should_Return_404_When_Account_Does_Not_Exist()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.s8.notas.inexistente@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin S8 Notas Inexistente",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.ADMIN);

        var result = await controller.ActualizarNotas(
            Guid.NewGuid(),
            new UpdateCuentaNotasRequest { Notas = "Nota nueva" },
            CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    private static CuentasController BuildController(AppDbContext db, Guid userId, RolUsuario role)
    {
        var controller = new CuentasController(
            db,
            new UserAccessService(
                db,
                new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
                Options.Create(new CachingOptions())),
            TestAuditService.Create(db),
            new NoOpPlazoFijoService());

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

    private sealed class NoOpPlazoFijoService : IPlazoFijoService
    {
        public Task<int> ProcesarVencimientosAsync(DateOnly hoy, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<PlazoFijoResponse> RenovarAsync(Guid cuentaId, RenovarPlazoFijoRequest request, Guid? actorUserId, HttpContext httpContext, CancellationToken cancellationToken)
            => Task.FromResult(new PlazoFijoResponse { CuentaId = cuentaId });
    }
}
