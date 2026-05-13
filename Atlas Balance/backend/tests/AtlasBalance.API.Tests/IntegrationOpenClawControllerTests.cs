using FluentAssertions;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.Middleware;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class IntegrationOpenClawControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static IntegrationOpenClawController BuildController(AppDbContext dbContext, IntegrationToken token)
    {
        var controller = new IntegrationOpenClawController(
            dbContext,
            new IntegrationAuthorizationService(dbContext),
            new TiposCambioServiceStub());

        var httpContext = new DefaultHttpContext();
        httpContext.Items[IntegrationHttpContextItemKeys.CurrentIntegrationToken] = token;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    [Fact]
    public async Task Titulares_Should_Reject_Token_With_WriteOnly_Scope()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Empresa", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta", Divisa = "EUR" };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            PermisoEscritura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "escritura"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.Titulares("full", CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        JsonSerializer.Serialize(objectResult.Value).Should().Contain("FORBIDDEN");
    }

    [Fact]
    public async Task Extractos_Should_Return_Derived_TipoMovimiento_Inside_Wrapped_Response()
    {
        await using var db = BuildDbContext();
        var creador = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "user@test.local",
            PasswordHash = "hash",
            NombreCompleto = "User",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        };
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Empresa", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta", Divisa = "EUR" };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = creador.Id
        };

        db.Usuarios.Add(creador);
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "lectura"
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                Fecha = new DateOnly(2026, 4, 10),
                Concepto = "Ingreso",
                Monto = 50m,
                Saldo = 50m,
                FilaNumero = 1,
                UsuarioCreacionId = creador.Id,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-5)
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                Fecha = new DateOnly(2026, 4, 11),
                Concepto = "Pago",
                Monto = -20m,
                Saldo = 30m,
                FilaNumero = 2,
                UsuarioCreacionId = creador.Id,
                FechaCreacion = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.Extractos("full", cuenta.Id, null, null, null, 100, 1, "fecha", "asc", CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.GetType().GetProperty("Exito")!.GetValue(okResult.Value).Should().Be(true);

        var payload = JsonSerializer.Serialize(okResult.Value);
        payload.Should().Contain("tipo_movimiento");
        payload.Should().Contain("INGRESO");
        payload.Should().Contain("EGRESO");
    }

    [Fact]
    public async Task Auditoria_Should_Not_Return_Values_For_SoftDeleted_Extractos()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Empresa", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta", Divisa = "EUR" };
        var activeExtractoId = Guid.NewGuid();
        var deletedExtractoId = Guid.NewGuid();
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "lectura"
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = activeExtractoId,
                CuentaId = cuenta.Id,
                Fecha = new DateOnly(2026, 5, 1),
                Concepto = "Visible",
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = deletedExtractoId,
                CuentaId = cuenta.Id,
                Fecha = new DateOnly(2026, 5, 2),
                Concepto = "Eliminado",
                Monto = 20m,
                Saldo = 30m,
                FilaNumero = 2,
                DeletedAt = DateTime.UtcNow
            });
        db.Auditorias.AddRange(
            new Auditoria
            {
                Id = Guid.NewGuid(),
                TipoAccion = "extracto_update",
                EntidadTipo = "EXTRACTOS",
                EntidadId = activeExtractoId,
                ValorAnterior = "VisibleAnterior",
                ValorNuevo = "VisibleNuevo",
                Timestamp = DateTime.UtcNow
            },
            new Auditoria
            {
                Id = Guid.NewGuid(),
                TipoAccion = "extracto_update",
                EntidadTipo = "EXTRACTOS",
                EntidadId = deletedExtractoId,
                ValorAnterior = "DeletedAnterior",
                ValorNuevo = "DeletedNuevo",
                Timestamp = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.Auditoria("full", cuenta.Id, null, null, null, "all", 100, 1, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = JsonSerializer.Serialize(okResult.Value);
        payload.Should().Contain("VisibleNuevo");
        payload.Should().NotContain("DeletedNuevo");
        payload.Should().NotContain("DeletedAnterior");
    }

    private sealed class TiposCambioServiceStub : ITiposCambioService
    {
        public Task<decimal> ConvertAsync(decimal amount, string divisaOrigen, string divisaDestino, CancellationToken cancellationToken)
            => Task.FromResult(amount);

        public Task<DivisaActivaDto> ActualizarDivisaAsync(string codigo, string? nombre, string? simbolo, bool activa, bool esBase, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<DivisaActivaDto> CrearDivisaAsync(string codigo, string? nombre, string? simbolo, bool activa, bool esBase, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TipoCambioDto> GuardarTipoCambioManualAsync(string divisaOrigen, string divisaDestino, decimal tasa, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DivisaActivaDto>> ListarDivisasAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TipoCambioDto>> ListarTiposCambioAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<SyncTiposCambioResult> SincronizarTiposCambioAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
