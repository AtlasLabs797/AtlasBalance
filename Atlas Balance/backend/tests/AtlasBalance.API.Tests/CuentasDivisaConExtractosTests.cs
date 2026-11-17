using System.Security.Claims;
using System.Text.Json;
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
/// B-14: cambiar la divisa de una cuenta con historial deja los importes de sus
/// extractos interpreted en otra moneda (el extracto no guarda divisa propia).
///
/// Criterio de "cuenta con extractos": hay al menos un extracto NO borrado
/// (soft delete: deleted_at IS NULL, el filtro global del DbContext). Los
/// extractos en la papelera no bloquean: la cuenta se considera vacia.
///
/// Con el defecto presente, el caso (a) falla: el backend devuelve 200 y deja
/// la cuenta en la divisa nueva, con los importes ya interpretados en otra
/// moneda.
/// </summary>
public sealed class CuentasDivisaConExtractosTests
{
    [Fact]
    public async Task Actualizar_Should_Return_Conflict_When_Cuenta_Has_Extractos_And_Divisa_Changes()
    {
        var (db, controller, cuentaId, titularId) = await BuildFixtureAsync("con-extractos");
        await using var _ = db;

        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = new DateOnly(2026, 5, 1),
            Concepto = "Ingreso en EUR",
            Monto = 100m,
            Saldo = 100m,
            FilaNumero = 1
        });
        await db.SaveChangesAsync();

        var result = await controller.Actualizar(cuentaId, BuildRequest(titularId, "USD"), CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        JsonSerializer.Serialize(conflict.Value).Should().Contain("no se puede cambiar su divisa");

        var cuenta = await db.Cuentas.SingleAsync(c => c.Id == cuentaId);
        cuenta.Divisa.Should().Be("EUR");
    }

    [Fact]
    public async Task Actualizar_Should_Apply_Divisa_Change_When_Cuenta_Has_No_Extractos()
    {
        var (db, controller, cuentaId, titularId) = await BuildFixtureAsync("sin-extractos");
        await using var _ = db;

        var result = await controller.Actualizar(cuentaId, BuildRequest(titularId, "USD"), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var cuenta = await db.Cuentas.SingleAsync(c => c.Id == cuentaId);
        cuenta.Divisa.Should().Be("USD");
    }

    [Fact]
    public async Task Actualizar_Should_Apply_Divisa_Change_When_All_Extractos_Are_SoftDeleted()
    {
        var (db, controller, cuentaId, titularId) = await BuildFixtureAsync("extractos-en-papelera");
        await using var _ = db;

        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = new DateOnly(2026, 5, 1),
            Monto = 100m,
            Saldo = 100m,
            FilaNumero = 1,
            DeletedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Actualizar(cuentaId, BuildRequest(titularId, "USD"), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var cuenta = await db.Cuentas.SingleAsync(c => c.Id == cuentaId);
        cuenta.Divisa.Should().Be("USD");
    }

    [Fact]
    public async Task Actualizar_Should_Keep_Divisa_When_Cuenta_Has_Extractos_And_Divisa_Does_Not_Change()
    {
        var (db, controller, cuentaId, titularId) = await BuildFixtureAsync("misma-divisa");
        await using var _ = db;

        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = new DateOnly(2026, 5, 1),
            Monto = 100m,
            Saldo = 100m,
            FilaNumero = 1
        });
        await db.SaveChangesAsync();

        var result = await controller.Actualizar(cuentaId, BuildRequest(titularId, "EUR"), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var cuenta = await db.Cuentas.SingleAsync(c => c.Id == cuentaId);
        cuenta.Divisa.Should().Be("EUR");
        cuenta.Nombre.Should().Be("Cuenta EUR");
    }

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static SaveCuentaRequest BuildRequest(Guid titularId, string divisa) => new()
    {
        TitularId = titularId,
        Nombre = $"Cuenta {divisa}",
        Divisa = divisa,
        TipoCuenta = TipoCuenta.NORMAL,
        Activa = true
    };

    private static async Task<(AppDbContext Db, CuentasController Controller, Guid CuentaId, Guid TitularId)> BuildFixtureAsync(string sufijo)
    {
        var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = $"admin.divisa.{sufijo}@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Divisa",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = $"Titular {sufijo}", Tipo = TipoTitular.EMPRESA });
        db.DivisasActivas.Add(new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true });
        db.DivisasActivas.Add(new DivisaActiva { Codigo = "USD", Activa = true });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = $"Cuenta {sufijo}",
            Divisa = "EUR",
            TipoCuenta = TipoCuenta.NORMAL,
            Activa = true
        });
        await db.SaveChangesAsync();

        return (db, BuildController(db, userId), cuentaId, titularId);
    }

    private static CuentasController BuildController(AppDbContext db, Guid userId)
    {
        var controller = new CuentasController(db, new UserAccessService(db, new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance), Options.Create(new CachingOptions())), TestAuditService.Create(db), new NoOpPlazoFijoService());
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

    private sealed class NoOpPlazoFijoService : IPlazoFijoService
    {
        public Task<int> ProcesarVencimientosAsync(DateOnly hoy, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<PlazoFijoResponse> RenovarAsync(Guid cuentaId, RenovarPlazoFijoRequest request, Guid? actorUserId, HttpContext httpContext, CancellationToken cancellationToken)
            => Task.FromResult(new PlazoFijoResponse { CuentaId = cuentaId });
    }
}
