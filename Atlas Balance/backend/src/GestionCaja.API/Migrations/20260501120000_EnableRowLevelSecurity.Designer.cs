using GestionCaja.API.Data;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCaja.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260501120000_EnableRowLevelSecurity")]
public partial class EnableRowLevelSecurity
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "8.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.HasPostgresExtension(modelBuilder, "pgcrypto");
        NpgsqlModelBuilderExtensions.HasPostgresEnum<RolUsuario>(modelBuilder);
        NpgsqlModelBuilderExtensions.HasPostgresEnum<TipoTitular>(modelBuilder);
        NpgsqlModelBuilderExtensions.HasPostgresEnum<TipoCuenta>(modelBuilder);
        NpgsqlModelBuilderExtensions.HasPostgresEnum<EstadoPlazoFijo>(modelBuilder);
        NpgsqlModelBuilderExtensions.HasPostgresEnum<EstadoTokenIntegracion>(modelBuilder);
        NpgsqlModelBuilderExtensions.HasPostgresEnum<FuenteTipoCambio>(modelBuilder);
        NpgsqlModelBuilderExtensions.HasPostgresEnum<EstadoProceso>(modelBuilder);
        NpgsqlModelBuilderExtensions.HasPostgresEnum<TipoProceso>(modelBuilder);
#pragma warning restore 612, 618
    }
}
