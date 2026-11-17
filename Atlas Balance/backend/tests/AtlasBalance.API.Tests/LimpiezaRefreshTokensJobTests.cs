using AtlasBalance.API.Data;
using AtlasBalance.API.Jobs;
using AtlasBalance.API.Models;
using FluentAssertions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

// -----------------------------------------------------------------------
// V-03.02 (J-5): AppDbContext declara un filtro global sobre RefreshToken
// (e.Usuario != null && e.Usuario.DeletedAt == null). LimpiezaRefreshTokensJob
// consultaba la tabla sin IgnoreQueryFilters, asi que los tokens de los
// usuarios con soft delete eran invisibles para la purga y se quedaban vivos
// hasta su expiracion natural.
//
// El proveedor InMemory de EF no soporta ExecuteDeleteAsync, asi que aqui se
// comprueba que filas SELECCIONARIA la purga (la consulta que la job ejecuta
// en produccion), no que se borren. El borrado en bloque contra PostgreSQL no
// queda cubierto por este fichero.
// -----------------------------------------------------------------------
public sealed class LimpiezaRefreshTokensJobTests
{
    [Fact]
    public async Task BuildPurgeQuery_Should_Select_Tokens_Of_SoftDeleted_Users_Even_When_They_Are_Not_Expired()
    {
        await using var db = BuildDbContext();
        var now = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);

        var usuarioBorrado = NewUsuario(now);
        usuarioBorrado.DeletedAt = now.AddDays(-10);
        var usuarioActivo = NewUsuario(now);

        var tokenDeUsuarioBorrado = NewToken(usuarioBorrado.Id, now.AddDays(5));
        var tokenDeUsuarioActivo = NewToken(usuarioActivo.Id, now.AddDays(5));

        db.Usuarios.AddRange(usuarioBorrado, usuarioActivo);
        db.RefreshTokens.AddRange(tokenDeUsuarioBorrado, tokenDeUsuarioActivo);
        await db.SaveChangesAsync();

        var job = new LimpiezaRefreshTokensJob(db, NullLogger<LimpiezaRefreshTokensJob>.Instance);
        var idsUsuariosBorrados = await job.GetUsuariosBorradosIdsAsync(CancellationToken.None);

        var seleccionados = await job
            .BuildPurgeQuery(now, idsUsuariosBorrados)
            .Select(t => t.Id)
            .ToListAsync();

        seleccionados.Should().ContainSingle().Which.Should().Be(tokenDeUsuarioBorrado.Id);
    }

    [Fact]
    public async Task BuildPurgeQuery_Should_Not_Select_Live_Tokens_Of_Active_Users()
    {
        await using var db = BuildDbContext();
        var now = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);

        var usuarioActivo = NewUsuario(now);
        var tokenVivo = NewToken(usuarioActivo.Id, now.AddDays(5));

        db.Usuarios.Add(usuarioActivo);
        db.RefreshTokens.Add(tokenVivo);
        await db.SaveChangesAsync();

        var job = new LimpiezaRefreshTokensJob(db, NullLogger<LimpiezaRefreshTokensJob>.Instance);
        var idsUsuariosBorrados = await job.GetUsuariosBorradosIdsAsync(CancellationToken.None);

        var seleccionados = await job
            .BuildPurgeQuery(now, idsUsuariosBorrados)
            .Select(t => t.Id)
            .ToListAsync();

        seleccionados.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildPurgeQuery_Should_Select_Expired_Tokens_Of_Active_Users()
    {
        await using var db = BuildDbContext();
        var now = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);

        var usuarioActivo = NewUsuario(now);
        var tokenCaducado = NewToken(usuarioActivo.Id, now.AddMinutes(-5));

        db.Usuarios.Add(usuarioActivo);
        db.RefreshTokens.Add(tokenCaducado);
        await db.SaveChangesAsync();

        var job = new LimpiezaRefreshTokensJob(db, NullLogger<LimpiezaRefreshTokensJob>.Instance);
        var idsUsuariosBorrados = await job.GetUsuariosBorradosIdsAsync(CancellationToken.None);

        var seleccionados = await job
            .BuildPurgeQuery(now, idsUsuariosBorrados)
            .Select(t => t.Id)
            .ToListAsync();

        seleccionados.Should().ContainSingle().Which.Should().Be(tokenCaducado.Id);
    }

    [Fact]
    public async Task GetUsuariosBorradosIdsAsync_Should_Include_SoftDeleted_Users_That_The_Global_Filter_Hides()
    {
        await using var db = BuildDbContext();
        var now = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);

        var usuarioBorrado = NewUsuario(now);
        usuarioBorrado.DeletedAt = now.AddDays(-10);
        var usuarioActivo = NewUsuario(now);

        db.Usuarios.AddRange(usuarioBorrado, usuarioActivo);
        await db.SaveChangesAsync();

        // El filtro global de AppDbContext oculta el usuario borrado.
        (await db.Usuarios.ToListAsync()).Should().ContainSingle(u => u.Id == usuarioActivo.Id);

        var job = new LimpiezaRefreshTokensJob(db, NullLogger<LimpiezaRefreshTokensJob>.Instance);
        var idsUsuariosBorrados = await job.GetUsuariosBorradosIdsAsync(CancellationToken.None);

        idsUsuariosBorrados.Should().ContainSingle().Which.Should().Be(usuarioBorrado.Id);
    }

    // -----------------------------------------------------------------------
    // V-03.02 (J-1): con 2 workers de Hangfire, el job no puede solaparse
    // consigo mismo, y un fallo puntual (base de datos reiniciandose) debe
    // reintentarse solo.
    // -----------------------------------------------------------------------

    [Fact]
    public void Job_Should_Have_Hangfire_Concurrency_And_Retry_Attributes()
    {
        var concurrencia = typeof(LimpiezaRefreshTokensJob)
            .GetCustomAttributes(typeof(DisableConcurrentExecutionAttribute), inherit: false)
            .Cast<DisableConcurrentExecutionAttribute>()
            .SingleOrDefault();

        concurrencia.Should().NotBeNull();
        concurrencia!.TimeoutSec.Should().Be(900);

        var reintento = typeof(LimpiezaRefreshTokensJob)
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), inherit: false)
            .Cast<AutomaticRetryAttribute>()
            .SingleOrDefault();

        reintento.Should().NotBeNull();
        reintento!.Attempts.Should().Be(3);
        reintento.DelaysInSeconds.Should().Equal(60, 300, 900);
    }

    private static Usuario NewUsuario(DateTime now)
    {
        return new Usuario
        {
            Id = Guid.NewGuid(),
            Email = $"usuario-{Guid.NewGuid():N}@atlas-balance.local",
            PasswordHash = "hash",
            NombreCompleto = "Usuario de prueba",
            Rol = RolUsuario.EMPLEADO,
            Activo = true,
            FechaCreacion = now
        };
    }

    private static RefreshToken NewToken(Guid usuarioId, DateTime expiraEn)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            TokenHash = Guid.NewGuid().ToString("N"),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ExpiraEn = expiraEn,
            CreadoEn = expiraEn.AddDays(-7)
        };
    }

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
