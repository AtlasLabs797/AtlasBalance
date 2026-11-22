using AtlasBalance.API.Data;
using AtlasBalance.API.Jobs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;

namespace AtlasBalance.API.Tests;

// A-3: un backup que se queda en PENDING para siempre detenia los backups
// automaticos (el planificador omite el automatico mientras exista cualquier
// PENDING y nada los recuperaba) y no habia aviso de "sin backup".
// J-1: los jobs de backup no tenian DisableConcurrentExecution ni
// AutomaticRetry, y el reintento de Hangfire no hacia nada porque el
// planificador guardaba `backup_auto_last_started_utc` antes de volcar.
public sealed class BackupSchedulerJobTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 21, 15, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExecuteAsync_Should_Mark_An_Abandoned_Pending_As_Failed_And_Run_The_Backup()
    {
        await using var db = BuildDbContext();
        AddConfig(db,
            ("backup_auto_enabled", "true"),
            ("backup_auto_frequency", "HOURLY"),
            ("backup_auto_interval_hours", "1"),
            ("backup_auto_last_started_utc", Now.AddHours(-3).ToString("O")));
        var pending = new Backup
        {
            Id = Guid.NewGuid(),
            FechaCreacion = Now.AddHours(-3),
            RutaArchivo = "C:\\atlas-balance\\backups\\backup_2026-11-16_181500.dump",
            Estado = EstadoProceso.PENDING
        };
        db.Backups.Add(pending);
        await db.SaveChangesAsync();

        var service = new FakeBackupService();
        var job = new BackupSchedulerJob(db, service, new FakeClock(Now), NullLogger<BackupSchedulerJob>.Instance);

        await job.ExecuteAsync();

        var reloaded = await db.Backups.IgnoreQueryFilters().SingleAsync(b => b.Id == pending.Id);
        reloaded.Estado.Should().Be(EstadoProceso.FAILED);
        reloaded.Notas.Should().NotBeNullOrWhiteSpace();
        service.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Skip_The_Backup_When_There_Is_A_Recent_Pending()
    {
        await using var db = BuildDbContext();
        AddConfig(db,
            ("backup_auto_enabled", "true"),
            ("backup_auto_frequency", "HOURLY"),
            ("backup_auto_interval_hours", "1"),
            ("backup_auto_last_started_utc", Now.AddHours(-3).ToString("O")));
        var pending = new Backup
        {
            Id = Guid.NewGuid(),
            FechaCreacion = Now.AddMinutes(-30),
            RutaArchivo = "C:\\atlas-balance\\backups\\backup_2026-11-16_204500.dump",
            Estado = EstadoProceso.PENDING
        };
        db.Backups.Add(pending);
        await db.SaveChangesAsync();

        var service = new FakeBackupService();
        var job = new BackupSchedulerJob(db, service, new FakeClock(Now), NullLogger<BackupSchedulerJob>.Instance);

        await job.ExecuteAsync();

        var reloaded = await db.Backups.IgnoreQueryFilters().SingleAsync(b => b.Id == pending.Id);
        reloaded.Estado.Should().Be(EstadoProceso.PENDING);
        service.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Notify_When_The_Last_Successful_Backup_Is_Older_Than_The_Threshold()
    {
        await using var db = BuildDbContext();
        AddConfig(db,
            ("backup_auto_enabled", "true"),
            ("backup_auto_frequency", "WEEKLY"),
            ("backup_auto_last_started_utc", Now.ToString("O")));
        db.Backups.Add(new Backup
        {
            Id = Guid.NewGuid(),
            FechaCreacion = Now.AddDays(-10),
            RutaArchivo = "C:\\atlas-balance\\backups\\backup_2026-11-06_021500.dump",
            Estado = EstadoProceso.SUCCESS
        });
        await db.SaveChangesAsync();

        var job = new BackupSchedulerJob(db, new FakeBackupService(), new FakeClock(Now), NullLogger<BackupSchedulerJob>.Instance);

        await job.ExecuteAsync();

        var notifications = await db.NotificacionesAdmin.Where(n => n.Tipo == "BACKUP").ToListAsync();
        notifications.Should().ContainSingle();
        notifications[0].Mensaje.Should().NotBeNullOrWhiteSpace();
        notifications[0].DetallesJson.Should().Contain("umbral_dias");
    }

    [Fact]
    public async Task ExecuteAsync_Should_Not_Notify_When_The_Last_Successful_Backup_Is_Recent()
    {
        await using var db = BuildDbContext();
        AddConfig(db,
            ("backup_auto_enabled", "true"),
            ("backup_auto_frequency", "WEEKLY"),
            ("backup_auto_last_started_utc", Now.ToString("O")));
        db.Backups.Add(new Backup
        {
            Id = Guid.NewGuid(),
            FechaCreacion = Now.AddDays(-2),
            RutaArchivo = "C:\\atlas-balance\\backups\\backup_2026-11-14_021500.dump",
            Estado = EstadoProceso.SUCCESS
        });
        await db.SaveChangesAsync();

        var job = new BackupSchedulerJob(db, new FakeBackupService(), new FakeClock(Now), NullLogger<BackupSchedulerJob>.Instance);

        await job.ExecuteAsync();

        (await db.NotificacionesAdmin.Where(n => n.Tipo == "BACKUP").ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Should_Not_Notify_When_The_Automatic_Backup_Is_Disabled()
    {
        await using var db = BuildDbContext();
        AddConfig(db,
            ("backup_auto_enabled", "false"),
            ("backup_auto_frequency", "WEEKLY"),
            ("backup_auto_last_started_utc", Now.ToString("O")));
        await db.SaveChangesAsync();

        var job = new BackupSchedulerJob(db, new FakeBackupService(), new FakeClock(Now), NullLogger<BackupSchedulerJob>.Instance);

        await job.ExecuteAsync();

        (await db.NotificacionesAdmin.Where(n => n.Tipo == "BACKUP").ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Should_Notify_Only_Once_When_It_Runs_Twice()
    {
        await using var db = BuildDbContext();
        AddConfig(db,
            ("backup_auto_enabled", "true"),
            ("backup_auto_frequency", "WEEKLY"),
            ("backup_auto_last_started_utc", Now.ToString("O")));
        db.Backups.Add(new Backup
        {
            Id = Guid.NewGuid(),
            FechaCreacion = Now.AddDays(-10),
            RutaArchivo = "C:\\atlas-balance\\backups\\backup_2026-11-06_021500.dump",
            Estado = EstadoProceso.SUCCESS
        });
        await db.SaveChangesAsync();

        var job = new BackupSchedulerJob(db, new FakeBackupService(), new FakeClock(Now), NullLogger<BackupSchedulerJob>.Instance);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        (await db.NotificacionesAdmin.Where(n => n.Tipo == "BACKUP").ToListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_Should_Retry_The_Backup_When_The_Previous_Run_Was_Interrupted()
    {
        await using var db = BuildDbContext();
        AddConfig(db,
            ("backup_auto_enabled", "true"),
            ("backup_auto_frequency", "HOURLY"),
            ("backup_auto_interval_hours", "1"),
            ("backup_auto_last_started_utc", Now.AddMinutes(-30).ToString("O")),
            ("backup_auto_last_result", "STARTED"));
        var pending = new Backup
        {
            Id = Guid.NewGuid(),
            FechaCreacion = Now.AddMinutes(-20),
            RutaArchivo = "C:\\atlas-balance\\backups\\backup_2026-11-16_205500.dump",
            Estado = EstadoProceso.PENDING
        };
        db.Backups.Add(pending);
        await db.SaveChangesAsync();

        var service = new FakeBackupService();
        var job = new BackupSchedulerJob(db, service, new FakeClock(Now), NullLogger<BackupSchedulerJob>.Instance);

        await job.ExecuteAsync();

        var reloaded = await db.Backups.IgnoreQueryFilters().SingleAsync(b => b.Id == pending.Id);
        reloaded.Estado.Should().Be(EstadoProceso.FAILED);
        service.CreateCalls.Should().Be(1);
    }

    [Fact]
    public void Backup_Jobs_Should_Disable_Concurrent_Execution_And_Retry()
    {
        foreach (var type in new[] { typeof(BackupSchedulerJob), typeof(BackupWeeklyJob), typeof(BackupOperationJob) })
        {
            type.GetCustomAttribute<DisableConcurrentExecutionAttribute>().Should().NotBeNull();
            var retry = type.GetCustomAttribute<AutomaticRetryAttribute>();
            retry.Should().NotBeNull();
            retry!.Attempts.Should().Be(3);
            retry.DelaysInSeconds.Should().BeEquivalentTo(new[] { 60, 300, 900 });
        }
    }

    private static void AddConfig(AppDbContext db, params (string Key, string Value)[] values)
    {
        foreach (var (key, value) in values)
        {
            db.Configuraciones.Add(new Configuracion { Clave = key, Valor = value });
        }
    }

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
    }

    private sealed class FakeBackupService : IBackupService
    {
        public int CreateCalls { get; private set; }

        public Task<Backup> CreateBackupAsync(TipoProceso tipo, Guid? iniciadoPorId, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(new Backup
            {
                Id = Guid.NewGuid(),
                FechaCreacion = DateTime.UtcNow,
                Estado = EstadoProceso.SUCCESS,
                Tipo = tipo
            });
        }

        public Task ApplyRetentionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
