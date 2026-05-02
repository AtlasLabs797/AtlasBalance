# Respuesta ante incidentes de seguridad

Version: V-01.05

## Cuando activar este plan

Activarlo ante cualquiera de estos casos:

- Login sospechoso o fuerza bruta sostenida.
- Cuenta admin o gerente comprometida.
- Token de integracion OpenClaw filtrado.
- Sospecha de acceso a cuentas/titulares fuera de permiso.
- Backup, exportacion o appsettings expuesto.
- Update descargado rechazado por digest o sospecha de paquete manipulado.
- Logs con secretos o datos financieros innecesarios.

## Primeros 30 minutos

1. Cortar el vector:
   - Desactivar usuario afectado.
   - Revocar tokens de integracion afectados.
   - Bloquear update automatico si el incidente apunta a releases.
   - Parar temporalmente el endpoint afectado si hay fuga activa.

2. Preservar evidencia:
   - Exportar auditoria filtrada por usuario/token/IP/horario.
   - Guardar logs de API/Watchdog.
   - No editar logs originales.

3. Reducir alcance:
   - Rotar `JwtSettings:Secret` si hay sospecha de JWT comprometido.
   - Rotar `WatchdogSettings:SharedSecret` si hay sospecha de operaciones Watchdog.
   - Rotar token de GitHub updates si existe.
   - Rotar SMTP/API key si aparecen en logs o archivos.

## Revocacion rapida

- Usuarios: desactivar desde `Usuarios` o resetear password con el script soportado.
- Sesiones: cualquier cambio de password, permisos, perfil o email revoca refresh tokens del usuario afectado.
- Integraciones: revocar token desde `Integraciones`.
- MFA: si un autenticador queda comprometido, resetear el usuario y forzar nuevo enrolamiento MFA.

## Restauracion

- Restaurar solo desde backups de origen conocido.
- Verificar que el backup pertenece a la ruta esperada y no fue reemplazado.
- Despues de restaurar:
  - Revisar usuarios admin.
  - Revisar tokens de integracion activos.
  - Revisar ultimos cambios de permisos.
  - Ejecutar health check.

## Postmortem obligatorio

Registrar en `Documentacion/LOG_ERRORES_INCIDENCIAS.md`:

- Fecha y ventana temporal.
- Vector de entrada.
- Usuarios, tokens, cuentas o titulares afectados.
- Datos expuestos o modificados.
- Acciones de contencion.
- Correccion aplicada.
- Tests o controles agregados.
- Pendientes operativos.

No documentar secretos, tokens completos, passwords, cookies ni datos financieros privados.
