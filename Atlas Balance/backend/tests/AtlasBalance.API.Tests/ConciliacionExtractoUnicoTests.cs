using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// B-6 (LOTE 5): un mismo extracto no puede quedar conciliado dos veces.
/// La parte de servicio esta en ConciliacionService (error de dominio 409).
/// Esta parte es la de BASE DE DATOS: hace falta un indice UNICO PARCIAL
/// sobre CONCILIACIONES(extracto_id) que solo afecte a las conciliaciones
/// en estado 'conciliada' y a las no borradas logicamente.
///
/// indice esperado (lo implementa el agente de Migrations + AppDbContext):
///   CREATE UNIQUE INDEX "ix_conciliaciones_extracto_id_conciliada"
///       ON "CONCILIACIONES" ("extracto_id")
///       WHERE "estado" = 'conciliada'
///         AND "extracto_id" IS NOT NULL
///         AND "deleted_at" IS NULL;
///
/// Test contra PostgreSQL real (Testcontainers).
/// </summary>
[Trait("Category", "Postgres")]
[Collection(PostgresCollection.Name)]
public sealed class ConciliacionExtractoUnicoTests
{
    private readonly PostgresFixture _fixture;

    public ConciliacionExtractoUnicoTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Dos_Conciliaciones_Conciliadas_Del_Mismo_Extracto_Deben_Fallar_Con_Error_De_Unicidad()
    {
        var connectionString = _fixture.ConnectionString + ";Include Error Detail=true";
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
        var usuarioId = Guid.NewGuid();
        var extractoId = Guid.NewGuid();
        var movimientoUnoId = Guid.NewGuid();
        var movimientoDosId = Guid.NewGuid();

        await using (var db = new AppDbContext(options))
        {
            db.Usuarios.Add(new Usuario
            {
                Id = usuarioId,
                Email = $"admin-{usuarioId:N}@test.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!Ab", workFactor: 12),
                NombreCompleto = "Admin Test Conciliacion Extracto UNIQUE",
                Rol = RolUsuario.ADMIN,
                Activo = true
            });
            db.Titulares.Add(new Titular
            {
                Id = titularId,
                Nombre = "Titular Test Conciliacion Extracto UNIQUE",
                Tipo = TipoTitular.EMPRESA
            });
            db.Cuentas.Add(new Cuenta
            {
                Id = cuentaId,
                TitularId = titularId,
                Nombre = "Cuenta Test Conciliacion Extracto UNIQUE",
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
                    Concepto = "Pago proveedor",
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
                    Concepto = "Pago proveedor duplicado",
                    Estado = "pendiente",
                    Origen = "manual",
                    UsuarioCreacionId = usuarioId,
                    FechaCreacion = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        // Primera conciliacion: el extracto queda conciliado.
        await using (var db = new AppDbContext(options))
        {
            db.Conciliaciones.Add(new Conciliacion
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                MovimientoEsperadoId = movimientoUnoId,
                ExtractoId = extractoId,
                Estado = "conciliada",
                Score = 95,
                Regla = "deterministica-v1",
                UsuarioConfirmacionId = usuarioId,
                FechaConfirmacion = DateTime.UtcNow,
                FechaCreacion = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Segunda conciliacion sobre el MISMO extracto: debe rebotar con
        // 23505 (unique_violation) cuando exista el indice unico parcial.
        await using (var db = new AppDbContext(options))
        {
            db.Conciliaciones.Add(new Conciliacion
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                MovimientoEsperadoId = movimientoDosId,
                ExtractoId = extractoId,
                Estado = "conciliada",
                Score = 70,
                Regla = "deterministica-v1",
                UsuarioConfirmacionId = usuarioId,
                FechaConfirmacion = DateTime.UtcNow,
                FechaCreacion = DateTime.UtcNow
            });

            var act = async () => await db.SaveChangesAsync();
            // EF envuelve la excepcion de PostgreSQL en DbUpdateException: el
            // 23505 (unique_violation) llega en la InnerException.
            var ex = await act.Should().ThrowAsync<DbUpdateException>();
            ex.Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be("23505");
        }
    }

    [Fact]
    public async Task Dos_Sugerencias_Sobre_El_Mismo_Extracto_Deben_Ser_Permitidas()
    {
        var connectionString = _fixture.ConnectionString + ";Include Error Detail=true";
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
        var usuarioId = Guid.NewGuid();
        var extractoId = Guid.NewGuid();
        var movimientoUnoId = Guid.NewGuid();
        var movimientoDosId = Guid.NewGuid();

        await using (var db = new AppDbContext(options))
        {
            db.Usuarios.Add(new Usuario
            {
                Id = usuarioId,
                Email = $"admin-{usuarioId:N}@test.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!Ab", workFactor: 12),
                NombreCompleto = "Admin Test Conciliacion Sugerida",
                Rol = RolUsuario.ADMIN,
                Activo = true
            });
            db.Titulares.Add(new Titular
            {
                Id = titularId,
                Nombre = "Titular Test Conciliacion Sugerida",
                Tipo = TipoTitular.EMPRESA
            });
            db.Cuentas.Add(new Cuenta
            {
                Id = cuentaId,
                TitularId = titularId,
                Nombre = "Cuenta Test Conciliacion Sugerida",
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
                    Concepto = "Pago proveedor",
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
                    Concepto = "Pago proveedor duplicado",
                    Estado = "pendiente",
                    Origen = "manual",
                    UsuarioCreacionId = usuarioId,
                    FechaCreacion = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        // El indice unico parcial solo aplica a 'conciliada': dos
        // sugerencias sobre el mismo extracto (movimientos distintos) siguen
        // siendo validas.
        await using (var db = new AppDbContext(options))
        {
            db.Conciliaciones.AddRange(
                new Conciliacion
                {
                    Id = Guid.NewGuid(),
                    CuentaId = cuentaId,
                    MovimientoEsperadoId = movimientoUnoId,
                    ExtractoId = extractoId,
                    Estado = "sugerida",
                    Score = 95,
                    Regla = "deterministica-v1",
                    FechaCreacion = DateTime.UtcNow
                },
                new Conciliacion
                {
                    Id = Guid.NewGuid(),
                    CuentaId = cuentaId,
                    MovimientoEsperadoId = movimientoDosId,
                    ExtractoId = extractoId,
                    Estado = "sugerida",
                    Score = 70,
                    Regla = "deterministica-v1",
                    FechaCreacion = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            var sugerencias = await db.Conciliaciones
                .Where(x => x.ExtractoId == extractoId && x.Estado == "sugerida")
                .ToListAsync();
            sugerencias.Should().HaveCount(2);
        }
    }
}
