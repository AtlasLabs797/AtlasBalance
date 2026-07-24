using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PlazoFijoUniqueIndexTests
{
    // -----------------------------------------------------------------------
    // V-02.06 (HIGH-5): el UNIQUE ix_plazos_fijos_cuenta_id ahora es
    // parcial (WHERE deleted_at IS NULL) gracias a la migracion
    // 20260710_RecreateUniqueIndexesWithSoftDeleteFilter. Esto permite
    // reutilizar la misma cuenta tras un soft-delete sin perder la
    // unicidad entre plazos activos. Test contra PostgreSQL real.
    // -----------------------------------------------------------------------

    private readonly PostgresFixture _fixture;

    public PlazoFijoUniqueIndexTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SoftDeletePlazoFijo_Then_CrearNuevo_ConMismoCuentaId_DeberiaPermitir()
    {
        var connectionString = _fixture.ConnectionString +
            ";Include Error Detail=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        await using (var db = new AppDbContext(options))
        {
            // Seed minimo: el FK exige que la cuenta exista y que el
            // DeletedById apunte a un usuario valido.
            db.Usuarios.Add(new Usuario
            {
                Id = adminId,
                Email = $"admin-{adminId:N}@test.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!Ab", workFactor: 12),
                NombreCompleto = "Admin Test UNIQUE Partial",
                Rol = RolUsuario.ADMIN,
                Activo = true
            });
            db.Titulares.Add(new Titular
            {
                Id = titularId,
                Nombre = "Titular Test UNIQUE Partial",
                Tipo = TipoTitular.EMPRESA
            });
            db.Cuentas.Add(new Cuenta
            {
                Id = cuentaId,
                TitularId = titularId,
                Nombre = "Cuenta Test UNIQUE Partial",
                Divisa = "EUR",
                Activa = true
            });
            await db.SaveChangesAsync();
        }

        Guid plazoOriginalId;
        Guid plazoNuevoId;

        await using (var db = new AppDbContext(options))
        {
            var plazoOriginal = new PlazoFijo
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow),
                FechaVencimiento = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                Renovable = false,
                Estado = EstadoPlazoFijo.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            };
            db.PlazosFijos.Add(plazoOriginal);
            await db.SaveChangesAsync();
            plazoOriginalId = plazoOriginal.Id;
        }

        // Soft-delete el plazo original. La fila sigue en BD pero
        // deleted_at != NULL, y el UNIQUE parcial debe permitir un
        // segundo plazo activo para la misma cuenta.
        await using (var db = new AppDbContext(options))
        {
            var plazoOriginal = await db.PlazosFijos.FirstAsync(x => x.Id == plazoOriginalId);
            plazoOriginal.DeletedAt = DateTime.UtcNow;
            plazoOriginal.DeletedById = adminId;
            plazoOriginal.Estado = EstadoPlazoFijo.CANCELADO;
            await db.SaveChangesAsync();
        }

        // El UNIQUE parcial (deleted_at IS NULL) debe permitir la nueva fila.
        // Si el UNIQUE no fuera parcial, SaveChangesAsync lanzaria
        // PostgresException con codigo 23505 (unique_violation) al
        // intentar crear el segundo plazo activo para la misma cuenta.
        await using (var db = new AppDbContext(options))
        {
            var plazoNuevo = new PlazoFijo
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                FechaInicio = DateOnly.FromDateTime(DateTime.UtcNow),
                FechaVencimiento = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                Renovable = false,
                Estado = EstadoPlazoFijo.ACTIVO,
                FechaCreacion = DateTime.UtcNow
            };
            db.PlazosFijos.Add(plazoNuevo);
            await db.SaveChangesAsync();
            plazoNuevoId = plazoNuevo.Id;
        }

        plazoNuevoId.Should().NotBe(plazoOriginalId);
        await using (var db = new AppDbContext(options))
        {
            var plazos = await db.PlazosFijos
                .IgnoreQueryFilters()
                .Where(x => x.CuentaId == cuentaId)
                .ToListAsync();

            plazos.Should().HaveCount(2);
            plazos.Count(x => x.DeletedAt != null).Should().Be(1);
            plazos.Count(x => x.DeletedAt == null).Should().Be(1);
        }
    }
}
