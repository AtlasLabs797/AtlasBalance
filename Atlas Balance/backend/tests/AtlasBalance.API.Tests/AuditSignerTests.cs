using System.Net;
using AtlasBalance.API.Constants;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

// -----------------------------------------------------------------------
// V-02.07: la firma HMAC de AUDITORIAS es lo que convierte la tabla en algo
// que puede usarse como prueba. Si la firma no valida despues de un viaje de
// ida y vuelta a Postgres, toda la auditoria se reporta como manipulada y el
// mecanismo pasa de defensa a generador de falsas alarmas. Estos tests fijan
// las dos propiedades que importan: detecta manipulacion, y NO detecta lo que
// solo son diferencias de representacion.
// -----------------------------------------------------------------------
public sealed class AuditSignerTests
{
    private static AuditSigner Firmador(string clave = "clave-de-firma-de-auditoria-de-pruebas-32+")
        => new(new AuditSigningKey(clave));

    private static Auditoria Fila() => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Secuencia = 42,
        UsuarioId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        TipoAccion = AuditActions.Login,
        EntidadTipo = "USUARIOS",
        EntidadId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Timestamp = new DateTime(2026, 7, 30, 10, 15, 30, DateTimeKind.Utc),
        IpAddress = IPAddress.Parse("10.0.0.5"),
        UserAgent = "Mozilla/5.0",
        SessionId = "sesion-abc",
        Origen = AuditOrigenes.Ui,
        DetallesJson = """{"email":"a@b.com"}"""
    };

    [Fact]
    public void Verificar_Should_Accept_Row_Signed_With_Same_Key()
    {
        var firmador = Firmador();
        var fila = Fila();

        fila.Firma = firmador.Firmar(fila);

        firmador.Verificar(fila).Should().BeTrue();
    }

    [Theory]
    [InlineData("TipoAccion")]
    [InlineData("UsuarioId")]
    [InlineData("IpAddress")]
    [InlineData("DetallesJson")]
    [InlineData("Origen")]
    [InlineData("SessionId")]
    [InlineData("UserAgent")]
    [InlineData("Timestamp")]
    public void Verificar_Should_Reject_When_Any_Signed_Field_Changes(string campo)
    {
        var firmador = Firmador();
        var fila = Fila();
        fila.Firma = firmador.Firmar(fila);

        // Manipulacion tipica de quien quiere tapar su rastro: cambiar el actor,
        // la accion o el origen de la fila sin tocar la firma.
        switch (campo)
        {
            case "TipoAccion": fila.TipoAccion = AuditActions.Logout; break;
            case "UsuarioId": fila.UsuarioId = Guid.NewGuid(); break;
            case "IpAddress": fila.IpAddress = IPAddress.Parse("10.0.0.6"); break;
            case "DetallesJson": fila.DetallesJson = """{"email":"otro@b.com"}"""; break;
            case "Origen": fila.Origen = AuditOrigenes.Api; break;
            case "SessionId": fila.SessionId = "otra-sesion"; break;
            case "UserAgent": fila.UserAgent = "curl/8"; break;
            case "Timestamp": fila.Timestamp = fila.Timestamp.AddMinutes(-30); break;
        }

        firmador.Verificar(fila).Should().BeFalse();
    }

    [Fact]
    public void Verificar_Should_Reject_Row_Signed_With_Another_Key()
    {
        var fila = Fila();
        fila.Firma = Firmador("clave-del-atacante-que-no-es-la-buena-32+").Firmar(fila);

        // Es el caso de la fila insertada por quien tiene la BD pero no la clave.
        Firmador().Verificar(fila).Should().BeFalse();
    }

    [Fact]
    public void Verificar_Should_Reject_Row_Without_Signature()
    {
        var fila = Fila();
        fila.Firma = null;

        Firmador().Verificar(fila).Should().BeFalse();
    }

    [Fact]
    public void Firmar_Should_Ignore_Sub_Microsecond_Precision()
    {
        // Postgres timestamptz guarda microsegundos y DateTime tiene 100 ns. Sin
        // truncar, la firma calculada antes del INSERT no validaria al releer la
        // fila y TODA la auditoria pareceria manipulada. Es el fallo mas caro
        // posible de este diseno, asi que va con test propio.
        var firmador = Firmador();
        var fila = Fila();
        fila.Timestamp = new DateTime(2026, 7, 30, 10, 15, 30, DateTimeKind.Utc).AddTicks(7);
        fila.Firma = firmador.Firmar(fila);

        // Lo que devolveria Postgres: mismos microsegundos, sin los 7 ticks.
        fila.Timestamp = new DateTime(2026, 7, 30, 10, 15, 30, DateTimeKind.Utc);

        firmador.Verificar(fila).Should().BeTrue();
    }

    [Fact]
    public void Firmar_Should_Treat_IPv4_Mapped_IPv6_As_The_Same_Address()
    {
        // Postgres inet puede devolver una IPv4 como IPv6 mapeada segun como se
        // inserto. La firma no puede depender de esa representacion.
        var firmador = Firmador();
        var fila = Fila();
        fila.IpAddress = IPAddress.Parse("10.0.0.5");
        fila.Firma = firmador.Firmar(fila);

        fila.IpAddress = IPAddress.Parse("::ffff:10.0.0.5");

        firmador.Verificar(fila).Should().BeTrue();
    }

    [Fact]
    public void Firmar_Should_Not_Allow_Moving_Content_Between_Fields()
    {
        // Sin prefijo de longitud, ("ab","c") y ("a","bc") producirian el mismo
        // payload y se podria mover texto de un campo a otro sin invalidar la
        // firma. Aqui se mueve una letra de EntidadTipo a ColumnaNombre.
        var firmador = Firmador();

        var a = Fila();
        a.EntidadTipo = "AB";
        a.ColumnaNombre = "C";

        var b = Fila();
        b.EntidadTipo = "A";
        b.ColumnaNombre = "BC";

        firmador.Firmar(a).Should().NotBe(firmador.Firmar(b));
    }

    [Fact]
    public void Firmar_Should_Distinguish_Null_From_Empty_String()
    {
        var firmador = Firmador();

        var conNull = Fila();
        conNull.ColumnaNombre = null;

        var conVacio = Fila();
        conVacio.ColumnaNombre = string.Empty;

        firmador.Firmar(conNull).Should().NotBe(firmador.Firmar(conVacio));
    }

    [Fact]
    public void Firmar_Should_Not_Cover_Secuencia()
    {
        // Deliberado: Postgres asigna secuencia durante el INSERT, asi que
        // firmarla obligaria a un UPDATE posterior que el trigger append-only
        // bloquea. El borrado lo detecta la continuidad de la secuencia, no la
        // firma. Si alguien cambia esto, que sea a sabiendas.
        var firmador = Firmador();
        var fila = Fila();
        var firmaOriginal = firmador.Firmar(fila);

        fila.Secuencia = 99_999;

        firmador.Firmar(fila).Should().Be(firmaOriginal);
    }
}
