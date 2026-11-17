using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// LOTE 5 (M-9, B-3 y B-6, parte de base de datos):
///   * B-3: EXTRACTOS.(cuenta_id, importacion_fingerprint) pasa a ser un indice
///     UNIQUE PARCIAL con "deleted_at" IS NULL, para que tras revertir un
///     lote (soft delete) se pueda reimportar la misma huella.
///   * B-6: CONCILIACIONES.(extracto_id) pasa a ser un indice UNIQUE PARCIAL
///     sobre las conciliaciones en estado 'conciliada' y no borradas.
///   * M-9: la migracion se aplica a mano y debe ser idempotente y aplicable
///     tanto sobre una base nueva como sobre una base ya migrada.
/// Test contra PostgreSQL real (Testcontainers).
/// </summary>
[Trait("Category", "Postgres")]
[Collection(PostgresCollection.Name)]
public sealed class IndicesParcialesLote5Tests
{
    private const string MigracionIndices = "20261117120000_IndicesParcialesHuellaYConciliacion";

    private readonly PostgresFixture _fixture;

    public IndicesParcialesLote5Tests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migracion_Sobre_Base_Nueva_Debe_Crear_Los_Indices_Parciales_Y_Ser_Idempotente()
    {
        var connectionString = await CrearBaseAuxiliarAsync("atlas_balance_lote5_nueva");
        var options = BuildOptions(connectionString);

        // Instalacion nueva: base vacia.
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        var indices = await LeerIndicesAsync(connectionString);

        indices.Should().ContainKey("ix_extractos_cuenta_id_importacion_fingerprint");
        indices["ix_extractos_cuenta_id_importacion_fingerprint"].Should().Contain("UNIQUE");
        indices["ix_extractos_cuenta_id_importacion_fingerprint"].Should().Contain("deleted_at IS NULL");
        indices["ix_extractos_cuenta_id_importacion_fingerprint"].Should().Contain("importacion_fingerprint IS NOT NULL");

        indices.Should().ContainKey("ix_conciliaciones_extracto_id_conciliada");
        indices["ix_conciliaciones_extracto_id_conciliada"].Should().Contain("UNIQUE");
        indices["ix_conciliaciones_extracto_id_conciliada"].Should().Contain("conciliada");

        // El indice simple sobre extracto_id lo sustituye el parcial.
        indices.Should().NotContainKey("ix_conciliaciones_extracto_id");

        // Idempotencia: se reaplica la migracion sobre la base ya migrada
        // (se borra su registro de __EFMigrationsHistory y se vuelve a migrar).
        await using (var db = new AppDbContext(options))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"__EFMigrationsHistory\" WHERE \"migration_id\" = {MigracionIndices}");
        }

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        var indicesTrasReaplicar = await LeerIndicesAsync(connectionString);
        indicesTrasReaplicar.Should().ContainKey("ix_conciliaciones_extracto_id_conciliada");
        indicesTrasReaplicar.Should().ContainKey("ix_extractos_cuenta_id_importacion_fingerprint");
        indicesTrasReaplicar.Should().NotContainKey("ix_conciliaciones_extracto_id");
    }

    [Fact]
    public async Task Migracion_Sobre_Base_Con_Duplicados_Debe_Purgarlos_Sin_Borrar_Filas_Y_Crear_El_Indice_Unico()
    {
        var connectionString = await CrearBaseAuxiliarAsync("atlas_balance_lote5_purga");
        var options = BuildOptions(connectionString);

        // Base ya migrada (instalacion existente).
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        // Se simula una base que todavia no tiene el indice unico parcial
        // (estado anterior a la migracion) y en la que ya existen dos
        // conciliaciones 'conciliada' del mismo extracto.
        await using (var db = new AppDbContext(options))
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP INDEX IF EXISTS \"ix_conciliaciones_extracto_id_conciliada\";");
        }

        var (conciliacionAntigua, conciliacionDuplicada, extractoId, movimientoNuevoId, cuentaId) =
            await SembrarConciliacionesDuplicadasAsync(options);

        // Se reaplica la migracion (se borra su registro y se vuelve a migrar).
        await using (var db = new AppDbContext(options))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"__EFMigrationsHistory\" WHERE \"migration_id\" = {MigracionIndices}");
        }

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            var todas = await db.Conciliaciones
                .IgnoreQueryFilters()
                .Where(c => c.ExtractoId == extractoId)
                .ToListAsync();

            todas.Should().HaveCount(2, "la purga marca las duplicadas, no borra filas");

            var conservada = todas.Single(c => c.Id == conciliacionAntigua);
            conservada.DeletedAt.Should().BeNull("se conserva la conciliacion mas antigua");

            var purgada = todas.Single(c => c.Id == conciliacionDuplicada);
            purgada.DeletedAt.Should().NotBeNull("la duplicada queda borrada logicamente");
        }

        // Con el indice en su sitio, una tercera conciliacion del mismo extracto
        // debe rebotar con 23505 (unique_violation).
        await using (var db = new AppDbContext(options))
        {
            db.Conciliaciones.Add(new Conciliacion
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                MovimientoEsperadoId = movimientoNuevoId,
                ExtractoId = extractoId,
                Estado = "conciliada",
                Score = 80,
                Regla = "deterministica-v1",
                FechaCreacion = DateTime.UtcNow
            });

            var act = async () => await db.SaveChangesAsync();
            // EF envuelve el error de Postgres en DbUpdateException.
            var ex = await act.Should().ThrowAsync<DbUpdateException>();
            ex.Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be("23505");
        }
    }

    [Fact]
    public async Task Indice_De_Huella_Parcial_Debe_Permitir_Reimportar_Una_Firma_Soft_Deleted()
    {
        var connectionString = await CrearBaseAuxiliarAsync("atlas_balance_lote5_huella");
        var options = BuildOptions(connectionString);

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        var cuentaId = await SembrarCuentaAsync(options);
        const string huella = "huella-lote-revertido-0001";

        await using (var db = new AppDbContext(options))
        {
            db.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 6, 29),
                Concepto = "Movimiento importado",
                Monto = -10m,
                Saldo = 90m,
                FilaNumero = 1,
                ImportacionFingerprint = huella
            });
            await db.SaveChangesAsync();
        }

        // Soft delete del extracto (como hace RevertirLote).
        await using (var db = new AppDbContext(options))
        {
            var extracto = await db.Extractos.FirstAsync(x => x.ImportacionFingerprint == huella);
            extracto.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // Reimportacion: el indice UNIQUE parcial debe permitirla.
        await using (var db = new AppDbContext(options))
        {
            db.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 6, 29),
                Concepto = "Movimiento reimportado",
                Monto = -10m,
                Saldo = 90m,
                FilaNumero = 2,
                ImportacionFingerprint = huella
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            var extractos = await db.Extractos
                .IgnoreQueryFilters()
                .Where(x => x.ImportacionFingerprint == huella)
                .ToListAsync();

            extractos.Should().HaveCount(2);
            extractos.Count(x => x.DeletedAt != null).Should().Be(1);
        }
    }

    private static DbContextOptions<AppDbContext> BuildOptions(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

    private async Task<string> CrearBaseAuxiliarAsync(string nombre)
    {
        var builder = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };

        await using (var conexion = new NpgsqlConnection(builder.ConnectionString))
        {
            await conexion.OpenAsync();
            await using var comando = conexion.CreateCommand();
            comando.CommandText = $"DROP DATABASE IF EXISTS \"{nombre}\";";
            await comando.ExecuteNonQueryAsync();
            comando.CommandText = $"CREATE DATABASE \"{nombre}\";";
            await comando.ExecuteNonQueryAsync();
        }

        builder.Database = nombre;
        return builder.ConnectionString;
    }

    private static async Task<Dictionary<string, string>> LeerIndicesAsync(string connectionString)
    {
        var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using var conexion = new NpgsqlConnection(connectionString);
        await conexion.OpenAsync();
        await using var comando = conexion.CreateCommand();
        comando.CommandText =
            "SELECT indexname, indexdef FROM pg_indexes " +
            "WHERE schemaname = 'public' AND tablename IN ('EXTRACTOS', 'CONCILIACIONES');";

        await using var reader = await comando.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            resultado[reader.GetString(0)] = reader.GetString(1);
        }

        return resultado;
    }

    private static async Task<Guid> SembrarCuentaAsync(DbContextOptions<AppDbContext> options)
    {
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();

        await using var db = new AppDbContext(options);
        db.Usuarios.Add(new Usuario
        {
            Id = usuarioId,
            Email = $"admin-{usuarioId:N}@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!Ab", workFactor: 12),
            NombreCompleto = "Admin Test Indices Lote 5",
            Rol = RolUsuario.ADMIN,
            Activo = true
        });
        db.Titulares.Add(new Titular
        {
            Id = titularId,
            Nombre = "Titular Test Indices Lote 5",
            Tipo = TipoTitular.EMPRESA
        });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta Test Indices Lote 5",
            Divisa = "EUR",
            Activa = true
        });
        await db.SaveChangesAsync();

        return cuentaId;
    }

    private static async Task<(Guid Antigua, Guid Duplicada, Guid ExtractoId, Guid MovimientoNuevoId, Guid CuentaId)>
        SembrarConciliacionesDuplicadasAsync(DbContextOptions<AppDbContext> options)
    {
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var extractoId = Guid.NewGuid();
        var movimientoUnoId = Guid.NewGuid();
        var movimientoDosId = Guid.NewGuid();
        var movimientoNuevoId = Guid.NewGuid();

        await using var db = new AppDbContext(options);
        db.Usuarios.Add(new Usuario
        {
            Id = usuarioId,
            Email = $"admin-{usuarioId:N}@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!Ab", workFactor: 12),
            NombreCompleto = "Admin Test Purga Conciliaciones",
            Rol = RolUsuario.ADMIN,
            Activo = true
        });
        db.Titulares.Add(new Titular
        {
            Id = titularId,
            Nombre = "Titular Test Purga Conciliaciones",
            Tipo = TipoTitular.EMPRESA
        });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta Test Purga Conciliaciones",
            Divisa = "EUR",
            Activa = true
        });
        db.Extractos.Add(new Extracto
        {
            Id = extractoId,
            CuentaId = cuentaId,
            Fecha = new DateOnly(2026, 6, 29),
            Concepto = "Pago factura REF-123",
            Monto = -125.50m,
            Saldo = 874.50m,
            FilaNumero = 1
        });
        db.MovimientosEsperados.AddRange(
            new MovimientoEsperado
            {
                Id = movimientoUnoId,
                CuentaId = cuentaId,
                FechaEsperada = new DateOnly(2026, 6, 29),
                Monto = -125.50m,
                Divisa = "EUR",
                Referencia = "REF 123",
                Estado = "pendiente",
                Origen = "manual",
                UsuarioCreacionId = usuarioId,
                FechaCreacion = DateTime.UtcNow
            },
            new MovimientoEsperado
            {
                Id = movimientoDosId,
                CuentaId = cuentaId,
                FechaEsperada = new DateOnly(2026, 6, 29),
                Monto = -125.50m,
                Divisa = "EUR",
                Referencia = "REF 123",
                Estado = "pendiente",
                Origen = "manual",
                UsuarioCreacionId = usuarioId,
                FechaCreacion = DateTime.UtcNow
            },
            new MovimientoEsperado
            {
                Id = movimientoNuevoId,
                CuentaId = cuentaId,
                FechaEsperada = new DateOnly(2026, 6, 29),
                Monto = -125.50m,
                Divisa = "EUR",
                Referencia = "REF 123",
                Estado = "pendiente",
                Origen = "manual",
                UsuarioCreacionId = usuarioId,
                FechaCreacion = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var antiguaId = Guid.NewGuid();
        var duplicadaId = Guid.NewGuid();

        db.Conciliaciones.Add(new Conciliacion
        {
            Id = antiguaId,
            CuentaId = cuentaId,
            MovimientoEsperadoId = movimientoUnoId,
            ExtractoId = extractoId,
            Estado = "conciliada",
            Score = 95,
            Regla = "deterministica-v1",
            UsuarioConfirmacionId = usuarioId,
            FechaConfirmacion = DateTime.UtcNow.AddDays(-2),
            FechaCreacion = DateTime.UtcNow.AddDays(-2)
        });
        db.Conciliaciones.Add(new Conciliacion
        {
            Id = duplicadaId,
            CuentaId = cuentaId,
            MovimientoEsperadoId = movimientoDosId,
            ExtractoId = extractoId,
            Estado = "conciliada",
            Score = 70,
            Regla = "deterministica-v1",
            UsuarioConfirmacionId = usuarioId,
            FechaConfirmacion = DateTime.UtcNow.AddDays(-1),
            FechaCreacion = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        return (antiguaId, duplicadaId, extractoId, movimientoNuevoId, cuentaId);
    }
}
