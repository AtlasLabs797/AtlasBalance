using System.Linq.Expressions;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<UsuarioEmail> UsuarioEmails => Set<UsuarioEmail>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Titular> Titulares => Set<Titular>();
    public DbSet<Cuenta> Cuentas => Set<Cuenta>();
    public DbSet<PlazoFijo> PlazosFijos => Set<PlazoFijo>();
    public DbSet<FormatoImportacion> FormatosImportacion => Set<FormatoImportacion>();
    public DbSet<Extracto> Extractos => Set<Extracto>();
    public DbSet<ExtractoColumnaExtra> ExtractosColumnasExtra => Set<ExtractoColumnaExtra>();
    public DbSet<PermisoUsuario> PermisosUsuario => Set<PermisoUsuario>();
    public DbSet<PreferenciaUsuarioCuenta> PreferenciasUsuarioCuenta => Set<PreferenciaUsuarioCuenta>();
    public DbSet<AlertaSaldo> AlertasSaldo => Set<AlertaSaldo>();
    public DbSet<AlertaDestinatario> AlertaDestinatarios => Set<AlertaDestinatario>();
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();
    public DbSet<IntegrationToken> IntegrationTokens => Set<IntegrationToken>();
    public DbSet<IntegrationPermission> IntegrationPermissions => Set<IntegrationPermission>();
    public DbSet<AuditoriaIntegracion> AuditoriaIntegraciones => Set<AuditoriaIntegracion>();
    public DbSet<TipoCambio> TiposCambio => Set<TipoCambio>();
    public DbSet<DivisaActiva> DivisasActivas => Set<DivisaActiva>();
    public DbSet<Configuracion> Configuraciones => Set<Configuracion>();
    public DbSet<Backup> Backups => Set<Backup>();
    public DbSet<Exportacion> Exportaciones => Set<Exportacion>();
    public DbSet<NotificacionAdmin> NotificacionesAdmin => Set<NotificacionAdmin>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("pgcrypto");
        modelBuilder.HasPostgresEnum<RolUsuario>();
        modelBuilder.HasPostgresEnum<TipoTitular>();
        modelBuilder.HasPostgresEnum<TipoCuenta>();
        modelBuilder.HasPostgresEnum<EstadoPlazoFijo>();
        modelBuilder.HasPostgresEnum<EstadoTokenIntegracion>();
        modelBuilder.HasPostgresEnum<FuenteTipoCambio>();
        modelBuilder.HasPostgresEnum<EstadoProceso>();
        modelBuilder.HasPostgresEnum<TipoProceso>();

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.ToTable("USUARIOS");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Rol);
            entity.HasIndex(e => e.Activo);
            entity.HasIndex(e => e.MfaEnabled);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.SecurityStamp).HasMaxLength(64).IsRequired();
            entity.Property(e => e.MfaSecret).HasMaxLength(2048);
        });

        modelBuilder.Entity<UsuarioEmail>(entity =>
        {
            entity.ToTable("USUARIO_EMAILS");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UsuarioId);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("REFRESH_TOKENS");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UsuarioId);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.ExpiraEn);
            entity.Property(e => e.IpAddress).HasColumnType("inet");
            entity.HasQueryFilter(e => e.Usuario != null && e.Usuario.DeletedAt == null);
            entity.HasOne(e => e.Usuario).WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Titular>(entity =>
        {
            entity.ToTable("TITULARES");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Nombre);
            entity.HasIndex(e => e.Tipo);
            entity.HasIndex(e => e.DeletedAt);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.DeletedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Cuenta>(entity =>
        {
            entity.ToTable("CUENTAS");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TitularId);
            entity.HasIndex(e => e.Divisa);
            entity.HasIndex(e => e.EsEfectivo);
            entity.HasIndex(e => e.TipoCuenta);
            entity.HasIndex(e => e.Activa);
            entity.HasIndex(e => e.DeletedAt);
            entity.HasOne(e => e.Titular).WithMany().HasForeignKey(e => e.TitularId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FormatoImportacion>().WithMany().HasForeignKey(e => e.FormatoId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.DeletedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlazoFijo>(entity =>
        {
            entity.ToTable("PLAZOS_FIJOS");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.InteresPrevisto).HasPrecision(18, 2);
            entity.HasIndex(e => e.CuentaId).IsUnique();
            entity.HasIndex(e => e.FechaVencimiento);
            entity.HasIndex(e => e.Estado);
            entity.HasIndex(e => e.CuentaReferenciaId);
            entity.HasIndex(e => e.DeletedAt);
            entity.HasOne(e => e.Cuenta).WithOne().HasForeignKey<PlazoFijo>(e => e.CuentaId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CuentaReferencia).WithMany().HasForeignKey(e => e.CuentaReferenciaId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.DeletedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FormatoImportacion>(entity =>
        {
            entity.ToTable("FORMATOS_IMPORTACION");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MapeoJson).HasColumnType("jsonb");
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioCreadorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.DeletedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Extracto>(entity =>
        {
            entity.ToTable("EXTRACTOS");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Monto).HasPrecision(18, 4);
            entity.Property(e => e.Saldo).HasPrecision(18, 4);
            entity.HasIndex(e => new { e.CuentaId, e.FilaNumero }).IsUnique();
            entity.HasIndex(e => new { e.CuentaId, e.Fecha });
            entity.HasIndex(e => new { e.CuentaId, e.DeletedAt });
            entity.HasIndex(e => e.Fecha);
            entity.HasIndex(e => e.Flagged);
            entity.HasIndex(e => e.Checked);
            entity.HasOne<Cuenta>().WithMany().HasForeignKey(e => e.CuentaId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.CheckedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.FlaggedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioCreacionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioModificacionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.DeletedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExtractoColumnaExtra>(entity =>
        {
            entity.ToTable("EXTRACTOS_COLUMNAS_EXTRA");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ExtractoId);
            entity.HasIndex(e => e.NombreColumna);
            entity.HasOne<Extracto>().WithMany().HasForeignKey(e => e.ExtractoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PermisoUsuario>(entity =>
        {
            entity.ToTable("PERMISOS_USUARIO");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UsuarioId);
            entity.HasIndex(e => new { e.UsuarioId, e.CuentaId });
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Cuenta>().WithMany().HasForeignKey(e => e.CuentaId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Titular>().WithMany().HasForeignKey(e => e.TitularId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PreferenciaUsuarioCuenta>(entity =>
        {
            entity.ToTable("PREFERENCIAS_USUARIO_CUENTA");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UsuarioId);
            entity.HasIndex(e => new { e.UsuarioId, e.CuentaId })
                .IsUnique()
                .HasFilter("\"cuenta_id\" IS NOT NULL");
            entity.HasIndex(e => e.UsuarioId)
                .IsUnique()
                .HasFilter("\"cuenta_id\" IS NULL");
            entity.Property(e => e.ColumnasVisibles).HasColumnType("jsonb");
            entity.Property(e => e.ColumnasEditables).HasColumnType("jsonb");
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Cuenta>().WithMany().HasForeignKey(e => e.CuentaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AlertaSaldo>(entity =>
        {
            entity.ToTable("ALERTAS_SALDO");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SaldoMinimo).HasPrecision(18, 4);
            entity.HasIndex(e => e.CuentaId)
                .IsUnique()
                .HasDatabaseName("ix_alertas_saldo_cuenta_id_unique")
                .HasFilter("\"cuenta_id\" IS NOT NULL");
            entity.HasIndex(e => e.TipoTitular)
                .IsUnique()
                .HasDatabaseName("ix_alertas_saldo_tipo_titular_unique")
                .HasFilter("\"cuenta_id\" IS NULL AND \"tipo_titular\" IS NOT NULL");
            entity.HasOne<Cuenta>().WithMany().HasForeignKey(e => e.CuentaId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AlertaDestinatario>(entity =>
        {
            entity.ToTable("ALERTA_DESTINATARIOS");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AlertaId, e.UsuarioId }).IsUnique();
            entity.HasOne<AlertaSaldo>().WithMany().HasForeignKey(e => e.AlertaId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Auditoria>(entity =>
        {
            entity.ToTable("AUDITORIAS");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IpAddress).HasColumnType("inet");
            entity.Property(e => e.DetallesJson).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.UsuarioId, e.Timestamp });
            entity.HasIndex(e => e.TipoAccion);
            entity.HasIndex(e => e.EntidadId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IntegrationToken>(entity =>
        {
            entity.ToTable("INTEGRATION_TOKENS");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.Estado);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioCreadorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.DeletedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IntegrationPermission>(entity =>
        {
            entity.ToTable("INTEGRATION_PERMISSIONS");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenId);
            entity.HasIndex(e => e.TitularId);
            entity.HasIndex(e => e.CuentaId);
            entity.HasOne<IntegrationToken>().WithMany().HasForeignKey(e => e.TokenId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Titular>().WithMany().HasForeignKey(e => e.TitularId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Cuenta>().WithMany().HasForeignKey(e => e.CuentaId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditoriaIntegracion>(entity =>
        {
            entity.ToTable("AUDITORIA_INTEGRACIONES");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Parametros).HasColumnType("jsonb");
            entity.Property(e => e.IpAddress).HasColumnType("inet");
            entity.HasIndex(e => e.TokenId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.CodigoRespuesta);
            entity.HasOne<IntegrationToken>().WithMany().HasForeignKey(e => e.TokenId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TipoCambio>(entity =>
        {
            entity.ToTable("TIPOS_CAMBIO");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Tasa).HasPrecision(18, 8);
            entity.HasIndex(e => new { e.DivisaOrigen, e.DivisaDestino }).IsUnique();
        });

        modelBuilder.Entity<DivisaActiva>(entity =>
        {
            entity.ToTable("DIVISAS_ACTIVAS");
            entity.HasKey(e => e.Codigo);
        });

        modelBuilder.Entity<Configuracion>(entity =>
        {
            entity.ToTable("CONFIGURACION");
            entity.HasKey(e => e.Clave);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.UsuarioModificacionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Backup>(entity =>
        {
            entity.ToTable("BACKUPS");
            entity.HasKey(e => e.Id);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.IniciadoPorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.DeletedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Exportacion>(entity =>
        {
            entity.ToTable("EXPORTACIONES");
            entity.HasKey(e => e.Id);
            entity.HasOne<Cuenta>().WithMany().HasForeignKey(e => e.CuentaId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.IniciadoPorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(e => e.DeletedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NotificacionAdmin>(entity =>
        {
            entity.ToTable("NOTIFICACIONES_ADMIN");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DetallesJson).HasColumnType("jsonb");
        });

        ApplySoftDeleteQueryFilters(modelBuilder);
    }

    private static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var deletedAtProp = Expression.Property(parameter, nameof(ISoftDelete.DeletedAt));
            var nullConstant = Expression.Constant(null, typeof(DateTime?));
            var body = Expression.Equal(deletedAtProp, nullConstant);
            var lambda = Expression.Lambda(body, parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }
}
