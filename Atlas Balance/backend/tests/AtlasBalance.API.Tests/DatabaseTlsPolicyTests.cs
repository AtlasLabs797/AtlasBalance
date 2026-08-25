using AtlasBalance.API.Services;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class DatabaseTlsPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Connection_String_Vacia_Debe_Ser_Ok(string? connectionString)
    {
        DatabaseTlsPolicy.Evaluate(connectionString).Decision.Should().Be(DatabaseTlsDecision.Ok);
    }

    [Fact]
    public void Connection_String_No_Parseable_Debe_Ser_Ok_Aqui()
    {
        // La validacion de formato no es responsabilidad de la politica TLS:
        // la conexion fallara por si sola al usarse.
        DatabaseTlsPolicy.Evaluate("esto no es una connection string ===").Decision
            .Should().Be(DatabaseTlsDecision.Ok);
    }

    [Theory]
    [InlineData("Host=localhost;Username=app;Password=x;SslMode=Disable")]
    [InlineData("Host=127.0.0.1;Username=app;Password=x;SslMode=Prefer")]
    [InlineData("Host=::1;Username=app;Password=x")]
    public void Host_Local_Debe_Estar_Exento(string connectionString)
    {
        DatabaseTlsPolicy.Evaluate(connectionString).Decision.Should().Be(DatabaseTlsDecision.Ok);
    }

    [Theory]
    [InlineData("Host=db.corp.local;Username=app;Password=x;SslMode=Disable")]
    [InlineData("Host=10.20.30.40;Username=app;Password=x;SslMode=Prefer")]
    public void Host_Remoto_Sin_Tls_Debe_Bloquear(string connectionString)
    {
        var verdict = DatabaseTlsPolicy.Evaluate(connectionString);

        verdict.Decision.Should().Be(DatabaseTlsDecision.InsecureRemote);
        verdict.Host.Should().NotBeNullOrEmpty();
        verdict.Mode.Should().BeOneOf(SslMode.Disable, SslMode.Prefer);
    }

    [Theory]
    [InlineData("Host=db.corp.local;Username=app;Password=x;SslMode=Require")]
    [InlineData("Host=db.corp.local;Username=app;Password=x;SslMode=VerifyFull")]
    public void Host_Remoto_Con_Tls_Obligatorio_Debe_Ser_Ok(string connectionString)
    {
        DatabaseTlsPolicy.Evaluate(connectionString).Decision.Should().Be(DatabaseTlsDecision.Ok);
    }
}
