using System.Diagnostics;
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

namespace AtlasBalance.API.Tests;

/// <summary>
/// V-02-04: cierre del pendiente "E2E con datos de volumen".
/// Siembra ~50k filas reales en EXTRACTOS contra PostgreSQL (Testcontainers) y ejercita
/// los endpoints paginados y agregados del ExtractosController para comprobar que
/// responden correctamente (forma de la respuesta, orden, totales) y en tiempo razonable.
/// No es un benchmark: los umbrales de latencia son deliberadamente generosos (smoke test).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class VolumeSmokeTests
{
    private const int TotalFilas = 50_000;
    private const int BatchSize = 5_000;

    private readonly PostgresFixture _fixture;

    public VolumeSmokeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Listar_Con_50k_Filas_Debe_Paginar_Ordenar_Y_Responder_En_Tiempo_Razonable()
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
                Email = "volume@test.local",
                PasswordHash = "hash",
                NombreCompleto = "Volume Smoke",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                PrimerLogin = false
            });
            setup.Titulares.Add(new Titular
            {
                Id = titularId,
                Nombre = "Titular Volumen",
                Tipo = TipoTitular.EMPRESA
            });
            setup.Cuentas.Add(new Cuenta
            {
                Id = cuentaId,
                TitularId = titularId,
                Nombre = "Cuenta Volumen",
                Divisa = "EUR",
                Activa = true
            });
            await setup.SaveChangesAsync();
        }

        // Siembra por lotes (AddRange + SaveChanges cada 5k) para no acumular 50k
        // entidades trackeadas de golpe en el ChangeTracker.
        var baseFecha = new DateOnly(2020, 1, 1);
        await using (var seedDb = new AppDbContext(options))
        {
            seedDb.ChangeTracker.AutoDetectChangesEnabled = false;

            for (var batchStart = 1; batchStart <= TotalFilas; batchStart += BatchSize)
            {
                var batch = new List<Extracto>(BatchSize);
                var batchEnd = Math.Min(batchStart + BatchSize - 1, TotalFilas);
                for (var fila = batchStart; fila <= batchEnd; fila++)
                {
                    batch.Add(new Extracto
                    {
                        Id = Guid.NewGuid(),
                        CuentaId = cuentaId,
                        Fecha = baseFecha.AddDays(fila % 1800),
                        Concepto = $"Movimiento volumen {fila}",
                        Monto = fila % 2 == 0 ? fila : -fila,
                        Saldo = fila,
                        FilaNumero = fila,
                        Checked = fila % 7 == 0,
                        Flagged = fila % 11 == 0
                    });
                }

                seedDb.Extractos.AddRange(batch);
                await seedDb.SaveChangesAsync();
                seedDb.ChangeTracker.Clear();
            }
        }

        await using var verifyCount = new AppDbContext(options);
        var seededTotal = await verifyCount.Extractos.CountAsync(x => x.CuentaId == cuentaId);
        seededTotal.Should().Be(TotalFilas);

        // --- Ejercitar el endpoint paginado real via HTTP-shape (controller + claims reales) ---
        await using var requestDb = new AppDbContext(options);
        var controller = BuildController(requestDb, userId, isAdmin: true);

        var maxLatency = TimeSpan.FromSeconds(15);
        var sw = new Stopwatch();

        // Pagina 1, orden por defecto (fecha desc, fila_numero desc)
        sw.Restart();
        var page1Result = await controller.Listar(
            page: 1, pageSize: 100, sortBy: "fecha", sortDir: "desc",
            cuentaId: cuentaId, ct: CancellationToken.None);
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(maxLatency, "la pagina 1 no deberia tardar mas que un smoke generoso");

        var page1 = ExtractPayload(page1Result);
        page1.Total.Should().BeGreaterThanOrEqualTo(TotalFilas);
        page1.Page.Should().Be(1);
        page1.PageSize.Should().Be(100);
        page1.Data.Should().HaveCount(100);
        page1.TotalPages.Should().Be((int)Math.Ceiling(page1.Total / 100.0));

        // Pagina alta (cerca del final) para forzar el Skip/Take sobre todo el dataset
        var lastPage = page1.TotalPages;
        sw.Restart();
        var lastPageResult = await controller.Listar(
            page: lastPage, pageSize: 100, sortBy: "fila_numero", sortDir: "asc",
            cuentaId: cuentaId, ct: CancellationToken.None);
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(maxLatency, "una pagina alta tampoco deberia tardar mas que el smoke generoso");

        var lastPageAsc = ExtractPayload(lastPageResult);
        lastPageAsc.Page.Should().Be(lastPage);
        lastPageAsc.Data.Should().NotBeEmpty();
        // Orden ascendente por fila_numero: la primera fila de la ultima pagina debe ser
        // menor o igual que la ultima (orden creciente) y coherente con el tamano total.
        var filasAsc = lastPageAsc.Data.Select(x => x.FilaNumero).ToList();
        filasAsc.Should().BeInAscendingOrder();

        // Orden descendente explicito por fila_numero, pagina 1: la primera fila debe ser
        // la de fila_numero mas alto (50000).
        sw.Restart();
        var descResult = await controller.Listar(
            page: 1, pageSize: 50, sortBy: "fila_numero", sortDir: "desc",
            cuentaId: cuentaId, ct: CancellationToken.None);
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(maxLatency);

        var descPage = ExtractPayload(descResult);
        descPage.Data.Should().HaveCount(50);
        descPage.Data.Select(x => x.FilaNumero).Should().BeInDescendingOrder();
        descPage.Data.First().FilaNumero.Should().Be(TotalFilas);

        // Pagina intermedia con pageSize distinto, para comprobar Skip/Take coherente
        sw.Restart();
        var midPageResult = await controller.Listar(
            page: 250, pageSize: 100, sortBy: "fila_numero", sortDir: "asc",
            cuentaId: cuentaId, ct: CancellationToken.None);
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(maxLatency);

        var midPage = ExtractPayload(midPageResult);
        midPage.Data.Should().HaveCount(100);
        midPage.Data.Select(x => x.FilaNumero).Should().BeInAscendingOrder();
        // Pagina 250 (0-indexed offset 249*100 = 24900) => filas 24901..25000
        midPage.Data.First().FilaNumero.Should().Be(24_901);
        midPage.Data.Last().FilaNumero.Should().Be(25_000);

        // --- Ejercitar un endpoint agregado (resumen de cuenta) con el mismo volumen ---
        sw.Restart();
        var resumenResult = await controller.GetCuentaResumen(cuentaId, periodo: "1m", paisId: null, ct: CancellationToken.None);
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(maxLatency, "el endpoint de resumen/dashboard tambien debe responder en tiempo razonable con volumen");

        var resumenOk = resumenResult.Should().BeOfType<OkObjectResult>().Subject;
        var resumen = resumenOk.Value.Should().BeOfType<CuentaResumenKpiResponse>().Subject;
        resumen.CuentaId.Should().Be(cuentaId);
        resumen.TitularId.Should().Be(titularId);
    }

    private static PaginatedResponse<ExtractoListItemResponse> ExtractPayload(IActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeOfType<PaginatedResponse<ExtractoListItemResponse>>().Subject;
    }

    private static ExtractosController BuildController(AppDbContext db, Guid userId, bool isAdmin)
    {
        var controller = new ExtractosController(db, new NoOpAlertaService());
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, isAdmin ? nameof(RolUsuario.ADMIN) : nameof(RolUsuario.GERENTE))
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
