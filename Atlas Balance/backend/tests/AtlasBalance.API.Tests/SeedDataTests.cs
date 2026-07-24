using System.Text.Json;
using FluentAssertions;
using AtlasBalance.API;
using AtlasBalance.API.Data;
using AtlasBalance.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class SeedDataTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void Initialize_Should_Seed_Default_Bank_Formats_When_Installing_From_Zero()
    {
        using var db = BuildDbContext();

        SeedData.Initialize(db, BuildSeedConfiguration());

        var formatos = db.FormatosImportacion.ToList();
        formatos.Should().HaveCount(6);
        formatos.Should().Contain(f => f.BancoNombre == "BBVA Empresa" && f.Divisa == "EUR");
        formatos.Should().Contain(f => f.BancoNombre == "BBVA Particular" && f.Divisa == "EUR");
        formatos.Should().Contain(f => f.BancoNombre == "BS Empresa" && f.Divisa == "EUR");
        formatos.Should().Contain(f => f.BancoNombre == "BS Particular" && f.Divisa == "EUR");
        formatos.Should().Contain(f => f.BancoNombre == "Banquinter Empresa" && f.Divisa == "EUR");
        formatos.Should().Contain(f => f.BancoNombre == "Banquinter Particular" && f.Divisa == "EUR");

        var banquinterEmpresa = formatos.Single(f => f.BancoNombre == "Banquinter Empresa" && f.Divisa == "EUR");
        using var doc = JsonDocument.Parse(banquinterEmpresa.MapeoJson);
        doc.RootElement.GetProperty("tipo_monto").GetString().Should().Be("tres_columnas");
        doc.RootElement.GetProperty("ingreso").GetInt32().Should().Be(8);
        doc.RootElement.GetProperty("egreso").GetInt32().Should().Be(7);

        db.Configuraciones
            .Single(c => c.Clave == "app_update_check_url")
            .Valor
            .Should()
            .Be(ConfigurationDefaults.UpdateCheckUrl);
        db.Configuraciones
            .Single(c => c.Clave == "app_update_auto_enabled")
            .Valor
            .Should()
            .Be("false");
        db.Configuraciones
            .Single(c => c.Clave == "app_update_auto_hour_utc")
            .Valor
            .Should()
            .Be("3");
    }

    [Fact]
    public void Initialize_Should_Add_Default_Bank_Formats_Without_Duplicating_Existing_Data()
    {
        using var db = BuildDbContext();
        var adminId = Guid.NewGuid();
        db.Usuarios.Add(new Usuario
        {
            Id = adminId,
            Email = "admin.seed@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Seed",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.SaveChanges();

        SeedData.Initialize(db);
        SeedData.Initialize(db);

        db.Usuarios.Should().HaveCount(1);
        db.FormatosImportacion.IgnoreQueryFilters().Should().HaveCount(6);
        db.FormatosImportacion
            .Single(f => f.BancoNombre == "BBVA Empresa" && f.Divisa == "EUR")
            .UsuarioCreadorId
            .Should()
            .Be(adminId);
    }

    [Fact]
    public void Initialize_Should_Not_Duplicate_Default_Format_When_Fixed_Id_Already_Exists()
    {
        using var db = BuildDbContext();
        db.Usuarios.Add(new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "admin.seed@test.local",
            PasswordHash = "hash",
            NombreCompleto = "Admin Seed",
            Rol = RolUsuario.ADMIN,
            Activo = true,
            PrimerLogin = false
        });
        db.FormatosImportacion.Add(new FormatoImportacion
        {
            Id = Guid.Parse("0ee8dcc6-10a3-49ed-9f5d-1a1ade414184"),
            Nombre = "Formato legado",
            BancoNombre = null,
            Divisa = null,
            MapeoJson = "{}",
            Activo = true
        });
        db.SaveChanges();

        var act = () => SeedData.Initialize(db);

        act.Should().NotThrow();
        db.FormatosImportacion.IgnoreQueryFilters().Should().HaveCount(6);
    }

    [Fact]
    public void Initialize_Should_Recover_From_Partial_Previous_Seed()
    {
        using var db = BuildDbContext();
        db.Configuraciones.Add(new Configuracion
        {
            Clave = "app_version",
            Valor = "V-PARTIAL"
        });
        db.DivisasActivas.Add(new DivisaActiva
        {
            Codigo = "EUR",
            Nombre = "Euro",
            Simbolo = "EUR",
            Activa = true,
            EsBase = true
        });
        db.TiposCambio.Add(new TipoCambio
        {
            Id = Guid.NewGuid(),
            DivisaOrigen = "EUR",
            DivisaDestino = "USD",
            Tasa = 1.08m,
            FechaActualizacion = DateTime.UtcNow,
            Fuente = FuenteTipoCambio.MANUAL
        });
        db.SaveChanges();

        SeedData.Initialize(db, BuildSeedConfiguration());
        SeedData.Initialize(db, BuildSeedConfiguration());

        db.Usuarios.Should().HaveCount(1);
        db.Configuraciones.Select(c => c.Clave).Should().OnlyHaveUniqueItems();
        db.Configuraciones.Single(c => c.Clave == "app_version").Valor.Should().Be("V-PARTIAL");
        db.DivisasActivas.Select(d => d.Codigo).Should().OnlyHaveUniqueItems();
        db.DivisasActivas.Should().HaveCount(4);
        db.TiposCambio.Select(t => $"{t.DivisaOrigen}-{t.DivisaDestino}").Should().OnlyHaveUniqueItems();
        db.TiposCambio.Should().HaveCount(3);
    }

    [Fact]
    public void Initialize_Should_Seed_Demo_Data_In_Development_Without_Duplicating()
    {
        using var db = BuildDbContext();

        SeedData.Initialize(db, BuildSeedConfiguration(), new TestHostEnvironment("Development"));
        SeedData.Initialize(db, BuildSeedConfiguration(), new TestHostEnvironment("Development"));

        db.Paises.Should().Contain(p => p.Nombre == "Espana" && p.CodigoIso2 == "ES");
        db.Paises.Should().Contain(p => p.Nombre == "Mexico" && p.CodigoIso2 == "MX");
        db.Paises.Should().Contain(p => p.Nombre == "Republica Dominicana" && p.CodigoIso2 == "DO");
        db.Titulares.Count(t => t.Nombre.StartsWith("Demo ")).Should().Be(3);
        db.Cuentas.Count(c => c.Nombre.StartsWith("Demo ")).Should().Be(5);
        db.Extractos.Should().HaveCount(25);
        db.PlazosFijos.Should().ContainSingle(p => p.CuentaId == Guid.Parse("72000000-0000-0000-0000-000000000005"));
        db.AlertasSaldo.Should().Contain(a => a.CuentaId == Guid.Parse("72000000-0000-0000-0000-000000000002"));
        db.PermisosUsuario.Should().ContainSingle(p =>
            p.CuentaId == null &&
            p.TitularId == null &&
            p.PaisId == null &&
            p.PuedeVerCuentas &&
            p.PuedeVerDashboard);
    }

    [Fact]
    public void Initialize_Should_Not_Seed_Demo_Data_In_Production()
    {
        using var db = BuildDbContext();
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SeedAdmin:Password"] = string.Concat("CorrectHorse", "BatteryStaple2026!"),
            ["DemoData:Enabled"] = "true"
        });

        SeedData.Initialize(db, config, new TestHostEnvironment("Production"));

        db.Cuentas.Should().NotContain(c => c.Nombre.StartsWith("Demo "));
        db.Extractos.Should().BeEmpty();
    }

    [Fact]
    public void Initialize_Should_Reject_Default_Admin_Password_In_Production()
    {
        using var db = BuildDbContext();
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SeedAdmin:Password"] = "CAMBIAR_PASSWORD_ADMIN_INICIAL_AQUI"
        });

        var act = () => SeedData.Initialize(db, config, new TestHostEnvironment("Production"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SeedAdmin:Password*");
    }

    [Fact]
    public void Initialize_Should_Use_Configured_Admin_Password_In_Production()
    {
        using var db = BuildDbContext();
        var seedValue = string.Concat("CorrectHorse", "BatteryStaple2026!");
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SeedAdmin:Email"] = "admin.prod@test.local",
            ["SeedAdmin:Password"] = seedValue
        });

        SeedData.Initialize(db, config, new TestHostEnvironment("Production"));

        var admin = db.Usuarios.Single();
        admin.Email.Should().Be("admin.prod@test.local");
        BCrypt.Net.BCrypt.Verify(seedValue, admin.PasswordHash).Should().BeTrue();
    }

    private static IConfiguration BuildSeedConfiguration() =>
        BuildConfiguration(new Dictionary<string, string?>
        {
            ["SeedAdmin:Password"] = "LocalSeedPasswordForTests2026!"
        });

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "AtlasBalance.API.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
