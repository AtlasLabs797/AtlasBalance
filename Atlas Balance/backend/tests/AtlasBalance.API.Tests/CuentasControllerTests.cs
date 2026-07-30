using System.Security.Claims;
using FluentAssertions;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

using AtlasBalance.API.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
namespace AtlasBalance.API.Tests;

public sealed class CuentasControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Resumen_Should_Anchor_Selected_Period_To_Latest_Movement()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var latestMovement = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddMonths(-2);
        var periodStart = latestMovement.AddMonths(-1);

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.cuenta.resumen@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Cuenta Resumen",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Resumen", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta Resumen",
            Iban = "ES9121000418450200051332",
            BancoNombre = "CaixaBank",
            Divisa = "EUR",
            Activa = true
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = periodStart.AddDays(-1),
                Monto = 999m,
                Saldo = 999m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = periodStart,
                Monto = 100m,
                Saldo = 1099m,
                FilaNumero = 2
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = latestMovement,
                Monto = -30m,
                Saldo = 1069m,
                FilaNumero = 3
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId);

        var result = await controller.Resumen(cuentaId, "1m", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var summary = ok.Value.Should().BeOfType<CuentaResumenResponse>().Subject;
        summary.CuentaId.Should().Be(cuentaId);
        summary.CuentaNombre.Should().Be("Cuenta Resumen");
        // V-02-07: el resumen enmascara el IBAN (respuesta agregada, no se usa para editar).
        summary.Iban.Should().Be("********************1332");
        summary.BancoNombre.Should().Be("CaixaBank");
        summary.TitularId.Should().Be(titularId);
        summary.TitularNombre.Should().Be("Titular Resumen");
        summary.TipoCuenta.Should().Be(nameof(TipoCuenta.NORMAL));
        summary.SaldoActual.Should().Be(1069m);
        summary.IngresosMes.Should().Be(100m);
        summary.EgresosMes.Should().Be(30m);
    }

    [Fact]
    public async Task Resumen_Should_Use_Highest_FilaNumero_As_CurrentSaldo()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.cuenta.actual@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Cuenta Actual",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Actual", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta Actual",
            Divisa = "EUR",
            Activa = true
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 5, 10),
                Concepto = "Movimiento con fecha posterior",
                Monto = 20m,
                Saldo = 20m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 4, 30),
                Concepto = "Movimiento importado ultimo",
                Monto = 80m,
                Saldo = 100m,
                FilaNumero = 2
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId);

        var result = await controller.Resumen(cuentaId, "1m", CancellationToken.None);

        var summary = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<CuentaResumenResponse>().Subject;
        summary.SaldoActual.Should().Be(100m);
    }

    [Fact]
    public async Task Resumen_Should_Expose_PlazoFijo_Metadata()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var referenciaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.cuenta.plazo.resumen@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Cuenta Plazo Resumen",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Plazo", Tipo = TipoTitular.AUTONOMO });
        db.Cuentas.AddRange(
            new Cuenta { Id = referenciaId, TitularId = titularId, Nombre = "Cuenta Referencia", Divisa = "EUR", TipoCuenta = TipoCuenta.NORMAL, Activa = true },
            new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Deposito Resumen", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true, Notas = "Notas cuenta" });
        db.PlazosFijos.Add(new PlazoFijo
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            CuentaReferenciaId = referenciaId,
            FechaInicio = new DateOnly(2026, 4, 25),
            FechaVencimiento = new DateOnly(2026, 10, 25),
            InteresPrevisto = 150m,
            Renovable = true,
            Estado = EstadoPlazoFijo.PROXIMO_VENCER,
            Notas = "Notas plazo",
            FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId);

        var result = await controller.Resumen(cuentaId, "1m", CancellationToken.None);

        var summary = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<CuentaResumenResponse>().Subject;
        summary.TipoCuenta.Should().Be(nameof(TipoCuenta.PLAZO_FIJO));
        summary.PlazoFijo.Should().NotBeNull();
        summary.PlazoFijo!.CuentaReferenciaNombre.Should().Be("Cuenta Referencia");
        summary.PlazoFijo.FechaVencimiento.Should().Be(new DateOnly(2026, 10, 25));
        summary.PlazoFijo.Estado.Should().Be(nameof(EstadoPlazoFijo.PROXIMO_VENCER));
        summary.Notas.Should().Be("Notas cuenta");
    }

    [Fact]
    public async Task Resumen_Should_Hide_PlazoFijo_Reference_Account_When_User_Cannot_Access_It()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var referenciaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.plazo.resumen@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Plazo",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Plazo", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = referenciaId, TitularId = titularId, Nombre = "Cuenta Referencia Privada", Divisa = "EUR", TipoCuenta = TipoCuenta.NORMAL, Activa = true },
            new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Deposito Gerente", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeVerCuentas = true
        });
        db.PlazosFijos.Add(new PlazoFijo
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            CuentaReferenciaId = referenciaId,
            FechaInicio = new DateOnly(2026, 4, 25),
            FechaVencimiento = new DateOnly(2026, 10, 25),
            Renovable = true,
            Estado = EstadoPlazoFijo.ACTIVO,
            FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Resumen(cuentaId, "1m", CancellationToken.None);

        var summary = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<CuentaResumenResponse>().Subject;
        summary.PlazoFijo.Should().NotBeNull();
        summary.PlazoFijo!.CuentaReferenciaId.Should().BeNull();
        summary.PlazoFijo.CuentaReferenciaNombre.Should().BeNull();
    }

    [Fact]
    public async Task Obtener_Should_Return_Forbid_When_Cuenta_Is_Outside_User_Scope()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularPermitidoId = Guid.NewGuid();
        var titularBloqueadoId = Guid.NewGuid();
        var cuentaPermitidaId = Guid.NewGuid();
        var cuentaBloqueadaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.idor.cuentas@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente IDOR Cuentas",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.AddRange(
            new Titular { Id = titularPermitidoId, Nombre = "Titular Permitido", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = titularBloqueadoId, Nombre = "Titular Bloqueado", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = cuentaPermitidaId, TitularId = titularPermitidoId, Nombre = "Cuenta Permitida", Divisa = "EUR", Activa = true },
            new Cuenta { Id = cuentaBloqueadaId, TitularId = titularBloqueadoId, Nombre = "Cuenta Bloqueada", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            TitularId = titularPermitidoId,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var permitida = await controller.Obtener(cuentaPermitidaId, false, CancellationToken.None);
        var bloqueada = await controller.Obtener(cuentaBloqueadaId, false, CancellationToken.None);

        permitida.Should().BeOfType<OkObjectResult>();
        bloqueada.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Resumen_Should_Return_Forbid_When_Cuenta_Is_Outside_User_Scope()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularPermitidoId = Guid.NewGuid();
        var titularBloqueadoId = Guid.NewGuid();
        var cuentaBloqueadaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.idor.resumen@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente IDOR Resumen",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.AddRange(
            new Titular { Id = titularPermitidoId, Nombre = "Titular Permitido", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = titularBloqueadoId, Nombre = "Titular Bloqueado", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaBloqueadaId,
            TitularId = titularBloqueadoId,
            Nombre = "Cuenta Bloqueada",
            Divisa = "EUR",
            Activa = true
        });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            TitularId = titularPermitidoId,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Resumen(cuentaBloqueadaId, "1m", CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Obtener_Should_Return_Forbid_When_Titular_Is_SoftDeleted_For_NonAdmin()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.idor.softdeleted@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente IDOR SoftDeleted",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular
        {
            Id = titularId,
            Nombre = "Titular Eliminado",
            Tipo = TipoTitular.EMPRESA,
            DeletedAt = DateTime.UtcNow
        });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta Huérfana",
            Divisa = "EUR",
            Activa = true
        });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            TitularId = titularId,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Obtener(cuentaId, false, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    private static CuentasController BuildController(AppDbContext db, Guid userId, RolUsuario role = RolUsuario.ADMIN)
    {
        var controller = new CuentasController(db, new UserAccessService(db, new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance), Options.Create(new CachingOptions())), new AuditService(db), new NoOpPlazoFijoService());
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString())
        ], "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    [Fact]
    public async Task Crear_Should_Create_PlazoFijo_With_Metadata()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var referenciaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.plazo@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Plazo",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Autonomo Uno", Tipo = TipoTitular.AUTONOMO });
        db.DivisasActivas.Add(new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true });
        db.Cuentas.Add(new Cuenta { Id = referenciaId, TitularId = titularId, Nombre = "Cuenta Referencia", Divisa = "EUR", Activa = true });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId);

        var result = await controller.Crear(new SaveCuentaRequest
        {
            TitularId = titularId,
            Nombre = "Deposito 6 meses",
            Divisa = "EUR",
            TipoCuenta = TipoCuenta.PLAZO_FIJO,
            PlazoFijo = new SavePlazoFijoRequest
            {
                FechaInicio = new DateOnly(2026, 4, 25),
                FechaVencimiento = new DateOnly(2026, 10, 25),
                InteresPrevisto = 120m,
                Renovable = true,
                CuentaReferenciaId = referenciaId,
                Notas = "Renovar si compensa"
            }
        }, CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();
        var cuenta = await db.Cuentas.SingleAsync(c => c.Nombre == "Deposito 6 meses");
        cuenta.TipoCuenta.Should().Be(TipoCuenta.PLAZO_FIJO);
        cuenta.EsEfectivo.Should().BeFalse();
        cuenta.FormatoId.Should().BeNull();

        var plazo = await db.PlazosFijos.SingleAsync(p => p.CuentaId == cuenta.Id);
        plazo.FechaVencimiento.Should().Be(new DateOnly(2026, 10, 25));
        plazo.Estado.Should().Be(EstadoPlazoFijo.ACTIVO);
    }

    [Fact]
    public async Task Crear_Should_Keep_Formato_For_Efectivo()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var formatoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.efectivo@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Efectivo",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Caja Central", Tipo = TipoTitular.EMPRESA });
        db.DivisasActivas.Add(new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true });
        db.FormatosImportacion.Add(new FormatoImportacion
        {
            Id = formatoId,
            Nombre = "Caja EUR",
            BancoNombre = "Caja",
            Divisa = "EUR",
            Activo = true,
            MapeoJson = "{\"tipo_monto\":\"una_columna\",\"fecha\":0,\"concepto\":1,\"monto\":2,\"saldo\":3,\"columnas_extra\":[]}"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId);

        var result = await controller.Crear(new SaveCuentaRequest
        {
            TitularId = titularId,
            Nombre = "Caja Oficina",
            Divisa = "EUR",
            TipoCuenta = TipoCuenta.EFECTIVO,
            FormatoId = formatoId,
            BancoNombre = "No deberia persistir",
            NumeroCuenta = "123",
            Iban = "ES00"
        }, CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();
        var cuenta = await db.Cuentas.SingleAsync(c => c.Nombre == "Caja Oficina");
        cuenta.TipoCuenta.Should().Be(TipoCuenta.EFECTIVO);
        cuenta.EsEfectivo.Should().BeTrue();
        cuenta.FormatoId.Should().Be(formatoId);
        cuenta.BancoNombre.Should().BeNull();
        cuenta.NumeroCuenta.Should().BeNull();
        cuenta.Iban.Should().BeNull();
    }

    [Fact]
    public async Task Listar_Should_Filter_By_TipoTitular_And_TipoCuenta()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var autonomoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.filtros@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Filtros",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.AddRange(
            new Titular { Id = empresaId, Nombre = "Empresa", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = autonomoId, Nombre = "Autonomo", Tipo = TipoTitular.AUTONOMO });
        db.Cuentas.AddRange(
            new Cuenta { Id = Guid.NewGuid(), TitularId = empresaId, Nombre = "Banco Empresa", Divisa = "EUR", TipoCuenta = TipoCuenta.NORMAL, Activa = true },
            new Cuenta { Id = Guid.NewGuid(), TitularId = autonomoId, Nombre = "Deposito Autonomo", Divisa = "EUR", TipoCuenta = TipoCuenta.PLAZO_FIJO, Activa = true });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId);

        var result = await controller.Listar(tipoTitular: TipoTitular.AUTONOMO, tipoCuenta: TipoCuenta.PLAZO_FIJO, cancellationToken: CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<CuentaListItemResponse>>().Subject;
        page.Data.Should().ContainSingle();
        page.Data.Single().Nombre.Should().Be("Deposito Autonomo");
        page.Data.Single().TitularTipo.Should().Be(nameof(TipoTitular.AUTONOMO));
        page.Data.Single().TipoCuenta.Should().Be(nameof(TipoCuenta.PLAZO_FIJO));
    }

    [Fact]
    public async Task Listar_Should_Filter_By_PaisId()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var paisAId = Guid.NewGuid();
        var paisBId = Guid.NewGuid();
        var titularId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.pais.cuentas@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Pais Cuentas",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Paises.AddRange(
            new Pais { Id = paisAId, Nombre = "Espana", CodigoIso2 = "ES", Activo = true },
            new Pais { Id = paisBId, Nombre = "Mexico", CodigoIso2 = "MX", Activo = true });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Pais", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularId, Nombre = "Cuenta ES", Divisa = "EUR", PaisId = paisAId, Activa = true },
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularId, Nombre = "Cuenta MX", Divisa = "MXN", PaisId = paisBId, Activa = true },
            new Cuenta { Id = Guid.NewGuid(), TitularId = titularId, Nombre = "Cuenta General", Divisa = "EUR", Activa = true });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId);

        var result = await controller.Listar(paisId: paisAId, cancellationToken: CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<CuentaListItemResponse>>().Subject;
        page.Total.Should().Be(1);
        page.Data.Should().ContainSingle();
        page.Data.Single().Nombre.Should().Be("Cuenta ES");
        page.Data.Single().PaisId.Should().Be(paisAId);
        page.Data.Single().PaisNombre.Should().Be("Espana");
    }

    [Fact]
    public async Task Listar_Should_Return_Masked_Iban_And_NumeroCuenta()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.listar.mask@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Listar Mask",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Mask", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta Mask",
            NumeroCuenta = "1234567890",
            Iban = "ES9121000418450200051332",
            Divisa = "EUR",
            Activa = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId);

        var result = await controller.Listar(cancellationToken: CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<CuentaListItemResponse>>().Subject;
        var item = page.Data.Single();
        item.Iban.Should().Be("********************1332");
        item.NumeroCuenta.Should().Be("******7890");
    }

    [Fact]
    public async Task Obtener_Should_Return_Full_Iban_And_NumeroCuenta_For_Edit_Form()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.detalle.mask@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Detalle Mask",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Detalle", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta
        {
            Id = cuentaId,
            TitularId = titularId,
            Nombre = "Cuenta Detalle",
            NumeroCuenta = "1234567890",
            Iban = "ES9121000418450200051332",
            Divisa = "EUR",
            Activa = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId);

        var result = await controller.Obtener(cuentaId, false, CancellationToken.None);

        var item = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<CuentaListItemResponse>().Subject;
        item.Iban.Should().Be("ES9121000418450200051332");
        item.NumeroCuenta.Should().Be("1234567890");
    }

    private sealed class NoOpPlazoFijoService : IPlazoFijoService
    {
        public Task<int> ProcesarVencimientosAsync(DateOnly hoy, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<PlazoFijoResponse> RenovarAsync(Guid cuentaId, RenovarPlazoFijoRequest request, Guid? actorUserId, HttpContext httpContext, CancellationToken cancellationToken)
            => Task.FromResult(new PlazoFijoResponse { CuentaId = cuentaId });
    }
}
