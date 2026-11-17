using System.Text;
using FluentAssertions;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// R-7 (parte 2 de 2) del informe de revision integral (2026-11-16).
///
/// La importacion masiva admite hasta 50.000 filas. Antes del arreglo, esas
/// filas (y los extractos que genera la confirmacion) se insertaban o se
/// actualizaban en un UNICO SaveChanges: un solo comando gigantesco con 50.000
/// sentencias, memoria proporcional al numero de filas, una transaccion abierta
/// durante todo el volcado, locks retenidos ese rato y un rollback masivo si
/// algo falla a mitad.
///
/// El arreglo parte la escritura en bloques acotados, cada uno con su propio
/// SaveChanges, dentro de la misma transaccion: el resultado final, la
/// atomicidad y la idempotencia no cambian.
///
/// Estos tests comprueban la ESTRUCTURA de la escritura (cuantos bloques y de
/// que tamano), no el rendimiento real: el rendimiento y los locks solo se
/// pueden medir contra PostgreSQL, no con el provider InMemory.
/// </summary>
public class ImportacionR7Tests
{
    /// <summary>Filas por importacion. Por encima del bloque maximo para forzar el troceado.</summary>
    private const int Filas = 5_100;

    /// <summary>Filas del test de idempotencia. Suficientes para recorrer varios bloques.</summary>
    private const int FilasIdempotencia = 2_100;

    /// <summary>
    /// Tope del bloque. El arreglo usa bloques de 1.000 filas; aqui se exige
    /// solo que ningun SaveChanges supere las 5.000 filas, para no acoplarse al
    /// valor exacto del bloque.
    /// </summary>
    private const int BloqueMaximo = 5_000;

    private static AppDbContext BuildDbContext(SaveChangesSpy spy)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(spy)
            .Options;
        return new AppDbContext(options);
    }

    // -----------------------------------------------------------------------
    // R-7: la creacion del lote (filas de validacion) se inserta por bloques.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CrearLoteAsync_Deberia_Insertar_Las_Filas_Del_Lote_Por_Bloques()
    {
        var spy = new SaveChangesSpy();
        await using var db = BuildDbContext(spy);
        var (userId, cuentaId) = await SeedCuentaAsync(db);
        var service = new ImportacionService(db, TestAuditService.Create(db));

        var lote = await service.CrearLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ImportacionLoteCrearRequest
            {
                CuentaId = cuentaId,
                RawData = BuildRawData(Filas),
                Separador = "tab",
                Mapeo = DefaultMapeo()
            },
            new DefaultHttpContext(),
            CancellationToken.None);

        lote.Lote.FilasTotal.Should().Be(Filas);

        var bloques = spy.LoteFilaAltasPorLlamada.Where(n => n > 0).ToList();
        bloques.Should().HaveCountGreaterThan(1,
            "las filas del lote no pueden entrar todas en un unico SaveChanges");
        bloques.Max().Should().BeLessThanOrEqualTo(BloqueMaximo,
            "cada SaveChanges debe insertar un bloque acotado de filas");
        bloques.Sum().Should().Be(Filas, "hay que insertar exactamente todas las filas");
    }

    // -----------------------------------------------------------------------
    // R-7: la confirmacion inserta los extractos por bloques y actualiza por
    // bloques el estado de las filas del lote (otra vez 50.000 filas en un
    // solo SaveChanges).
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ConfirmarLoteAsync_Deberia_Escribir_Los_Extractos_Y_El_Estado_Del_Lote_Por_Bloques()
    {
        var spy = new SaveChangesSpy();
        await using var db = BuildDbContext(spy);
        var (userId, cuentaId) = await SeedCuentaAsync(db);
        var service = new ImportacionService(db, TestAuditService.Create(db));

        var lote = await service.CrearLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ImportacionLoteCrearRequest
            {
                CuentaId = cuentaId,
                RawData = BuildRawData(Filas),
                Separador = "tab",
                Mapeo = DefaultMapeo()
            },
            new DefaultHttpContext(),
            CancellationToken.None);

        var response = await service.ConfirmarLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            lote.Lote.Id,
            new ImportacionLoteConfirmarRequest
            {
                FilasAImportar = Enumerable.Range(1, Filas).ToList(),
                AceptaAdvertencias = true
            },
            new DefaultHttpContext(),
            CancellationToken.None);

        response.FilasImportadas.Should().Be(Filas);

        var bloquesExtractos = spy.ExtractoAltasPorLlamada.Where(n => n > 0).ToList();
        bloquesExtractos.Should().HaveCountGreaterThan(1,
            "los extractos no pueden entrar todos en un unico SaveChanges");
        bloquesExtractos.Max().Should().BeLessThanOrEqualTo(BloqueMaximo,
            "cada SaveChanges debe insertar un bloque acotado de extractos");
        bloquesExtractos.Sum().Should().Be(Filas, "hay que insertar exactamente todos los extractos");

        var bloquesEstado = spy.LoteFilaModificacionesPorLlamada.Where(n => n > 0).ToList();
        bloquesEstado.Should().HaveCountGreaterThan(1,
            "el estado de las filas del lote no puede actualizarse en un unico SaveChanges");
        bloquesEstado.Max().Should().BeLessThanOrEqualTo(BloqueMaximo,
            "cada SaveChanges debe actualizar un bloque acotado de filas");
        bloquesEstado.Sum().Should().Be(Filas, "hay que actualizar exactamente todas las filas validas");
    }

    // -----------------------------------------------------------------------
    // R-7: la idempotencia sigue intacta al trocear la insercion. El reintento
    // con la misma clave devuelve el lote ya creado y no duplica filas.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CrearLoteAsync_Con_La_Misma_Clave_De_Idempotencia_No_Debe_Duplicar_Filas()
    {
        var spy = new SaveChangesSpy();
        await using var db = BuildDbContext(spy);
        var (userId, cuentaId) = await SeedCuentaAsync(db);
        var service = new ImportacionService(db, TestAuditService.Create(db));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Idempotency-Key"] = "r7-clave-idempotencia";

        var primero = await service.CrearLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ImportacionLoteCrearRequest
            {
                CuentaId = cuentaId,
                RawData = BuildRawData(FilasIdempotencia),
                Separador = "tab",
                Mapeo = DefaultMapeo()
            },
            httpContext,
            CancellationToken.None);

        var segundo = await service.CrearLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ImportacionLoteCrearRequest
            {
                CuentaId = cuentaId,
                RawData = BuildRawData(FilasIdempotencia),
                Separador = "tab",
                Mapeo = DefaultMapeo()
            },
            httpContext,
            CancellationToken.None);

        segundo.Lote.Id.Should().Be(primero.Lote.Id, "la clave de idempotencia devuelve el lote ya creado");

        var lotes = await db.ImportacionLotes.CountAsync();
        lotes.Should().Be(1, "no debe crearse un lote nuevo con la misma clave");

        var filas = await db.ImportacionLoteFilas.CountAsync();
        filas.Should().Be(FilasIdempotencia, "el reintento no debe duplicar las filas del lote");
    }

    private static string BuildRawData(int filas)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= filas; i++)
        {
            sb.Append("01/04/2026\tMovimiento ").Append(i).Append("\t100\t100\n");
        }

        return sb.ToString();
    }

    private static async Task<(Guid UserId, Guid CuentaId)> SeedCuentaAsync(AppDbContext db)
    {
        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular R7", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta R7", Divisa = "EUR", Activa = true };
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            PuedeImportar = true,
            PuedeAprobarImportaciones = true
        });
        await db.SaveChangesAsync();
        return (userId, cuenta.Id);
    }

    private static MapeoColumnasRequest DefaultMapeo() =>
        new()
        {
            Fecha = 0,
            Concepto = 1,
            Monto = 2,
            Saldo = 3
        };

    /// <summary>
    /// Registra, en cada SaveChanges, cuantas filas de cada tipo se escriben.
    /// Asi se puede comprobar que la escritura va troceada y no en un comando
    /// gigante, sin depender de PostgreSQL.
    /// </summary>
    private sealed class SaveChangesSpy : SaveChangesInterceptor
    {
        public List<int> LoteFilaAltasPorLlamada { get; } = new();
        public List<int> LoteFilaModificacionesPorLlamada { get; } = new();
        public List<int> ExtractoAltasPorLlamada { get; } = new();

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Capturar(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Capturar(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Capturar(DbContext? context)
        {
            if (context is null)
            {
                return;
            }

            // Una sola pasada por el ChangeTracker: el coste de Entries<T>() es
            // proporcional a las entidades trackeadas y aqui llegan a miles.
            var entradas = context.ChangeTracker.Entries();
            LoteFilaAltasPorLlamada.Add(entradas.Count(e => e.Entity is ImportacionLoteFila && e.State == EntityState.Added));
            LoteFilaModificacionesPorLlamada.Add(entradas.Count(e => e.Entity is ImportacionLoteFila && e.State == EntityState.Modified));
            ExtractoAltasPorLlamada.Add(entradas.Count(e => e.Entity is Extracto && e.State == EntityState.Added));
        }
    }
}
