using System.Text.Json;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Middleware;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// Lote 5 (B-1): una divisa sin tipo de cambio utilizable no aporta al total
/// convertido. El total se mantiene igual (solo suma lo convertible) pero la
/// respuesta tiene que decir QUE divisas se quedaron fuera, para que el
/// usuario no lea un total bajo sin saber por que.
/// </summary>
public sealed class DashboardDivisasSinTipoCambioTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static DashboardService BuildService(AppDbContext db)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var cacheService = new CacheService(cache, NullLogger<CacheService>.Instance);
        var cachingOptions = Options.Create(new CachingOptions());
        var tiposCambioService = new TiposCambioService(
            db,
            cacheService,
            new StaticHttpClientFactory(),
            NullLogger<TiposCambioService>.Instance,
            new PlainTextSecretProtector(),
            cachingOptions);
        return new DashboardService(db, cacheService, tiposCambioService, cachingOptions);
    }

    private static void SeedAdmin(AppDbContext db, Guid adminId, string email)
    {
        db.Usuarios.Add(new Usuario
        {
            Id = adminId,
            Email = email,
            PasswordHash = "hash",
            NombreCompleto = "Admin Divisas",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
    }

    private static void SeedConfig(AppDbContext db, Guid adminId)
    {
        db.DivisasActivas.AddRange(
            new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true },
            new DivisaActiva { Codigo = "USD", Activa = true, EsBase = false },
            new DivisaActiva { Codigo = "GBP", Activa = true, EsBase = false });

        db.Configuraciones.AddRange(
            new Configuracion { Clave = "divisa_principal_default", Valor = "EUR", FechaModificacion = DateTime.UtcNow, UsuarioModificacionId = adminId },
            new Configuracion { Clave = "dashboard_color_ingresos", Valor = "#43B430", FechaModificacion = DateTime.UtcNow, UsuarioModificacionId = adminId },
            new Configuracion { Clave = "dashboard_color_egresos", Valor = "#FF4757", FechaModificacion = DateTime.UtcNow, UsuarioModificacionId = adminId },
            new Configuracion { Clave = "dashboard_color_saldo", Valor = "#7B7B7B", FechaModificacion = DateTime.UtcNow, UsuarioModificacionId = adminId });
    }

    [Fact]
    public async Task GetPrincipalAsync_Should_List_Divisas_Sin_Tipo_Cambio_Sin_Tocar_El_Total()
    {
        await using var db = BuildDbContext();
        var adminId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaEurId = Guid.NewGuid();
        var cuentaUsdId = Guid.NewGuid();
        var cuentaGbpId = Guid.NewGuid();

        SeedAdmin(db, adminId, "admin.divisas.sin.tasa@test.local");
        SeedConfig(db, adminId);

        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Multi", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = cuentaEurId, TitularId = titularId, Nombre = "Cuenta EUR", Divisa = "EUR", Activa = true },
            new Cuenta { Id = cuentaUsdId, TitularId = titularId, Nombre = "Cuenta USD", Divisa = "USD", Activa = true },
            new Cuenta { Id = cuentaGbpId, TitularId = titularId, Nombre = "Cuenta GBP", Divisa = "GBP", Activa = true });

        // Solo GBP tiene tipo de cambio hacia EUR (1 GBP = 0,5 EUR). USD se queda fuera.
        db.TiposCambio.Add(new TipoCambio
        {
            Id = Guid.NewGuid(),
            DivisaOrigen = "GBP",
            DivisaDestino = "EUR",
            Tasa = 0.5m,
            FechaActualizacion = DateTime.UtcNow,
            Fuente = FuenteTipoCambio.MANUAL
        });

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        db.Extractos.AddRange(
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaEurId, Fecha = today, Monto = 100m, Saldo = 100m, FilaNumero = 1 },
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaUsdId, Fecha = today, Monto = 50m, Saldo = 50m, FilaNumero = 1 },
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaGbpId, Fecha = today, Monto = 20m, Saldo = 20m, FilaNumero = 1 });
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var result = await sut.GetPrincipalAsync(adminId, "EUR", null, CancellationToken.None);

        result.DivisasSinTipoCambio.Should().BeEquivalentTo(["USD"]);
        // El total no cambia: 100 EUR + 20 GBP convertidos. USD no aporta.
        result.TotalConvertido.Should().Be(110m);
        // El saldo en su divisa se sigue viendo: lo que falta es la conversion.
        result.SaldosPorDivisa.Should().ContainKey("USD").WhoseValue.Should().Be(50m);
    }

    [Fact]
    public async Task GetTitularAsync_Should_List_Divisas_Sin_Tipo_Cambio()
    {
        await using var db = BuildDbContext();
        var adminId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaEurId = Guid.NewGuid();
        var cuentaUsdId = Guid.NewGuid();

        SeedAdmin(db, adminId, "admin.divisas.titular@test.local");
        SeedConfig(db, adminId);

        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Titular", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = cuentaEurId, TitularId = titularId, Nombre = "Cuenta EUR", Divisa = "EUR", Activa = true },
            new Cuenta { Id = cuentaUsdId, TitularId = titularId, Nombre = "Cuenta USD", Divisa = "USD", Activa = true });

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        db.Extractos.AddRange(
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaEurId, Fecha = today, Monto = 100m, Saldo = 100m, FilaNumero = 1 },
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaUsdId, Fecha = today, Monto = 50m, Saldo = 50m, FilaNumero = 1 });
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var result = await sut.GetTitularAsync(adminId, titularId, "EUR", null, CancellationToken.None);

        result.DivisasSinTipoCambio.Should().BeEquivalentTo(["USD"]);
        result.TotalConvertido.Should().Be(100m);
    }

    [Fact]
    public async Task GetSaldosDivisaAsync_Should_List_Divisas_Sin_Tipo_Cambio()
    {
        await using var db = BuildDbContext();
        var adminId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaEurId = Guid.NewGuid();
        var cuentaUsdId = Guid.NewGuid();

        SeedAdmin(db, adminId, "admin.divisas.saldos@test.local");
        SeedConfig(db, adminId);

        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Saldos", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = cuentaEurId, TitularId = titularId, Nombre = "Cuenta EUR", Divisa = "EUR", Activa = true },
            new Cuenta { Id = cuentaUsdId, TitularId = titularId, Nombre = "Cuenta USD", Divisa = "USD", Activa = true });

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        db.Extractos.AddRange(
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaEurId, Fecha = today, Monto = 100m, Saldo = 100m, FilaNumero = 1 },
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaUsdId, Fecha = today, Monto = 50m, Saldo = 50m, FilaNumero = 1 });
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var result = await sut.GetSaldosDivisaAsync(adminId, "EUR", null, null, CancellationToken.None);

        result.DivisasSinTipoCambio.Should().BeEquivalentTo(["USD"]);
        result.TotalConvertido.Should().Be(100m);
    }

    [Fact]
    public async Task GetPrincipalAsync_Should_Not_List_Divisas_Que_Tienen_Tipo_Cambio()
    {
        await using var db = BuildDbContext();
        var adminId = Guid.NewGuid();
        var titularId = Guid.NewGuid();
        var cuentaEurId = Guid.NewGuid();
        var cuentaGbpId = Guid.NewGuid();

        SeedAdmin(db, adminId, "admin.divisas.con.tasa@test.local");
        SeedConfig(db, adminId);

        db.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Con Tasa", Tipo = TipoTitular.EMPRESA });
        db.Cuentas.AddRange(
            new Cuenta { Id = cuentaEurId, TitularId = titularId, Nombre = "Cuenta EUR", Divisa = "EUR", Activa = true },
            new Cuenta { Id = cuentaGbpId, TitularId = titularId, Nombre = "Cuenta GBP", Divisa = "GBP", Activa = true });

        db.TiposCambio.Add(new TipoCambio
        {
            Id = Guid.NewGuid(),
            DivisaOrigen = "GBP",
            DivisaDestino = "EUR",
            Tasa = 0.5m,
            FechaActualizacion = DateTime.UtcNow,
            Fuente = FuenteTipoCambio.MANUAL
        });

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        db.Extractos.AddRange(
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaEurId, Fecha = today, Monto = 100m, Saldo = 100m, FilaNumero = 1 },
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaGbpId, Fecha = today, Monto = 20m, Saldo = 20m, FilaNumero = 1 });
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var result = await sut.GetPrincipalAsync(adminId, "EUR", null, CancellationToken.None);

        result.DivisasSinTipoCambio.Should().BeEmpty();
        result.TotalConvertido.Should().Be(110m);
    }

    [Fact]
    public async Task Saldos_OpenClaw_Should_Report_Divisas_Sin_Tipo_Cambio_Sin_Tocar_El_Total()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Empresa", Tipo = TipoTitular.EMPRESA };
        var cuentaEur = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta EUR", Divisa = "EUR", Activa = true };
        var cuentaUsd = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta USD", Divisa = "USD", Activa = true };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.DivisasActivas.Add(new DivisaActiva { Codigo = "EUR", Activa = true, EsBase = true });
        db.Titulares.Add(titular);
        db.Cuentas.AddRange(cuentaEur, cuentaUsd);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.AddRange(
            new IntegrationPermission { Id = Guid.NewGuid(), TokenId = token.Id, CuentaId = cuentaEur.Id, AccesoTipo = "lectura" },
            new IntegrationPermission { Id = Guid.NewGuid(), TokenId = token.Id, CuentaId = cuentaUsd.Id, AccesoTipo = "lectura" });

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        db.Extractos.AddRange(
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaEur.Id, Fecha = today, Monto = 100m, Saldo = 100m, FilaNumero = 1 },
            new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaUsd.Id, Fecha = today, Monto = 50m, Saldo = 50m, FilaNumero = 1 });
        await db.SaveChangesAsync();

        var controller = new IntegrationOpenClawController(
            db,
            new IntegrationAuthorizationService(db),
            new TiposCambioSinTasaStub());

        var httpContext = new DefaultHttpContext();
        httpContext.Items[IntegrationHttpContextItemKeys.CurrentIntegrationToken] = token;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        var result = await controller.Saldos("full", null, null, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(okResult.Value));
        var datos = document.RootElement.GetProperty("Datos");

        datos.GetProperty("divisas_sin_tipo_cambio")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Should().BeEquivalentTo(["USD"]);
        // El total no cambia: USD no aporta porque no hay tipo de cambio.
        datos.GetProperty("total_convertido").GetProperty("monto").GetDecimal().Should().Be(100m);
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client = new()
        {
            BaseAddress = new Uri("https://example.invalid/")
        };

        public HttpClient CreateClient(string name) => _client;
    }

    /// <summary>
    /// Solo EUR tiene tasa. Cualquier otra divisa (USD) devuelve null, como
    /// hace el servicio real cuando no hay tipo de cambio.
    /// </summary>
    private sealed class TiposCambioSinTasaStub : ITiposCambioService
    {
        public Task<IReadOnlyDictionary<string, decimal?>> BulkConvertAsync(
            IReadOnlyDictionary<string, decimal> amountsBySource,
            string divisaDestino,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in amountsBySource)
            {
                result[entry.Key] = entry.Key.Equals(divisaDestino, StringComparison.OrdinalIgnoreCase)
                    ? entry.Value
                    : null;
            }

            return Task.FromResult<IReadOnlyDictionary<string, decimal?>>(result);
        }

        public Task<decimal> ConvertAsync(decimal amount, string divisaOrigen, string divisaDestino, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<decimal?> TryConvertAsync(decimal amount, string divisaOrigen, string divisaDestino, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TipoCambioDto>> ListarTiposCambioAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DivisaActivaDto>> ListarDivisasAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TipoCambioDto> GuardarTipoCambioManualAsync(string divisaOrigen, string divisaDestino, decimal tasa, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<DivisaActivaDto> CrearDivisaAsync(string codigo, string? nombre, string? simbolo, bool activa, bool esBase, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<DivisaActivaDto> ActualizarDivisaAsync(string codigo, string? nombre, string? simbolo, bool activa, bool esBase, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<SyncTiposCambioResult> SincronizarTiposCambioAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
