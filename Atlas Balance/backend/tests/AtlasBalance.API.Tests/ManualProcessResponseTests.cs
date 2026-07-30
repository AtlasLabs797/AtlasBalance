using System.Text.Json;
using FluentAssertions;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Jobs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
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
            new FakeWatchdogClientService(),
            new FakeBackupConfigurationService(),
            new FakeGoogleDriveBackupService(),
            logger: null,
            backgroundJobs: new RecordingBackgroundJobClient());
        controller.ControllerContext = BuildControllerContext();

        var result = await controller.BackupManual(CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var json = JsonSerializer.Serialize(accepted.Value);

        json.Should().Contain("operation_id");
        json.Should().Contain("PENDING");
        (await db.BackupOperations.SingleAsync()).Estado.Should().Be("PENDING");
    }

    [Fact]
    public async Task DriveImport_Should_Return_Accepted_And_Job_Should_Complete_The_Same_Operation()
    {
        await using var db = BuildDbContext();
        var drive = new FakeGoogleDriveBackupService();
        var controller = new BackupsController(
            db,
            new FakeBackupService(),
            new FakeWatchdogClientService(),
            new FakeBackupConfigurationService(),
            drive,
            logger: null,
            backgroundJobs: new RecordingBackgroundJobClient());
        controller.ControllerContext = BuildControllerContext();

        var result = await controller.ImportGoogleDriveFile(
            new GoogleDriveImportRequest { FileId = "drive-file-1" },
            CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
        var operation = await db.BackupOperations.SingleAsync();
        operation.Tipo.Should().Be("DRIVE_IMPORT");
        operation.Estado.Should().Be("PENDING");

        var job = new BackupOperationJob(
            db,
            new FakeBackupService(),
            drive,
            NullLogger<BackupOperationJob>.Instance);
        await job.ExecuteDriveImportAsync(operation.Id, "drive-file-1", null, CancellationToken.None);

        operation.Estado.Should().Be("SUCCESS");
        operation.BackupId.Should().NotBeNull();
        operation.ResultadoJson.Should().Contain("backup_id");
    }

    [Fact]
    public async Task Restore_Should_Persist_Operation_Before_Watchdog_And_Only_Accept_Matching_State()
    {
        var root = Path.Combine(Path.GetTempPath(), $"atlas-backup-operation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var dumpPath = Path.Combine(root, "restore.dump");
        await File.WriteAllTextAsync(dumpPath, "fixture");

        try
        {
            await using var db = BuildDbContext();
            var backup = new Backup { Id = Guid.NewGuid(), RutaArchivo = dumpPath, Estado = EstadoProceso.SUCCESS };
            db.Backups.Add(backup);
            db.Configuraciones.Add(new Configuracion { Clave = "backup_path", Valor = root });
            await db.SaveChangesAsync();

            var watchdog = new FakeWatchdogClientService();
            var observedPersistedOperation = false;
            watchdog.OnRestore = async operationId =>
            {
                observedPersistedOperation = await db.BackupOperations.AnyAsync(x => x.Id == operationId && x.Estado == "PENDING");
            };
            var controller = new BackupsController(
                db,
                new FakeBackupService(),
                watchdog,
                new FakeBackupConfigurationService(),
                new FakeGoogleDriveBackupService());
            controller.ControllerContext = BuildControllerContext();

            var result = await controller.Restaurar(
                backup.Id,
                new RestaurarBackupRequest { Confirmacion = "RESTAURAR" },
                CancellationToken.None);

            var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
            observedPersistedOperation.Should().BeTrue();
            var operation = await db.BackupOperations.SingleAsync();
            operation.Estado.Should().Be("RUNNING");
            JsonSerializer.Serialize(accepted.Value).Should().Contain(operation.Id.ToString());

            watchdog.State = new WatchdogStateResponse { Estado = "SUCCESS", OperationId = Guid.NewGuid() };
            await controller.GetOperation(operation.Id, CancellationToken.None);
            (await db.BackupOperations.SingleAsync()).Estado.Should().Be("RUNNING");

            watchdog.State = new WatchdogStateResponse { Estado = "SUCCESS", OperationId = operation.Id };
            await controller.GetOperation(operation.Id, CancellationToken.None);
            (await db.BackupOperations.SingleAsync()).Estado.Should().Be("SUCCESS");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
    public async Task ExportacionManual_Should_Return_Forbidden_When_User_Cannot_Write_Cuenta()
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

    private sealed class RecordingBackgroundJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => Guid.NewGuid().ToString("N");

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
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
        private readonly bool _canWriteCuenta;

        public FakeUserAccessService(bool canAccessCuenta = true, bool canWriteCuenta = true)
        {
            _canAccessCuenta = canAccessCuenta;
            _canWriteCuenta = canWriteCuenta;
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
            return Task.FromResult(_canWriteCuenta);
        }

        public Task<bool> CanEditCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<bool> CanReviewCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<bool> CanApproveImportacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult(_canWriteCuenta);
        }

        public Task<bool> CanConciliarCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult(_canWriteCuenta);
        }

        public Task<bool> CanCerrarConciliacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult(_canWriteCuenta);
        }
    }

    private sealed class FakeWatchdogClientService : IWatchdogClientService
    {
        public Func<Guid, Task>? OnRestore { get; set; }
        public WatchdogStateResponse State { get; set; } = new();

        public Task<bool> SolicitarRestauracionAsync(string backupPath, Guid? solicitadoPorId, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public async Task<bool> SolicitarRestauracionAsync(string backupPath, Guid? solicitadoPorId, Guid operationId, CancellationToken cancellationToken)
        {
            if (OnRestore is not null)
            {
                await OnRestore(operationId);
            }

            return true;
        }

        public Task<bool> SolicitarActualizacionAsync(string? sourcePath, string? targetPath, string? packageZipPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<WatchdogStateResponse> GetEstadoAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(State);
        }

        public Task<bool> EstaDisponibleAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class FakeBackupConfigurationService : IBackupConfigurationService
    {
        public Task<BackupConfigResponse> GetAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new BackupConfigResponse());
        }

        public Task<(bool Success, string? Error)> UpdateAsync(UpdateBackupConfigRequest request, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken)
        {
            return Task.FromResult((true, (string?)null));
        }
    }

    private sealed class FakeGoogleDriveBackupService : IGoogleDriveBackupService
    {
        public Task<GoogleDriveLinkStartResponse> StartLinkAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new GoogleDriveLinkStartResponse());
        }

        public Task<GoogleDriveLinkStatusResponse> PollLinkAsync(Guid sessionId, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken)
        {
            return Task.FromResult(new GoogleDriveLinkStatusResponse());
        }

        public Task DisconnectAsync(Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<GoogleDriveLinkStatusResponse> TestConnectionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new GoogleDriveLinkStatusResponse());
        }

        public Task UploadBackupAsync(Backup backup, string backupPath, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task UploadBackupByIdAsync(Guid backupId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DeleteRemoteBackupCopyAsync(BackupCloudCopy copy, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GoogleDriveBackupFileResponse>> ListFilesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<GoogleDriveBackupFileResponse>>(Array.Empty<GoogleDriveBackupFileResponse>());
        }

        public Task<Backup> ImportAsync(string fileId, Guid? userId, HttpContext? httpContext, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Backup
            {
                Id = Guid.NewGuid(),
                Estado = EstadoProceso.SUCCESS,
                Tipo = TipoProceso.MANUAL,
                RutaArchivo = @"C:\temp\backup.dump",
                TamanioBytes = 1024,
                IniciadoPorId = userId
            });
        }
    }
}
