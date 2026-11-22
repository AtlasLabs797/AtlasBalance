using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// LOTE 5 del informe de revision integral (2026-11-16):
///   B-5: el score no impone el tope de importe por movimiento.
///   B-6: un mismo extracto puede conciliarse dos veces, SetEstado acepta
///         cualquier transicion de estado y FirstAsync da 500 si el
///         movimiento esperado ya no existe.
/// </summary>
public class ConciliacionLote5Tests
{
    private static AppDbContext BuildDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new AppDbContext(options);
    }

    private static Configuracion[] ToleranciaPorDefecto() =>
    [
        new Configuracion { Clave = "conciliacion_tolerance_amount", Valor = "2", Tipo = "decimal" },
        new Configuracion { Clave = "conciliacion_tolerance_percent", Valor = "0.01", Tipo = "decimal" }
    ];

    // -----------------------------------------------------------------------
    // B-5: tope de importe por movimiento
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SugerirAsync_No_Debe_Proponer_Un_Extracto_Cuyo_Importe_Excede_La_Tolerancia_Del_Movimiento()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var extractoGrandeId = Guid.NewGuid();
        var movimientoGrandeId = Guid.NewGuid();
        var movimientoPequenoId = Guid.NewGuid();

        await using (var db = BuildDbContext(dbName))
        {
            var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Lote5 B5", Tipo = TipoTitular.EMPRESA };
            var cuenta = new Cuenta { Id = cuentaId, TitularId = titular.Id, Nombre = "Cuenta Lote5 B5", Divisa = "EUR", Activa = true };
            // Un solo extracto de 900 EUR. El movimiento de 50 EUR no tiene
            // ningun extracto propio: si el score no impone el tope de importe,
            // se le propone este extracto de 900 EUR.
            var extracto = new Extracto
            {
                Id = extractoGrandeId,
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 6, 29),
                Concepto = "Cargo grande REF-BIG",
                Monto = 900m,
                Saldo = 900m,
                FilaNumero = 1
            };

            db.Titulares.Add(titular);
            db.Cuentas.Add(cuenta);
            db.Extractos.Add(extracto);
            db.Configuraciones.AddRange(ToleranciaPorDefecto());
            db.PermisosUsuario.Add(new PermisoUsuario
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                CuentaId = cuentaId,
                PuedeConciliar = true,
                PuedeCerrarConciliacion = true
            });
            await db.SaveChangesAsync();

            var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
            var grande = await service.CrearMovimientoEsperadoAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new MovimientoEsperadoCrearRequest
                {
                    CuentaId = cuentaId,
                    FechaEsperada = new DateOnly(2026, 6, 29),
                    Monto = 900m,
                    Referencia = "REF BIG",
                    Concepto = "Cargo grande"
                },
                new DefaultHttpContext(),
                CancellationToken.None);
            var pequeno = await service.CrearMovimientoEsperadoAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new MovimientoEsperadoCrearRequest
                {
                    CuentaId = cuentaId,
                    FechaEsperada = new DateOnly(2026, 6, 29),
                    Monto = 50m,
                    Referencia = "REF SMALL",
                    Concepto = "Movimiento pequeno"
                },
                new DefaultHttpContext(),
                CancellationToken.None);

            movimientoGrandeId = grande.Id;
            movimientoPequenoId = pequeno.Id;
        }

        await using (var db = BuildDbContext(dbName))
        {
            var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
            var sugerencias = await service.SugerirAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new ConciliacionSugerirRequest { CuentaId = cuentaId, VentanaDias = 1 },
                new DefaultHttpContext(),
                CancellationToken.None);

            sugerencias.Sugerencias.Should().NotContain(x => x.MovimientoEsperadoId == movimientoPequenoId);
            sugerencias.Sugerencias.Should().Contain(x => x.MovimientoEsperadoId == movimientoGrandeId && x.ExtractoId == extractoGrandeId);
            sugerencias.SugerenciasCreadas.Should().Be(1);
        }
    }

    [Fact]
    public async Task SugerirAsync_No_Debe_Proponer_Un_Extracto_Que_Se_Pasa_Por_Poco_De_La_Tolerancia_Del_Movimiento()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var movimientoGrandeId = Guid.NewGuid();
        var movimientoPequenoId = Guid.NewGuid();

        await using (var db = BuildDbContext(dbName))
        {
            var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Lote5 B5 borde", Tipo = TipoTitular.EMPRESA };
            var cuenta = new Cuenta { Id = cuentaId, TitularId = titular.Id, Nombre = "Cuenta Lote5 B5 borde", Divisa = "EUR", Activa = true };
            db.Titulares.Add(titular);
            db.Cuentas.Add(cuenta);
            // Extractos de 52,50 EUR y de 900 EUR. La tolerancia del
            // movimiento de 50 EUR es max(2, 50 * 0,01) = 2 EUR, asi que
            // 52,50 EUR se queda fuera del tope por 0,50 EUR.
            db.Extractos.AddRange(
                new Extracto
                {
                    Id = Guid.NewGuid(),
                    CuentaId = cuentaId,
                    Fecha = new DateOnly(2026, 6, 29),
                    Concepto = "Pago medio REF-MED",
                    Monto = 52.50m,
                    Saldo = 52.50m,
                    FilaNumero = 1
                },
                new Extracto
                {
                    Id = Guid.NewGuid(),
                    CuentaId = cuentaId,
                    Fecha = new DateOnly(2026, 6, 29),
                    Concepto = "Cargo grande REF-BIG",
                    Monto = 900m,
                    Saldo = 952.50m,
                    FilaNumero = 2
                });
            db.Configuraciones.AddRange(ToleranciaPorDefecto());
            db.PermisosUsuario.Add(new PermisoUsuario
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                CuentaId = cuentaId,
                PuedeConciliar = true,
                PuedeCerrarConciliacion = true
            });
            await db.SaveChangesAsync();

            var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
            var grande = await service.CrearMovimientoEsperadoAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new MovimientoEsperadoCrearRequest
                {
                    CuentaId = cuentaId,
                    FechaEsperada = new DateOnly(2026, 6, 29),
                    Monto = 900m,
                    Referencia = "REF BIG",
                    Concepto = "Cargo grande"
                },
                new DefaultHttpContext(),
                CancellationToken.None);
            var pequeno = await service.CrearMovimientoEsperadoAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new MovimientoEsperadoCrearRequest
                {
                    CuentaId = cuentaId,
                    FechaEsperada = new DateOnly(2026, 6, 29),
                    Monto = 50m,
                    Referencia = "REF SMALL",
                    Concepto = "Movimiento pequeno"
                },
                new DefaultHttpContext(),
                CancellationToken.None);

            movimientoGrandeId = grande.Id;
            movimientoPequenoId = pequeno.Id;
        }

        await using (var db = BuildDbContext(dbName))
        {
            var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
            var sugerencias = await service.SugerirAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                new ConciliacionSugerirRequest { CuentaId = cuentaId, VentanaDias = 1 },
                new DefaultHttpContext(),
                CancellationToken.None);

            sugerencias.Sugerencias.Should().NotContain(x => x.MovimientoEsperadoId == movimientoPequenoId);
            sugerencias.Sugerencias.Should().Contain(x => x.MovimientoEsperadoId == movimientoGrandeId);
            sugerencias.SugerenciasCreadas.Should().Be(1);
        }
    }

    // -----------------------------------------------------------------------
    // B-6 (a): un mismo extracto no puede conciliarse dos veces
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarAsync_Debe_Rechazar_La_Segunda_Conciliacion_Del_Mismo_Extracto()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var primeraConciliacionId = Guid.NewGuid();
        var segundaConciliacionId = Guid.NewGuid();

        await using (var db = BuildDbContext(dbName))
        {
            var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Lote5 B6a", Tipo = TipoTitular.EMPRESA };
            var cuenta = new Cuenta { Id = cuentaId, TitularId = titular.Id, Nombre = "Cuenta Lote5 B6a", Divisa = "EUR", Activa = true };
            var extracto = new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 6, 29),
                Concepto = "Pago factura REF-123",
                Monto = -125.50m,
                Saldo = 874.50m,
                FilaNumero = 1
            };
            var movimientoUno = new MovimientoEsperado
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                FechaEsperada = new DateOnly(2026, 6, 29),
                Monto = -125.50m,
                Divisa = "EUR",
                Referencia = "REF 123",
                Concepto = "Pago proveedor",
                Estado = "pendiente",
                Origen = "manual",
                UsuarioCreacionId = userId,
                FechaCreacion = DateTime.UtcNow
            };
            var movimientoDos = new MovimientoEsperado
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                FechaEsperada = new DateOnly(2026, 6, 29),
                Monto = -125.50m,
                Divisa = "EUR",
                Referencia = "REF 123",
                Concepto = "Pago proveedor duplicado",
                Estado = "pendiente",
                Origen = "manual",
                UsuarioCreacionId = userId,
                FechaCreacion = DateTime.UtcNow
            };

            db.Titulares.Add(titular);
            db.Cuentas.Add(cuenta);
            db.Extractos.Add(extracto);
            db.MovimientosEsperados.AddRange(movimientoUno, movimientoDos);
            db.PermisosUsuario.Add(new PermisoUsuario
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                CuentaId = cuentaId,
                PuedeConciliar = true,
                PuedeCerrarConciliacion = true
            });
            // Dos sugerencias distintas (movimientos distintos) sobre el MISMO
            // extracto: la segunda conciliacion del extracto debe fallar.
            db.Conciliaciones.AddRange(
                new Conciliacion
                {
                    Id = primeraConciliacionId,
                    CuentaId = cuentaId,
                    MovimientoEsperadoId = movimientoUno.Id,
                    ExtractoId = extracto.Id,
                    Estado = "sugerida",
                    Score = 95,
                    Regla = "deterministica-v1",
                    FechaCreacion = DateTime.UtcNow
                },
                new Conciliacion
                {
                    Id = segundaConciliacionId,
                    CuentaId = cuentaId,
                    MovimientoEsperadoId = movimientoDos.Id,
                    ExtractoId = extracto.Id,
                    Estado = "sugerida",
                    Score = 70,
                    Regla = "deterministica-v1",
                    FechaCreacion = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        await using (var db = BuildDbContext(dbName))
        {
            var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));

            var primera = await service.ConfirmarAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                primeraConciliacionId,
                new ConciliacionCambiarEstadoRequest(),
                new DefaultHttpContext(),
                CancellationToken.None);
            primera.Estado.Should().Be("conciliada");

            var act = () => service.ConfirmarAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                segundaConciliacionId,
                new ConciliacionCambiarEstadoRequest(),
                new DefaultHttpContext(),
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ConciliacionException>();
            ex.Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        }
    }

    // -----------------------------------------------------------------------
    // B-6 (b): transiciones de estado validas
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarAsync_Debe_Permitir_El_Pasaje_De_Excepcion_A_Conciliada()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var conciliacionId = Guid.NewGuid();

        await using (var db = BuildDbContext(dbName))
        {
            var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Lote5 B6b excepcion", Tipo = TipoTitular.EMPRESA };
            var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Lote5 B6b excepcion", Divisa = "EUR", Activa = true };
            var movimiento = new MovimientoEsperado
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                FechaEsperada = new DateOnly(2026, 6, 29),
                Monto = -125.50m,
                Divisa = "EUR",
                Estado = "excepcion",
                Origen = "manual",
                UsuarioCreacionId = userId,
                FechaCreacion = DateTime.UtcNow
            };

            db.Titulares.Add(titular);
            db.Cuentas.Add(cuenta);
            db.MovimientosEsperados.Add(movimiento);
            db.PermisosUsuario.Add(new PermisoUsuario
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                CuentaId = cuenta.Id,
                PuedeConciliar = true,
                PuedeCerrarConciliacion = true
            });
            db.Conciliaciones.Add(new Conciliacion
            {
                Id = conciliacionId,
                CuentaId = cuenta.Id,
                MovimientoEsperadoId = movimiento.Id,
                Estado = "excepcion",
                Score = 95,
                Regla = "deterministica-v1",
                FechaCreacion = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using (var db = BuildDbContext(dbName))
        {
            var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
            var act = () => service.ConfirmarAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                conciliacionId,
                new ConciliacionCambiarEstadoRequest(),
                new DefaultHttpContext(),
                CancellationToken.None);

            // El boton "Conciliar" de la pantalla aparece en toda fila que no
            // este conciliada, incluida una excepcion: el servidor no puede
            // rechazar ese paso con un 409.
            var result = await act.Should().NotThrowAsync();
            result.Which.Estado.Should().Be("conciliada");
        }
    }

    [Fact]
    public async Task ConfirmarAsync_Debe_Rechazar_El_Pasaje_De_Resuelta_A_Conciliada()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var conciliacionId = Guid.NewGuid();

        await using (var db = BuildDbContext(dbName))
        {
            var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Lote5 B6b resuelta", Tipo = TipoTitular.EMPRESA };
            var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Lote5 B6b resuelta", Divisa = "EUR", Activa = true };
            var movimiento = new MovimientoEsperado
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                FechaEsperada = new DateOnly(2026, 6, 29),
                Monto = -125.50m,
                Divisa = "EUR",
                Estado = "resuelta",
                Origen = "manual",
                UsuarioCreacionId = userId,
                FechaCreacion = DateTime.UtcNow
            };

            db.Titulares.Add(titular);
            db.Cuentas.Add(cuenta);
            db.MovimientosEsperados.Add(movimiento);
            db.PermisosUsuario.Add(new PermisoUsuario
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                CuentaId = cuenta.Id,
                PuedeConciliar = true,
                PuedeCerrarConciliacion = true
            });
            db.Conciliaciones.Add(new Conciliacion
            {
                Id = conciliacionId,
                CuentaId = cuenta.Id,
                MovimientoEsperadoId = movimiento.Id,
                Estado = "resuelta",
                Score = 95,
                Regla = "deterministica-v1",
                FechaCreacion = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using (var db = BuildDbContext(dbName))
        {
            var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
            var act = () => service.ConfirmarAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                conciliacionId,
                new ConciliacionCambiarEstadoRequest(),
                new DefaultHttpContext(),
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ConciliacionException>();
            ex.Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        }
    }

    [Fact]
    public async Task ResolverAsync_Debe_Permitir_Cerrar_Una_Excepcion()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var conciliacionId = Guid.NewGuid();

        await using (var db = BuildDbContext(dbName))
        {
            var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Lote5 B6b cierre", Tipo = TipoTitular.EMPRESA };
            var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Lote5 B6b cierre", Divisa = "EUR", Activa = true };
            var movimiento = new MovimientoEsperado
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                FechaEsperada = new DateOnly(2026, 6, 29),
                Monto = -125.50m,
                Divisa = "EUR",
                Estado = "excepcion",
                Origen = "manual",
                UsuarioCreacionId = userId,
                FechaCreacion = DateTime.UtcNow
            };

            db.Titulares.Add(titular);
            db.Cuentas.Add(cuenta);
            db.MovimientosEsperados.Add(movimiento);
            db.PermisosUsuario.Add(new PermisoUsuario
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                CuentaId = cuenta.Id,
                PuedeConciliar = true,
                PuedeCerrarConciliacion = true
            });
            db.Conciliaciones.Add(new Conciliacion
            {
                Id = conciliacionId,
                CuentaId = cuenta.Id,
                MovimientoEsperadoId = movimiento.Id,
                Estado = "excepcion",
                Score = 95,
                Regla = "deterministica-v1",
                FechaCreacion = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using (var db = BuildDbContext(dbName))
        {
            var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
            var resuelta = await service.ResolverAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                conciliacionId,
                new ConciliacionCambiarEstadoRequest { Observacion = "revisado" },
                new DefaultHttpContext(),
                CancellationToken.None);

            resuelta.Estado.Should().Be("resuelta");
            resuelta.FechaResolucion.Should().NotBeNull();
        }
    }

    // -----------------------------------------------------------------------
    // B-6 (c): el movimiento esperado puede no existir (soft delete)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarAsync_Debe_Devolver_NotFound_Si_El_Movimiento_Esperado_No_Existe()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var conciliacionId = Guid.NewGuid();

        await using (var db = BuildDbContext(dbName))
        {
            var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Lote5 B6c", Tipo = TipoTitular.EMPRESA };
            var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Lote5 B6c", Divisa = "EUR", Activa = true };
            var movimiento = new MovimientoEsperado
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                FechaEsperada = new DateOnly(2026, 6, 29),
                Monto = -125.50m,
                Divisa = "EUR",
                Estado = "sugerida",
                Origen = "manual",
                UsuarioCreacionId = userId,
                FechaCreacion = DateTime.UtcNow
            };

            db.Titulares.Add(titular);
            db.Cuentas.Add(cuenta);
            db.MovimientosEsperados.Add(movimiento);
            db.PermisosUsuario.Add(new PermisoUsuario
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                CuentaId = cuenta.Id,
                PuedeConciliar = true,
                PuedeCerrarConciliacion = true
            });
            db.Conciliaciones.Add(new Conciliacion
            {
                Id = conciliacionId,
                CuentaId = cuenta.Id,
                MovimientoEsperadoId = movimiento.Id,
                Estado = "sugerida",
                Score = 95,
                Regla = "deterministica-v1",
                FechaCreacion = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            // Soft delete del movimiento: la conciliacion queda huerfana.
            movimiento.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = BuildDbContext(dbName))
        {
            var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
            var act = () => service.ConfirmarAsync(
                userId,
                RolUsuario.EMPLEADO.ToString(),
                conciliacionId,
                new ConciliacionCambiarEstadoRequest(),
                new DefaultHttpContext(),
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ConciliacionException>();
            ex.Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        }
    }
}
