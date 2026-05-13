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
using Xunit;

namespace AtlasBalance.API.Tests;

public class ManualProcessResponseTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task BackupManual_Should_Return_String_Estado()
    {
        await using var db = BuildDbContext();
        var controller = new BackupsController(
            db,
            new FakeBackupService(),
            new FakeWatchdogClientService());
        controller.ControllerContext = BuildControllerContext();

        var result = await controller.BackupManual(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);

        json.Should().Contain("\"Estado\":\"SUCCESS\"");
        json.Should().NotContain("\"Estado\":1");
    }

    [Fact]
    public async Task ExportacionManual_Should_Return_String_Estado()
    {
        await using var db = BuildDbContext();
        var controller = new ExportacionesController(
            db,
            new FakeExportacionService(),
            new FakeUserAccessService());
        controller.ControllerContext = BuildControllerContext();

        var result = await controller.Manual(
            new ExportacionManualRequest { CuentaId = Guid.NewGuid() },
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);

        json.Should().Contain("\"Estado\":\"SUCCESS\"");
        json.Should().NotContain("\"Estado\":1");
    }

    [Fact]
    public async Task ExportacionManual_Should_Return_Forbidden_When_User_Cannot_Read_Cuenta()
    {
        await using var db = BuildDbContext();
        var controller = new ExportacionesController(
            db,
            new FakeExportacionService(),
            new FakeUserAccessService(canAccessCuenta: false));
        controller.ControllerContext = BuildControllerContext();

        var result = await controller.Manual(
            new ExportacionManualRequest { CuentaId = Guid.NewGuid() },
            CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ExportacionManual_Should_Return_413_When_Export_Is_Too_Large()
    {
        await using var db = BuildDbContext();
        var controller = new ExportacionesController(
            db,
            new TooLargeExportacionService(),
            new FakeUserAccessService());
        controller.ControllerContext = BuildControllerContext();

        var result = await controller.Manual(
            new ExportacionManualRequest { CuentaId = Guid.NewGuid() },
            CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
    }

    private static ControllerContext BuildControllerContext()
    {
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    private sealed class FakeBackupService : IBackupService
    {
        public Task<Backup> CreateBackupAsync(TipoProceso tipo, Guid? iniciadoPorId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Backup
            {
                Id = Guid.NewGuid(),
                Estado = EstadoProceso.SUCCESS,
                Tipo = tipo,
                RutaArchivo = @"C:\temp\backup.dump",
                TamanioBytes = 1024,
                IniciadoPorId = iniciadoPorId
            });
        }

        public Task ApplyRetentionAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeExportacionService : IExportacionService
    {
        public Task<Exportacion> ExportarCuentaAsync(Guid cuentaId, TipoProceso tipo, Guid? iniciadoPorId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Exportacion
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Estado = EstadoProceso.SUCCESS,
                Tipo = tipo,
                RutaArchivo = @"C:\temp\exportacion.xlsx",
                TamanioBytes = 2048,
                IniciadoPorId = iniciadoPorId
            });
        }

        public Task<int> ExportarMensualAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class TooLargeExportacionService : IExportacionService
    {
        public Task<Exportacion> ExportarCuentaAsync(Guid cuentaId, TipoProceso tipo, Guid? iniciadoPorId, CancellationToken cancellationToken)
        {
            throw new ExportacionTooLargeException("Exportacion demasiado grande");
        }

        public Task<int> ExportarMensualAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class FakeUserAccessService : IUserAccessService
    {
        private readonly bool _canAccessCuenta;

        public FakeUserAccessService(bool canAccessCuenta = true)
        {
            _canAccessCuenta = canAccessCuenta;
        }

        public Task<UserAccessScope> GetScopeAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            return Task.FromResult(new UserAccessScope
            {
                UserId = Guid.NewGuid(),
                IsAdmin = true,
                HasPermissions = true,
                HasGlobalAccess = true
            });
        }

        public IQueryable<Titular> ApplyTitularScope(IQueryable<Titular> query, UserAccessScope scope)
        {
            return query;
        }

        public IQueryable<Cuenta> ApplyCuentaScope(IQueryable<Cuenta> query, UserAccessScope scope)
        {
            return query;
        }

        public Task<bool> CanAccessTitularAsync(Guid titularId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<bool> CanAccessCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult(_canAccessCuenta);
        }

        public Task<bool> CanWriteCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<bool> CanEditCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class FakeWatchdogClientService : IWatchdogClientService
    {
        public Task<bool> SolicitarRestauracionAsync(string backupPath, Guid? solicitadoPorId, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<bool> SolicitarActualizacionAsync(string? sourcePath, string? targetPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<WatchdogStateResponse> GetEstadoAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new WatchdogStateResponse());
        }
    }
}
