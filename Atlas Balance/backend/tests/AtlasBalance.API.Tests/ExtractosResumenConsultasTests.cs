using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// R-7: GetCuentasTitular y GetTitularesResumen llamaban a BuildSummary
/// dentro de un bucle por cuenta. Cada iteracion lanzaba varias consultas a
/// la BD (N+1), de modo que el numero de consultas crecia con el numero de
/// cuentas del titular.
///
/// Se mide con SQL real porque el proveedor InMemory de los tests no ejecuta
/// comandos ni admite interceptores de comando.
/// </summary>
[Trait("Category", "Postgres")]
[Collection(PostgresCollection.Name)]
public sealed class ExtractosResumenConsultasTests
{
    private readonly PostgresFixture _fixture;

    public ExtractosResumenConsultasTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetCuentasTitular_No_Escala_Las_Consultas_Con_El_Numero_De_Cuentas()
    {
        var contador = new SqlContadorInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(contador)
            .Options;

        var userId = Guid.NewGuid();
        var titularConCincoCuentas = Guid.NewGuid();
        var titularConUnaCuenta = Guid.NewGuid();
        var paisId = Guid.NewGuid();

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.MigrateAsync();

            setup.Usuarios.Add(new Usuario
            {
                Id = userId,
                Email = "r7.resumen@test.local",
                PasswordHash = "hash",
                NombreCompleto = "Admin Resumen R7",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                PrimerLogin = false
            });
            setup.Paises.Add(new Pais { Id = paisId, Nombre = "Pais R7", CodigoIso2 = "ZZ", Activo = true });
            setup.Titulares.AddRange(
                new Titular { Id = titularConCincoCuentas, Nombre = "Titular R7 Cinco", Tipo = TipoTitular.EMPRESA },
                new Titular { Id = titularConUnaCuenta, Nombre = "Titular R7 Uno", Tipo = TipoTitular.EMPRESA });

            var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var fueraDePeriodo = hoy.AddMonths(-6);

            // Un titular con una cuenta y otro con cinco: si el numero de
            // consultas depende de las cuentas, la diferencia se ve.
            for (var i = 0; i < 5; i++)
            {
                var titularId = i == 0 ? titularConUnaCuenta : titularConCincoCuentas;
                var cuentaId = Guid.NewGuid();
                setup.Cuentas.Add(new Cuenta
                {
                    Id = cuentaId,
                    TitularId = titularId,
                    Nombre = $"Cuenta R7 {i}",
                    Divisa = "EUR",
                    Activa = true,
                    PaisId = paisId,
                    BancoNombre = "Atlas Bank",
                    Iban = "ES9121000418450200051332"
                });
                setup.Extractos.AddRange(
                    new Extracto
                    {
                        Id = Guid.NewGuid(),
                        CuentaId = cuentaId,
                        Fecha = fueraDePeriodo,
                        Concepto = "Fuera de periodo",
                        Monto = 1000m,
                        Saldo = 1000m,
                        FilaNumero = 1
                    },
                    new Extracto
                    {
                        Id = Guid.NewGuid(),
                        CuentaId = cuentaId,
                        Fecha = hoy,
                        Concepto = "Egreso",
                        Monto = -100m * (i + 1),
                        Saldo = 900m,
                        FilaNumero = 2
                    },
                    new Extracto
                    {
                        Id = Guid.NewGuid(),
                        CuentaId = cuentaId,
                        Fecha = hoy,
                        Concepto = "Ingreso",
                        Monto = 50m * (i + 1),
                        Saldo = 950m,
                        FilaNumero = 3
                    });
            }
            await setup.SaveChangesAsync();
        }

        var consultasConCincoCuentas = await MedirConsultas(options, contador, titularConCincoCuentas, userId, cuentasEsperadas: 4);
        var consultasConUnaCuenta = await MedirConsultas(options, contador, titularConUnaCuenta, userId, cuentasEsperadas: 1);

        // El numero de consultas no puede crecer con el numero de cuentas:
        // antes eran ~5 por cuenta (20 con cuatro cuentas) mas la constante.
        consultasConCincoCuentas.Should().BeLessThanOrEqualTo(
            consultasConUnaCuenta + 3,
            "las consultas no pueden escalar con el numero de cuentas. SQL capturada: " + string.Join(" | ", contador.Sql));

        consultasConCincoCuentas.Should().BeLessThanOrEqualTo(
            15,
            "el resumen por titular debe cabe en un numero acotado de consultas. SQL capturada: " + string.Join(" | ", contador.Sql));
    }

    [Fact]
    public async Task GetCuentasTitular_Debe_Devolver_Los_Mismos_Importes_Que_El_Resumen_Por_Cuenta()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        var userId = Guid.NewGuid();
        var titularId = Guid.NewGuid();

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.MigrateAsync();

            setup.Usuarios.Add(new Usuario
            {
                Id = userId,
                Email = "r7.importes@test.local",
                PasswordHash = "hash",
                NombreCompleto = "Admin Importes R7",
                Rol = RolUsuario.ADMIN,
                Activo = true,
                PrimerLogin = false
            });
            setup.Titulares.Add(new Titular { Id = titularId, Nombre = "Titular Importes R7", Tipo = TipoTitular.EMPRESA });

            var cuentaId = Guid.NewGuid();
            setup.Cuentas.Add(new Cuenta
            {
                Id = cuentaId,
                TitularId = titularId,
                Nombre = "Cuenta Importes R7",
                Divisa = "EUR",
                Activa = true,
                BancoNombre = "Atlas Bank",
                Iban = "ES9121000418450200051332"
            });

            var hoy = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            setup.Extractos.AddRange(
                new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaId, Fecha = hoy.AddMonths(-6), Monto = 1000m, Saldo = 1000m, FilaNumero = 1 },
                new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaId, Fecha = hoy, Monto = -100m, Saldo = 900m, FilaNumero = 2 },
                new Extracto { Id = Guid.NewGuid(), CuentaId = cuentaId, Fecha = hoy, Monto = 50m, Saldo = 950m, FilaNumero = 3 });
            await setup.SaveChangesAsync();
        }

        await using var db = new AppDbContext(options);
        var controller = BuildController(db, userId);
        var titular = await controller.GetCuentasTitular(titularId, "1m", null, CancellationToken.None);
        var titularOk = titular.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<TitularConCuentasResponse>().Subject;
        var resumen = titularOk.Cuentas.Should().ContainSingle().Subject;

        var cuenta = await controller.GetCuentaResumen(resumen.CuentaId, "1m", null, CancellationToken.None);
        var cuentaOk = cuenta.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<CuentaResumenKpiResponse>().Subject;

        resumen.SaldoActual.Should().Be(cuentaOk.SaldoActual);
        resumen.IngresosMes.Should().Be(cuentaOk.IngresosMes);
        resumen.EgresosMes.Should().Be(cuentaOk.EgresosMes);
        resumen.UltimaActualizacion.Should().Be(cuentaOk.UltimaActualizacion);
        resumen.Iban.Should().Be(cuentaOk.Iban);

        // Los valores esperados: el extracto de hace 6 meses queda fuera del
        // periodo de 1 mes, que se ancla al ultimo movimiento.
        cuentaOk.SaldoActual.Should().Be(950m);
        cuentaOk.IngresosMes.Should().Be(50m);
        cuentaOk.EgresosMes.Should().Be(100m);
    }

    private async Task<int> MedirConsultas(
        DbContextOptions<AppDbContext> options,
        SqlContadorInterceptor contador,
        Guid titularId,
        Guid userId,
        int cuentasEsperadas)
    {
        contador.Reiniciar();

        await using var db = new AppDbContext(options);
        var controller = BuildController(db, userId);
        var result = await controller.GetCuentasTitular(titularId, "1m", null, CancellationToken.None);
        result.Should().BeOfType<OkObjectResult>();

        var titularOk = ((OkObjectResult)result).Value.Should().BeOfType<TitularConCuentasResponse>().Subject;
        titularOk.Cuentas.Should().HaveCount(cuentasEsperadas);

        return contador.Selects;
    }

    private static ExtractosController BuildController(AppDbContext db, Guid userId)
    {
        var controller = new ExtractosController(db, new NoOpAlertaService(), NullLogger<ExtractosController>.Instance);
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

    private sealed class SqlContadorInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _sql = [];

        public int Selects => _sql.Count;

        public IReadOnlyList<string> Sql => _sql;

        public void Reiniciar() => _sql.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Registrar(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Registrar(command.CommandText);
            return ValueTask.FromResult(result);
        }

        private void Registrar(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return;
            var normalizado = sql.Replace("\"", string.Empty).ToLowerInvariant();
            var limpio = System.Text.RegularExpressions.Regex.Replace(normalizado, @"\s+", " ").Trim();
            if (!limpio.StartsWith("select", StringComparison.Ordinal)) return;
            _sql.Add(limpio);
        }
    }

    private sealed class NoOpAlertaService : IAlertaService
    {
        public Task EvaluateSaldoPostAsync(Guid cuentaId, Guid? actorUserId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AlertaActivaItemResponse>> GetAlertasActivasAsync(UserAccessScope scope, Guid? paisId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AlertaActivaItemResponse>>([]);
    }
}
