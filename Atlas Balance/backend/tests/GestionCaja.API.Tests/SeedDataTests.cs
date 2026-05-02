using System.Text.Json;
using FluentAssertions;
using GestionCaja.API;
using GestionCaja.API.Data;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GestionCaja.API.Tests;

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
        formatos.Should().HaveCount(8);
        formatos.Should().Contain(f => f.BancoNombre == "Sabadell" && f.Divisa == "EUR");
        formatos.Should().Contain(f => f.BancoNombre == "BBVA" && f.Divisa == "EUR");
        formatos.Should().Contain(f => f.BancoNombre == "BBVA" && f.Divisa == "MXN");
        formatos.Should().Contain(f => f.BancoNombre == "Banco Caribe" && f.Divisa == "DOP");
        formatos.Should().Contain(f => f.BancoNombre == "Banco Caribe" && f.Divisa == "USD");
        formatos.Should().Contain(f => f.BancoNombre == "Banco Popular" && f.Divisa == "DOP");
        formatos.Should().Contain(f => f.BancoNombre == "Banco Popular" && f.Divisa == "USD");

        var bbvaMxn = formatos.Single(f => f.BancoNombre == "BBVA" && f.Divisa == "MXN");
        using var doc = JsonDocument.Parse(bbvaMxn.MapeoJson);
        doc.RootElement.GetProperty("tipo_monto").GetString().Should().Be("dos_columnas");
        doc.RootElement.GetProperty("ingreso").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("egreso").GetInt32().Should().Be(2);

        db.Configuraciones
            .Single(c => c.Clave == "app_update_check_url")
            .Valor
            .Should()
            .Be(ConfigurationDefaults.UpdateCheckUrl);
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
        db.FormatosImportacion.IgnoreQueryFilters().Should().HaveCount(8);
        db.FormatosImportacion
            .Single(f => f.BancoNombre == "BBVA" && f.Divisa == "EUR")
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
            Id = Guid.Parse("e1b2cba0-60bd-4854-9b24-d2e88763fa5d"),
            Nombre = "Formato legado",
            BancoNombre = null,
            Divisa = null,
            MapeoJson = "{}",
            Activo = true
        });
        db.SaveChanges();

        var act = () => SeedData.Initialize(db);

        act.Should().NotThrow();
        db.FormatosImportacion.IgnoreQueryFilters().Should().HaveCount(8);
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
        public string ApplicationName { get; set; } = "GestionCaja.API.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
