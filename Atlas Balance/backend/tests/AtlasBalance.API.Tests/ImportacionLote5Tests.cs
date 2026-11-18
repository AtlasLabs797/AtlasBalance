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
/// LOTE 5 del informe de revision integral (2026-11-16).
/// Cubre, con el provider InMemory, la parte de servicio de:
/// B-3 (la deduplicacion por huella ignora las filas con soft delete),
/// B-4 (reversion atomica, validacion de estado e invalidacion de conciliaciones),
/// B-9 (el saldo anterior del movimiento de plazo fijo ignora las filas borradas)
/// y B-13 (un importe con parte entera "0" no se interpreta como miles).
///
/// La atomicidad de la reversion (B-4) solo se puede comprobar contra
/// PostgreSQL real: en InMemory no hay transacciones. Ese caso vive en
/// ImportacionLote5PostgresTests.
/// </summary>
public class ImportacionLote5Tests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    // -----------------------------------------------------------------------
    // B-3: tras revertir un lote, las filas borradas no pueden contar como
    // duplicados: el lote se tiene que poder reimportar.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ConfirmarLoteAsync_Tras_Revertir_El_Lote_Anterior_Deberia_Permitir_Reimportar()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedCuentaAsync(db, puedeEliminarLineas: true);
        var service = new ImportacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
        var raw = "01/04/2026\tIngreso cliente\t100\t100";

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

        var primeraConfirmacion = await service.ConfirmarLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            primerLote.Lote.Id,
            new ImportacionLoteConfirmarRequest(),
            new DefaultHttpContext(),
            CancellationToken.None);

        primeraConfirmacion.FilasImportadas.Should().Be(1);

        await service.RevertirLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            primerLote.Lote.Id,
            new ImportacionLoteRevertirRequest { Motivo = "prueba" },
            new DefaultHttpContext(),
            CancellationToken.None);

        // Mismo contenido, lote nuevo (el lote revertido ya esta cerrado).
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

        var segundaConfirmacion = await service.ConfirmarLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            segundoLote.Lote.Id,
            new ImportacionLoteConfirmarRequest(),
            new DefaultHttpContext(),
            CancellationToken.None);

        segundaConfirmacion.FilasImportadas.Should().Be(1, "las filas con soft delete no son duplicados");
        segundaConfirmacion.FilasDuplicadas.Should().Be(0);

        var vivos = await db.Extractos.Where(e => e.DeletedAt == null).ToListAsync();
        vivos.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // B-4: solo un lote confirmado es reversible. El resto es un error de
    // dominio (409), no un 500.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task RevertirLoteAsync_Deberia_Rechazar_Un_Lote_No_Confirmado()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedCuentaAsync(db, puedeEliminarLineas: true);
        var service = new ImportacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));

        var lote = await service.CrearLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ImportacionLoteCrearRequest
            {
                CuentaId = cuentaId,
                RawData = "01/04/2026\tIngreso cliente\t100\t100",
                Separador = "tab",
                Mapeo = DefaultMapeo()
            },
            new DefaultHttpContext(),
            CancellationToken.None);

        var act = () => service.RevertirLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            lote.Lote.Id,
            new ImportacionLoteRevertirRequest { Motivo = "prueba" },
            new DefaultHttpContext(),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ImportacionException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        ex.Which.Message.Should().Contain("validado");

        var loteRecargado = await db.ImportacionLotes.FirstAsync(x => x.Id == lote.Lote.Id);
        loteRecargado.Estado.Should().Be("validado");
    }

    // -----------------------------------------------------------------------
    // B-4: al revertir un lote se invalidan las conciliaciones de los
    // extractos que se borran (estado "excepcion" del dominio) y el
    // movimiento esperado vuelve a la cola de sugerencias.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task RevertirLoteAsync_Deberia_Invalidar_Las_Conciliaciones_De_Los_Extractos_Borrados()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedCuentaAsync(db, puedeEliminarLineas: true);
        var service = new ImportacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));

        var lote = await service.CrearLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ImportacionLoteCrearRequest
            {
                CuentaId = cuentaId,
                RawData = "01/04/2026\tIngreso cliente\t100\t100",
                Separador = "tab",
                Mapeo = DefaultMapeo()
            },
            new DefaultHttpContext(),
            CancellationToken.None);

        await service.ConfirmarLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            lote.Lote.Id,
            new ImportacionLoteConfirmarRequest(),
            new DefaultHttpContext(),
            CancellationToken.None);

        var extracto = await db.Extractos.FirstAsync(x => x.ImportacionLoteId == lote.Lote.Id);

        var movimiento = new MovimientoEsperado
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            FechaEsperada = new DateOnly(2026, 4, 1),
            Monto = 100m,
            Divisa = "EUR",
            Estado = "sugerida"
        };
        var conciliacion = new Conciliacion
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            MovimientoEsperadoId = movimiento.Id,
            ExtractoId = extracto.Id,
            Estado = "sugerida"
        };
        db.MovimientosEsperados.Add(movimiento);
        db.Conciliaciones.Add(conciliacion);
        await db.SaveChangesAsync();

        await service.RevertirLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            lote.Lote.Id,
            new ImportacionLoteRevertirRequest { Motivo = "prueba" },
            new DefaultHttpContext(),
            CancellationToken.None);

        var conciliacionRecargada = await db.Conciliaciones.FirstAsync(x => x.Id == conciliacion.Id);
        conciliacionRecargada.Estado.Should().Be("excepcion");
        conciliacionRecargada.Observacion.Should().Contain("revertido");

        var movimientoRecargado = await db.MovimientosEsperados.FirstAsync(x => x.Id == movimiento.Id);
        movimientoRecargado.Estado.Should().Be("pendiente");
    }

    // -----------------------------------------------------------------------
    // B-9: el saldo anterior de un movimiento de plazo fijo se calcula sobre
    // los extractos vivos, no sobre los que estan borrados (soft delete).
    // -----------------------------------------------------------------------
    [Fact]
    public async Task RegistrarMovimientoPlazoFijoAsync_No_Debe_Usar_El_Saldo_De_Extractos_Borrados()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Plazo", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta
        {
            Id = Guid.NewGuid(),
            TitularId = titular.Id,
            Nombre = "Deposito",
            Divisa = "EUR",
            TipoCuenta = TipoCuenta.PLAZO_FIJO,
            Activa = true
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = new DateOnly(2026, 3, 1),
            Concepto = "Apertura",
            Monto = 1000m,
            Saldo = 100m,
            FilaNumero = 1
        });
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = new DateOnly(2026, 3, 2),
            Concepto = "Revertido",
            Monto = 900m,
            Saldo = 999m,
            FilaNumero = 2,
            DeletedAt = DateTime.UtcNow
        });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var service = new ImportacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));

        var result = await service.RegistrarMovimientoPlazoFijoAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ImportacionPlazoFijoMovimientoRequest
            {
                CuentaId = cuenta.Id,
                TipoMovimiento = "INGRESO",
                Fecha = new DateOnly(2026, 4, 2),
                Monto = 50m,
                Concepto = "Aportacion"
            },
            new DefaultHttpContext(),
            CancellationToken.None);

        result.SaldoAnterior.Should().Be(100m, "un extracto con soft delete no es el saldo vigente");
        result.SaldoActual.Should().Be(150m);
    }

    // -----------------------------------------------------------------------
    // B-13: si la parte entera es 0, el numero no se trata como miles.
    // "0,500" es 0,5 (coma decimal), no 500.
    // -----------------------------------------------------------------------
    [Theory]
    [InlineData("0,500", 0.5)]
    [InlineData("0,50", 0.5)]
    [InlineData("1,500", 1500)]
    [InlineData("12,345", 12345)]
    [InlineData("1.234", 1234)]
    public async Task ConfirmarAsync_No_Debe_Interpretar_Una_Parte_Entera_Cero_Como_Miles(string raw, decimal expected)
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedCuentaAsync(db);
        var service = new ImportacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));

        var result = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ImportacionConfirmarRequest
            {
                CuentaId = cuentaId,
                RawData = $"01/04/2026\tMovimiento\t{raw}\t{raw}",
                Separador = "tab",
                Mapeo = DefaultMapeo()
            },
            new DefaultHttpContext(),
            CancellationToken.None);

        result.FilasImportadas.Should().Be(1);

        var extracto = await db.Extractos.SingleAsync();
        extracto.Monto.Should().Be(expected);
        extracto.Saldo.Should().Be(expected);
    }

    // -----------------------------------------------------------------------
    // B-17: el orden de las filas pegadas se detecta por las fechas. La app
    // numera las filas de abajo arriba (la linea superior del pegado es la
    // ultima del lote y de ahi sale el "saldo actual"), asi que espera que la
    // linea mas reciente este ARRIBA. Si el fichero viene del mas antiguo al
    // mas reciente, el saldo actual seria el de la fila mas antigua: se avisa.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ValidarAsync_Debe_Avisar_Si_Las_Filas_Vienen_Del_Mas_Antiguo_Al_Mas_Reciente()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedCuentaAsync(db);
        var service = new ImportacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));

        // 01/04 (mas antigua) arriba, 03/04 (mas reciente) abajo.
        var raw = "01/04/2026\tIngreso cliente\t100\t100\n03/04/2026\tPago proveedor\t50\t50";

        var validacion = await service.ValidarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ImportacionValidarRequest
            {
                CuentaId = cuentaId,
                RawData = raw,
                Separador = "tab",
                Mapeo = DefaultMapeo()
            },
            CancellationToken.None);

        validacion.Advertencias.Should().ContainSingle();
        validacion.Advertencias[0].Should().Contain("mas antiguo a mas reciente");
    }

    [Fact]
    public async Task ValidarAsync_No_Debe_Avisar_Si_Las_Filas_Vienen_Del_Mas_Reciente_Al_Mas_Antiguo()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedCuentaAsync(db);
        var service = new ImportacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));

        // Orden esperado: la linea mas reciente arriba del todo.
        var raw = "03/04/2026\tPago proveedor\t50\t50\n01/04/2026\tIngreso cliente\t100\t100";

        var validacion = await service.ValidarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ImportacionValidarRequest
            {
                CuentaId = cuentaId,
                RawData = raw,
                Separador = "tab",
                Mapeo = DefaultMapeo()
            },
            CancellationToken.None);

        validacion.Advertencias.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidarAsync_No_Debe_Avisar_Si_El_Fichero_Tiene_Una_Sola_Fecha()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedCuentaAsync(db);
        var service = new ImportacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));

        var raw = "01/04/2026\tIngreso cliente\t100\t100\n01/04/2026\tPago proveedor\t50\t50";

        var validacion = await service.ValidarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ImportacionValidarRequest
            {
                CuentaId = cuentaId,
                RawData = raw,
                Separador = "tab",
                Mapeo = DefaultMapeo()
            },
            CancellationToken.None);

        validacion.Advertencias.Should().BeEmpty("con una sola fecha no se puede saber el orden");
    }

    private static async Task<(Guid UserId, Guid CuentaId)> SeedCuentaAsync(AppDbContext db, bool puedeEliminarLineas = false)
    {
        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Lote 5", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Lote 5", Divisa = "EUR", Activa = true };
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            PuedeImportar = true,
            PuedeAprobarImportaciones = true,
            // S-3: solo los tests que revierten un lote necesitan este
            // permiso (la reversion borra masivamente los extractos).
            PuedeEliminarLineas = puedeEliminarLineas
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
}
