using System.Security.Claims;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// B-7: `PUT /api/extractos/{id}` no debe devolver 500 si falla la evaluacion
/// de alertas: el cambio ya esta guardado y la respuesta debe seguir siendo 200.
/// Mismo patron que `Crear`, que ya lo protege.
/// </summary>
public sealed class ExtractosAlertaPostTests
{
    [Fact]
    public async Task Actualizar_Should_Return_Ok_And_Persist_Change_When_Alerta_Evaluation_Fails()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();
        var extractoId = Guid.NewGuid();

        db.Usuarios.Add(new Usuario
        {
            Id = userId,
            Email = "admin.alerta-post@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Alerta Post",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Alerta", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.Add(new Cuenta { Id = cuentaId, TitularId = titularId, Nombre = "Cuenta Alerta", Divisa = "EUR", Activa = true });
        db.Extractos.Add(new Extracto
        {
            Id = extractoId,
            CuentaId = cuentaId,
            Fecha = new DateOnly(2026, 5, 1),
            Concepto = "Movimiento",
            Monto = 10m,
            Saldo = 10m,
            FilaNumero = 1
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, userId, new ThrowingAlertaService());

        var result = await controller.Actualizar(
            extractoId,
            new UpdateExtractoRequest { Monto = 25m },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var guardado = await db.Extractos.AsNoTracking().SingleAsync(x => x.Id == extractoId);
        guardado.Monto.Should().Be(25m);
    }

    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static ExtractosController BuildController(AppDbContext db, Guid userId, IAlertaService alertaService)
    {
        var controller = new ExtractosController(db, alertaService, NullLogger<ExtractosController>.Instance, TestUserAccess.Create(db));
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, RolUsuario.ADMIN.ToString())
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

    private sealed class ThrowingAlertaService : IAlertaService
    {
        public Task EvaluateSaldoPostAsync(Guid cuentaId, Guid? actorUserId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Fallo simulado en la evaluacion de alertas.");

        public Task<IReadOnlyList<AlertaActivaItemResponse>> GetAlertasActivasAsync(UserAccessScope scope, Guid? paisId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AlertaActivaItemResponse>>([]);
    }
}
