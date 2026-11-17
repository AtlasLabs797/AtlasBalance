using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// LOTE 5 (2026-11-16) contra PostgreSQL real. El provider InMemory no
/// reproduce ni las transacciones ni los indices unicos, asi que estos casos
/// solo se pueden comprobar aqui.
///
/// 1. B-3 (extremo a extremo): revertir un lote y reimportarlo. Requiere que
///    el indice unico de huella filtre `deleted_at` (parte de BASE DE DATOS de
///    B-3, resuelta en AppDbContext + Migrations por otro agente). Con el
///    indice actual, la reimportacion choca con el 23505 y este test falla.
///
/// 2. B-4 (atomicidad): el borrado de extractos y el guardado del lote van en
///    la misma transaccion. Se fuerza un fallo en el guardado del lote (CHECK
///    temporal) y se comprueba que el borrado de extractos se deshace.
///
/// 3. B-3 por el camino REAL de la API (ConfirmarLoteAsync, que es lo que
///    llama POST /api/importacion/lotes/{id}/confirmar). Ese camino estaba
///    roto: TryAcquireAdvisoryXactLockAsync leia con SqlQueryRaw&lt;int&gt; un
///    boolean, Npgsql lanzaba InvalidCastException y el fail-closed devolvia
///    false, de modo que la confirmacion respondia siempre 409 contra
///    PostgreSQL real y reimportar un lote revertido era imposible.
///
/// Nota: los dos primeros tests usan la via directa (ConfirmarAsync con
/// LoteId) y marcan el lote como "confirmado" a mano. El tercero usa el
/// camino real completo.
/// </summary>
[Trait("Category", "Postgres")]
[Collection(PostgresCollection.Name)]
public sealed class ImportacionLote5PostgresTests
{
    private readonly PostgresFixture _fixture;

    public ImportacionLote5PostgresTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private DbContextOptions<AppDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString + ";Include Error Detail=true")
            .UseSnakeCaseNamingConvention()
            .Options;

    [Fact]
    public async Task Revertir_Lote_Y_Reimportarlo_Deberia_Funcionar_Con_Postgres_Real()
    {
        var options = BuildOptions();

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.MigrateAsync();
        }

        var (userId, cuentaId) = await SeedCuentaAsync(options);
        var raw = "01/04/2026\tIngreso cliente\t100\t100";

        await using (var db = new AppDbContext(options))
        {
            var service = new ImportacionService(db, TestAuditService.Create(db));

            var primerLote = await service.CrearLoteAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new ImportacionLoteCrearRequest
                {
                    CuentaId = cuentaId,
                    RawData = raw,
                    Separador = "tab",
                    Mapeo = DefaultMapeo()
                },
                new DefaultHttpContext(),
                CancellationToken.None);

            var primera = await service.ConfirmarAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new ImportacionConfirmarRequest
                {
                    CuentaId = cuentaId,
                    LoteId = primerLote.Lote.Id,
                    RawData = raw,
                    Separador = "tab",
                    Mapeo = DefaultMapeo()
                },
                new DefaultHttpContext(),
                CancellationToken.None);

            primera.FilasImportadas.Should().Be(1);

            await MarcarLoteConfirmadoAsync(options, primerLote.Lote.Id);

            await service.RevertirLoteAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                primerLote.Lote.Id,
                new ImportacionLoteRevertirRequest { Motivo = "prueba" },
                new DefaultHttpContext(),
                CancellationToken.None);
        }

        // Mismo contenido en un lote nuevo: la huella es la misma que la del
        // extracto revertido, pero ese esta borrado y no puede bloquear la
        // reimportacion.
        await using (var db = new AppDbContext(options))
        {
            var service = new ImportacionService(db, TestAuditService.Create(db));

            var segundoLote = await service.CrearLoteAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new ImportacionLoteCrearRequest
                {
                    CuentaId = cuentaId,
                    RawData = raw,
                    Separador = "tab",
                    Mapeo = DefaultMapeo()
                },
                new DefaultHttpContext(),
                CancellationToken.None);

            var segunda = await service.ConfirmarAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new ImportacionConfirmarRequest
                {
                    CuentaId = cuentaId,
                    LoteId = segundoLote.Lote.Id,
                    RawData = raw,
                    Separador = "tab",
                    Mapeo = DefaultMapeo()
                },
                new DefaultHttpContext(),
                CancellationToken.None);

            segunda.FilasImportadas.Should().Be(1);
            segunda.FilasDuplicadas.Should().Be(0);
        }
    }

    [Fact]
    public async Task RevertirLoteAsync_Si_Falla_El_Guardado_Del_Lote_No_Debe_Quedar_Ningun_Extracto_Borrado()
    {
        var options = BuildOptions();

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.MigrateAsync();
        }

        var (userId, cuentaId) = await SeedCuentaAsync(options);
        Guid loteId;

        await using (var db = new AppDbContext(options))
        {
            var service = new ImportacionService(db, TestAuditService.Create(db));
            var raw = "01/04/2026\tIngreso cliente\t100\t100";

            var lote = await service.CrearLoteAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new ImportacionLoteCrearRequest
                {
                    CuentaId = cuentaId,
                    RawData = raw,
                    Separador = "tab",
                    Mapeo = DefaultMapeo()
                },
                new DefaultHttpContext(),
                CancellationToken.None);

            var confirmacion = await service.ConfirmarAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new ImportacionConfirmarRequest
                {
                    CuentaId = cuentaId,
                    LoteId = lote.Lote.Id,
                    RawData = raw,
                    Separador = "tab",
                    Mapeo = DefaultMapeo()
                },
                new DefaultHttpContext(),
                CancellationToken.None);

            confirmacion.FilasImportadas.Should().Be(1);
            loteId = lote.Lote.Id;
        }

        await MarcarLoteConfirmadoAsync(options, loteId);

        // CHECK temporal que hace fallar el guardado del lote DESPUES de que los
        // extractos ya se han marcado como borrados. Acotado a este lote para no
        // chocar con otros lotes del mismo contenedor que ya esten revertidos.
        await using (var db = new AppDbContext(options))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE \"IMPORTACION_LOTES\" ADD CONSTRAINT \"tmp_lote5_no_revert\" CHECK (\"id\" <> '{loteId}' OR \"estado\" <> 'revertido')");
        }

        try
        {
            await using var db = new AppDbContext(options);
            var service = new ImportacionService(db, TestAuditService.Create(db));

            var act = () => service.RevertirLoteAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                loteId,
                new ImportacionLoteRevertirRequest { Motivo = "prueba" },
                new DefaultHttpContext(),
                CancellationToken.None);

            await act.Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            await using var db = new AppDbContext(options);
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"IMPORTACION_LOTES\" DROP CONSTRAINT IF EXISTS \"tmp_lote5_no_revert\"");
        }

        await using (var verify = new AppDbContext(options))
        {
            var borrados = await verify.Extractos
                .IgnoreQueryFilters()
                .Where(e => e.ImportacionLoteId == loteId && e.DeletedAt != null)
                .CountAsync();

            borrados.Should().Be(0, "el borrado de extractos y el guardado del lote son una sola transaccion");

            var lote = await verify.ImportacionLotes.FirstAsync(x => x.Id == loteId);
            lote.Estado.Should().Be("confirmado");
        }
    }

    /// <summary>
    /// Estado que deja ConfirmarLoteAsync tras confirmar. Se aplica a mano
    /// porque ese camino no es utilizable contra PostgreSQL real (ver nota de
    /// la clase).
    /// </summary>
    private static async Task MarcarLoteConfirmadoAsync(DbContextOptions<AppDbContext> options, Guid loteId)
    {
        await using var db = new AppDbContext(options);
        var lote = await db.ImportacionLotes.FirstAsync(x => x.Id == loteId);
        lote.Estado = "confirmado";
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// B-3 (LOTE 5) extremo a extremo por el camino REAL de la API:
    /// POST /api/importacion/lotes/{id}/confirmar -> ConfirmarLoteAsync.
    /// Antes de corregir el MED-14 ese camino respondia siempre 409 contra
    /// PostgreSQL real, con lo que la reimportacion de un lote revertido era
    /// inservible: por muy bien que estuviera el indice de huella, el usuario
    /// no podia confirmar el lote nuevo.
    /// </summary>
    [Fact]
    public async Task Confirmar_Lote_Por_El_Camino_Real_No_Debe_Devolver_409_Y_La_Reimportacion_Debe_Funcionar()
    {
        var options = BuildOptions();

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.MigrateAsync();
        }

        var (userId, cuentaId) = await SeedCuentaAsync(options);
        var raw = "01/04/2026\tIngreso cliente\t100\t100";

        Guid primerLoteId;
        await using (var db = new AppDbContext(options))
        {
            var service = new ImportacionService(db, TestAuditService.Create(db));

            var lote = await service.CrearLoteAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new ImportacionLoteCrearRequest
                {
                    CuentaId = cuentaId,
                    RawData = raw,
                    Separador = "tab",
                    Mapeo = DefaultMapeo()
                },
                new DefaultHttpContext(),
                CancellationToken.None);

            primerLoteId = lote.Lote.Id;

            var confirmacion = await service.ConfirmarLoteAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                primerLoteId,
                new ImportacionLoteConfirmarRequest { AceptaAdvertencias = true },
                new DefaultHttpContext(),
                CancellationToken.None);

            confirmacion.FilasImportadas.Should().Be(1, "el camino real de confirmacion no debe devolver 409 por el lock");

            await service.RevertirLoteAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                primerLoteId,
                new ImportacionLoteRevertirRequest { Motivo = "prueba" },
                new DefaultHttpContext(),
                CancellationToken.None);
        }

        // Mismo contenido en un lote nuevo: la huella es la misma que la del
        // extracto revertido, pero ese esta borrado y no puede bloquear la
        // reimportacion (indice parcial de B-3).
        await using (var db = new AppDbContext(options))
        {
            var service = new ImportacionService(db, TestAuditService.Create(db));

            var segundoLote = await service.CrearLoteAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new ImportacionLoteCrearRequest
                {
                    CuentaId = cuentaId,
                    RawData = raw,
                    Separador = "tab",
                    Mapeo = DefaultMapeo()
                },
                new DefaultHttpContext(),
                CancellationToken.None);

            var confirmacion = await service.ConfirmarLoteAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                segundoLote.Lote.Id,
                new ImportacionLoteConfirmarRequest { AceptaAdvertencias = true },
                new DefaultHttpContext(),
                CancellationToken.None);

            confirmacion.FilasImportadas.Should().Be(1, "un lote revertido debe poder reimportarse");
        }

        await using (var verify = new AppDbContext(options))
        {
            var vivos = await verify.Extractos
                .Where(e => e.CuentaId == cuentaId)
                .CountAsync();

            vivos.Should().Be(1, "el extracto reimportado esta activo y el revertido sigue borrado");
        }
    }

    private static async Task<(Guid UserId, Guid CuentaId)> SeedCuentaAsync(DbContextOptions<AppDbContext> options)
    {
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        await using var db = new AppDbContext(options);
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = $"importacion-lote5-{userId:N}@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!Ab", workFactor: 12),
            NombreCompleto = "Importacion Lote 5",
            Rol = RolUsuario.EMPLEADO,
            Activo = true
        });
        db.Titulares.Add(new Titular
        {
            Id = titularId,
            Nombre = "Titular Lote 5 Postgres",
            Tipo = TipoTitular.EMPRESA
        });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta Lote 5 Postgres",
            Divisa = "EUR",
            Activa = true
        });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeImportar = true,
            PuedeAprobarImportaciones = true
        });
        await db.SaveChangesAsync();

        return (userId, cuentaId);
    }

    private static MapeoColumnasRequest DefaultMapeo() =>
        new()
        {
            Fecha = 0,
            Concepto = 1,
            Monto = 2,
            Saldo = 3
        };
}
