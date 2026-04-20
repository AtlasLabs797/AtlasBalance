using System.Security.Claims;
using FluentAssertions;
using GestionCaja.API.Controllers;
using GestionCaja.API.Data;
using GestionCaja.API.DTOs;
using GestionCaja.API.Models;
using GestionCaja.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GestionCaja.API.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ExtractosConcurrencyTests
{
    private readonly PostgresFixture _fixture;

    public ExtractosConcurrencyTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Crear_Concurrente_Debe_Generar_FilaNumeros_Unicos()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaId = Guid.NewGuid();

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.MigrateAsync();

            setup.Usuarios.Add(new Usuario
            {
                Id = userId,
                Email = "concurrency@test.local",
                PasswordHash = "hash",
                NombreCompleto = "Concurrency",
                Rol = RolUsuario.GERENTE,
                Activo = true,
                PrimerLogin = false
            });
            setup.Titulares.Add(new Titular
            {
                Id = titularId,
                Nombre = "Titular Concurrency",
                Tipo = TipoTitular.EMPRESA
            });
            setup.Cuentas.Add(new Cuenta
            {
                Id = cuentaId,
                TitularId = titularId,
                Nombre = "Cuenta Concurrency",
                Divisa = "EUR",
                Activa = true
            });
            setup.PermisosUsuario.Add(new PermisoUsuario
            {
                Id = Guid.NewGuid(),
                UsuarioId = userId,
                CuentaId = cuentaId,
                TitularId = titularId,
                PuedeAgregarLineas = true
            });
            await setup.SaveChangesAsync();
        }

        var tasks = Enumerable.Range(1, 10).Select(async index =>
        {
            await using var db = new AppDbContext(options);
            var controller = BuildController(db, userId);
            var result = await controller.Crear(new CreateExtractoRequest
            {
                CuentaId = cuentaId,
                Fecha = new DateOnly(2026, 4, 1),
                Concepto = $"Fila {index}",
                Monto = index,
                Saldo = index
            }, CancellationToken.None);

            result.Should().BeOfType<OkObjectResult>();
        });

        await Task.WhenAll(tasks);

        await using var verify = new AppDbContext(options);
        var filas = await verify.Extractos
            .Where(x => x.CuentaId == cuentaId)
            .Select(x => x.FilaNumero)
            .ToListAsync();

        filas.Should().HaveCount(10);
        filas.Distinct().Should().HaveCount(10);
    }

    private static ExtractosController BuildController(AppDbContext db, Guid userId)
    {
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

        return controller;
    }

    private sealed class NoOpAlertaService : IAlertaService
    {
        public Task EvaluateSaldoPostAsync(Guid cuentaId, Guid? actorUserId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AlertaActivaItemResponse>> GetAlertasActivasAsync(UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AlertaActivaItemResponse>>([]);
    }
}
