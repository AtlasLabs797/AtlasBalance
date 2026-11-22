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
/// R-3 (seccion 9 del informe de revision): contrato de la API.
///
/// Los listados paginados devuelven <c>PaginatedResponse&lt;T&gt;</c>, que el
/// serializador de la API (SnakeCaseLower) convierte a
/// <c>{ data, total, page, page_size, total_pages }</c>, y admiten
/// <c>?sortBy=campo&amp;sortDir=asc|desc</c>.
///
/// Estos tests comprueban las tres cosas que pedia la seccion 9:
/// la paginacion con el total real, la ordenacion por <c>sortBy</c>/<c>sortDir</c>
/// y que el caso por defecto de la ordenacion respete el sentido pedido.
/// </summary>
public sealed class ContratoApiPaginacionTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    // -----------------------------------------------------------------------
    // GET /api/importacion/lotes/{id}/filas
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListarLoteFilas_Should_Paginar_Y_Traer_El_Total_Real_Del_Lote()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Contrato", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Contrato", Divisa = "EUR", Activa = true };
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            PuedeVerCuentas = true,
            PuedeImportar = true
        });

        var lote = new ImportacionLote
        {
            Id = Guid.NewGuid(),
            CuentaId = cuenta.Id,
            UsuarioCreadorId = userId,
            Estado = "validado"
        };
        db.ImportacionLotes.Add(lote);
        db.ImportacionLoteFilas.AddRange(
            new ImportacionLoteFila { Id = Guid.NewGuid(), LoteId = lote.Id, Indice = 1, Valida = true, Estado = "validada" },
            new ImportacionLoteFila { Id = Guid.NewGuid(), LoteId = lote.Id, Indice = 2, Valida = true, Estado = "validada" },
            new ImportacionLoteFila { Id = Guid.NewGuid(), LoteId = lote.Id, Indice = 3, Valida = false, Estado = "error" });
        await db.SaveChangesAsync();

        var service = new ImportacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
        var rol = RolUsuario.EMPLEADO.ToString();

        var primera = await service.ListarLoteFilasAsync(userId, rol, lote.Id, 1, 2, "indice", "asc", CancellationToken.None);

        primera.Data.Should().HaveCount(2);
        primera.Total.Should().Be(3, "el total cuenta todas las filas del lote, no solo las de la pagina");
        primera.TotalPages.Should().Be(2);
        primera.Page.Should().Be(1);
        primera.PageSize.Should().Be(2);
        primera.Data.Select(x => x.Indice).Should().Equal(1, 2);

        var segunda = await service.ListarLoteFilasAsync(userId, rol, lote.Id, 2, 2, "indice", "asc", CancellationToken.None);

        segunda.Data.Should().ContainSingle();
        segunda.Data[0].Indice.Should().Be(3);
        segunda.Total.Should().Be(3);
        segunda.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task ListarLoteFilas_Con_SortDir_Desc_Debe_Invertir_El_Orden()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Orden", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Orden", Divisa = "EUR", Activa = true };
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            PuedeImportar = true
        });

        var lote = new ImportacionLote { Id = Guid.NewGuid(), CuentaId = cuenta.Id, UsuarioCreadorId = userId };
        db.ImportacionLotes.Add(lote);
        db.ImportacionLoteFilas.AddRange(
            new ImportacionLoteFila { Id = Guid.NewGuid(), LoteId = lote.Id, Indice = 1 },
            new ImportacionLoteFila { Id = Guid.NewGuid(), LoteId = lote.Id, Indice = 2 },
            new ImportacionLoteFila { Id = Guid.NewGuid(), LoteId = lote.Id, Indice = 3 });
        await db.SaveChangesAsync();

        var service = new ImportacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
        var rol = RolUsuario.EMPLEADO.ToString();

        var asc = await service.ListarLoteFilasAsync(userId, rol, lote.Id, 1, 3, "indice", "asc", CancellationToken.None);
        var desc = await service.ListarLoteFilasAsync(userId, rol, lote.Id, 1, 3, "indice", "desc", CancellationToken.None);

        asc.Data.Select(x => x.Indice).Should().Equal(1, 2, 3);
        desc.Data.Select(x => x.Indice).Should().Equal(3, 2, 1);
    }

    // -----------------------------------------------------------------------
    // GET /api/importacion/lotes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListarLotes_Should_Admitir_SortBy_Y_SortDir_Y_Mantener_Total_Y_TotalPages()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Lotes", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Lotes", Divisa = "EUR", Activa = true };
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            CuentaId = cuenta.Id,
            PuedeVerCuentas = true,
            PuedeImportar = true
        });

        db.ImportacionLotes.AddRange(
            new ImportacionLote { Id = Guid.NewGuid(), CuentaId = cuenta.Id, UsuarioCreadorId = userId, NombreArchivo = "b.csv", FechaCreacion = new DateTime(2026, 3, 2) },
            new ImportacionLote { Id = Guid.NewGuid(), CuentaId = cuenta.Id, UsuarioCreadorId = userId, NombreArchivo = "a.csv", FechaCreacion = new DateTime(2026, 3, 1) },
            new ImportacionLote { Id = Guid.NewGuid(), CuentaId = cuenta.Id, UsuarioCreadorId = userId, NombreArchivo = "c.csv", FechaCreacion = new DateTime(2026, 3, 3) });
        await db.SaveChangesAsync();

        var service = new ImportacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
        var rol = RolUsuario.EMPLEADO.ToString();

        var porNombreAsc = await service.ListarLotesAsync(userId, rol, cuenta.Id, 1, 10, "nombre_archivo", "asc", CancellationToken.None);
        var porNombreDesc = await service.ListarLotesAsync(userId, rol, cuenta.Id, 1, 10, "nombre_archivo", "desc", CancellationToken.None);

        porNombreAsc.Data.Select(x => x.NombreArchivo).Should().Equal("a.csv", "b.csv", "c.csv");
        porNombreDesc.Data.Select(x => x.NombreArchivo).Should().Equal("c.csv", "b.csv", "a.csv");
        porNombreAsc.Total.Should().Be(3);
        porNombreAsc.TotalPages.Should().Be(1);

        var paginada = await service.ListarLotesAsync(userId, rol, cuenta.Id, 2, 2, "nombre_archivo", "asc", CancellationToken.None);
        paginada.Data.Should().ContainSingle();
        paginada.Data[0].NombreArchivo.Should().Be("c.csv");
        paginada.Total.Should().Be(3);
        paginada.TotalPages.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // GET /api/conciliacion
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListarConciliaciones_Should_Paginar_Y_Traer_El_Total_Real()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId, _) = await SeedConciliacionAsync(db);

        var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
        var rol = RolUsuario.EMPLEADO.ToString();

        var primera = await service.ListarConciliacionesAsync(userId, rol, null, null, 1, 2, "fecha_creacion", "desc", CancellationToken.None);

        primera.Data.Should().HaveCount(2);
        primera.Total.Should().Be(3, "el recorte silencioso de 500 filas ha desaparecido: el total es el real");
        primera.TotalPages.Should().Be(2);
        primera.Page.Should().Be(1);
        primera.PageSize.Should().Be(2);

        var segunda = await service.ListarConciliacionesAsync(userId, rol, null, null, 2, 2, "fecha_creacion", "desc", CancellationToken.None);
        segunda.Data.Should().ContainSingle();
        segunda.Total.Should().Be(3);
        segunda.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task ListarConciliaciones_Debe_Mantener_Los_Filtros_De_Cuenta_Y_De_Estado()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId, otraCuentaId) = await SeedConciliacionAsync(db);

        var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
        var rol = RolUsuario.EMPLEADO.ToString();

        var porCuenta = await service.ListarConciliacionesAsync(userId, rol, cuentaId, null, 1, 50, "fecha_creacion", "desc", CancellationToken.None);
        porCuenta.Total.Should().Be(2);
        porCuenta.Data.Should().OnlyContain(x => x.CuentaId == cuentaId);

        var porEstado = await service.ListarConciliacionesAsync(userId, rol, null, "conciliada", 1, 50, "fecha_creacion", "desc", CancellationToken.None);
        porEstado.Total.Should().Be(1);
        porEstado.Data.Should().ContainSingle();
        porEstado.Data[0].CuentaId.Should().Be(otraCuentaId);
        porEstado.Data[0].Estado.Should().Be("conciliada");
    }

    [Fact]
    public async Task ListarConciliaciones_Con_SortDir_Desc_Debe_Invertir_El_Orden()
    {
        await using var db = BuildDbContext();
        var (userId, _, _) = await SeedConciliacionAsync(db);

        var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
        var rol = RolUsuario.EMPLEADO.ToString();

        var asc = await service.ListarConciliacionesAsync(userId, rol, null, null, 1, 10, "fecha_creacion", "asc", CancellationToken.None);
        var desc = await service.ListarConciliacionesAsync(userId, rol, null, null, 1, 10, "fecha_creacion", "desc", CancellationToken.None);

        asc.Data.Select(x => x.FechaCreacion).Should().BeInAscendingOrder();
        desc.Data.Select(x => x.FechaCreacion).Should().BeInDescendingOrder();
        asc.Data.First().Id.Should().Be(desc.Data.Last().Id);
    }

    // -----------------------------------------------------------------------
    // GET /api/conciliacion/movimientos-esperados
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListarMovimientosEsperados_Should_Paginar_Y_Traer_El_Total_Real()
    {
        await using var db = BuildDbContext();
        var (userId, cuentaId, _) = await SeedConciliacionAsync(db);

        var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
        var rol = RolUsuario.EMPLEADO.ToString();

        var primera = await service.ListarMovimientosEsperadosAsync(userId, rol, null, null, 1, 2, "fecha_esperada", "desc", CancellationToken.None);

        primera.Data.Should().HaveCount(2);
        primera.Total.Should().Be(3);
        primera.TotalPages.Should().Be(2);
        primera.Page.Should().Be(1);
        primera.PageSize.Should().Be(2);

        var segunda = await service.ListarMovimientosEsperadosAsync(userId, rol, null, null, 2, 2, "fecha_esperada", "desc", CancellationToken.None);
        segunda.Data.Should().ContainSingle();
        segunda.Total.Should().Be(3);
        segunda.TotalPages.Should().Be(2);

        var porCuenta = await service.ListarMovimientosEsperadosAsync(userId, rol, cuentaId, null, 1, 50, "fecha_esperada", "desc", CancellationToken.None);
        porCuenta.Total.Should().Be(2);
        porCuenta.Data.Should().OnlyContain(x => x.CuentaId == cuentaId);

        var porEstado = await service.ListarMovimientosEsperadosAsync(userId, rol, null, "pendiente", 1, 50, "fecha_esperada", "desc", CancellationToken.None);
        porEstado.Total.Should().Be(2);
        porEstado.Data.Should().HaveCount(2);
        porEstado.Data.Should().OnlyContain(x => x.Estado == "pendiente");
    }

    /// <summary>
    /// El recorte silencioso de 500 filas solo se ve con mas de 500 filas: la
    /// segunda pagina tiene que venirLlena y el total tiene que ser el real.
    /// </summary>
    [Fact]
    public async Task ListarMovimientosEsperados_No_Trunca_En_Silencio_Y_Expone_El_Total_Real()
    {
        await using var db = BuildDbContext();
        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Volumen", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Volumen", Divisa = "EUR", Activa = true };
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            PuedeConciliar = true
        });

        for (var i = 0; i < 600; i++)
        {
            db.MovimientosEsperados.Add(new MovimientoEsperado
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                FechaEsperada = new DateOnly(2026, 1, 1).AddDays(i % 300),
                Monto = -1m,
                Divisa = "EUR",
                Estado = "pendiente"
            });
        }
        await db.SaveChangesAsync();

        var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
        var rol = RolUsuario.EMPLEADO.ToString();

        var primera = await service.ListarMovimientosEsperadosAsync(userId, rol, null, null, 1, 500, "fecha_esperada", "desc", CancellationToken.None);
        primera.Total.Should().Be(600, "el total es el real, no el recortado");
        primera.TotalPages.Should().Be(2);
        primera.Data.Should().HaveCount(500);

        var segunda = await service.ListarMovimientosEsperadosAsync(userId, rol, null, null, 2, 500, "fecha_esperada", "desc", CancellationToken.None);
        segunda.Data.Should().HaveCount(100, "la segunda pagina no puede quedar vacia por un recorte silencioso");
        segunda.Total.Should().Be(600);
    }

    [Fact]
    public async Task ListarMovimientosEsperados_Con_SortDir_Desc_Debe_Invertir_El_Orden()
    {
        await using var db = BuildDbContext();
        var (userId, _, _) = await SeedConciliacionAsync(db);

        var service = new ConciliacionService(db, TestAuditService.Create(db), TestUserAccess.Create(db));
        var rol = RolUsuario.EMPLEADO.ToString();

        var asc = await service.ListarMovimientosEsperadosAsync(userId, rol, null, null, 1, 10, "fecha_esperada", "asc", CancellationToken.None);
        var desc = await service.ListarMovimientosEsperadosAsync(userId, rol, null, null, 1, 10, "fecha_esperada", "desc", CancellationToken.None);

        asc.Data.Select(x => x.FechaEsperada).Should().BeInAscendingOrder();
        desc.Data.Select(x => x.FechaEsperada).Should().BeInDescendingOrder();
    }

    // -----------------------------------------------------------------------
    // Caso por defecto de la ordenacion (rojo de la seccion 9)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Cuentas_Con_SortBy_Desconocido_Debe_Respetar_SortDir()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Cuentas", Tipo = TipoTitular.EMPRESA };
        db.Titulares.Add(titular);
        db.Cuentas.AddRange(
            CuentaConFecha(titular.Id, "Cuenta A", new DateTime(2026, 1, 1)),
            CuentaConFecha(titular.Id, "Cuenta B", new DateTime(2026, 2, 1)),
            CuentaConFecha(titular.Id, "Cuenta C", new DateTime(2026, 3, 1)));
        await db.SaveChangesAsync();

        var controller = BuildCuentasController(db, Guid.NewGuid());

        var desc = await controller.Listar(page: 1, pageSize: 10, sortBy: "campo_que_no_existe", sortDir: "desc", cancellationToken: CancellationToken.None);
        var asc = await controller.Listar(page: 1, pageSize: 10, sortBy: "campo_que_no_existe", sortDir: "asc", cancellationToken: CancellationToken.None);

        var pageDesc = desc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<CuentaListItemResponse>>().Subject;
        var pageAsc = asc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<CuentaListItemResponse>>().Subject;

        pageDesc.Data.Select(x => x.FechaCreacion).Should().BeInDescendingOrder();
        pageAsc.Data.Select(x => x.FechaCreacion).Should().BeInAscendingOrder();
        pageDesc.Total.Should().Be(3);
        pageDesc.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task Titulares_Con_SortBy_Desconocido_Debe_Respetar_SortDir()
    {
        await using var db = BuildDbContext();
        db.Titulares.AddRange(
            new Titular { Id = Guid.NewGuid(), Nombre = "Titular A", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = Guid.NewGuid(), Nombre = "Titular B", Tipo = TipoTitular.EMPRESA },
            new Titular { Id = Guid.NewGuid(), Nombre = "Titular C", Tipo = TipoTitular.EMPRESA });
        await db.SaveChangesAsync();

        var controller = BuildTitularesController(db, Guid.NewGuid());

        var desc = await controller.Listar(page: 1, pageSize: 10, sortBy: "campo_que_no_existe", sortDir: "desc", cancellationToken: CancellationToken.None);
        var asc = await controller.Listar(page: 1, pageSize: 10, sortBy: "campo_que_no_existe", sortDir: "asc", cancellationToken: CancellationToken.None);

        var pageDesc = desc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<TitularListItemResponse>>().Subject;
        var pageAsc = asc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<TitularListItemResponse>>().Subject;

        pageDesc.Data.Select(x => x.Nombre).Should().Equal("Titular C", "Titular B", "Titular A");
        pageAsc.Data.Select(x => x.Nombre).Should().Equal("Titular A", "Titular B", "Titular C");
        pageDesc.Total.Should().Be(3);
    }

    [Fact]
    public async Task FormatosImportacion_Con_SortBy_Desconocido_Debe_Respetar_SortDir()
    {
        await using var db = BuildDbContext();
        db.FormatosImportacion.AddRange(
            FormatoConFecha("Formato A", new DateTime(2026, 1, 1)),
            FormatoConFecha("Formato B", new DateTime(2026, 2, 1)),
            FormatoConFecha("Formato C", new DateTime(2026, 3, 1)));
        await db.SaveChangesAsync();

        var controller = new FormatosImportacionController(db, TestAuditService.Create(db))
        {
            ControllerContext = BuildControllerContext(Guid.NewGuid())
        };

        var desc = await controller.Listar(page: 1, pageSize: 10, sortBy: "campo_que_no_existe", sortDir: "desc", cancellationToken: CancellationToken.None);
        var asc = await controller.Listar(page: 1, pageSize: 10, sortBy: "campo_que_no_existe", sortDir: "asc", cancellationToken: CancellationToken.None);

        var pageDesc = desc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<FormatoImportacionResponse>>().Subject;
        var pageAsc = asc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<FormatoImportacionResponse>>().Subject;

        pageDesc.Data.Select(x => x.FechaCreacion).Should().BeInDescendingOrder();
        pageAsc.Data.Select(x => x.FechaCreacion).Should().BeInAscendingOrder();
        pageDesc.Total.Should().Be(3);
        pageDesc.TotalPages.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Listados que ya venian paginados y solo anadian sortBy/sortDir
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Paises_Should_Admitir_SortBy_Y_SortDir()
    {
        await using var db = BuildDbContext();
        db.Paises.AddRange(
            new Pais { Id = Guid.NewGuid(), Nombre = "Alfa", CodigoIso2 = "AA", Activo = true },
            new Pais { Id = Guid.NewGuid(), Nombre = "Beta", CodigoIso2 = "BB", Activo = true },
            new Pais { Id = Guid.NewGuid(), Nombre = "Gamma", CodigoIso2 = "CC", Activo = true });
        await db.SaveChangesAsync();

        var controller = new PaisesController(db, TestUserAccess.Create(db), TestAuditService.Create(db))
        {
            ControllerContext = BuildControllerContext(Guid.NewGuid())
        };

        var asc = await controller.Listar(cancellationToken: CancellationToken.None);
        var desc = await controller.Listar(sortBy: "nombre", sortDir: "desc", cancellationToken: CancellationToken.None);

        var pageAsc = asc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<PaisResponse>>().Subject;
        var pageDesc = desc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<PaisResponse>>().Subject;

        pageAsc.Data.Select(x => x.Nombre).Should().Equal("Alfa", "Beta", "Gamma");
        pageDesc.Data.Select(x => x.Nombre).Should().Equal("Gamma", "Beta", "Alfa");
        pageDesc.Total.Should().Be(3);
        pageDesc.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task Auditoria_Should_Admitir_SortBy_Y_SortDir()
    {
        await using var db = BuildDbContext();
        db.Auditorias.AddRange(
            new Auditoria { Id = Guid.NewGuid(), TipoAccion = "b_accion", EntidadTipo = "CUENTAS", Timestamp = new DateTime(2026, 1, 1) },
            new Auditoria { Id = Guid.NewGuid(), TipoAccion = "c_accion", EntidadTipo = "CUENTAS", Timestamp = new DateTime(2026, 2, 1) },
            new Auditoria { Id = Guid.NewGuid(), TipoAccion = "a_accion", EntidadTipo = "CUENTAS", Timestamp = new DateTime(2026, 3, 1) });
        await db.SaveChangesAsync();

        var controller = BuildAuditoriaController(db);

        var porTipoAsc = await controller.Listar(page: 1, pageSize: 10, sortBy: "tipo_accion", sortDir: "asc", ct: CancellationToken.None);
        var porTipoDesc = await controller.Listar(page: 1, pageSize: 10, sortBy: "tipo_accion", sortDir: "desc", ct: CancellationToken.None);

        var pageAsc = porTipoAsc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<AuditoriaListItemResponse>>().Subject;
        var pageDesc = porTipoDesc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<AuditoriaListItemResponse>>().Subject;

        pageAsc.Data.Select(x => x.TipoAccion).Should().Equal("a_accion", "b_accion", "c_accion");
        pageDesc.Data.Select(x => x.TipoAccion).Should().Equal("c_accion", "b_accion", "a_accion");
        pageDesc.Total.Should().Be(3);
        pageDesc.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task Revision_Comisiones_Should_Admitir_SortBy_Y_SortDir()
    {
        var comisiones = new List<RevisionComisionItemResponse>
        {
            Comision("Comision antigua", new DateOnly(2026, 1, 10)),
            Comision("Comision media", new DateOnly(2026, 2, 10)),
            Comision("Comision nueva", new DateOnly(2026, 3, 10))
        };

        var controller = BuildRevisionController(comisiones, []);

        var asc = await controller.Comisiones(page: 1, pageSize: 50, sortBy: "fecha", sortDir: "asc", cancellationToken: CancellationToken.None);
        var desc = await controller.Comisiones(page: 1, pageSize: 50, sortBy: "fecha", sortDir: "desc", cancellationToken: CancellationToken.None);

        var pageAsc = asc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<RevisionComisionItemResponse>>().Subject;
        var pageDesc = desc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<RevisionComisionItemResponse>>().Subject;

        pageAsc.Data.Select(x => x.Fecha).Should().BeInAscendingOrder();
        pageDesc.Data.Select(x => x.Fecha).Should().BeInDescendingOrder();
        pageDesc.Total.Should().Be(3);
        pageDesc.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task Revision_Seguros_Should_Admitir_SortBy_Y_SortDir()
    {
        var seguros = new List<RevisionSeguroItemResponse>
        {
            Seguro("Seguro antiguo", new DateOnly(2026, 1, 10)),
            Seguro("Seguro medio", new DateOnly(2026, 2, 10)),
            Seguro("Seguro nuevo", new DateOnly(2026, 3, 10))
        };

        var controller = BuildRevisionController([], seguros);

        var asc = await controller.Seguros(page: 1, pageSize: 50, sortBy: "fecha", sortDir: "asc", cancellationToken: CancellationToken.None);
        var desc = await controller.Seguros(page: 1, pageSize: 50, sortBy: "fecha", sortDir: "desc", cancellationToken: CancellationToken.None);

        var pageAsc = asc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<RevisionSeguroItemResponse>>().Subject;
        var pageDesc = desc.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<PaginatedResponse<RevisionSeguroItemResponse>>().Subject;

        pageAsc.Data.Select(x => x.Fecha).Should().BeInAscendingOrder();
        pageDesc.Data.Select(x => x.Fecha).Should().BeInDescendingOrder();
        pageDesc.Total.Should().Be(3);
        pageDesc.TotalPages.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // GET /api/revision/comisiones y /api/revision/seguros (servicio real)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Revision_Comisiones_Should_Ordenar_En_El_Servicio_Respetando_SortDir()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Revision", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Revision", Divisa = "EUR", Activa = true };
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "revision_comisiones_importe_minimo",
            Valor = "0",
            Tipo = "number",
            Descripcion = "Importe minimo"
        });

        var fechas = new[] { new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 10), new DateOnly(2026, 3, 10) };
        foreach (var fecha in fechas)
        {
            db.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                Fecha = fecha,
                Concepto = "Comision mantenimiento",
                Monto = -50m,
                Saldo = 100m,
                FilaNumero = 1
            });
        }

        await db.SaveChangesAsync();

        var sut = new RevisionService(db, TestUserAccess.Create(db));
        var scope = new UserAccessScope
        {
            UserId = Guid.NewGuid(),
            IsAdmin = true,
            HasPermissions = true,
            HasGlobalAccess = true
        };

        var asc = await sut.GetComisionesAsync(scope, new RevisionQueryRequest { SortBy = "fecha", SortDir = "asc" }, CancellationToken.None);
        var desc = await sut.GetComisionesAsync(scope, new RevisionQueryRequest { SortBy = "fecha", SortDir = "desc" }, CancellationToken.None);
        // Sin parametros se conserva el orden historico (mas reciente primero).
        var porDefecto = await sut.GetComisionesAsync(scope, new RevisionQueryRequest(), CancellationToken.None);

        asc.Data.Select(x => x.Fecha).Should().BeInAscendingOrder();
        desc.Data.Select(x => x.Fecha).Should().BeInDescendingOrder();
        porDefecto.Data.Select(x => x.Fecha).Should().BeInDescendingOrder();
        asc.Total.Should().Be(3);
        desc.Total.Should().Be(3);
        asc.TotalPages.Should().Be(1);
    }

    [Fact]
    public async Task Revision_Seguros_Should_Ordenar_En_El_Servicio_Respetando_SortDir()
    {
        await using var db = BuildDbContext();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Seguros", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Seguros", Divisa = "EUR", Activa = true };
        db.Titulares.Add(titular);
        db.Cuentas.Add(cuenta);

        var fechas = new[] { new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 10), new DateOnly(2026, 3, 10) };
        foreach (var fecha in fechas)
        {
            db.Extractos.Add(new Extracto
            {
                Id = Guid.NewGuid(),
                CuentaId = cuenta.Id,
                Fecha = fecha,
                Concepto = "Recibo poliza MAPFRE hogar",
                Monto = -180m,
                Saldo = 100m,
                FilaNumero = 1
            });
        }

        await db.SaveChangesAsync();

        var sut = new RevisionService(db, TestUserAccess.Create(db));
        var scope = new UserAccessScope
        {
            UserId = Guid.NewGuid(),
            IsAdmin = true,
            HasPermissions = true,
            HasGlobalAccess = true
        };

        var asc = await sut.GetSegurosAsync(scope, new RevisionQueryRequest { SortBy = "fecha", SortDir = "asc" }, CancellationToken.None);
        var desc = await sut.GetSegurosAsync(scope, new RevisionQueryRequest { SortBy = "fecha", SortDir = "desc" }, CancellationToken.None);

        asc.Data.Select(x => x.Fecha).Should().BeInAscendingOrder();
        desc.Data.Select(x => x.Fecha).Should().BeInDescendingOrder();
        asc.Total.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Dos cuentas y tres conciliaciones: dos en la primera cuenta y una en la
    /// segunda. El permiso es global (sin cuenta) para que el listado alcance
    /// las dos cuentas y los filtros por cuenta se puedan comprobar.
    /// </summary>
    private static async Task<(Guid userId, Guid cuentaId, Guid otraCuentaId)> SeedConciliacionAsync(AppDbContext db)
    {
        var userId = Guid.NewGuid();
        var titular = new Titular { Id = Guid.NewGuid(), Nombre = "Titular Conciliacion", Tipo = TipoTitular.EMPRESA };
        var cuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = "Cuenta Conciliacion", Divisa = "EUR", Activa = true };
        var otraCuenta = new Cuenta { Id = Guid.NewGuid(), TitularId = titular.Id, Nombre = " Otra Conciliacion", Divisa = "EUR", Activa = true };

        db.Titulares.Add(titular);
        db.Cuentas.AddRange(cuenta, otraCuenta);
        db.PermisosUsuario.Add(new PermisoUsuario
        {
            Id = Guid.NewGuid(),
            UsuarioId = userId,
            PuedeConciliar = true,
            PuedeCerrarConciliacion = true
        });

        for (var i = 1; i <= 3; i++)
        {
            var movimiento = new MovimientoEsperado
            {
                Id = Guid.NewGuid(),
                CuentaId = i <= 2 ? cuenta.Id : otraCuenta.Id,
                FechaEsperada = new DateOnly(2026, 4, i),
                Monto = -10m * i,
                Divisa = "EUR",
                Estado = i == 3 ? "conciliada" : "pendiente",
                FechaCreacion = new DateTime(2026, 4, i)
            };
            db.MovimientosEsperados.Add(movimiento);
            db.Conciliaciones.Add(new Conciliacion
            {
                Id = Guid.NewGuid(),
                CuentaId = movimiento.CuentaId,
                MovimientoEsperadoId = movimiento.Id,
                Estado = i == 3 ? "conciliada" : "sugerida",
                FechaCreacion = new DateTime(2026, 4, i)
            });
        }

        await db.SaveChangesAsync();
        return (userId, cuenta.Id, otraCuenta.Id);
    }

    private static Cuenta CuentaConFecha(Guid titularId, string nombre, DateTime fecha) => new()
    {
        Id = Guid.NewGuid(),
        TitularId = titularId,
        Nombre = nombre,
        Divisa = "EUR",
        Activa = true,
        FechaCreacion = fecha
    };

    private static FormatoImportacion FormatoConFecha(string nombre, DateTime fecha) => new()
    {
        Id = Guid.NewGuid(),
        Nombre = nombre,
        Divisa = "EUR",
        Activo = true,
        FechaCreacion = fecha
    };

    private static RevisionComisionItemResponse Comision(string concepto, DateOnly fecha) => new()
    {
        ExtractoId = Guid.NewGuid(),
        CuentaId = Guid.NewGuid(),
        Titular = "Titular",
        Cuenta = "Cuenta",
        Divisa = "EUR",
        Fecha = fecha,
        Monto = -50m,
        Concepto = concepto,
        EstadoDevolucion = "PENDIENTE"
    };

    private static RevisionSeguroItemResponse Seguro(string concepto, DateOnly fecha) => new()
    {
        ExtractoId = Guid.NewGuid(),
        CuentaId = Guid.NewGuid(),
        Titular = "Titular",
        Cuenta = "Cuenta",
        Divisa = "EUR",
        Fecha = fecha,
        Importe = -50m,
        Concepto = concepto,
        Estado = "PENDIENTE"
    };

    private static ControllerContext BuildControllerContext(Guid userId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, RolUsuario.ADMIN.ToString())
        ], "TestAuth");

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    private static CuentasController BuildCuentasController(AppDbContext db, Guid userId) =>
        new(db, TestUserAccess.Create(db), TestAuditService.Create(db), new NoOpPlazoFijoService())
        {
            ControllerContext = BuildControllerContext(userId)
        };

    private static TitularesController BuildTitularesController(AppDbContext db, Guid userId) =>
        new(db, TestUserAccess.Create(db), TestAuditService.Create(db))
        {
            ControllerContext = BuildControllerContext(userId)
        };

    private static AuditoriaController BuildAuditoriaController(AppDbContext db)
    {
        var signer = TestAuditService.Signer();
        return new AuditoriaController(
            db,
            new AuditIntegrityService(db, signer),
            TestAuditService.Create(db),
            signer)
        {
            ControllerContext = BuildControllerContext(Guid.NewGuid())
        };
    }

    private static RevisionController BuildRevisionController(
        IReadOnlyList<RevisionComisionItemResponse> comisiones,
        IReadOnlyList<RevisionSeguroItemResponse> seguros) =>
        new(new StubRevisionService(comisiones, seguros), TestUserAccessForRevision())
        {
            ControllerContext = BuildControllerContext(Guid.NewGuid())
        };

    private static IUserAccessService TestUserAccessForRevision() =>
        new StubUserAccessService(new UserAccessScope
        {
            UserId = Guid.NewGuid(),
            HasPermissions = true,
            HasGlobalAccess = true
        });

    private sealed class NoOpPlazoFijoService : IPlazoFijoService
    {
        public Task<int> ProcesarVencimientosAsync(DateOnly hoy, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<PlazoFijoResponse> RenovarAsync(Guid cuentaId, RenovarPlazoFijoRequest request, Guid? actorUserId, HttpContext httpContext, CancellationToken cancellationToken)
            => Task.FromResult(new PlazoFijoResponse { CuentaId = cuentaId });
    }

    private sealed class StubUserAccessService : IUserAccessService
    {
        private readonly UserAccessScope _scope;

        public StubUserAccessService(UserAccessScope scope)
        {
            _scope = scope;
        }

        public Task<UserAccessScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
            => Task.FromResult(_scope);

        public Task<UserAccessScope> GetScopeAsync(Guid usuarioId, string rol, CancellationToken cancellationToken)
            => Task.FromResult(_scope);

        public IQueryable<Titular> ApplyTitularScope(IQueryable<Titular> query, UserAccessScope scope) => query;
        public IQueryable<Cuenta> ApplyCuentaScope(IQueryable<Cuenta> query, UserAccessScope scope) => query;

        public Task<bool> CanAccessTitularAsync(Guid titularId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(true);
        public Task<bool> CanAccessCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(true);
        public Task<bool> CanWriteCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(true);
        public Task<bool> CanEditCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(true);
        public Task<bool> CanReviewCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(true);
        public Task<bool> CanConciliarCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(true);
        public Task<bool> CanCerrarConciliacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> CanCuentaAsync(UserAccessAction accion, Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<IReadOnlySet<Guid>> GetCuentasAccesiblesAsync(UserAccessAction accion, Guid usuarioId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
    }

    /// <summary>
    /// Stub de la revision: pagina las filas como hace RevisionService y
    /// aplica la misma ordenacion (la resolucion vive en el servicio desde
    /// R-3, seccion 9), de modo que el test del controller comprueba que
    /// `sortBy`/`sortDir` llegan al servicio.
    /// </summary>
    private sealed class StubRevisionService : IRevisionService
    {
        private readonly IReadOnlyList<RevisionComisionItemResponse> _comisiones;
        private readonly IReadOnlyList<RevisionSeguroItemResponse> _seguros;

        public StubRevisionService(
            IReadOnlyList<RevisionComisionItemResponse> comisiones,
            IReadOnlyList<RevisionSeguroItemResponse> seguros)
        {
            _comisiones = comisiones;
            _seguros = seguros;
        }

        public Task<PaginatedResponse<RevisionComisionItemResponse>> GetComisionesAsync(UserAccessScope scope, RevisionQueryRequest request, CancellationToken cancellationToken)
        {
            var (data, total, page, pageSize) = Paginar(_comisiones.Count, request);
            var ordenadas = OrdenarComisiones(_comisiones, request.SortBy, request.SortDir).ToList();
            return Task.FromResult(new PaginatedResponse<RevisionComisionItemResponse>
            {
                Data = ordenadas.Skip(data).Take(pageSize).ToList(),
                Total = total,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize)
            });
        }

        public Task<PaginatedResponse<RevisionSeguroItemResponse>> GetSegurosAsync(UserAccessScope scope, RevisionQueryRequest request, CancellationToken cancellationToken)
        {
            var (data, total, page, pageSize) = Paginar(_seguros.Count, request);
            var ordenadas = OrdenarSeguros(_seguros, request.SortBy, request.SortDir).ToList();
            return Task.FromResult(new PaginatedResponse<RevisionSeguroItemResponse>
            {
                Data = ordenadas.Skip(data).Take(pageSize).ToList(),
                Total = total,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize)
            });
        }

        // R-3 (seccion 9): misma regla que RevisionService.ApplyRevisionSorting
        // para el criterio "fecha" (el que usa el test) y el orden historico
        // (mas reciente primero) cuando no se pide nada.
        private static IEnumerable<RevisionComisionItemResponse> OrdenarComisiones(
            IReadOnlyList<RevisionComisionItemResponse> filas,
            string? sortBy,
            string? sortDir)
        {
            var desc = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(sortBy, "fecha", StringComparison.OrdinalIgnoreCase))
            {
                return filas;
            }

            return desc ? filas.OrderByDescending(x => x.Fecha) : filas.OrderBy(x => x.Fecha);
        }

        private static IEnumerable<RevisionSeguroItemResponse> OrdenarSeguros(
            IReadOnlyList<RevisionSeguroItemResponse> filas,
            string? sortBy,
            string? sortDir)
        {
            var desc = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(sortBy, "fecha", StringComparison.OrdinalIgnoreCase))
            {
                return filas;
            }

            return desc ? filas.OrderByDescending(x => x.Fecha) : filas.OrderBy(x => x.Fecha);
        }

        public Task SetEstadoAsync(UserAccessScope scope, Guid extractoId, string tipo, string estado, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<VerificarDevolucionResponse> VerificarDevolucionAsync(UserAccessScope scope, Guid extractoId, CancellationToken cancellationToken)
            => Task.FromResult(new VerificarDevolucionResponse { Encontrada = true, Message = "ok" });

        public Task<RevisionSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new RevisionSettingsResponse());

        private static (int skip, int total, int page, int pageSize) Paginar(int total, RevisionQueryRequest request)
        {
            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize, 10, 200);
            return ((page - 1) * pageSize, total, page, pageSize);
        }
    }
}
