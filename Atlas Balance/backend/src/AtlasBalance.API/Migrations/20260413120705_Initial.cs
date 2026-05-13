using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasBalance.API.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:estado_proceso", "pending,success,failed")
                .Annotation("Npgsql:Enum:estado_token_integracion", "activo,revocado")
                .Annotation("Npgsql:Enum:fuente_tipo_cambio", "api,manual")
                .Annotation("Npgsql:Enum:rol_usuario", "admin,gerente,empleado_ultra,empleado_plus,empleado")
                .Annotation("Npgsql:Enum:tipo_proceso", "auto,manual")
                .Annotation("Npgsql:Enum:tipo_titular", "empresa,particular")
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,");

            migrationBuilder.CreateTable(
                name: "DIVISAS_ACTIVAS",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: true),
                    simbolo = table.Column<string>(type: "text", nullable: true),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    es_base = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_divisas_activas", x => x.codigo);
                });

            migrationBuilder.CreateTable(
                name: "NOTIFICACIONES_ADMIN",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: true),
                    mensaje = table.Column<string>(type: "text", nullable: true),
                    leida = table.Column<bool>(type: "boolean", nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    detalles_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notificaciones_admin", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "TIPOS_CAMBIO",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    divisa_origen = table.Column<string>(type: "text", nullable: false),
                    divisa_destino = table.Column<string>(type: "text", nullable: false),
                    tasa = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    fecha_actualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fuente = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tipos_cambio", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "USUARIOS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    nombre_completo = table.Column<string>(type: "text", nullable: false),
                    rol = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    primer_login = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_ultima_login = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_login_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "AUDITORIAS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_accion = table.Column<string>(type: "text", nullable: false),
                    entidad_tipo = table.Column<string>(type: "text", nullable: true),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: true),
                    celda_referencia = table.Column<string>(type: "text", nullable: true),
                    columna_nombre = table.Column<string>(type: "text", nullable: true),
                    valor_anterior = table.Column<string>(type: "text", nullable: true),
                    valor_nuevo = table.Column<string>(type: "text", nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    detalles_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditorias", x => x.id);
                    table.ForeignKey(
                        name: "fk_auditorias_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BACKUPS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ruta_archivo = table.Column<string>(type: "text", nullable: false),
                    tamanio_bytes = table.Column<long>(type: "bigint", nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    iniciado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notas = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backups", x => x.id);
                    table.ForeignKey(
                        name: "fk_backups_usuarios_deleted_by_id",
                        column: x => x.deleted_by_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_backups_usuarios_iniciado_por_id",
                        column: x => x.iniciado_por_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CONFIGURACION",
                columns: table => new
                {
                    clave = table.Column<string>(type: "text", nullable: false),
                    valor = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: true),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    fecha_modificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    usuario_modificacion_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion", x => x.clave);
                    table.ForeignKey(
                        name: "fk_configuracion_usuarios_usuario_modificacion_id",
                        column: x => x.usuario_modificacion_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FORMATOS_IMPORTACION",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    banco_nombre = table.Column<string>(type: "text", nullable: true),
                    divisa = table.Column<string>(type: "text", nullable: true),
                    mapeo_json = table.Column<string>(type: "jsonb", nullable: false),
                    usuario_creador_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_formatos_importacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_formatos_importacion_usuarios_deleted_by_id",
                        column: x => x.deleted_by_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_formatos_importacion_usuarios_usuario_creador_id",
                        column: x => x.usuario_creador_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "INTEGRATION_TOKENS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    permiso_lectura = table.Column<bool>(type: "boolean", nullable: false),
                    permiso_escritura = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_ultima_uso = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fecha_revocacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    usuario_creador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_integration_tokens_usuarios_deleted_by_id",
                        column: x => x.deleted_by_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_integration_tokens_usuarios_usuario_creador_id",
                        column: x => x.usuario_creador_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REFRESH_TOKENS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expira_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revocado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reemplazado_por = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TITULARES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    identificacion = table.Column<string>(type: "text", nullable: true),
                    contacto_email = table.Column<string>(type: "text", nullable: true),
                    contacto_telefono = table.Column<string>(type: "text", nullable: true),
                    notas = table.Column<string>(type: "text", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_titulares", x => x.id);
                    table.ForeignKey(
                        name: "fk_titulares_usuarios_deleted_by_id",
                        column: x => x.deleted_by_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "USUARIO_EMAILS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    es_principal = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_emails", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuario_emails_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AUDITORIA_INTEGRACIONES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "text", nullable: false),
                    metodo = table.Column<string>(type: "text", nullable: false),
                    parametros = table.Column<string>(type: "jsonb", nullable: true),
                    codigo_respuesta = table.Column<int>(type: "integer", nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    tiempo_ejecucion_ms = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditoria_integraciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_auditoria_integraciones_integration_tokens_token_id",
                        column: x => x.token_id,
                        principalTable: "INTEGRATION_TOKENS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CUENTAS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titular_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    numero_cuenta = table.Column<string>(type: "text", nullable: true),
                    iban = table.Column<string>(type: "text", nullable: true),
                    banco_nombre = table.Column<string>(type: "text", nullable: true),
                    divisa = table.Column<string>(type: "text", nullable: false),
                    formato_id = table.Column<Guid>(type: "uuid", nullable: true),
                    es_efectivo = table.Column<bool>(type: "boolean", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuentas", x => x.id);
                    table.ForeignKey(
                        name: "fk_cuentas_formatos_importacion_formato_id",
                        column: x => x.formato_id,
                        principalTable: "FORMATOS_IMPORTACION",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cuentas_titulares_titular_id",
                        column: x => x.titular_id,
                        principalTable: "TITULARES",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cuentas_usuarios_deleted_by_id",
                        column: x => x.deleted_by_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ALERTAS_SALDO",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    saldo_minimo = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_ultima_alerta = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alertas_saldo", x => x.id);
                    table.ForeignKey(
                        name: "fk_alertas_saldo_cuentas_cuenta_id",
                        column: x => x.cuenta_id,
                        principalTable: "CUENTAS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EXPORTACIONES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_exportacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ruta_archivo = table.Column<string>(type: "text", nullable: true),
                    tamanio_bytes = table.Column<long>(type: "bigint", nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    iniciado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exportaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_exportaciones_cuentas_cuenta_id",
                        column: x => x.cuenta_id,
                        principalTable: "CUENTAS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exportaciones_usuarios_deleted_by_id",
                        column: x => x.deleted_by_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exportaciones_usuarios_iniciado_por_id",
                        column: x => x.iniciado_por_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EXTRACTOS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    concepto = table.Column<string>(type: "text", nullable: true),
                    monto = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    saldo = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fila_numero = table.Column<int>(type: "integer", nullable: false),
                    @checked = table.Column<bool>(name: "checked", type: "boolean", nullable: false),
                    checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    checked_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    flagged = table.Column<bool>(type: "boolean", nullable: false),
                    flagged_nota = table.Column<string>(type: "text", nullable: true),
                    flagged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    flagged_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_creacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usuario_modificacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_modificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extractos", x => x.id);
                    table.ForeignKey(
                        name: "fk_extractos_cuentas_cuenta_id",
                        column: x => x.cuenta_id,
                        principalTable: "CUENTAS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_extractos_usuarios_checked_by_id",
                        column: x => x.checked_by_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_extractos_usuarios_deleted_by_id",
                        column: x => x.deleted_by_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_extractos_usuarios_flagged_by_id",
                        column: x => x.flagged_by_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_extractos_usuarios_usuario_creacion_id",
                        column: x => x.usuario_creacion_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_extractos_usuarios_usuario_modificacion_id",
                        column: x => x.usuario_modificacion_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "INTEGRATION_PERMISSIONS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titular_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acceso_tipo = table.Column<string>(type: "text", nullable: false),
                    fecha_creacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_permissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_integration_permissions_cuentas_cuenta_id",
                        column: x => x.cuenta_id,
                        principalTable: "CUENTAS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_integration_permissions_integration_tokens_token_id",
                        column: x => x.token_id,
                        principalTable: "INTEGRATION_TOKENS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_integration_permissions_titulares_titular_id",
                        column: x => x.titular_id,
                        principalTable: "TITULARES",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PERMISOS_USUARIO",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    titular_id = table.Column<Guid>(type: "uuid", nullable: true),
                    puede_agregar_lineas = table.Column<bool>(type: "boolean", nullable: false),
                    puede_editar_lineas = table.Column<bool>(type: "boolean", nullable: false),
                    puede_eliminar_lineas = table.Column<bool>(type: "boolean", nullable: false),
                    puede_importar = table.Column<bool>(type: "boolean", nullable: false),
                    puede_ver_dashboard = table.Column<bool>(type: "boolean", nullable: false),
                    columnas_visibles = table.Column<string>(type: "jsonb", nullable: true),
                    columnas_editables = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permisos_usuario", x => x.id);
                    table.ForeignKey(
                        name: "fk_permisos_usuario_cuentas_cuenta_id",
                        column: x => x.cuenta_id,
                        principalTable: "CUENTAS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_permisos_usuario_titulares_titular_id",
                        column: x => x.titular_id,
                        principalTable: "TITULARES",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_permisos_usuario_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ALERTA_DESTINATARIOS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    alerta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerta_destinatarios", x => x.id);
                    table.ForeignKey(
                        name: "fk_alerta_destinatarios_alertas_saldo_alerta_id",
                        column: x => x.alerta_id,
                        principalTable: "ALERTAS_SALDO",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_alerta_destinatarios_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "USUARIOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EXTRACTOS_COLUMNAS_EXTRA",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    extracto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_columna = table.Column<string>(type: "text", nullable: false),
                    valor = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extractos_columnas_extra", x => x.id);
                    table.ForeignKey(
                        name: "fk_extractos_columnas_extra_extractos_extracto_id",
                        column: x => x.extracto_id,
                        principalTable: "EXTRACTOS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alerta_destinatarios_alerta_id_usuario_id",
                table: "ALERTA_DESTINATARIOS",
                columns: new[] { "alerta_id", "usuario_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_alerta_destinatarios_usuario_id",
                table: "ALERTA_DESTINATARIOS",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_alertas_saldo_cuenta_id",
                table: "ALERTAS_SALDO",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_integraciones_codigo_respuesta",
                table: "AUDITORIA_INTEGRACIONES",
                column: "codigo_respuesta");

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_integraciones_timestamp",
                table: "AUDITORIA_INTEGRACIONES",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_integraciones_token_id",
                table: "AUDITORIA_INTEGRACIONES",
                column: "token_id");

            migrationBuilder.CreateIndex(
                name: "ix_auditorias_entidad_id",
                table: "AUDITORIAS",
                column: "entidad_id");

            migrationBuilder.CreateIndex(
                name: "ix_auditorias_timestamp",
                table: "AUDITORIAS",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_auditorias_tipo_accion",
                table: "AUDITORIAS",
                column: "tipo_accion");

            migrationBuilder.CreateIndex(
                name: "ix_auditorias_usuario_id_timestamp",
                table: "AUDITORIAS",
                columns: new[] { "usuario_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_backups_deleted_by_id",
                table: "BACKUPS",
                column: "deleted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_backups_iniciado_por_id",
                table: "BACKUPS",
                column: "iniciado_por_id");

            migrationBuilder.CreateIndex(
                name: "ix_configuracion_usuario_modificacion_id",
                table: "CONFIGURACION",
                column: "usuario_modificacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_activa",
                table: "CUENTAS",
                column: "activa");

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_deleted_at",
                table: "CUENTAS",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_deleted_by_id",
                table: "CUENTAS",
                column: "deleted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_divisa",
                table: "CUENTAS",
                column: "divisa");

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_es_efectivo",
                table: "CUENTAS",
                column: "es_efectivo");

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_formato_id",
                table: "CUENTAS",
                column: "formato_id");

            migrationBuilder.CreateIndex(
                name: "ix_cuentas_titular_id",
                table: "CUENTAS",
                column: "titular_id");

            migrationBuilder.CreateIndex(
                name: "ix_exportaciones_cuenta_id",
                table: "EXPORTACIONES",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ix_exportaciones_deleted_by_id",
                table: "EXPORTACIONES",
                column: "deleted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_exportaciones_iniciado_por_id",
                table: "EXPORTACIONES",
                column: "iniciado_por_id");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_checked",
                table: "EXTRACTOS",
                column: "checked");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_checked_by_id",
                table: "EXTRACTOS",
                column: "checked_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_cuenta_id_deleted_at",
                table: "EXTRACTOS",
                columns: new[] { "cuenta_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_extractos_cuenta_id_fecha",
                table: "EXTRACTOS",
                columns: new[] { "cuenta_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_extractos_cuenta_id_fila_numero",
                table: "EXTRACTOS",
                columns: new[] { "cuenta_id", "fila_numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_extractos_deleted_by_id",
                table: "EXTRACTOS",
                column: "deleted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_fecha",
                table: "EXTRACTOS",
                column: "fecha");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_flagged",
                table: "EXTRACTOS",
                column: "flagged");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_flagged_by_id",
                table: "EXTRACTOS",
                column: "flagged_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_usuario_creacion_id",
                table: "EXTRACTOS",
                column: "usuario_creacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_usuario_modificacion_id",
                table: "EXTRACTOS",
                column: "usuario_modificacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_columnas_extra_extracto_id",
                table: "EXTRACTOS_COLUMNAS_EXTRA",
                column: "extracto_id");

            migrationBuilder.CreateIndex(
                name: "ix_extractos_columnas_extra_nombre_columna",
                table: "EXTRACTOS_COLUMNAS_EXTRA",
                column: "nombre_columna");

            migrationBuilder.CreateIndex(
                name: "ix_formatos_importacion_deleted_by_id",
                table: "FORMATOS_IMPORTACION",
                column: "deleted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_formatos_importacion_usuario_creador_id",
                table: "FORMATOS_IMPORTACION",
                column: "usuario_creador_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_permissions_cuenta_id",
                table: "INTEGRATION_PERMISSIONS",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_permissions_titular_id",
                table: "INTEGRATION_PERMISSIONS",
                column: "titular_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_permissions_token_id",
                table: "INTEGRATION_PERMISSIONS",
                column: "token_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_tokens_deleted_by_id",
                table: "INTEGRATION_TOKENS",
                column: "deleted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_tokens_estado",
                table: "INTEGRATION_TOKENS",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_integration_tokens_token_hash",
                table: "INTEGRATION_TOKENS",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_tokens_usuario_creador_id",
                table: "INTEGRATION_TOKENS",
                column: "usuario_creador_id");

            migrationBuilder.CreateIndex(
                name: "ix_permisos_usuario_cuenta_id",
                table: "PERMISOS_USUARIO",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ix_permisos_usuario_titular_id",
                table: "PERMISOS_USUARIO",
                column: "titular_id");

            migrationBuilder.CreateIndex(
                name: "ix_permisos_usuario_usuario_id",
                table: "PERMISOS_USUARIO",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_permisos_usuario_usuario_id_cuenta_id",
                table: "PERMISOS_USUARIO",
                columns: new[] { "usuario_id", "cuenta_id" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expira_en",
                table: "REFRESH_TOKENS",
                column: "expira_en");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "REFRESH_TOKENS",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_usuario_id",
                table: "REFRESH_TOKENS",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_tipos_cambio_divisa_origen_divisa_destino",
                table: "TIPOS_CAMBIO",
                columns: new[] { "divisa_origen", "divisa_destino" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_titulares_deleted_at",
                table: "TITULARES",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_titulares_deleted_by_id",
                table: "TITULARES",
                column: "deleted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_titulares_nombre",
                table: "TITULARES",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "ix_titulares_tipo",
                table: "TITULARES",
                column: "tipo");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_emails_usuario_id",
                table: "USUARIO_EMAILS",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_activo",
                table: "USUARIOS",
                column: "activo");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "USUARIOS",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_rol",
                table: "USUARIOS",
                column: "rol");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ALERTA_DESTINATARIOS");

            migrationBuilder.DropTable(
                name: "AUDITORIA_INTEGRACIONES");

            migrationBuilder.DropTable(
                name: "AUDITORIAS");

            migrationBuilder.DropTable(
                name: "BACKUPS");

            migrationBuilder.DropTable(
                name: "CONFIGURACION");

            migrationBuilder.DropTable(
                name: "DIVISAS_ACTIVAS");

            migrationBuilder.DropTable(
                name: "EXPORTACIONES");

            migrationBuilder.DropTable(
                name: "EXTRACTOS_COLUMNAS_EXTRA");

            migrationBuilder.DropTable(
                name: "INTEGRATION_PERMISSIONS");

            migrationBuilder.DropTable(
                name: "NOTIFICACIONES_ADMIN");

            migrationBuilder.DropTable(
                name: "PERMISOS_USUARIO");

            migrationBuilder.DropTable(
                name: "REFRESH_TOKENS");

            migrationBuilder.DropTable(
                name: "TIPOS_CAMBIO");

            migrationBuilder.DropTable(
                name: "USUARIO_EMAILS");

            migrationBuilder.DropTable(
                name: "ALERTAS_SALDO");

            migrationBuilder.DropTable(
                name: "EXTRACTOS");

            migrationBuilder.DropTable(
                name: "INTEGRATION_TOKENS");

            migrationBuilder.DropTable(
                name: "CUENTAS");

            migrationBuilder.DropTable(
                name: "FORMATOS_IMPORTACION");

            migrationBuilder.DropTable(
                name: "TITULARES");

            migrationBuilder.DropTable(
                name: "USUARIOS");
        }
    }
}
