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

        return new FormatosImportacionController(db, new AuditService(db))
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

    private static JsonElement JsonElementFrom<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
