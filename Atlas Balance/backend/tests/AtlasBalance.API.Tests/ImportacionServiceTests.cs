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

public class ImportacionServiceTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetContextoAsync_Should_Respect_Titular_And_Cuenta_Scoped_ImportPermissions()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titularA = new Titular { Id = Guid.NewGuid(), Nombre = "Titular A", Tipo = TipoTitular.EMPRESA };
        var titularB = new Titular { Id = Guid.NewGuid(), Nombre = "Titular B", Tipo = TipoTitular.EMPRESA };

        var cuentaA1 = new Cuenta { Id = Guid.NewGuid(), TitularId = titularA.Id, Nombre = "Cuenta A1", Divisa = "EUR", Activa = true };
        var cuentaA2 = new Cuenta { Id = Guid.NewGuid(), TitularId = titularA.Id, Nombre = "Cuenta A2", Divisa = "USD", Activa = true };
        var cuentaB1 = new Cuenta { Id = Guid.NewGuid(), TitularId = titularB.Id, Nombre = "Cuenta B1", Divisa = "EUR", Activa = true };
        var cuentaB2 = new Cuenta { Id = Guid.NewGuid(), TitularId = titularB.Id, Nombre = "Cuenta B2", Divisa = "MXN", Activa = true };

        db.Titulares.AddRange(titularA, titularB);
        db.Cuentas.AddRange(cuentaA1, cuentaA2, cuentaB1, cuentaB2);
        db.PermisosUsuario.AddRange(
            new PermisoUsuario
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                TitularId = titularA.Id,
                CuentaId = null,
                PuedeImportar = true
            },
            new PermisoUsuario
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                TitularId = titularB.Id,
                CuentaId = cuentaB1.Id,
                PuedeImportar = true
            });
        await db.SaveChangesAsync();

        var service = new ImportacionService(db, new AuditService(db));

        var result = await service.GetContextoAsync(userId, RolUsuario.EMPLEADO.ToString(), CancellationToken.None);

        result.Cuentas.Select(c => c.Id).Should().BeEquivalentTo([cuentaA1.Id, cuentaA2.Id, cuentaB1.Id]);
        result.Cuentas.Select(c => c.Id).Should().NotContain(cuentaB2.Id);
    }

    [Fact]
    public async Task GetContextoAsync_Should_Return_TipoCuenta_For_PlazoFijo()
    {
        await using var db = BuildDbContext();

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
        await db.SaveChangesAsync();

        var service = new ImportacionService(db, new AuditService(db));

        var result = await service.GetContextoAsync(Guid.NewGuid(), RolUsuario.ADMIN.ToString(), CancellationToken.None);

        result.Cuentas.Should().ContainSingle();
        result.Cuentas[0].TipoCuenta.Should().Be(nameof(TipoCuenta.PLAZO_FIJO));
    }

    [Fact]
    public async Task GetContextoAsync_Should_Parse_SnakeCase_Mapeo_From_Active_Formato()
    {
        await using var db = BuildDbContext();

        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var formato = new FormatoImportacion
        {
            Id = Guid.NewGuid(),
            Nombre = "BBVA MXN",
            BancoNombre = "BBVA",
            Divisa = "MXN",
            Activo = true,
            MapeoJson = """
                {"tipo_monto":"dos_columnas","fecha":0,"concepto":1,"egreso":2,"ingreso":3,"saldo":4,"columnas_extra":[{"nombre":"Referencia","indice":5}]}
                """
        };
        var cuenta = new Cuenta
        {
            Id = Guid.NewGuid(),
            TitularId = titular.Id,
            Nombre = "Cuenta Import",
            BancoNombre = "BBVA",
            Divisa = "MXN",
            FormatoId = formato.Id,
            Activa = true
        };

        db.Titulares.Add(titular);
        db.FormatosImportacion.Add(formato);
        db.Cuentas.Add(cuenta);
        await db.SaveChangesAsync();

        var service = new ImportacionService(db, new AuditService(db));

        var result = await service.GetContextoAsync(Guid.NewGuid(), RolUsuario.ADMIN.ToString(), CancellationToken.None);

        var mapeo = result.Cuentas.Single().FormatoPredefinido;
        mapeo.Should().NotBeNull();
        mapeo!.TipoMonto.Should().Be("dos_columnas");
        mapeo.Ingreso.Should().Be(3);
        mapeo.Egreso.Should().Be(2);
        mapeo.ColumnasExtra.Should().ContainSingle(extra => extra.Nombre == "Referencia" && extra.Indice == 5);
    }

    [Fact]
    public async Task ValidarAsync_Should_Parse_All_Required_Date_Formats_And_ExtraColumns()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionValidarRequest
        {
            CuentaId = cuenta.Id,
            RawData = string.Join('\n', [
                "01/04/2026\tVenta 1\t1200,50\t3000,25\tREF-001",
                "2026-04-02\tVenta 2\t-500.00\t2500.25\tREF-002",
                "03-04-2026\tVenta 3\t10\t2510.25\tREF-003",
                "46025\tVenta 4\t15\t2525.25\tREF-004"
            ]),
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 1,
                Monto = 2,
                Saldo = 3,
                ColumnasExtra =
                [
                    new MapeoColumnaExtraRequest
                    {
                        Nombre = "referencia",
                        Indice = 4
                    }
                ]
            }
        };

        var service = new ImportacionService(db, new AuditService(db));

        var result = await service.ValidarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, CancellationToken.None);

        result.FilasOk.Should().Be(4);
        result.FilasError.Should().Be(0);
        result.Filas.Should().OnlyContain(row => row.Valida);
        result.Filas.Should().OnlyContain(row => row.Datos.ContainsKey("extra:referencia"));
    }

    [Fact]
    public async Task ValidarAsync_Should_Reject_Formatted_Import_For_PlazoFijo()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Plazo", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Deposito", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var service = new ImportacionService(db, new AuditService(db));
        var request = new ImportacionValidarRequest
        {
            CuentaId = cuenta.Id,
            RawData = "01/04/2026\tMovimiento\t100\t100",
            Separador = "tab",
            Mapeo = DefaultMapeo()
        };

        var act = () => service.ValidarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ImportacionException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ex.Which.Message.Should().Contain("plazo fijo");
    }

    [Fact]
    public async Task RegistrarMovimientoPlazoFijoAsync_Should_Create_Signed_Extracto_And_Update_Balance()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Plazo", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Deposito", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = new DateOnly(2026, 4, 1),
            Concepto = "Apertura",
            Monto = 1000m,
            Saldo = 1000m,
            FilaNumero = 1
        });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var service = new ImportacionService(db, new AuditService(db));

        var result = await service.RegistrarMovimientoPlazoFijoAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ImportacionPlazoFijoMovimientoRequest
            {
                CuentaId = cuenta.Id,
                TipoMovimiento = "EGRESO",
                Fecha = new DateOnly(2026, 4, 2),
                Monto = 150m,
                Concepto = "Retirada parcial"
            },
            new DefaultHttpContext(),
            CancellationToken.None);

        result.Monto.Should().Be(-150m);
        result.SaldoAnterior.Should().Be(1000m);
        result.SaldoActual.Should().Be(850m);

        var imported = await db.Extractos.OrderBy(e => e.FilaNumero).LastAsync();
        imported.Monto.Should().Be(-150m);
        imported.Saldo.Should().Be(850m);
        imported.FilaNumero.Should().Be(2);
        imported.Concepto.Should().Be("Retirada parcial");
    }

    [Fact]
    public async Task ConfirmarAsync_Should_Normalize_Ingreso_Egreso_Columns_To_Signed_Monto()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuenta.Id,
            RawData = string.Join('\n', [
                "01/04/2026\tCobro cliente\t1000,50\t\t5000,50",
                "02/04/2026\tPago proveedor\t\t250,25\t4750,25"
            ]),
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                TipoMonto = "dos_columnas",
                Fecha = 0,
                Concepto = 1,
                Ingreso = 2,
                Egreso = 3,
                Saldo = 4
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            request,
            new DefaultHttpContext(),
            CancellationToken.None);

        result.FilasImportadas.Should().Be(2);

        var extractos = await db.Extractos
            .OrderByDescending(e => e.FilaNumero)
            .ToListAsync();

        extractos.Select(e => e.Monto).Should().Equal(1000.50m, -250.25m);
    }

    [Fact]
    public async Task ConfirmarAsync_Should_Accept_BbvaMx_Signed_Egreso_Column()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta BBVA MX", Divisa = "MXN", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuenta.Id,
            RawData = string.Join('\n', [
                "18/03/2026\tSPEI RECIBIDO\t\t3,150.00\t10,684.25",
                "14/03/2026\tDLO*SPOTIFY\t-74.00\t\t7,534.25",
                "06/03/2026\tDLO*SPOTIFY\t-74.00\t\t7,608.25"
            ]),
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                TipoMonto = "dos_columnas",
                Fecha = 0,
                Concepto = 1,
                Egreso = 2,
                Ingreso = 3,
                Saldo = 4
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            request,
            new DefaultHttpContext(),
            CancellationToken.None);

        result.FilasImportadas.Should().Be(3);

        var extractos = await db.Extractos
            .OrderByDescending(e => e.FilaNumero)
            .ToListAsync();

        extractos.Select(e => e.Monto).Should().Equal(3150.00m, -74.00m, -74.00m);
    }

    [Fact]
    public async Task ValidarAsync_Should_Reject_Ambiguous_Ingreso_Egreso_Rows()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionValidarRequest
        {
            CuentaId = cuenta.Id,
            RawData = string.Join('\n', [
                "01/04/2026\tAmbigua\t100\t50\t5000",
                "02/04/2026\tVacia\t\t\t5000"
            ]),
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                TipoMonto = "dos_columnas",
                Fecha = 0,
                Concepto = 1,
                Ingreso = 2,
                Egreso = 3,
                Saldo = 4
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ValidarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, CancellationToken.None);

        result.FilasOk.Should().Be(0);
        result.Filas[0].Errores.Should().Contain("La fila tiene ingreso y egreso a la vez");
        result.Filas[1].Errores.Should().Contain("La fila no tiene importe");
    }

    [Fact]
    public async Task ConfirmarAsync_Should_Use_Ingreso_Egreso_And_Validate_Monto_When_Three_Columns_Are_Mapped()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuenta.Id,
            RawData = string.Join('\n', [
                "01/04/2026\tCobro cliente\t1000,50\t\t1000,50\t5000,50",
                "02/04/2026\tPago proveedor\t\t250,25\t250,25\t4750,25"
            ]),
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                TipoMonto = "tres_columnas",
                Fecha = 0,
                Concepto = 1,
                Ingreso = 2,
                Egreso = 3,
                Monto = 4,
                Saldo = 5
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            request,
            new DefaultHttpContext(),
            CancellationToken.None);

        result.FilasImportadas.Should().Be(2);

        var extractos = await db.Extractos
            .OrderByDescending(e => e.FilaNumero)
            .ToListAsync();

        extractos.Select(e => e.Monto).Should().Equal(1000.50m, -250.25m);
    }

    [Fact]
    public async Task ValidarAsync_Should_Reject_Three_Column_Rows_When_Monto_Does_Not_Match_Ingreso_Egreso()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionValidarRequest
        {
            CuentaId = cuenta.Id,
            RawData = "01/04/2026\tCobro raro\t100\t\t99\t5000",
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                TipoMonto = "tres_columnas",
                Fecha = 0,
                Concepto = 1,
                Ingreso = 2,
                Egreso = 3,
                Monto = 4,
                Saldo = 5
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ValidarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, CancellationToken.None);

        result.FilasOk.Should().Be(0);
        result.Filas[0].Errores.Should().Contain("Monto no coincide con ingreso/egreso");
    }

    [Theory]
    [InlineData("1.234,56 \u20AC", 1234.56)]
    [InlineData("\u20AC1,234.56", 1234.56)]
    [InlineData("1.234,56\u20AC", 1234.56)]
    [InlineData("1234,56", 1234.56)]
    [InlineData("-1.234,56", -1234.56)]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("1.234", 1234)]
    [InlineData("1,234", 1234)]
    [InlineData("(1.234,56)", -1234.56)]
    public async Task ConfirmarAsync_Should_Parse_Euro_Symbol_Formats(string raw, decimal expected)
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuenta.Id,
            RawData = $"01/04/2026\tMovimiento\t{raw}\t{raw}",
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 1,
                Monto = 2,
                Saldo = 3
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            request,
            new DefaultHttpContext(),
            CancellationToken.None);

        result.FilasImportadas.Should().Be(1);
        var extracto = await db.Extractos.SingleAsync();
        extracto.Monto.Should().Be(expected);
        extracto.Saldo.Should().Be(expected);
    }

    [Fact]
    public async Task ValidarAsync_Should_Parse_Thousands_Separators_Without_Treating_Them_As_Decimals()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuenta.Id,
            RawData = string.Join('\n', [
                "01/04/2026;Cobro ES;1.234;5.678",
                "2026-04-02;Cobro US;1,234;6,789"
            ]),
            Separador = "semicolon",
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 1,
                Monto = 2,
                Saldo = 3
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            request,
            new DefaultHttpContext(),
            CancellationToken.None);

        result.FilasImportadas.Should().Be(2);

        var extractos = await db.Extractos
            .OrderByDescending(e => e.FilaNumero)
            .ToListAsync();

        extractos.Should().HaveCount(2);
        extractos[0].Monto.Should().Be(1234m);
        extractos[0].Saldo.Should().Be(5678m);
        extractos[1].Monto.Should().Be(1234m);
        extractos[1].Saldo.Should().Be(6789m);
    }

    [Fact]
    public async Task ValidarAsync_Should_Reject_Duplicate_Mapping_Indexes_And_Extra_Names()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var duplicateIndexRequest = new ImportacionValidarRequest
        {
            CuentaId = cuenta.Id,
            RawData = "01/04/2026\tVenta\t10\t20",
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 0,
                Monto = 2,
                Saldo = 3
            }
        };

        var duplicateExtraNameRequest = new ImportacionValidarRequest
        {
            CuentaId = cuenta.Id,
            RawData = "01/04/2026\tVenta\t10\t20\tA\tB",
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 1,
                Monto = 2,
                Saldo = 3,
                ColumnasExtra =
                [
                    new MapeoColumnaExtraRequest { Nombre = "referencia", Indice = 4 },
                    new MapeoColumnaExtraRequest { Nombre = "Referencia", Indice = 5 }
                ]
            }
        };

        var service = new ImportacionService(db, new AuditService(db));

        var duplicateIndexAction = () => service.ValidarAsync(userId, RolUsuario.EMPLEADO.ToString(), duplicateIndexRequest, CancellationToken.None);
        var duplicateExtraNameAction = () => service.ValidarAsync(userId, RolUsuario.EMPLEADO.ToString(), duplicateExtraNameRequest, CancellationToken.None);

        await duplicateIndexAction.Should().ThrowAsync<ImportacionException>()
            .Where(ex => ex.StatusCode == StatusCodes.Status400BadRequest && ex.Message.Contains("Índice de columna duplicado"));

        await duplicateExtraNameAction.Should().ThrowAsync<ImportacionException>()
            .Where(ex => ex.StatusCode == StatusCodes.Status400BadRequest && ex.Message.Contains("Clave de columna extra duplicada"));
    }

    [Fact]
    public async Task ValidarAsync_Should_Return_Specific_Messages_For_Empty_And_NonNumeric_Values()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionValidarRequest
        {
            CuentaId = cuenta.Id,
            RawData = string.Join('\n', [
                "01/04/2026\tVenta\tabc\t",
                "\tVenta\t10\txyz"
            ]),
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 1,
                Monto = 2,
                Saldo = 3
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ValidarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, CancellationToken.None);

        result.FilasError.Should().Be(2);
        result.Filas[0].Errores.Should().Contain("Monto no numerico");
        result.Filas[0].Errores.Should().Contain("Saldo vacío");
        result.Filas[1].Errores.Should().Contain("Fecha vacía");
        result.Filas[1].Errores.Should().Contain("Saldo no numerico");
    }

    [Fact]
    public async Task ValidarAsync_Should_Allow_Concept_Rows_With_Missing_Amount_Date_And_Balance_As_Warnings()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionValidarRequest
        {
            CuentaId = cuenta.Id,
            RawData = string.Join('\n', [
                "22/04/2026\tMovimiento completo\t100\t500",
                "\tEGARARECYCLING\t\t"
            ]),
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 1,
                Monto = 2,
                Saldo = 3
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ValidarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, CancellationToken.None);

        result.FilasOk.Should().Be(2);
        result.FilasError.Should().Be(0);
        result.Filas[1].Valida.Should().BeTrue();
        result.Filas[1].Datos["fecha"].Should().Be("22/04/2026");
        result.Filas[1].Datos["monto"].Should().Be("0");
        result.Filas[1].Datos["saldo"].Should().Be("500");
        result.Filas[1].Advertencias.Should().BeEquivalentTo([
            "Monto vacio; se importara como 0.",
            "Fecha vacia; se usara la fecha anterior (22/04/2026).",
            "Saldo vacio; se usara el saldo anterior (500)."
        ]);
    }

    [Fact]
    public async Task ValidarAsync_Should_Allow_Concept_Rows_With_Missing_Amount_And_Date_When_Balance_Is_Present()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedImportableCuentaAsync(db);

        var request = new ImportacionValidarRequest
        {
            CuentaId = cuentaId,
            RawData = string.Join('\n', [
                "07/04/2026\tREMESA RECIBOS\t3180,00\t54018,20",
                "\tOlivares Palomares, Sergio\t\t815,00"
            ]),
            Separador = "tab",
            Mapeo = DefaultMapeo()
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ValidarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, CancellationToken.None);

        result.FilasOk.Should().Be(2);
        result.FilasError.Should().Be(0);
        result.Filas[1].Valida.Should().BeTrue();
        result.Filas[1].Datos["fecha"].Should().Be("07/04/2026");
        result.Filas[1].Datos["monto"].Should().Be("0");
        result.Filas[1].Datos["saldo"].Should().Be("815,00");
        result.Filas[1].Advertencias.Should().BeEquivalentTo([
            "Monto vacio; se importara como 0.",
            "Fecha vacia; se usara la fecha anterior (07/04/2026)."
        ]);
    }

    [Fact]
    public async Task ConfirmarAsync_Should_Import_Concept_Rows_With_Warning_Fallbacks()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuenta.Id,
            RawData = string.Join('\n', [
                "22/04/2026\tMovimiento completo\t100\t500",
                "\tEGARARECYCLING\t\t"
            ]),
            Separador = "tab",
            FilasAImportar = [1, 2],
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 1,
                Monto = 2,
                Saldo = 3
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            request,
            new DefaultHttpContext(),
            CancellationToken.None);

        result.FilasImportadas.Should().Be(2);
        result.FilasConError.Should().Be(0);

        var imported = await db.Extractos.OrderByDescending(e => e.FilaNumero).ToListAsync();
        imported[1].Fecha.Should().Be(new DateOnly(2026, 4, 22));
        imported[1].Concepto.Should().Be("EGARARECYCLING");
        imported[1].Monto.Should().Be(0m);
        imported[1].Saldo.Should().Be(500m);
    }

    [Fact]
    public async Task ConfirmarAsync_Should_Import_Concept_Rows_With_Provided_Balance_And_Missing_Amount_Date()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedImportableCuentaAsync(db);

        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuentaId,
            RawData = string.Join('\n', [
                "07/04/2026\tREMESA RECIBOS\t3180,00\t54018,20",
                "\tOlivares Palomares, Sergio\t\t815,00"
            ]),
            Separador = "tab",
            FilasAImportar = [1, 2],
            Mapeo = DefaultMapeo()
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            request,
            new DefaultHttpContext(),
            CancellationToken.None);

        result.FilasImportadas.Should().Be(2);
        result.FilasConError.Should().Be(0);

        var imported = await db.Extractos.OrderByDescending(e => e.FilaNumero).ToListAsync();
        imported[1].Fecha.Should().Be(new DateOnly(2026, 4, 7));
        imported[1].Concepto.Should().Be("Olivares Palomares, Sergio");
        imported[1].Monto.Should().Be(0m);
        imported[1].Saldo.Should().Be(815m);
    }

    [Fact]
    public async Task ConfirmarAsync_Should_Import_Only_Selected_Valid_Rows_And_Audit_The_Batch()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuenta.Id,
            RawData = string.Join('\n', [
                "01/04/2026\tVenta 1\t1200,50\t3000,25\tREF-001",
                "99/99/2026\tFila rota\t10\t3010,25\tREF-002",
                "2026-04-03\tVenta 3\t15\t3025,25\tREF-003"
            ]),
            Separador = "tab",
            FilasAImportar = [1, 3],
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 1,
                Monto = 2,
                Saldo = 3,
                ColumnasExtra =
                [
                    new MapeoColumnaExtraRequest
                    {
                        Nombre = "referencia",
                        Indice = 4
                    }
                ]
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var httpContext = new DefaultHttpContext();

        var result = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            request,
            httpContext,
            CancellationToken.None);

        result.FilasProcesadas.Should().Be(3);
        result.FilasImportadas.Should().Be(2);
        result.FilasConError.Should().Be(1);

        var extractos = await db.Extractos
            .OrderBy(e => e.FilaNumero)
            .ToListAsync();

        extractos.Should().HaveCount(2);
        extractos.Select(e => e.FilaNumero).Should().Equal(1, 2);

        var extras = await db.ExtractosColumnasExtra
            .OrderBy(e => e.NombreColumna)
            .ToListAsync();

        extras.Should().HaveCount(2);
        extras.Should().OnlyContain(extra => extra.NombreColumna == "referencia");

        var auditRows = await db.Auditorias
            .Where(a => a.EntidadId == cuenta.Id && a.TipoAccion == "importacion_confirmada")
            .ToListAsync();

        auditRows.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConfirmarAsync_Should_Be_Idempotent_When_Reimporting_The_Same_File()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedImportableCuentaAsync(db);
        var service = new ImportacionService(db, new AuditService(db));
        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuentaId,
            RawData = string.Join('\n', [
                "01/04/2026\tVenta 1\t1200,50\t3000,25",
                "02/04/2026\tPago proveedor\t-200,25\t2800,00"
            ]),
            Separador = "tab",
            Mapeo = DefaultMapeo()
        };

        var first = await service.ConfirmarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, new DefaultHttpContext(), CancellationToken.None);
        var second = await service.ConfirmarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, new DefaultHttpContext(), CancellationToken.None);

        first.FilasImportadas.Should().Be(2);
        first.FilasDuplicadas.Should().Be(0);
        second.FilasImportadas.Should().Be(0);
        second.FilasDuplicadas.Should().Be(2);

        var extractos = await db.Extractos.OrderBy(e => e.ImportacionFilaOrigen).ToListAsync();
        extractos.Should().HaveCount(2);
        extractos.Should().OnlyContain(e => e.ImportacionFingerprint != null);
        extractos.Should().OnlyContain(e => e.ImportacionLoteHash != null);
        extractos.Select(e => e.ImportacionFilaOrigen).Should().Equal(1, 2);
        extractos.Select(e => e.FechaImportacion).Should().OnlyContain(x => x.HasValue);
    }

    [Fact]
    public async Task ConfirmarAsync_Should_Import_Only_New_Rows_When_Reimporting_A_Partial_Selection()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedImportableCuentaAsync(db);
        var service = new ImportacionService(db, new AuditService(db));
        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuentaId,
            RawData = string.Join('\n', [
                "01/04/2026\tVenta 1\t100\t100",
                "02/04/2026\tVenta 2\t200\t300",
                "03/04/2026\tVenta 3\t300\t600"
            ]),
            Separador = "tab",
            Mapeo = DefaultMapeo(),
            FilasAImportar = [1, 3]
        };

        var first = await service.ConfirmarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, new DefaultHttpContext(), CancellationToken.None);

        request.FilasAImportar = [1, 2, 3];
        var second = await service.ConfirmarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, new DefaultHttpContext(), CancellationToken.None);

        first.FilasImportadas.Should().Be(2);
        second.FilasImportadas.Should().Be(1);
        second.FilasDuplicadas.Should().Be(2);

        var extractos = await db.Extractos.OrderBy(e => e.ImportacionFilaOrigen).ToListAsync();
        extractos.Should().HaveCount(3);
        extractos.Select(e => e.ImportacionFilaOrigen).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ConfirmarAsync_Should_Preserve_Repeated_Rows_But_Not_Duplicate_Them_On_Reimport()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedImportableCuentaAsync(db);
        var service = new ImportacionService(db, new AuditService(db));
        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuentaId,
            RawData = string.Join('\n', [
                "01/04/2026\tComision repetida\t-1,20\t998,80",
                "01/04/2026\tComision repetida\t-1,20\t998,80"
            ]),
            Separador = "tab",
            Mapeo = DefaultMapeo()
        };

        var first = await service.ConfirmarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, new DefaultHttpContext(), CancellationToken.None);
        var second = await service.ConfirmarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, new DefaultHttpContext(), CancellationToken.None);

        first.FilasImportadas.Should().Be(2);
        second.FilasImportadas.Should().Be(0);
        second.FilasDuplicadas.Should().Be(2);

        var extractos = await db.Extractos.OrderBy(e => e.ImportacionFilaOrigen).ToListAsync();
        extractos.Should().HaveCount(2);
        extractos.Select(e => e.ImportacionFilaOrigen).Should().Equal(1, 2);
        extractos.Select(e => e.ImportacionFingerprint).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ConfirmarAsync_Should_Preserve_Pasted_Order_When_Viewing_FilaNumero_Descending()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuenta.Id,
            RawData = string.Join('\n', [
                "2026-04-01\tLinea superior\t5\t100",
                "2026-04-03\tLinea inferior\t10\t110"
            ]),
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 1,
                Monto = 2,
                Saldo = 3
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            request,
            new DefaultHttpContext(),
            CancellationToken.None);

        result.FilasImportadas.Should().Be(2);

        var orderedByFila = await db.Extractos
            .OrderByDescending(e => e.FilaNumero)
            .ToListAsync();

        orderedByFila.Should().HaveCount(2);
        orderedByFila[0].Concepto.Should().Be("Linea superior");
        orderedByFila[0].FilaNumero.Should().Be(2);
        orderedByFila[1].Concepto.Should().Be("Linea inferior");
        orderedByFila[1].FilaNumero.Should().Be(1);
    }

    [Fact]
    public async Task ConfirmarAsync_Should_Not_Reuse_FilaNumero_From_SoftDeleted_Rows()
    {
        await using var db = BuildDbContext();

        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Import", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Import", Divisa = "EUR", Activa = true };
        var deletedExtracto = new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = new DateOnly(2026, 3, 1),
            Concepto = "Eliminado",
            Monto = 10m,
            Saldo = 10m,
            FilaNumero = 7,
            DeletedAt = DateTime.UtcNow
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.Extractos.Add(deletedExtracto);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuenta.Id,
            RawData = "01/04/2026\tMovimiento nuevo\t20\t30",
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 1,
                Monto = 2,
                Saldo = 3
            }
        };

        var service = new ImportacionService(db, new AuditService(db));
        var result = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            request,
            new DefaultHttpContext(),
            CancellationToken.None);

        result.FilasImportadas.Should().Be(1);

        var imported = await db.Extractos
            .SingleAsync(e => e.DeletedAt == null);

        imported.FilaNumero.Should().Be(8);
    }

    [Fact]
    public async Task ValidarAsync_Should_Reject_RawData_Over_Size_Limit()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedImportableCuentaAsync(db);
        var service = new ImportacionService(db, new AuditService(db));
        var request = new ImportacionValidarRequest
        {
            CuentaId = cuentaId,
            RawData = new string('x', (5 * 1024 * 1024) + 1),
            Separador = "tab",
            Mapeo = DefaultMapeo()
        };

        var act = () => service.ValidarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ImportacionException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
    }

    [Fact]
    public async Task ValidarAsync_Should_Reject_RawData_Over_Row_Limit()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedImportableCuentaAsync(db);
        var service = new ImportacionService(db, new AuditService(db));
        var row = "01/04/2026\tMovimiento\t1\t1";
        var request = new ImportacionValidarRequest
        {
            CuentaId = cuentaId,
            RawData = string.Join('\n', Enumerable.Repeat(row, 50_001)),
            Separador = "tab",
            Mapeo = DefaultMapeo()
        };

        var act = () => service.ValidarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ImportacionException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
    }

    [Fact]
    public async Task ValidarAsync_Should_Reject_Too_Many_Extra_Columns()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedImportableCuentaAsync(db);
        var service = new ImportacionService(db, new AuditService(db));
        var request = new ImportacionValidarRequest
        {
            CuentaId = cuentaId,
            RawData = "01/04/2026\tMovimiento\t1\t1",
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 1,
                Monto = 2,
                Saldo = 3,
                ColumnasExtra = Enumerable.Range(0, 65)
                    .Select(i => new MapeoColumnaExtraRequest { Nombre = $"Extra{i}", Indice = i + 4 })
                    .ToList()
            }
        };

        var act = () => service.ValidarAsync(userId, RolUsuario.EMPLEADO.ToString(), request, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ImportacionException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ex.Which.Message.Should().Contain("64");
    }

    [Fact]
    public async Task ConfirmarAsync_Should_Not_Persist_Blank_Extra_Column_Values()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId) = await SeedImportableCuentaAsync(db);
        var service = new ImportacionService(db, new AuditService(db));
        var request = new ImportacionConfirmarRequest
        {
            CuentaId = cuentaId,
            RawData = "01/04/2026\tMovimiento\t1\t1\tREF-1\t",
            Separador = "tab",
            Mapeo = new MapeoColumnasRequest
            {
                Fecha = 0,
                Concepto = 1,
                Monto = 2,
                Saldo = 3,
                ColumnasExtra =
                [
                    new MapeoColumnaExtraRequest { Nombre = "Referencia", Indice = 4 },
                    new MapeoColumnaExtraRequest { Nombre = "Vacia", Indice = 5 }
                ]
            }
        };

        var result = await service.ConfirmarAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            request,
            new DefaultHttpContext(),
            CancellationToken.None);

        result.FilasImportadas.Should().Be(1);
        var extra = await db.ExtractosColumnasExtra.SingleAsync();
        extra.NombreColumna.Should().Be("Referencia");
        extra.Valor.Should().Be("REF-1");
    }

    private static async Task<(Guid UserId, Guid CuentaId)> SeedImportableCuentaAsync(AppDbContext db)
    {
        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Limites", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Limites", Divisa = "EUR", Activa = true };
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            PuedeImportar = true
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
