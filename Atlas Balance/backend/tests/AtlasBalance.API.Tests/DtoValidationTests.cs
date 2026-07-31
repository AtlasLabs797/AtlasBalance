using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// Cobertura de las anotaciones de validacion de los DTOs.
///
/// Hace falta un fichero propio porque el resto de tests de controller instancian
/// el controller a pelo (<c>new ExtractosController(...)</c>), y por ahi no pasa
/// el pipeline de MVC: <c>[ApiController]</c> nunca llega a mirar el ModelState,
/// asi que una anotacion rota no rompe ningun test. Aqui se ejecuta el validador
/// directamente, que es lo que hace ASP.NET Core antes de entrar a la accion.
/// </summary>
public class DtoValidationTests
{
    private static IReadOnlyList<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        // validateAllProperties: true es obligatorio. Sin el solo se evalua
        // [Required] y los [Range]/[MaxLength] se ignoran en silencio.
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }

    private static bool HasErrorFor(IReadOnlyList<ValidationResult> results, string member) =>
        results.Any(r => r.MemberNames.Contains(member));

    private static CreateExtractoRequest ValidExtracto() => new()
    {
        CuentaId = Guid.NewGuid(),
        Fecha = new DateOnly(2026, 1, 15),
        Concepto = "Pago proveedor",
        Monto = -250.75m,
        Saldo = 1_000.00m
    };

    [Fact]
    public void CreateExtracto_Should_Accept_ValidRequest()
    {
        Validate(ValidExtracto()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-9_999_999_999.9999)]
    [InlineData(9_999_999_999.9999)]
    [InlineData(0)]
    [InlineData(0.0001)]
    [InlineData(-0.0001)]
    public void CreateExtracto_Should_Accept_AmountsInsideRange(decimal monto)
    {
        var dto = ValidExtracto();
        dto.Monto = monto;

        HasErrorFor(Validate(dto), nameof(CreateExtractoRequest.Monto)).Should().BeFalse();
    }

    [Theory]
    [InlineData(10_000_000_000)]
    [InlineData(-10_000_000_000)]
    public void CreateExtracto_Should_Reject_AmountsOutsideRange(decimal monto)
    {
        var dto = ValidExtracto();
        dto.Monto = monto;

        HasErrorFor(Validate(dto), nameof(CreateExtractoRequest.Monto)).Should().BeTrue();
    }

    [Fact]
    public void CreateExtracto_Should_Reject_SaldoOutsideRange()
    {
        var dto = ValidExtracto();
        dto.Saldo = decimal.MaxValue;

        HasErrorFor(Validate(dto), nameof(CreateExtractoRequest.Saldo)).Should().BeTrue();
    }

    [Fact]
    public void CreateExtracto_Should_Reject_OverlongConcepto()
    {
        var dto = ValidExtracto();
        dto.Concepto = new string('x', 513);

        HasErrorFor(Validate(dto), nameof(CreateExtractoRequest.Concepto)).Should().BeTrue();
    }

    [Fact]
    public void CreateExtracto_Should_Reject_OverlongComentarios()
    {
        var dto = ValidExtracto();
        dto.Comentarios = new string('x', 1001);

        HasErrorFor(Validate(dto), nameof(CreateExtractoRequest.Comentarios)).Should().BeTrue();
    }

    // El update es parcial: los campos a null significan "no tocar", no "invalido".
    [Fact]
    public void UpdateExtracto_Should_Accept_NullFields()
    {
        Validate(new UpdateExtractoRequest()).Should().BeEmpty();
    }

    [Fact]
    public void UpdateExtracto_Should_Reject_AmountOutsideRange()
    {
        var dto = new UpdateExtractoRequest { Monto = 10_000_000_000m };

        HasErrorFor(Validate(dto), nameof(UpdateExtractoRequest.Monto)).Should().BeTrue();
    }

    /// <summary>
    /// `[MaxLength]` sobre un `Dictionary<,>` no es un caso obvio: el atributo
    /// resuelve la longitud por reflexion sobre la propiedad Count y lanza
    /// InvalidCastException si el tipo no le vale, lo que seria un 500 en vez de
    /// un 400. Este test confirma que con Dictionary cuenta entradas de verdad.
    /// </summary>
    [Fact]
    public void CreateExtracto_Should_Cap_ColumnasExtra_Entries()
    {
        var dentro = ValidExtracto();
        dentro.ColumnasExtra = Enumerable.Range(0, 100)
            .ToDictionary(i => $"col{i}", i => (string?)"x");
        HasErrorFor(Validate(dentro), nameof(CreateExtractoRequest.ColumnasExtra)).Should().BeFalse();

        var fuera = ValidExtracto();
        fuera.ColumnasExtra = Enumerable.Range(0, 101)
            .ToDictionary(i => $"col{i}", i => (string?)"x");
        HasErrorFor(Validate(fuera), nameof(CreateExtractoRequest.ColumnasExtra)).Should().BeTrue();
    }

    [Fact]
    public void Desglose_Should_Cap_Lineas_And_Bound_Importe()
    {
        var muchasLineas = new ExtractoDesgloseUpsertRequest
        {
            Lineas = Enumerable.Range(0, 501)
                .Select(_ => new ExtractoDesgloseLineaRequest { TerceroNombre = "x", Importe = 1m })
                .ToList()
        };
        HasErrorFor(Validate(muchasLineas), nameof(ExtractoDesgloseUpsertRequest.Lineas)).Should().BeTrue();

        var importeEnorme = new ExtractoDesgloseLineaRequest
        {
            TerceroNombre = "Proveedor",
            Importe = decimal.MaxValue
        };
        HasErrorFor(Validate(importeEnorme), nameof(ExtractoDesgloseLineaRequest.Importe)).Should().BeTrue();
    }

    /// <summary>
    /// JsonStringEnumConverter rechaza cadenas desconocidas pero no valida
    /// enteros, asi que `"rol": 99` llegaba al controller como (RolUsuario)99.
    /// Los tres enums afectados son enums nativos de Postgres, de modo que la
    /// escritura moria en Npgsql con un 500. [EnumDataType] lo baja a 400.
    /// </summary>
    [Fact]
    public void Enums_Should_Reject_OutOfRange_Integer_Values()
    {
        var usuario = new CreateUsuarioRequest
        {
            Email = "a@b.local",
            NombreCompleto = "Test",
            Password = "unaClaveLargaDe12+",
            Rol = (RolUsuario)99
        };
        HasErrorFor(Validate(usuario), nameof(CreateUsuarioRequest.Rol)).Should().BeTrue();

        var cuenta = new SaveCuentaRequest
        {
            Nombre = "Cuenta",
            Divisa = "EUR",
            TipoCuenta = (TipoCuenta)99
        };
        HasErrorFor(Validate(cuenta), nameof(SaveCuentaRequest.TipoCuenta)).Should().BeTrue();

        var titular = new SaveTitularRequest { Nombre = "Titular", Tipo = (TipoTitular)99 };
        HasErrorFor(Validate(titular), nameof(SaveTitularRequest.Tipo)).Should().BeTrue();
    }

    [Fact]
    public void Enums_Should_Accept_DefinedValues()
    {
        var usuario = new CreateUsuarioRequest
        {
            Email = "a@b.local",
            NombreCompleto = "Test",
            Password = "unaClaveLargaDe12+",
            Rol = RolUsuario.GERENTE
        };
        HasErrorFor(Validate(usuario), nameof(CreateUsuarioRequest.Rol)).Should().BeFalse();

        // TipoCuenta es nullable: ausente significa "usa el valor por defecto",
        // no invalido.
        var cuenta = new SaveCuentaRequest { Nombre = "Cuenta", Divisa = "EUR", TipoCuenta = null };
        HasErrorFor(Validate(cuenta), nameof(SaveCuentaRequest.TipoCuenta)).Should().BeFalse();
    }

    [Theory]
    [InlineData("sin-arroba")]
    [InlineData("@sindominio")]
    [InlineData("sinarroba.local@")]
    [InlineData("dos@@arrobas.local")]
    [InlineData("")]
    public void Login_Should_Reject_MalformedEmail(string email)
    {
        var dto = new LoginRequest { Email = email, Password = "loQueSea1234" };

        HasErrorFor(Validate(dto), nameof(LoginRequest.Email)).Should().BeTrue();
    }

    /// <summary>
    /// Deja constancia de hasta donde llega [EmailAddress] y hasta donde no.
    /// EmailAddressAttribute es deliberadamente laxo: exige una unica arroba que
    /// no este en los extremos y poco mas, asi que admite espacios y dominios sin
    /// punto. Sirve para cazar el "esto no es un email", no para garantizar que
    /// la direccion existe. Se deja asi a proposito: apretar el formato con un
    /// regex propio rechaza direcciones raras pero validas, y en esta app los
    /// emails los teclea un admin sobre una tabla con indice unico.
    /// Si algun dia hace falta de verdad, la via es verificar por envio, no
    /// un regex mas largo.
    /// </summary>
    [Theory]
    [InlineData("espacio en@medio.local")]
    [InlineData("sin@punto")]
    public void EmailAddress_Attribute_Is_Permissive_ByDesign(string email)
    {
        var dto = new LoginRequest { Email = email, Password = "loQueSea1234" };

        HasErrorFor(Validate(dto), nameof(LoginRequest.Email)).Should().BeFalse();
    }

    [Fact]
    public void Login_Should_Accept_WellFormedEmail()
    {
        var dto = new LoginRequest { Email = "admin@atlasbalance.local", Password = "loQueSea1234" };

        Validate(dto).Should().BeEmpty();
    }

    [Fact]
    public void CreateUsuario_Should_Cap_Emails_And_Permisos_Collections()
    {
        var dto = new CreateUsuarioRequest
        {
            Email = "a@b.local",
            NombreCompleto = "Test",
            Password = "unaClaveLargaDe12+",
            Emails = Enumerable.Range(0, 21).Select(i => $"e{i}@b.local").ToArray()
        };

        HasErrorFor(Validate(dto), nameof(CreateUsuarioRequest.Emails)).Should().BeTrue();
    }

    [Fact]
    public void EstablecerDivisaPorDefecto_Should_Reject_BlankCodigo()
    {
        // Sin [Required], Normalize convertia el vacio en "EUR" y el endpoint
        // cambiaba la divisa base de toda la app en silencio.
        var dto = new AtlasBalance.API.Controllers.EstablecerDivisaPorDefectoRequest { Codigo = "" };

        HasErrorFor(Validate(dto), nameof(AtlasBalance.API.Controllers.EstablecerDivisaPorDefectoRequest.Codigo))
            .Should().BeTrue();
    }

    [Fact]
    public void SaveAlertaSaldo_Should_Bound_SaldoMinimo()
    {
        var fuera = new SaveAlertaSaldoRequest { SaldoMinimo = decimal.MaxValue };
        HasErrorFor(Validate(fuera), nameof(SaveAlertaSaldoRequest.SaldoMinimo)).Should().BeTrue();

        var dentro = new SaveAlertaSaldoRequest { SaldoMinimo = 1500.50m };
        HasErrorFor(Validate(dentro), nameof(SaveAlertaSaldoRequest.SaldoMinimo)).Should().BeFalse();
    }

    // --- Cobertura retroactiva de ImportacionDtos -------------------------------
    // Estas anotaciones existian desde V-02.07 y no las ejercitaba ningun test.

    [Fact]
    public void ImportacionValidar_Should_Reject_MissingCuentaId()
    {
        var dto = new ImportacionValidarRequest { RawData = "fecha;concepto;monto" };

        HasErrorFor(Validate(dto), nameof(ImportacionValidarRequest.CuentaId)).Should().BeTrue();
    }

    [Fact]
    public void ImportacionValidar_Should_Reject_OverlongSeparador()
    {
        var dto = new ImportacionValidarRequest
        {
            CuentaId = Guid.NewGuid(),
            RawData = "fecha;concepto;monto",
            Separador = new string(';', 9)
        };

        HasErrorFor(Validate(dto), nameof(ImportacionValidarRequest.Separador)).Should().BeTrue();
    }

    [Fact]
    public void ImportacionValidar_Should_Reject_RawDataOverFiveMegabytes()
    {
        var dto = new ImportacionValidarRequest
        {
            CuentaId = Guid.NewGuid(),
            RawData = new string('x', (5 * 1024 * 1024) + 1)
        };

        HasErrorFor(Validate(dto), nameof(ImportacionValidarRequest.RawData)).Should().BeTrue();
    }

    /// <summary>
    /// Regresion de un bug real de V-02.07. <c>[Range(typeof(decimal), ...)]</c>
    /// parsea sus limites con la cultura del proceso salvo que se le diga lo
    /// contrario. El servidor corre en es-ES, donde el punto es separador de
    /// miles, asi que "0.0001" se convertia en 1: el minimo efectivo era un euro
    /// y cualquier movimiento por debajo se rechazaba.
    ///
    /// El test fija la cultura a es-ES a proposito para que falle si alguien
    /// quita <c>ParseLimitsInInvariantCulture</c>.
    /// </summary>
    [Fact]
    public void ImportacionPlazoFijo_Should_Accept_SubUnitAmounts_UnderSpanishCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-ES");

            var dto = new ImportacionPlazoFijoMovimientoRequest
            {
                CuentaId = Guid.NewGuid(),
                TipoMovimiento = "INGRESO",
                Fecha = new DateOnly(2026, 1, 15),
                Monto = 0.50m,
                Concepto = "Interes mensual"
            };

            HasErrorFor(Validate(dto), nameof(ImportacionPlazoFijoMovimientoRequest.Monto))
                .Should().BeFalse("0,50 EUR esta dentro del rango 0.0001 - 9999999999.9999");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ImportacionPlazoFijo_Should_Still_Reject_ZeroAndNegativeAmounts()
    {
        foreach (var monto in new[] { 0m, -1m })
        {
            var dto = new ImportacionPlazoFijoMovimientoRequest
            {
                CuentaId = Guid.NewGuid(),
                TipoMovimiento = "INGRESO",
                Fecha = new DateOnly(2026, 1, 15),
                Monto = monto
            };

            HasErrorFor(Validate(dto), nameof(ImportacionPlazoFijoMovimientoRequest.Monto))
                .Should().BeTrue("un movimiento de {0} no tiene sentido", monto);
        }
    }
}
