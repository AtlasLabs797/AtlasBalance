using FluentAssertions;
using AtlasBalance.API.Caching;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// LOTE 6 del informe de revision integral (2026-11-16).
///
/// S-3: revertir un lote de importacion es un borrado masivo de extractos.
/// Hoy solo exigia `puede_aprobar_importaciones || puede_importar`, asi que
/// quien solo podia importar tambien podia borrar el lote entero. Ahora exige
/// `puede_eliminar_lineas`.
///
/// S-9/M-3: las reglas de permiso por accion viven en UserAccessService.
/// ConciliacionService e ImportacionService deben consultarlas, no
/// reimplementarlas. Estos tests fijan el comportamiento observable (que no
/// cambia) y la definicion unica de las reglas.
/// </summary>
public class PermisosLote6Tests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static IUserAccessService BuildUserAccessService(AppDbContext db)
    {
        return new UserAccessService(
            db,
            new CacheService(new MemoryCache(new MemoryCacheOptions()), NullLogger<CacheService>.Instance),
            Options.Create(new CachingOptions()));
    }

    private static IImportacionService BuildImportacionService(AppDbContext db, IUserAccessService? userAccess = null)
    {
        return new ImportacionService(db, TestAuditService.Create(db), userAccess ?? BuildUserAccessService(db));
    }

    private static IConciliacionService BuildConciliacionService(AppDbContext db, IUserAccessService? userAccess = null)
    {
        return new ConciliacionService(db, TestAuditService.Create(db), userAccess ?? BuildUserAccessService(db));
    }

    private static ClaimsPrincipal PrincipalFor(Guid userId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, nameof(RolUsuario.EMPLEADO))
        ], "TestAuth"));

    // -----------------------------------------------------------------------
    // S-3: la reversion exige `puede_eliminar_lineas`.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task RevertirLoteAsync_Con_Usuario_Que_Solo_Puede_Importar_Debe_Devolver_403_Y_No_Borrar_Extractos()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var (cuentaId, loteId) = await SeedLoteConfirmadoAsync(db, userId);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var service = BuildImportacionService(db);

        var act = () => service.RevertirLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            loteId,
            new ImportacionLoteRevertirRequest { Motivo = "prueba" },
            new DefaultHttpContext(),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ImportacionException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var extractos = await db.Extractos.IgnoreQueryFilters().Where(e => e.ImportacionLoteId == loteId).ToListAsync();
        extractos.Should().NotBeEmpty();
        extractos.Should().OnlyContain(e => e.DeletedAt == null, "un 403 no puede borrar ningun extracto");

        var lote = await db.ImportacionLotes.FirstAsync(l => l.Id == loteId);
        lote.Estado.Should().Be("confirmado");
    }

    [Fact]
    public async Task RevertirLoteAsync_Con_Permiso_EliminarLineas_Debe_Revertir_El_Lote()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var (cuentaId, loteId) = await SeedLoteConfirmadoAsync(db, userId);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeImportar = true,
            PuedeEliminarLineas = true
        });
        await db.SaveChangesAsync();

        var service = BuildImportacionService(db);

        var revertido = await service.RevertirLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            loteId,
            new ImportacionLoteRevertirRequest { Motivo = "prueba" },
            new DefaultHttpContext(),
            CancellationToken.None);

        revertido.Estado.Should().Be("revertido");

        var extractos = await db.Extractos.IgnoreQueryFilters().Where(e => e.ImportacionLoteId == loteId).ToListAsync();
        extractos.Should().OnlyContain(e => e.DeletedAt != null);

        var lote = await db.ImportacionLotes.FirstAsync(l => l.Id == loteId);
        lote.Estado.Should().Be("revertido");
    }

    [Fact]
    public async Task RevertirLoteAsync_Con_Lote_Inexistente_Debe_Devolver_404_Y_No_403()
    {
        // El 404 (lote inexistente) y el 403 (lote existente sin permiso) deben
        // seguir siendo distinguibles.
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var (cuentaId, _) = await SeedLoteConfirmadoAsync(db, userId);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var service = BuildImportacionService(db);

        var act = () => service.RevertirLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            Guid.NewGuid(),
            new ImportacionLoteRevertirRequest { Motivo = "prueba" },
            new DefaultHttpContext(),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ImportacionException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    // -----------------------------------------------------------------------
    // S-9/M-3: UserAccessService es la definicion unica de las reglas.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CanConciliarCuentaAsync_Con_Usuario_Que_Solo_Puede_EditarLineas_Debe_Ser_Falso()
    {
        // Conciliar es estricto: `puede_editar_lineas` NO da acceso a conciliar.
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var cuentaId = await SeedCuentaAsync(db, userId, puedeEditarLineas: true);

        var service = BuildUserAccessService(db);
        var scope = await service.GetScopeAsync(PrincipalFor(userId), CancellationToken.None);

        (await service.CanConciliarCuentaAsync(cuentaId, scope, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task CanCerrarConciliacionAsync_Con_Usuario_Que_Solo_Puede_Conciliar_Debe_Ser_Falso()
    {
        // Cerrar es estricto: `puede_conciliar` NO da acceso a cerrar.
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var cuentaId = await SeedCuentaAsync(db, userId, puedeConciliar: true);

        var service = BuildUserAccessService(db);
        var scope = await service.GetScopeAsync(PrincipalFor(userId), CancellationToken.None);

        (await service.CanCerrarConciliacionAsync(cuentaId, scope, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task CanApproveImportacionAsync_Con_Usuario_Que_Solo_Puede_Importar_Debe_Ser_True()
    {
        // Aprobar una importacion sigue siendo `puede_aprobar_importaciones ||
        // puede_importar`.
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var cuentaId = await SeedCuentaAsync(db, userId, puedeImportar: true);

        var service = BuildUserAccessService(db);
        var scope = await service.GetScopeAsync(PrincipalFor(userId), CancellationToken.None);

        (await service.CanApproveImportacionAsync(cuentaId, scope, CancellationToken.None)).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // No-regresion del alcance de conciliacion.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Usuario_Solo_Con_PuedeCerrarConciliacion_Entra_En_El_Alcance_General_Pero_No_En_Las_Sugerencias()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var cuentaId = await SeedCuentaAsync(db, userId, puedeCerrarConciliacion: true);
        await SeedMovimientoAsync(db, cuentaId, userId);

        var service = BuildConciliacionService(db);
        var rol = RolUsuario.EMPLEADO.ToString();

        // R-3 (seccion 9): el listado de movimientos esperados pasa a ser paginado.
        var movimientos = await service.ListarMovimientosEsperadosAsync(userId, rol, null, null, 1, 100, "fecha_esperada", "desc", CancellationToken.None);
        movimientos.Data.Should().ContainSingle("el alcance general incluye a quien puede cerrar");

        var sugerencias = await service.SugerirAsync(
            userId,
            rol,
            new ConciliacionSugerirRequest(),
            new DefaultHttpContext(),
            CancellationToken.None);
        sugerencias.MovimientosEvaluados.Should().Be(0, "sugerir exige `puede_conciliar`");

        var act = () => service.SugerirAsync(
            userId,
            rol,
            new ConciliacionSugerirRequest { CuentaId = cuentaId },
            new DefaultHttpContext(),
            CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ConciliacionException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Usuario_Con_PuedeConciliar_Puede_Conciliar_Pero_No_Cerrar()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var cuentaId = await SeedCuentaAsync(db, userId, puedeConciliar: true);
        var conciliacionId = await SeedConciliacionAsync(db, cuentaId, userId);

        var service = BuildConciliacionService(db);
        var rol = RolUsuario.EMPLEADO.ToString();

        var confirmada = await service.ConfirmarAsync(
            userId,
            rol,
            conciliacionId,
            new ConciliacionCambiarEstadoRequest { Observacion = "ok" },
            new DefaultHttpContext(),
            CancellationToken.None);
        confirmada.Estado.Should().Be("conciliada");

        var act = () => service.ResolverAsync(
            userId,
            rol,
            conciliacionId,
            new ConciliacionCambiarEstadoRequest { Observacion = "cierre" },
            new DefaultHttpContext(),
            CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ConciliacionException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Usuario_Solo_Con_PuedeEditarLineas_No_Concilia()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var cuentaId = await SeedCuentaAsync(db, userId, puedeEditarLineas: true);
        await SeedMovimientoAsync(db, cuentaId, userId);

        var service = BuildConciliacionService(db);
        var rol = RolUsuario.EMPLEADO.ToString();

        var act = () => service.SugerirAsync(
            userId,
            rol,
            new ConciliacionSugerirRequest { CuentaId = cuentaId },
            new DefaultHttpContext(),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ConciliacionException>();
        ex.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Admin_Puede_Conciliar_Y_Cerrar()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var cuentaId = await SeedCuentaAsync(db, userId);
        var conciliacionId = await SeedConciliacionAsync(db, cuentaId, userId);

        var service = BuildConciliacionService(db);
        var rol = RolUsuario.ADMIN.ToString();

        var confirmada = await service.ConfirmarAsync(
            userId,
            rol,
            conciliacionId,
            new ConciliacionCambiarEstadoRequest { Observacion = "ok" },
            new DefaultHttpContext(),
            CancellationToken.None);
        confirmada.Estado.Should().Be("conciliada");

        var resuelta = await service.ResolverAsync(
            userId,
            rol,
            conciliacionId,
            new ConciliacionCambiarEstadoRequest { Observacion = "cierre" },
            new DefaultHttpContext(),
            CancellationToken.None);
        resuelta.Estado.Should().Be("resuelta");
    }

    // -----------------------------------------------------------------------
    // S-9/M-3: los servicios consultan UserAccessService (no reimplementan la
    // regla). El espia deja constancia de que la consulta ocurre.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ConciliacionService_Consulta_La_Regla_De_Conciliar_En_UserAccessService()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var cuentaId = await SeedCuentaAsync(db, userId, puedeConciliar: true);
        var spy = new SpyUserAccessService(BuildUserAccessService(db));
        var service = BuildConciliacionService(db, spy);

        await service.SugerirAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            new ConciliacionSugerirRequest { CuentaId = cuentaId },
            new DefaultHttpContext(),
            CancellationToken.None);

        spy.MetodosConsultados.Should().Contain(nameof(IUserAccessService.CanConciliarCuentaAsync));
        spy.AccionesConsultadas.Should().Contain(UserAccessAction.Conciliar);
    }

    [Fact]
    public async Task ImportacionService_Consulta_La_Regla_De_EliminarLineas_En_UserAccessService()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var (cuentaId, loteId) = await SeedLoteConfirmadoAsync(db, userId);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuentaId,
            PuedeImportar = true
        });
        await db.SaveChangesAsync();

        var spy = new SpyUserAccessService(BuildUserAccessService(db));
        var service = BuildImportacionService(db, spy);

        var act = () => service.RevertirLoteAsync(
            userId,
            RolUsuario.EMPLEADO.ToString(),
            loteId,
            new ImportacionLoteRevertirRequest { Motivo = "prueba" },
            new DefaultHttpContext(),
            CancellationToken.None);

        await act.Should().ThrowAsync<ImportacionException>();
        spy.AccionesConsultadas.Should().Contain(UserAccessAction.EliminarLineas);
    }

    // -----------------------------------------------------------------------
    // Seeds
    // -----------------------------------------------------------------------
    private static async Task<Guid> SeedCuentaAsync(
        AppDbContext db,
        Guid usuarioId,
        bool puedeConciliar = false,
        bool puedeCerrarConciliacion = false,
        bool puedeEditarLineas = false,
        bool puedeImportar = false)
    {
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Lote 6", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Lote 6", Divisa = "EUR", Activa = true };
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            CuentaId = cuenta.Id,
            PuedeConciliar = puedeConciliar,
            PuedeCerrarConciliacion = puedeCerrarConciliacion,
            PuedeEditarLineas = puedeEditarLineas,
            PuedeImportar = puedeImportar
        });
        await db.SaveChangesAsync();
        return cuenta.Id;
    }

    private static async Task<(Guid CuentaId, Guid LoteId)> SeedLoteConfirmadoAsync(AppDbContext db, Guid usuarioId)
    {
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Lote 6", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Lote 6", Divisa = "EUR", Activa = true };
        var lote = new ImportacionLote
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            UsuarioCreadorId = usuarioId,
            Estado = "confirmado",
            Separador = "tab",
            MapeoJson = "{}",
            ResumenJson = "{}",
            ContenidoOriginal = "01/04/2026\tIngreso cliente\t100\t100",
            LoteHash = "hash-lote-6",
            Sha256 = "sha-lote-6",
            FilasTotal = 1,
            FilasValidas = 1,
            FechaConfirmacion = DateTime.UtcNow,
            ConfirmadoPorId = usuarioId
        };
        var extracto = new Extracto
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            Fecha = new DateOnly(2026, 4, 1),
            Concepto = "Ingreso cliente",
            Monto = 100m,
            Saldo = 100m,
            FilaNumero = 1,
            ImportacionLoteId = lote.Id,
            ImportacionLoteHash = lote.LoteHash,
            ImportacionFingerprint = "huella-lote-6"
        };

        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.ImportacionLotes.Add(lote);
        db.Extractos.Add(extracto);
        await db.SaveChangesAsync();
        return (cuenta.Id, lote.Id);
    }

    private static async Task SeedMovimientoAsync(AppDbContext db, Guid cuentaId, Guid usuarioId)
    {
        db.MovimientosEsperados.Add(new MovimientoEsperado
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            FechaEsperada = new DateOnly(2026, 4, 1),
            Monto = 100m,
            Divisa = "EUR",
            Concepto = "Ingreso cliente",
            Estado = "pendiente",
            UsuarioCreacionId = usuarioId
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedConciliacionAsync(AppDbContext db, Guid cuentaId, Guid usuarioId)
    {
        var movimiento = new MovimientoEsperado
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            FechaEsperada = new DateOnly(2026, 4, 1),
            Monto = 100m,
            Divisa = "EUR",
            Concepto = "Ingreso cliente",
            Estado = "sugerida",
            UsuarioCreacionId = usuarioId
        };
        var conciliacion = new Conciliacion
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            MovimientoEsperadoId = movimiento.Id,
            Estado = "sugerida",
            UsuarioSugerenciaId = usuarioId
        };
        db.MovimientosEsperados.Add(movimiento);
        db.Conciliaciones.Add(conciliacion);
        await db.SaveChangesAsync();
        return conciliacion.Id;
    }

    /// <summary>
    /// Espia sobre un UserAccessService real: delega todo y deja constancia de
    /// los metodos y acciones consultados. Sirve para comprobar que
    /// ConciliacionService e ImportacionService piden la regla a la fuente
    /// unica en vez de aplicarla por su cuenta.
    /// </summary>
    private sealed class SpyUserAccessService : IUserAccessService
    {
        private readonly IUserAccessService _inner;

        public SpyUserAccessService(IUserAccessService inner)
        {
            _inner = inner;
        }

        public List<string> MetodosConsultados { get; } = [];
        public List<UserAccessAction> AccionesConsultadas { get; } = [];

        public Task<UserAccessScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
            => _inner.GetScopeAsync(user, cancellationToken);

        public Task<UserAccessScope> GetScopeAsync(Guid usuarioId, string rol, CancellationToken cancellationToken)
            => _inner.GetScopeAsync(usuarioId, rol, cancellationToken);

        public IQueryable<Titular> ApplyTitularScope(IQueryable<Titular> query, UserAccessScope scope)
            => _inner.ApplyTitularScope(query, scope);

        public IQueryable<Cuenta> ApplyCuentaScope(IQueryable<Cuenta> query, UserAccessScope scope)
            => _inner.ApplyCuentaScope(query, scope);

        public Task<bool> CanAccessTitularAsync(Guid titularId, UserAccessScope scope, CancellationToken cancellationToken)
            => _inner.CanAccessTitularAsync(titularId, scope, cancellationToken);

        public Task<bool> CanAccessCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => _inner.CanAccessCuentaAsync(cuentaId, scope, cancellationToken);

        public Task<bool> CanWriteCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => _inner.CanWriteCuentaAsync(cuentaId, scope, cancellationToken);

        public Task<bool> CanEditCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => _inner.CanEditCuentaAsync(cuentaId, scope, cancellationToken);

        public Task<bool> CanReviewCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => _inner.CanReviewCuentaAsync(cuentaId, scope, cancellationToken);

        public Task<bool> CanApproveImportacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            MetodosConsultados.Add(nameof(CanApproveImportacionAsync));
            return _inner.CanApproveImportacionAsync(cuentaId, scope, cancellationToken);
        }

        public Task<bool> CanConciliarCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            MetodosConsultados.Add(nameof(CanConciliarCuentaAsync));
            return _inner.CanConciliarCuentaAsync(cuentaId, scope, cancellationToken);
        }

        public Task<bool> CanCerrarConciliacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            MetodosConsultados.Add(nameof(CanCerrarConciliacionAsync));
            return _inner.CanCerrarConciliacionAsync(cuentaId, scope, cancellationToken);
        }

        public Task<bool> CanCuentaAsync(UserAccessAction accion, Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
        {
            MetodosConsultados.Add(nameof(CanCuentaAsync));
            AccionesConsultadas.Add(accion);
            return _inner.CanCuentaAsync(accion, cuentaId, scope, cancellationToken);
        }

        public Task<IReadOnlySet<Guid>> GetCuentasAccesiblesAsync(UserAccessAction accion, Guid usuarioId, CancellationToken cancellationToken)
        {
            MetodosConsultados.Add(nameof(GetCuentasAccesiblesAsync));
            AccionesConsultadas.Add(accion);
            return _inner.GetCuentasAccesiblesAsync(accion, usuarioId, cancellationToken);
        }
    }
}
