using System.Security.Claims;
using System.Text.Json;
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

namespace AtlasBalance.API.Tests;

public sealed class ExtractosControllerTests
{
    [Fact]
    public void SaveColumnasVisiblesRequest_Should_Deserialize_Global_Scope_From_Snake_Case_Json()
    {
        const string withNullCuentaId = """
        {
          "cuenta_id": null,
          "titular_id": null,
          "pais_id": null,
          "columnas_visibles": ["fecha", "monto"]
        }
        """;

        const string withoutCuentaId = """
        {
          "columnas_visibles": ["fecha", "monto"]
        }
        """;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        var requestWithNull = JsonSerializer.Deserialize<SaveColumnasVisiblesRequest>(withNullCuentaId, options);
        var requestWithoutCuenta = JsonSerializer.Deserialize<SaveColumnasVisiblesRequest>(withoutCuentaId, options);

        requestWithNull.Should().NotBeNull();
        requestWithNull!.CuentaId.Should().BeNull();
        requestWithNull.TitularId.Should().BeNull();
        requestWithNull.PaisId.Should().BeNull();
        requestWithNull.ColumnasVisibles.Should().BeEquivalentTo("fecha", "monto");

        requestWithoutCuenta.Should().NotBeNull();
        requestWithoutCuenta!.CuentaId.Should().BeNull();
        requestWithoutCuenta.ColumnasVisibles.Should().BeEquivalentTo("fecha", "monto");
    }

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task SaveColumnasVisibles_Should_Store_Global_Scope_When_CuentaId_Is_Null()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        await db.SaveChangesAsync();

        var controller = new ExtractosController(db, new NoOpAlertaService());
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.GERENTE))
        ], "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        var result = await controller.SaveColumnasVisibles(
            new SaveColumnasVisiblesRequest
            {
                CuentaId = null,
                ColumnasVisibles = ["fecha", "monto"]
            },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var pref = await db.PreferenciasUsuarioCuenta.SingleAsync();
        pref.UsuarioId.Should().Be(userId);
        pref.PaisId.Should().BeNull();
        pref.TitularId.Should().BeNull();
        pref.CuentaId.Should().BeNull();
        pref.ColumnasVisibles.Should().Contain("monto");

        var readResult = await controller.GetColumnasVisibles(ct: CancellationToken.None);
        readResult.Should().BeOfType<OkObjectResult>();
        var readPayload = readResult.As<OkObjectResult>().Value;
        readPayload.Should().NotBeNull();
        readPayload!.GetType().GetProperty("columnas_visibles")!.GetValue(readPayload)
            .Should().BeEquivalentTo(new[] { "fecha", "monto" });
    }

    [Fact]
    public async Task GetCuentaResumen_Should_Anchor_Selected_Period_To_Latest_Movement()
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
            Email = "admin.resumen@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Resumen",
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

        var controller = BuildController(db, userId, RolUsuario.ADMIN);

        var result = await controller.GetCuentaResumen(cuentaId, "1m", null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var summary = ok.Value.Should().BeOfType<CuentaResumenKpiResponse>().Subject;
        summary.Iban.Should().Be("ES9121000418450200051332");
        summary.BancoNombre.Should().Be("CaixaBank");
        summary.SaldoActual.Should().Be(1069m);
        summary.IngresosMes.Should().Be(100m);
        summary.EgresosMes.Should().Be(30m);
    }

    [Fact]
    public async Task Crear_Should_Insert_Intermediate_Row_And_Shift_FilaNumeros()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.insert@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Insert",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Insert", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Insert", Divisa = "EUR", Activa = true });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Abajo",
                Monto = 1m,
                Saldo = 1m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Medio",
                Monto = 2m,
                Saldo = 3m,
                FilaNumero = 2
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Arriba",
                Monto = 3m,
                Saldo = 6m,
                FilaNumero = 3
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.ADMIN);

        var result = await controller.Crear(new CreateExtractoRequest
        {
            CuentaId = cuentaId,
            InsertBeforeFilaNumero = 3,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Concepto = "Nueva intermedia",
            Monto = 0m,
            Saldo = 6m
        }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ordered = await db.Extractos.Where(e => e.CuentaId == cuentaId).OrderByDescending(e => e.FilaNumero).ToListAsync();
        ordered.Select(e => e.Concepto).Should().Equal("Arriba", "Nueva intermedia", "Medio", "Abajo");
        ordered.Select(e => e.FilaNumero).Should().Equal(4, 3, 2, 1);
    }

    [Fact]
    public async Task Listar_Should_Not_Return_Deleted_Rows_To_NonAdmin_Even_When_Requested()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.extractos@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Extractos",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Extractos", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Extractos", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeVerCuentas = true
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Visible",
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Eliminado",
                Monto = 20m,
                Saldo = 30m,
                FilaNumero = 2,
                DeletedAt = DateTime.UtcNow,
                DeletedById = userId
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(incluirEliminados: true, ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var page = ok.Value.Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Total.Should().Be(1);
        page.Data.Should().ContainSingle();
        page.Data.Single().Concepto.Should().Be("Visible");
        page.Data.Single().DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Listar_Should_Return_Empty_For_DashboardOnly_GlobalPermission()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularAId = Guid.NewGuid();
        var titularBId = Guid.NewGuid();
        var cuentaAId = Guid.NewGuid();
        var cuentaBId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.dashboard-only@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Dashboard",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.AddRange(
            new Titular { Id = titularAId, Nombre = "Titular A", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = titularBId, Nombre = "Titular B", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = cuentaAId, TitularId = titularAId, Nombre = "Cuenta A", Divisa = "EUR", Activa = true },
            new Cuenta { Id = cuentaBId, TitularId = titularBId, Nombre = "Cuenta B", Divisa = "USD", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = null,
            TitularId = null,
            PuedeAgregarLineas = false,
            PuedeEditarLineas = false,
            PuedeEliminarLineas = false,
            PuedeImportar = false,
            PuedeVerDashboard = true
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaAId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Cuenta A",
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaBId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Cuenta B",
                Monto = 20m,
                Saldo = 20m,
                FilaNumero = 1
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var page = ok.Value.Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Total.Should().Be(0);
        page.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Listar_Should_Return_Empty_For_DashboardOnly_ScopedPermission()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.dashboard-scoped@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Dashboard Scoped",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Scoped", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Scoped", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            TitularId = titularId,
            PuedeVerDashboard = true
        });
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Concepto = "No visible",
            Monto = 10m,
            Saldo = 10m,
            FilaNumero = 1
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var page = ok.Value.Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Total.Should().Be(0);
        page.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Listar_Should_Return_All_Rows_For_ViewAccounts_GlobalPermission()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularAId = Guid.NewGuid();
        var titularBId = Guid.NewGuid();
        var cuentaAId = Guid.NewGuid();
        var cuentaBId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.all-accounts@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Todas Cuentas",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.AddRange(
            new Titular { Id = titularAId, Nombre = "Titular A", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = titularBId, Nombre = "Titular B", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = cuentaAId, TitularId = titularAId, Nombre = "Cuenta A", Divisa = "EUR", Activa = true },
            new Cuenta { Id = cuentaBId, TitularId = titularBId, Nombre = "Cuenta B", Divisa = "USD", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = null,
            TitularId = null,
            PuedeVerCuentas = true
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaAId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Cuenta A",
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuentaBId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Cuenta B",
                Monto = 20m,
                Saldo = 20m,
                FilaNumero = 1
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Listar(ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var page = ok.Value.Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Total.Should().Be(2);
        page.Data.Select(row => row.Concepto).Should().BeEquivalentTo("Cuenta A", "Cuenta B");
    }

    [Fact]
    public async Task Listar_Should_Return_AvailableExtraColumns_From_Filtered_Result_Not_Only_Current_Page()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var firstExtractoId = Guid.NewGuid();
        var secondExtractoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.extractos.columnas-disponibles@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Columnas Disponibles",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Columnas Disponibles", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Columnas Disponibles", Divisa = "EUR", Activa = true });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = firstExtractoId,
                CuentaId = cuentaId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Pagina sin extra",
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = secondExtractoId,
                CuentaId = cuentaId,
                Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Concepto = "Pagina con extra",
                Monto = 20m,
                Saldo = 30m,
                FilaNumero = 2
            });
        db.ExtractosColumnasExtra.Add(new ExtractoColumnaExtra
        {
            Id = Guid.NewGuid(),
            ExtractoId = secondExtractoId,
            NombreColumna = "canal",
            Valor = "Banca online"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.ADMIN);

        var result = await controller.Listar(page: 1, pageSize: 1, sortBy: "fila_numero", sortDir: "asc", ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var page = ok.Value.Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Data.Should().ContainSingle(row => row.Id == firstExtractoId);
        page.ColumnasDisponibles.Should().ContainSingle().Which.Should().Be("canal");
    }

    [Fact]
    public async Task Listar_Should_Filter_By_PaisId()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var paisAId = Guid.NewGuid();
        var paisBId = Guid.NewGuid();
        var cuentaAId = Guid.NewGuid();
        var cuentaBId = Guid.NewGuid();
        var cuentaSinPaisId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.extractos.pais@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Extractos Pais",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Paises.AddRange(
            new Pais { Id = paisAId, Nombre = "Espana", CodigoIso2 = "ES", Activo = true },
            new Pais { Id = paisBId, Nombre = "Mexico", CodigoIso2 = "MX", Activo = true });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Pais", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = cuentaAId, TitularId = titularId, Nombre = "Cuenta ES", Divisa = "EUR", PaisId = paisAId, Activa = true },
            new Cuenta { Id = cuentaBId, TitularId = titularId, Nombre = "Cuenta MX", Divisa = "MXN", PaisId = paisBId, Activa = true },
            new Cuenta { Id = cuentaSinPaisId, TitularId = titularId, Nombre = "Cuenta General", Divisa = "EUR", Activa = true });
        db.Extractos.AddRange(
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaAId, Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date), Concepto = "Movimiento ES", Monto = 10m, Saldo = 10m, FilaNumero = 1 },
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaBId, Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date), Concepto = "Movimiento MX", Monto = 20m, Saldo = 20m, FilaNumero = 1 },
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaSinPaisId, Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date), Concepto = "Movimiento General", Monto = 30m, Saldo = 30m, FilaNumero = 1 });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.ADMIN);

        var result = await controller.Listar(paisId: paisAId, ct: CancellationToken.None);

        var page = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
        page.Total.Should().Be(1);
        page.Data.Should().ContainSingle();
        page.Data.Single().Concepto.Should().Be("Movimiento ES");
        page.Data.Single().CuentaId.Should().Be(cuentaAId);
    }

    [Fact]
    public async Task GetCuentaResumen_Should_Return_NotFound_When_PaisId_Does_Not_Match()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var paisAId = Guid.NewGuid();
        var paisBId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.extractos.resumen.pais@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Extractos Resumen Pais",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Paises.AddRange(
            new Pais { Id = paisAId, Nombre = "Espana", CodigoIso2 = "ES", Activo = true },
            new Pais { Id = paisBId, Nombre = "Mexico", CodigoIso2 = "MX", Activo = true });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Pais", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta ES", Divisa = "EUR", PaisId = paisAId, Activa = true });
        db.Extractos.Add(new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaId, Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date), Concepto = "Movimiento ES", Monto = 10m, Saldo = 10m, FilaNumero = 1 });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.ADMIN);

        var result = await controller.GetCuentaResumen(cuentaId, "1m", paisBId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Restaurar_Should_Require_DeletePermission()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var extractoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.restore@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Restore",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Restore", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Restore", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeVerCuentas = true,
            PuedeAgregarLineas = true,
            PuedeEliminarLineas = false
        });
        db.Extractos.Add(new Extracto
        {
            Id = extractoId,
            CuentaId = cuentaId,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Concepto = "Eliminado",
            Monto = 10m,
            Saldo = 10m,
            FilaNumero = 1,
            DeletedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Restaurar(extractoId, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        (await db.Extractos.IgnoreQueryFilters().SingleAsync(x => x.Id == extractoId)).DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAuditCelda_Should_Not_Return_Audit_For_Deleted_Extracto_To_NonAdmin()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var extractoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.audit-soft-delete@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Audit",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Audit", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Audit", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeVerCuentas = true
        });
        db.Extractos.Add(new Extracto
        {
            Id = extractoId,
            CuentaId = cuentaId,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Concepto = "Eliminado",
            Monto = 10m,
            Saldo = 10m,
            FilaNumero = 1,
            DeletedAt = DateTime.UtcNow
        });
        db.Auditorias.Add(new Auditoria
        {
            Id = Guid.NewGuid(),
            TipoAccion = "extracto_actualizado",
            EntidadTipo = "EXTRACTOS",
            EntidadId = extractoId,
            ColumnaNombre = "concepto",
            ValorAnterior = "Antes",
            ValorNuevo = "Despues",
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.GetAuditCelda(extractoId, "concepto", ct: CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ToggleFlag_Should_Require_Flagged_EditPermission_When_Flag_Changes()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var extractoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.flag-note@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Nota Flag",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Flag", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Flag", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeEditarLineas = true
        });
        db.PreferenciasUsuarioCuenta.Add(new PreferenciaUsuarioCuenta
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            ColumnasEditables = """["flagged_nota"]"""
        });
        db.Extractos.Add(new Extracto
        {
            Id = extractoId,
            CuentaId = cuentaId,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Concepto = "Flag",
            Monto = 10m,
            Saldo = 10m,
            FilaNumero = 1,
            Flagged = false
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.ToggleFlag(extractoId, new ToggleFlagRequest { Flagged = true, Nota = "No autorizada" }, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        var extracto = await db.Extractos.SingleAsync(x => x.Id == extractoId);
        extracto.Flagged.Should().BeFalse();
        extracto.FlaggedNota.Should().BeNull();
    }

    [Fact]
    public async Task ToggleFlag_Should_Allow_Note_Edit_When_Flag_Does_Not_Change()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var extractoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.flag-note-ok@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Nota Flag OK",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Flag OK", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Flag OK", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeEditarLineas = true
        });
        db.PreferenciasUsuarioCuenta.Add(new PreferenciaUsuarioCuenta
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            ColumnasEditables = """["flagged_nota"]"""
        });
        db.Extractos.Add(new Extracto
        {
            Id = extractoId,
            CuentaId = cuentaId,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Concepto = "Flag",
            Monto = 10m,
            Saldo = 10m,
            FilaNumero = 1,
            Flagged = true,
            FlaggedNota = "Anterior"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.ToggleFlag(extractoId, new ToggleFlagRequest { Flagged = true, Nota = "Nueva" }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var extracto = await db.Extractos.SingleAsync(x => x.Id == extractoId);
        extracto.Flagged.Should().BeTrue();
        extracto.FlaggedNota.Should().Be("Nueva");
    }

    [Fact]
    public async Task SaveColumnasVisibles_Should_Store_Exact_Country_Titular_Account_Scope()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var paisId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.columnas.scope@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Columnas Scope",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Paises.Add(new Pais { Id = paisId, Nombre = "Espana", CodigoIso2 = "ES", Activo = true });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Columnas", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, PaisId = paisId, Nombre = "Cuenta Columnas", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            PaisId = paisId,
            TitularId = titularId,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.SaveColumnasVisibles(
            new SaveColumnasVisiblesRequest
            {
                CuentaId = cuentaId,
                ColumnasVisibles = ["fecha", "monto"]
            },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var pref = await db.PreferenciasUsuarioCuenta.SingleAsync();
        pref.UsuarioId.Should().Be(userId);
        pref.PaisId.Should().Be(paisId);
        pref.TitularId.Should().Be(titularId);
        pref.CuentaId.Should().Be(cuentaId);
        pref.ColumnasVisibles.Should().Contain("monto");
    }

    [Fact]
    public async Task Actualizar_Should_Not_Treat_VisibleColumns_Preference_As_Unrestricted_EditColumns()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var paisId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var extractoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.columnas.edit@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Columnas Edit",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Paises.Add(new Pais { Id = paisId, Nombre = "Espana", CodigoIso2 = "ES", Activo = true });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Edit", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, PaisId = paisId, Nombre = "Cuenta Edit", Divisa = "EUR", Activa = true });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            PaisId = paisId,
            TitularId = titularId,
            PuedeVerCuentas = true,
            PuedeEditarLineas = true
        });
        db.PreferenciasUsuarioCuenta.AddRange(
            new PreferenciaUsuarioCuenta
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                PaisId = paisId,
                TitularId = titularId,
                ColumnasEditables = """["fecha"]"""
            },
            new PreferenciaUsuarioCuenta
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                PaisId = paisId,
                TitularId = titularId,
                CuentaId = cuentaId,
                ColumnasVisibles = """["fecha","monto"]"""
            });
        db.Extractos.Add(new Extracto
        {
            Id = extractoId,
            CuentaId = cuentaId,
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            Concepto = "Movimiento",
            Monto = 10m,
            Saldo = 10m,
            FilaNumero = 1
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.Actualizar(
            extractoId,
            new UpdateExtractoRequest { Monto = 20m },
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        var extracto = await db.Extractos.SingleAsync(x => x.Id == extractoId);
        extracto.Monto.Should().Be(10m);
    }

    [Fact]
    public async Task GetCuentasTitular_Should_Forbid_Unauthorized_Titular()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "gerente.sinpermiso@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Gerente Sin Permiso",
            Rol = RolUsuario.GERENTE,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Privado", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = Guid.NewGuid(), TitularId = titularId, Nombre = "Cuenta Privada", Divisa = "EUR", Activa = true });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, RolUsuario.GERENTE);

        var result = await controller.GetCuentasTitular(titularId, "1m", null, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    private static ExtractosController BuildController(AppDbContext db, Guid userId, RolUsuario role)
    {
        var controller = new ExtractosController(db, new NoOpAlertaService());
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

    private sealed class NoOpAlertaService : IAlertaService
    {
        public Task EvaluateSaldoPostAsync(Guid cuentaId, Guid? actorUserId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AlertaActivaItemResponse>> GetAlertasActivasAsync(UserAccessScope scope, Guid? paisId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AlertaActivaItemResponse>>([]);
    }
}
