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

public sealed class FormatosImportacionControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task Crear_Should_Accept_Two_Column_Ingreso_Egreso_Format()
    {
        await using var db = BuildDbContext();
        var controller = await BuildControllerAsync(db);

        var result = await controller.Crear(new SaveFormatoImportacionRequest
        {
            Nombre = "Banco Dos Columnas",
            BancoNombre = "Banco Dos Columnas",
            Divisa = "EUR",
            Activo = true,
            MapeoJson = JsonElementFrom(new
            {
                tipo_monto = "dos_columnas",
                fecha = 0,
                concepto = 1,
                ingreso = 2,
                egreso = 3,
                saldo = 4
            })
        }, CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();

        var formato = await db.FormatosImportacion.SingleAsync();
        using var doc = JsonDocument.Parse(formato.MapeoJson);
        var root = doc.RootElement;
        root.GetProperty("tipo_monto").GetString().Should().Be("dos_columnas");
        root.GetProperty("ingreso").GetInt32().Should().Be(2);
        root.GetProperty("egreso").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Crear_Should_Report_Missing_Required_Index_Instead_Of_False_Duplicate()
    {
        await using var db = BuildDbContext();
        var controller = await BuildControllerAsync(db);

        var result = await controller.Crear(new SaveFormatoImportacionRequest
        {
            Nombre = "Banco Mapeo Incompleto",
            BancoNombre = "Banco Mapeo Incompleto",
            Divisa = "EUR",
            Activo = true,
            MapeoJson = JsonElementFrom(new
            {
                tipo_monto = "dos_columnas",
                concepto = 1,
                ingreso = 2,
                egreso = 3,
                saldo = 4
            })
        }, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new
        {
            error = "Faltan indices obligatorios para el tipo de monto seleccionado"
        });
    }

    [Fact]
    public async Task Crear_Should_Reject_More_Than_64_Extra_Columns()
    {
        await using var db = BuildDbContext();
        var controller = await BuildControllerAsync(db);
        var extras = Enumerable.Range(0, 65)
            .Select(i => new { nombre = $"extra_{i}", indice = i + 4 })
            .ToArray();

        var result = await controller.Crear(ValidRequestWithExtras(extras), CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new
        {
            error = "El mapeo no puede incluir mas de 64 columnas extra"
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Crear_Should_Reject_Overlong_Extra_Column_Text(bool overlongName)
    {
        await using var db = BuildDbContext();
        var controller = await BuildControllerAsync(db);
        var extra = new
        {
            nombre = overlongName ? new string('n', 81) : "referencia",
            indice = 4,
            etiqueta = overlongName ? "Referencia" : new string('e', 81)
        };

        var result = await controller.Crear(ValidRequestWithExtras(new[] { extra }), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Crear_Should_Reject_Mapeo_Json_Over_64_Kib()
    {
        await using var db = BuildDbContext();
        var controller = await BuildControllerAsync(db);
        var request = ValidRequestWithExtras(Array.Empty<object>());
        request.MapeoJson = JsonElementFrom(new
        {
            tipo_monto = "una_columna",
            fecha = 0,
            concepto = 1,
            monto = 2,
            saldo = 3,
            relleno = new string('x', 64 * 1024)
        });

        var result = await controller.Crear(request, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new
        {
            error = "Mapeo JSON no puede superar 65536 caracteres"
        });
    }

    [Fact]
    public async Task ListarColumnasExtraSugeridas_Should_Return_Distinct_Ordered_Names()
    {
        await using var db = BuildDbContext();
        var controller = await BuildControllerAsync(db);

        var cuenta = new Cuenta
        {
            Id = Guid.NewGuid(),
            Nombre = "Cuenta Test",
            TitularId = Guid.NewGuid(),
            PaisId = Guid.NewGuid(),
            Divisa = "EUR",
            Activa = true,
        };
        db.Cuentas.Add(cuenta);

        var extractoA = new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = new DateOnly(2026, 7, 1),
            Concepto = "Concepto A",
            Monto = 100m,
            Saldo = 100m,
            FilaNumero = 1,
            FechaCreacion = DateTime.UtcNow,
        };
        var extractoB = new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = new DateOnly(2026, 7, 2),
            Concepto = "Concepto B",
            Monto = 200m,
            Saldo = 200m,
            FilaNumero = 2,
            FechaCreacion = DateTime.UtcNow,
        };
        db.Extractos.AddRange(extractoA, extractoB);
        db.ExtractosColumnasExtra.AddRange(
            new ExtractoColumnaExtra { Id = Guid.NewGuid(), ExtractoId = extractoA.Id, NombreColumna = "referencia", Valor = "REF-1" },
            new ExtractoColumnaExtra { Id = Guid.NewGuid(), ExtractoId = extractoA.Id, NombreColumna = "cheque", Valor = "CHQ-1" },
            new ExtractoColumnaExtra { Id = Guid.NewGuid(), ExtractoId = extractoB.Id, NombreColumna = "referencia", Valor = "REF-2" },
            new ExtractoColumnaExtra { Id = Guid.NewGuid(), ExtractoId = extractoB.Id, NombreColumna = "documento", Valor = "DOC-1" },
            new ExtractoColumnaExtra { Id = Guid.NewGuid(), ExtractoId = extractoB.Id, NombreColumna = "", Valor = "" }
        );
        await db.SaveChangesAsync();

        var result = await controller.ListarColumnasExtraSugeridas(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<ListarColumnasExtraSugeridasResponse>().Subject;
        payload.Data.Should().BeEquivalentTo(new[] { "cheque", "documento", "referencia" });
    }

    [Fact]
    public async Task ListarColumnasExtraSugeridas_Should_Exclude_Soft_Deleted_Rows()
    {
        await using var db = BuildDbContext();
        var controller = await BuildControllerAsync(db);

        var cuenta = new Cuenta
        {
            Id = Guid.NewGuid(),
            Nombre = "Cuenta Test",
            TitularId = Guid.NewGuid(),
            PaisId = Guid.NewGuid(),
            Divisa = "EUR",
            Activa = true,
        };
        db.Cuentas.Add(cuenta);

        var extracto = new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = new DateOnly(2026, 7, 1),
            Concepto = "Concepto",
            Monto = 100m,
            Saldo = 100m,
            FilaNumero = 1,
            FechaCreacion = DateTime.UtcNow,
        };
        db.Extractos.Add(extracto);
        db.ExtractosColumnasExtra.AddRange(
            new ExtractoColumnaExtra { Id = Guid.NewGuid(), ExtractoId = extracto.Id, NombreColumna = "visible", Valor = "v" },
            new ExtractoColumnaExtra
            {
                Id = Guid.NewGuid(),
                ExtractoId = extracto.Id,
                NombreColumna = "borrado",
                Valor = "b",
                DeletedAt = DateTime.UtcNow,
                DeletedById = Guid.NewGuid(),
            }
        );
        await db.SaveChangesAsync();

        var result = await controller.ListarColumnasExtraSugeridas(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<ListarColumnasExtraSugeridasResponse>().Subject;
        payload.Data.Should().BeEquivalentTo(new[] { "visible" });
    }

    [Fact]
    public async Task ListarColumnasExtraSugeridas_Should_Return_Empty_When_No_Extras()
    {
        await using var db = BuildDbContext();
        var controller = await BuildControllerAsync(db);

        var result = await controller.ListarColumnasExtraSugeridas(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<ListarColumnasExtraSugeridasResponse>().Subject;
        payload.Data.Should().BeEmpty();
    }

    private static async Task<FormatosImportacionController> BuildControllerAsync(AppDbContext db)
    {
        var adminId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = adminId,
            Email = "admin.formatos@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Formatos",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.DivisasActivas.Add(new DivisaActiva
        {
            Codigo = "EUR",
            Nombre = "Euro",
            Simbolo = "EUR",
            Activa = true,
            EsBase = true
        });
        await db.SaveChangesAsync();

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, adminId.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.ADMIN))
        ], "TestAuth");

        return new FormatosImportacionController(db, TestAuditService.Create(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private static SaveFormatoImportacionRequest ValidRequestWithExtras<T>(IReadOnlyList<T> extras)
    {
        return new SaveFormatoImportacionRequest
        {
            Nombre = "Banco Seguro",
            BancoNombre = "Banco Seguro",
            Divisa = "EUR",
            Activo = true,
            MapeoJson = JsonElementFrom(new
            {
                tipo_monto = "una_columna",
                fecha = 0,
                concepto = 1,
                monto = 2,
                saldo = 3,
                columnas_extra = extras
            })
        };
    }

    private static JsonElement JsonElementFrom<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
