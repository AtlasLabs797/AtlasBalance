using FluentAssertions;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Middleware;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class IntegrationOpenClawControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static IntegrationOpenClawController BuildController(AppDbContext dbContext, IntegrationToken token)
    {
        var controller = new IntegrationOpenClawController(
            dbContext,
            new IntegrationAuthorizationService(dbContext),
            new TiposCambioServiceStub());

        var httpContext = new DefaultHttpContext();
        httpContext.Items[IntegrationHttpContextItemKeys.CurrentIntegrationToken] = token;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    [Fact]
    public async Task Titulares_Should_Reject_Token_With_WriteOnly_Scope()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Empresa", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta", Divisa = "EUR" };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            PermisoEscritura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "escritura"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.Titulares("full", CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        JsonSerializer.Serialize(objectResult.Value).Should().Contain("FORBIDDEN");
    }

    [Fact]
    public async Task Extractos_Should_Return_Derived_TipoMovimiento_Inside_Wrapped_Response()
    {
        await using var db = BuildDbContext();
        var creador = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "user@test.local",
            PasswordHash = "hash",
            NombreCompleto = "User",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        };
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Empresa", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta", Divisa = "EUR" };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = creador.Id
        };

        db.Usuarios.Add(creador);
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "lectura"
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                Fecha = new DateOnly(2026, 4, 10),
                Concepto = "Ingreso",
                Monto = 50m,
                Saldo = 50m,
                FilaNumero = 1,
                UsuarioCreacionId = creador.Id,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-5)
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                Fecha = new DateOnly(2026, 4, 11),
                Concepto = "Pago",
                Monto = -20m,
                Saldo = 30m,
                FilaNumero = 2,
                UsuarioCreacionId = creador.Id,
                FechaCreacion = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.Extractos("full", cuenta.Id, null, null, null, 100, 1, "fecha", "asc", CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.GetType().GetProperty("Exito")!.GetValue(okResult.Value).Should().Be(true);

        var payload = JsonSerializer.Serialize(okResult.Value);
        payload.Should().Contain("tipo_movimiento");
        payload.Should().Contain("INGRESO");
        payload.Should().Contain("EGRESO");
    }

    [Fact]
    public async Task Saldos_Should_Use_Highest_FilaNumero_As_CurrentSaldo()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Empresa", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta", Divisa = "EUR" };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "lectura"
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                Fecha = new DateOnly(2026, 5, 10),
                Concepto = "Movimiento con fecha posterior",
                Monto = 20m,
                Saldo = 20m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                Fecha = new DateOnly(2026, 4, 30),
                Concepto = "Movimiento importado ultimo",
                Monto = 80m,
                Saldo = 100m,
                FilaNumero = 2
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.Saldos("full", null, null, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(okResult.Value));
        var cuentaJson = document.RootElement
            .GetProperty("Datos")
            .GetProperty("cuentas")
            .EnumerateArray()
            .Single();
        cuentaJson.GetProperty("saldo_actual").GetDecimal().Should().Be(100m);
    }

    [Fact]
    public async Task Auditoria_Should_Not_Return_Values_For_SoftDeleted_Extractos()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Empresa", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta", Divisa = "EUR" };
        var activeExtractoId = Guid.NewGuid();
        var deletedExtractoId = Guid.NewGuid();
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "lectura"
        });
        db.Extractos.AddRange(
            new Extracto
            {
                Id = activeExtractoId,
                CuentaId = cuenta.Id,
                Fecha = new DateOnly(2026, 5, 1),
                Concepto = "Visible",
                Monto = 10m,
                Saldo = 10m,
                FilaNumero = 1
            },
            new Extracto
            {
                Id = deletedExtractoId,
                CuentaId = cuenta.Id,
                Fecha = new DateOnly(2026, 5, 2),
                Concepto = "Eliminado",
                Monto = 20m,
                Saldo = 30m,
                FilaNumero = 2,
                DeletedAt = DateTime.UtcNow
            });
        db.Auditorias.AddRange(
            new Auditoria
            {
                Id = Guid.NewGuid(),
                TipoAccion = "extracto_update",
                EntidadTipo = "EXTRACTOS",
                EntidadId = activeExtractoId,
                ValorAnterior = "VisibleAnterior",
                ValorNuevo = "VisibleNuevo",
                Timestamp = DateTime.UtcNow
            },
            new Auditoria
            {
                Id = Guid.NewGuid(),
                TipoAccion = "extracto_update",
                EntidadTipo = "EXTRACTOS",
                EntidadId = deletedExtractoId,
                ValorAnterior = "DeletedAnterior",
                ValorNuevo = "DeletedNuevo",
                Timestamp = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.Auditoria("full", cuenta.Id, null, null, null, "all", 100, 1, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = JsonSerializer.Serialize(okResult.Value);
        payload.Should().Contain("VisibleNuevo");
        payload.Should().NotContain("DeletedNuevo");
        payload.Should().NotContain("DeletedAnterior");
    }

    [Fact]
    public async Task Saldos_Should_Not_Expose_Full_Iban()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Empresa", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta
        {
            Id = Guid.NewGuid(),
            TitularId = titular.Id,
            Nombre = "Cuenta",
            Iban = "ES9121000418450200051332",
            Divisa = "EUR"
        };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "lectura"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.Saldos("full", null, null, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = JsonSerializer.Serialize(okResult.Value);
        // V-02-07: el IBAN no se enmascara para la integracion externa, no se envia.
        payload.Should().NotContain("ES9121000418450200051332");
        payload.Should().NotContain("1332");
        payload.Should().NotContain("iban");
    }

    [Fact]
    public async Task Extractos_Should_Not_Expose_Usuario_Creacion()
    {
        await using var db = BuildDbContext();
        var creador = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "empleado.interno@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Empleado Interno Nombre Completo",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        };
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Empresa", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta", Divisa = "EUR" };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = creador.Id
        };

        db.Usuarios.Add(creador);
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "lectura"
        });
        db.Extractos.Add(new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = new DateOnly(2026, 4, 10),
            Concepto = "Ingreso",
            Monto = 50m,
            Saldo = 50m,
            FilaNumero = 1,
            UsuarioCreacionId = creador.Id,
            FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.Extractos("full", cuenta.Id, null, null, null, 100, 1, "fecha", "asc", CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = JsonSerializer.Serialize(okResult.Value);
        payload.Should().NotContain("usuario_creacion");
        payload.Should().NotContain("Empleado Interno Nombre Completo");
    }

    [Fact]
    public async Task Titulares_Should_Return_Pseudonym_Not_Real_Names()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Nombre Real Titular", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Nombre Real Cuenta", Divisa = "EUR" };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "lectura"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.Titulares("full", CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = JsonSerializer.Serialize(okResult.Value);

        payload.Should().Contain(IntegrationPseudonyms.ForTitular(titular.Id));
        payload.Should().Contain(IntegrationPseudonyms.ForCuenta(cuenta.Id));
        payload.Should().NotContain("Nombre Real Titular");
        payload.Should().NotContain("Nombre Real Cuenta");
    }

    [Fact]
    public async Task Saldos_Should_Return_Pseudonym_Not_Real_Names()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Nombre Real Titular Saldos", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Nombre Real Cuenta Saldos", Divisa = "EUR" };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "lectura"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.Saldos("full", null, null, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = JsonSerializer.Serialize(okResult.Value);

        payload.Should().Contain(IntegrationPseudonyms.ForTitular(titular.Id));
        payload.Should().Contain(IntegrationPseudonyms.ForCuenta(cuenta.Id));
        payload.Should().NotContain("Nombre Real Titular Saldos");
        payload.Should().NotContain("Nombre Real Cuenta Saldos");
    }

    [Fact]
    public async Task Pseudonym_Should_Be_Stable_Across_Consecutive_Calls()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Empresa Estable", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Estable", Divisa = "EUR" };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "lectura"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var firstCall = await controller.Titulares("full", CancellationToken.None);
        var secondCall = await controller.Titulares("full", CancellationToken.None);

        var firstPayload = JsonSerializer.Serialize(firstCall.Should().BeOfType<OkObjectResult>().Subject.Value);
        var secondPayload = JsonSerializer.Serialize(secondCall.Should().BeOfType<OkObjectResult>().Subject.Value);

        using var firstDoc = JsonDocument.Parse(firstPayload);
        using var secondDoc = JsonDocument.Parse(secondPayload);

        var firstNombre = firstDoc.RootElement.GetProperty("Datos").GetProperty("titulares")[0].GetProperty("nombre").GetString();
        var secondNombre = secondDoc.RootElement.GetProperty("Datos").GetProperty("titulares")[0].GetProperty("nombre").GetString();

        firstNombre.Should().NotBeNullOrEmpty();
        firstNombre.Should().Be(secondNombre);
        firstNombre.Should().Be(IntegrationPseudonyms.ForTitular(titular.Id));
    }

    [Fact]
    public async Task ResolverNombres_Should_Return_RealName_For_Entity_In_Scope()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular En Scope", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta En Scope", Divisa = "EUR" };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "lectura"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.ResolverNombres(
            new ResolverNombresRequest
            {
                TitularIds = [titular.Id],
                CuentaIds = [cuenta.Id]
            },
            CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<IntegrationApiResponse<ResolverNombresResponse>>().Subject;

        response.Datos.Should().NotBeNull();
        response.Datos!.Titulares.Should().ContainSingle(x => x.Id == titular.Id && x.Nombre == "Titular En Scope");
        response.Datos!.Cuentas.Should().ContainSingle(x => x.Id == cuenta.Id && x.Nombre == "Cuenta En Scope");
    }

    [Fact]
    public async Task ResolverNombres_Should_Not_Return_Entity_Out_Of_Scope_And_Should_Return_200()
    {
        await using var db = BuildDbContext();
        var titularEnScope = new Titular { Id = Guid.NewGuid(), Nombre = "Titular En Scope", Tipo = TipoTitular.EMPRESA };
        var cuentaEnScope = new Cuenta { Id = Guid.NewGuid(), TitularId = titularEnScope.Id, Nombre = "Cuenta En Scope", Divisa = "EUR" };
        var titularFueraDeScope = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Fuera De Scope", Tipo = TipoTitular.EMPRESA };
        var cuentaFueraDeScope = new Cuenta { Id = Guid.NewGuid(), TitularId = titularFueraDeScope.Id, Nombre = "Cuenta Fuera De Scope", Divisa = "EUR" };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.Titulares.AddRange(titularEnScope, titularFueraDeScope);
        db.Cuentas.AddRange(cuentaEnScope, cuentaFueraDeScope);
        db.IntegrationTokens.Add(token);
        // El token solo tiene permiso de lectura sobre cuentaEnScope/titularEnScope.
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuentaEnScope.Id,
            AccesoTipo = "lectura"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var result = await controller.ResolverNombres(
            new ResolverNombresRequest
            {
                TitularIds = [titularEnScope.Id, titularFueraDeScope.Id],
                CuentaIds = [cuentaEnScope.Id, cuentaFueraDeScope.Id]
            },
            CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK); // no distincion de "no existe" vs "no autorizado": 200 siempre.
        var response = okResult.Value.Should().BeOfType<IntegrationApiResponse<ResolverNombresResponse>>().Subject;

        response.Datos!.Titulares.Should().ContainSingle(x => x.Id == titularEnScope.Id);
        response.Datos!.Titulares.Should().NotContain(x => x.Id == titularFueraDeScope.Id);
        response.Datos!.Cuentas.Should().ContainSingle(x => x.Id == cuentaEnScope.Id);
        response.Datos!.Cuentas.Should().NotContain(x => x.Id == cuentaFueraDeScope.Id);
    }

    [Fact]
    public async Task ResolverNombres_Should_Reject_Batch_Over_200_Ids()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Empresa", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta", Divisa = "EUR" };
        var token = new IntegrationToken
        {
            Id = Guid.NewGuid(),
            Nombre = "token",
            TokenHash = "hash",
            PermisoLectura = true,
            Estado = EstadoTokenIntegracion.Activo,
            UsuarioCreadorId = Guid.NewGuid()
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.IntegrationTokens.Add(token);
        db.IntegrationPermissions.Add(new IntegrationPermission
        {
            Id = Guid.NewGuid(),
            TokenId = token.Id,
            CuentaId = cuenta.Id,
            AccesoTipo = "lectura"
        });
        await db.SaveChangesAsync();

        var controller = BuildController(db, token);

        var muchosTitularIds = Enumerable.Range(0, 150).Select(_ => Guid.NewGuid()).ToList();
        var muchosCuentaIds = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList();

        var result = await controller.ResolverNombres(
            new ResolverNombresRequest
            {
                TitularIds = muchosTitularIds,
                CuentaIds = muchosCuentaIds
            },
            CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        JsonSerializer.Serialize(objectResult.Value).Should().Contain("BAD_REQUEST");
    }

    private sealed class TiposCambioServiceStub : ITiposCambioService
    {
        public Task<decimal> ConvertAsync(decimal amount, string divisaOrigen, string divisaDestino, CancellationToken cancellationToken)
            => Task.FromResult(amount);

        public Task<decimal?> TryConvertAsync(decimal amount, string divisaOrigen, string divisaDestino, CancellationToken cancellationToken)
            => Task.FromResult<decimal?>(amount);

        public Task<IReadOnlyDictionary<string, decimal?>> BulkConvertAsync(
            IReadOnlyDictionary<string, decimal> amountsBySource,
            string divisaDestino,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, decimal?>>(
                amountsBySource.ToDictionary(x => x.Key, x => (decimal?)x.Value, StringComparer.OrdinalIgnoreCase));

        public Task<DivisaActivaDto> ActualizarDivisaAsync(string codigo, string? nombre, string? simbolo, bool activa, bool esBase, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<DivisaActivaDto> CrearDivisaAsync(string codigo, string? nombre, string? simbolo, bool activa, bool esBase, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TipoCambioDto> GuardarTipoCambioManualAsync(string divisaOrigen, string divisaDestino, decimal tasa, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DivisaActivaDto>> ListarDivisasAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TipoCambioDto>> ListarTiposCambioAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<SyncTiposCambioResult> SincronizarTiposCambioAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
