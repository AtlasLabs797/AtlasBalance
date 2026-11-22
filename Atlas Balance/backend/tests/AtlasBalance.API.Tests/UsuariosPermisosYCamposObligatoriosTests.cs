using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.RateLimiting;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// LOTE 6 del informe de revision integral (2026-11-16).
///
/// S-5: el PUT de usuario (PUT /api/usuarios/{id}) exige el CUERPO COMPLETO.
/// Antes, un cuerpo parcial aplicaba los valores por defecto del DTO:
/// rol -> EMPLEADO, activo -> false, primer_login -> false, puede_usar_ia ->
/// false, emails -> [] y permisos -> []. Un PUT que se olvidaba de un campo
/// degradaba al usuario o le borraba la matriz de permisos y los emails.
///
/// S-2 (lado admin): la REGLA de columnas que fija el administrador
/// (ColumnasVisiblesAdmin) se separa de la PREFERENCIA del usuario
/// (ColumnasVisibles + OrdenColumnas). Guardar permisos del admin ya no
/// destruye la preferencia ni el orden de columnas del usuario.
///
/// Nota: los cuerpos se construyen como JSON y no con el DTO tipado porque
/// S-5 trata justamente de un cuerpo PARCIAL: con el DTO tipado no se puede
/// expressing "el campo no viene" sin depender de los valores por defecto.
/// </summary>
public class UsuariosPermisosYCamposObligatoriosTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static ControllerContext BuildControllerContext(Guid adminId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, adminId.ToString()),
            new Claim(ClaimTypes.Role, "ADMIN")
        ], "TestAuth");

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    private static UpdateUsuarioRequest Body(string json) =>
        JsonSerializer.Deserialize<UpdateUsuarioRequest>(json, JsonOptions)!;

    // -----------------------------------------------------------------------
    // Seeds
    // -----------------------------------------------------------------------

    private static async Task<(Usuario Usuario, Pais Pais, Titular Titular, Cuenta Cuenta)> SeedAsync(
        AppDbContext db,
        RolUsuario rol = RolUsuario.EMPLEADO)
    {
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "objetivo@atlasbalance.local",
            NombreCompleto = "Objetivo",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            Rol = rol,
            Activo = true,
            PrimerLogin = true,
            PuedeUsarIa = false,
            FechaCreacion = DateTime.UtcNow
        };
        var pais = new Pais { Id = Guid.NewGuid(), Nombre = "Pais", Activo = true };
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta
        {
            Id = Guid.NewGuid(),
            TitularId = titular.Id,
            PaisId = pais.Id,
            Nombre = "Cuenta",
            Divisa = "EUR",
            Activa = true
        };

        db.Usuarios.Add(usuario);
        db.Paises.Add(pais);
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.UsuarioEmails.Add(new UsuarioEmail
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            Email = "original@atlasbalance.local",
            EsPrincipal = true
        });
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PaisId = pais.Id,
            PuedeVerCuentas = true
        });
        await db.SaveChangesAsync();
        return (usuario, pais, titular, cuenta);
    }

    // -----------------------------------------------------------------------
    // S-5: el PUT exige el cuerpo completo
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Actualizar_Sin_Rol_Debe_Devolver_400_Y_No_Bajar_A_Empleado()
    {
        await using var db = BuildDbContext();
        var (usuario, _, _, _) = await SeedAsync(db, RolUsuario.ADMIN);
        // Otro administrador activo: si no, el 400 por "debe quedar al menos un
        // administrador activo" enmascararia el 400 por campo ausente.
        db.Usuarios.Add(new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "otro.admin@atlasbalance.local",
            NombreCompleto = "Otro Admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Valid1234!Ab", workFactor: 12),
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false,
            FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());

        var body = Body("""
            {
              "email": "objetivo@atlasbalance.local",
              "nombre_completo": "Objetivo",
              "activo": true,
              "primer_login": false,
              "puede_usar_ia": true,
              "emails": ["alertas@atlasbalance.local"],
              "permisos": []
            }
            """);

        var result = await controller.Actualizar(usuario.Id, body, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Usuarios.SingleAsync(u => u.Id == usuario.Id)).Rol.Should().Be(RolUsuario.ADMIN);
    }

    [Fact]
    public async Task Actualizar_Sin_Activo_Debe_Devolver_400_Y_No_Desactivar_El_Usuario()
    {
        await using var db = BuildDbContext();
        var (usuario, _, _, _) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());

        var body = Body("""
            {
              "email": "objetivo@atlasbalance.local",
              "nombre_completo": "Objetivo",
              "rol": "EMPLEADO",
              "primer_login": false,
              "puede_usar_ia": true,
              "emails": ["alertas@atlasbalance.local"],
              "permisos": []
            }
            """);

        var result = await controller.Actualizar(usuario.Id, body, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Usuarios.SingleAsync(u => u.Id == usuario.Id)).Activo.Should().BeTrue();
    }

    [Fact]
    public async Task Actualizar_Sin_Emails_Debe_Devolver_400_Y_No_Borrar_Los_Emails()
    {
        await using var db = BuildDbContext();
        var (usuario, _, _, _) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());

        var body = Body("""
            {
              "email": "objetivo@atlasbalance.local",
              "nombre_completo": "Objetivo",
              "rol": "EMPLEADO",
              "activo": true,
              "primer_login": false,
              "puede_usar_ia": true,
              "permisos": []
            }
            """);

        var result = await controller.Actualizar(usuario.Id, body, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        var emails = await db.UsuarioEmails.Where(e => e.UsuarioId == usuario.Id).ToListAsync();
        emails.Should().ContainSingle(e => e.Email == "original@atlasbalance.local");
    }

    [Fact]
    public async Task Actualizar_Sin_Permisos_Debe_Devolver_400_Y_No_Borrar_La_Matriz()
    {
        await using var db = BuildDbContext();
        var (usuario, _, titular, cuenta) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());

        var body = Body("""
            {
              "email": "objetivo@atlasbalance.local",
              "nombre_completo": "Objetivo",
              "rol": "EMPLEADO",
              "activo": true,
              "primer_login": false,
              "puede_usar_ia": true,
              "emails": ["alertas@atlasbalance.local"]
            }
            """);

        var result = await controller.Actualizar(usuario.Id, body, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        var permisos = await db.PermisosUsuario.Where(p => p.UsuarioId == usuario.Id).ToListAsync();
        permisos.Should().ContainSingle(p => p.CuentaId == cuenta.Id && p.TitularId == titular.Id);
    }

    [Fact]
    public async Task Actualizar_Con_Cuerpo_Completo_Debe_Aplicar_Todos_Los_Campos()
    {
        await using var db = BuildDbContext();
        var (usuario, _, _, _) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());

        var body = Body("""
            {
              "email": "objetivo@atlasbalance.local",
              "nombre_completo": "Objetivo Renombrado",
              "rol": "GERENTE",
              "activo": true,
              "primer_login": false,
              "puede_usar_ia": true,
              "emails": ["alertas@atlasbalance.local"],
              "permisos": [
                {
                  "puede_ver_cuentas": true,
                  "puede_revisar_lineas": true,
                  "puede_aprobar_importaciones": true,
                  "puede_conciliar": true,
                  "puede_cerrar_conciliacion": true
                }
              ]
            }
            """);

        var result = await controller.Actualizar(usuario.Id, body, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var persisted = await db.Usuarios.SingleAsync(u => u.Id == usuario.Id);
        persisted.NombreCompleto.Should().Be("Objetivo Renombrado");
        persisted.Rol.Should().Be(RolUsuario.GERENTE);
        persisted.Activo.Should().BeTrue();
        persisted.PrimerLogin.Should().BeFalse();
        persisted.PuedeUsarIa.Should().BeTrue();

        var emails = await db.UsuarioEmails.Where(e => e.UsuarioId == usuario.Id).ToListAsync();
        emails.Should().ContainSingle(e => e.Email == "alertas@atlasbalance.local");

        var permisos = await db.PermisosUsuario.Where(p => p.UsuarioId == usuario.Id).ToListAsync();
        permisos.Should().ContainSingle();
        permisos[0].PuedeVerCuentas.Should().BeTrue();
        permisos[0].PuedeRevisarLineas.Should().BeTrue();
        permisos[0].PuedeAprobarImportaciones.Should().BeTrue();
        permisos[0].PuedeConciliar.Should().BeTrue();
        permisos[0].PuedeCerrarConciliacion.Should().BeTrue();
    }

    [Fact]
    public async Task Actualizar_Sin_Campos_Obligatorios_Debe_Mencionar_Los_Campos_Que_Faltan()
    {
        await using var db = BuildDbContext();
        var (usuario, _, _, _) = await SeedAsync(db);
        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());

        var body = Body("""
            {
              "email": "objetivo@atlasbalance.local",
              "nombre_completo": "Objetivo"
            }
            """);

        var result = await controller.Actualizar(usuario.Id, body, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var mensaje = badRequest.Value?.ToString() ?? string.Empty;
        mensaje.Should().Contain("rol");
        mensaje.Should().Contain("activo");
        mensaje.Should().Contain("primer_login");
        mensaje.Should().Contain("puede_usar_ia");
        mensaje.Should().Contain("emails");
        mensaje.Should().Contain("permisos");
    }

    // -----------------------------------------------------------------------
    // S-2 (lado admin): la regla del admin no pisa la preferencia del usuario
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuardarPermisos_Con_ColumnasVisibles_Debe_Guardar_La_Whitelist_Del_Admin(
        bool individualAccount)
    {
        await using var db = BuildDbContext();
        var (usuario, pais, titular, cuenta) = await SeedAsync(db);
        db.PreferenciasUsuarioCuenta.Add(new PreferenciaUsuarioCuenta
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            PaisId = pais.Id,
            TitularId = titular.Id,
            CuentaId = cuenta.Id,
            // PREFERENCIA del usuario (la que escribe el propio usuario).
            ColumnasVisibles = """["concepto"]""",
            OrdenColumnas = """["monto","fecha","concepto"]"""
        });
        await db.SaveChangesAsync();

        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());

        var request = new SavePermisoUsuarioRequest
        {
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PaisId = pais.Id,
            PuedeVerCuentas = true,
            ColumnasVisibles = ["fecha", "monto"]
        };

        var result = individualAccount
            ? await controller.GuardarPermisoCuenta(usuario.Id, cuenta.Id, request, CancellationToken.None)
            : await controller.GuardarPermisos(usuario.Id, [request], CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var preferencia = await db.PreferenciasUsuarioCuenta.SingleAsync(p => p.UsuarioId == usuario.Id);
        // La REGLA del admin va a ColumnasVisiblesAdmin.
        JsonSerializer.Deserialize<string[]>(preferencia.ColumnasVisiblesAdmin!)
            .Should().Equal("fecha", "monto");
        // La PREFERENCIA del usuario sigue intacta.
        JsonSerializer.Deserialize<string[]>(preferencia.ColumnasVisibles!)
            .Should().Equal("concepto");
        JsonSerializer.Deserialize<string[]>(preferencia.OrdenColumnas!)
            .Should().Equal("monto", "fecha", "concepto");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuardarPermisos_No_Debe_Destruir_La_Preferencia_Ni_El_Orden_Del_Usuario(
        bool individualAccount)
    {
        await using var db = BuildDbContext();
        var (usuario, pais, titular, cuenta) = await SeedAsync(db);
        db.PreferenciasUsuarioCuenta.Add(new PreferenciaUsuarioCuenta
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            PaisId = pais.Id,
            TitularId = titular.Id,
            CuentaId = cuenta.Id,
            ColumnasVisibles = """["concepto"]""",
            OrdenColumnas = """["monto","fecha","concepto"]"""
        });
        await db.SaveChangesAsync();

        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());

        var request = new SavePermisoUsuarioRequest
        {
            CuentaId = cuenta.Id,
            TitularId = titular.Id,
            PaisId = pais.Id,
            PuedeVerCuentas = true,
            // El admin envia su regla de columnas: es justo el caso que antes
            // pisaba la preferencia del usuario.
            ColumnasVisibles = ["fecha", "monto"]
        };

        var result = individualAccount
            ? await controller.GuardarPermisoCuenta(usuario.Id, cuenta.Id, request, CancellationToken.None)
            : await controller.GuardarPermisos(usuario.Id, [request], CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var preferencia = await db.PreferenciasUsuarioCuenta.SingleAsync(p => p.UsuarioId == usuario.Id);
        JsonSerializer.Deserialize<string[]>(preferencia.ColumnasVisibles!)
            .Should().Equal("concepto");
        JsonSerializer.Deserialize<string[]>(preferencia.OrdenColumnas!)
            .Should().Equal("monto", "fecha", "concepto");
    }

    [Fact]
    public async Task ObtenerPermisos_Debe_Devolver_La_Whitelist_Del_Admin()
    {
        await using var db = BuildDbContext();
        var (usuario, pais, titular, cuenta) = await SeedAsync(db);
        db.PreferenciasUsuarioCuenta.Add(new PreferenciaUsuarioCuenta
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            PaisId = pais.Id,
            TitularId = titular.Id,
            CuentaId = cuenta.Id,
            ColumnasVisibles = """["concepto"]""",
            ColumnasVisiblesAdmin = """["fecha","monto"]"""
        });
        await db.SaveChangesAsync();

        var controller = new UsuariosController(db, TestAuditService.Create(db));
        controller.ControllerContext = BuildControllerContext(Guid.NewGuid());

        var result = await controller.ObtenerPermisos(usuario.Id, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var permisos = ok.Value.Should().BeAssignableTo<IReadOnlyList<PermisoUsuarioResponse>>().Subject;
        permisos.Should().ContainSingle();
        permisos[0].ColumnasVisibles.Should().Equal("fecha", "monto");
    }

    [Fact]
    public async Task AuthMe_Debe_Devolver_La_Whitelist_Del_Admin()
    {
        await using var db = BuildDbContext();
        var (usuario, pais, titular, cuenta) = await SeedAsync(db);
        db.PreferenciasUsuarioCuenta.Add(new PreferenciaUsuarioCuenta
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            PaisId = pais.Id,
            TitularId = titular.Id,
            CuentaId = cuenta.Id,
            ColumnasVisibles = """["concepto"]""",
            ColumnasVisiblesAdmin = """["fecha","monto"]"""
        });
        await db.SaveChangesAsync();

        var authService = BuildAuthService(db);
        var result = await authService.GetCurrentAsync(usuario.Id, CancellationToken.None);

        result.Permisos.Should().ContainSingle();
        result.Permisos[0].ColumnasVisibles.Should().Equal("fecha", "monto");
    }

    private static AuthService BuildAuthService(AppDbContext db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "test-secret-key-minimum-32-characters-long",
                ["JwtSettings:AccessTokenExpMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpDays"] = "7",
                ["Security:RequireMfaForWebUsers"] = "false"
            })
            .Build();

        return new AuthService(
            db,
            configuration,
            TestAuditService.Create(db),
            new PlainTextSecretProtector(),
            new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
            Options.Create(new CachingOptions()),
            Options.Create(new RateLimitingOptions()));
    }
}
