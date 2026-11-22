# Archivo de documentacion anterior a V-03

Contenido retirado del arbol vivo de `Documentacion/` en el **LOTE 8** de la revision
integral (rama V-03.02), el **2026-11-18**. ID **M-17** del informe
`Documentacion/INFORME_REVISION_INTEGRAL_2026-11-16.md` (seccion 8.2).

**Se archiva, no se borra.** Todo lo que hay en esta carpeta sigue en git: los
movimientos son renombrados (`git status` los muestra como `R`), asi que se deshacen
con `git revert` o moviendo el fichero de vuelta. **No se ha reescrito el contenido de
ningun informe**: se han movido tal cual.

Motivo del archivado: `Documentacion/` acumulaba 17 informes de auditoria, revision y
notas de versiones anteriores a V-03 que ya no son fuente de verdad (los cierran
versiones posteriores y la auditoria viva
`Documentacion/AUDITORIA_CHECKLIST_SEGURIDAD_RENDIMIENTO_2026-11-16.md`). Esta carpeta
es el historico consultable; la documentacion viva sigue en la raiz de `Documentacion/`.

## Que hay aqui

### Informes de auditoria y revision (venian de la raiz de `Documentacion/`)

| Fichero | Version | Fecha | Que es |
|---|---|---|---|
| `INFORME_INCIDENTE_LOGIN_MFA_V0207.md` | V-02.07 | 2026-08-05 | Incidencias de acceso (login y MFA) en la instalacion on-premise y correcciones preparadas bajo V-02.08. |
| `REVIEW_REPORT_2026-06-30.md` | V-02.02 | 2026-06-30 | Revision tecnica de solo lectura del backend y el frontend, con severidades CRITICAL/HIGH/MEDIUM/LOW/INFO. |
| `AUDITORIA_SEGURIDAD_PRE_LAUNCH_2026-08-04.md` | V-02.07 | 2026-08-04 | Auditoria de seguridad previa al lanzamiento (13 controles) y dictamen con los gates operativos pendientes. |
| `AUDITORIA_CONCURRENCIA_2026-07-10.md` | V-02.04 | 2026-07-10 | Auditoria de concurrencia e integridad de datos (hallazgos `CONC-001`...). |
| `AUDITORIA_SEGURIDAD_BUGS_PRE_INTERNET_2026-07-10.md` | V-02.04 | 2026-07-10 | Analisis de seguridad y bugs abiertos antes de exponer la aplicacion a internet. |
| `INFORME_REVISION_2026-09-24.md` | V-03.01 | 2026-09-24 | Informe de revision del codigo nuevo de V-03.01 (9 hallazgos cerrados en `REGISTRO_BUGS.md`). |
| `AUDITORIA_USO_BUGS_SEGURIDAD_V-01.05_2026-04-25.md` | V-01.05 | 2026-04-25 | Auditoria de uso, bugs y seguridad de la primera version entregable. |
| `SEGURIDAD_AUDITORIA_V-01.02.md` | V-01.02 | 2026-04-20 | Auditoria de seguridad inicial (credenciales en ficheros auxiliares, entre otros). |
| `SEGURIDAD_AUDITORIA_V-01.03.md` | V-01.03 | 2026-04-25 | Auditoria de seguridad de la V-01.03 (cache distribuida, entre otros). |
| `SEGURIDAD_AUDITORIA_V-01.05.md` | V-01.05 | 2026-04-25 (addendum 2026-05-01) | Auditoria de seguridad de la V-01.05 y su addendum posterior. |
| `SEGURIDAD_AUDITORIA_V-01.06.md` | V-01.06 | 2026-05-12 | Auditoria de seguridad de la V-01.06. |
| `SEGURIDAD_AUDITORIA_V-02-04.md` | V-02.04 | 2026-07-02 | Auditoria de seguridad completa (OWASP Top 10 2025 / CWE Top 25) con `npm audit` y barrido de secretos. |
| `SEGURIDAD_CHECKLIST_APP_V-01.05_2026-05-01.md` | V-01.05 | 2026-05-01 | Checklist de seguridad aplicado a la aplicacion. |

### Informes de diseno (venian de `Documentacion/Diseno/`)

| Fichero | Version | Que es |
|---|---|---|
| `AUDITORIA_UI_UX_GLOBAL_V-02-02.md` | V-02.02 | Informe global UI/UX premium y plan de mejora coordinado. |
| `AUDITORIA_GENERAL_MAVIS_V-02-02.md` | V-02.02 | Auditoria general de solo lectura (frontend, backend y permisos). |
| `AUDITORIA_UI_UX_SKILLS_V-02-02.md` | V-02.02 | Auditoria UI/UX por skills, con capturas de pantalla de la V-02-02. |

Los tres han quedado en `Documentacion/Archivo/` (y no en `Documentacion/Diseno/Archivo/`)
porque esa carpeta la creo otro agente del mismo lote para el ID **M-11** y su `README.md`
no se ha tocado.

### Notas y logs de versiones antiguas (venian de la raiz de `Documentacion/`)

| Fichero | Version | Fecha | Que es |
|---|---|---|---|
| `INCIDENCIAS_INSTALACION_WINDOWS_SERVER_2019_V-01.05.txt` | V-01.05 | 2026-04-25 | Log de incidencias de instalacion en Windows Server 2019. |
| `mejoradiseno.md` | (V-01.05) | 2026-04-25 | Informe de mejora UI/UX aplicado con skills locales; superado por `Documentacion/Diseno/DESIGN.md` y `Documentacion/Diseno/design-system/`. |

## Lo que NO se ha movido, y por que

- `DIAGNOSTICO_PERMISOS_CUENTAS_2026-10-05.md` - es de **V-03.02** (no anterior a V-03)
  y lo citan `Documentacion/Versiones/v-03.02.md` y `Documentacion/REGISTRO_BUGS.md`.
- `AUDITORIA_CHECKLIST_SEGURIDAD_RENDIMIENTO_2026-11-16.md` - auditoria viva de V-03.02.
- `CORRECCIONES.md` - companion de `Documentacion/SPEC.md`, no un informe. Sus correcciones
  ya estan aplicadas en `SPEC.md` y resumidas en `AGENTS.md` (seccion 12).
- `SEGURIDAD_RESPUESTA_INCIDENTES.md` - procedimiento de respuesta ante incidentes de
  seguridad, no un informe; no hay documento que lo sustituya.
- `Documentacion/Versiones/` - historico por diseno, se consulta.
- `Documentacion/Diseno/Archivo/` - archivo de material de diseno del ID M-11; intacto.

## Referencias

Referencias por ruta actualizadas a la nueva ubicacion (una linea por referencia):

| Fichero | Referencias actualizadas |
|---|---|
| `Documentacion/AUDITORIA_CHECKLIST_SEGURIDAD_RENDIMIENTO_2026-11-16.md` | 2 |
| `Documentacion/DOCUMENTACION_TECNICA.md` | 4 |
| `Documentacion/REGISTRO_BUGS.md` | 6 |
| `Documentacion/LOG_ERRORES_INCIDENCIAS.md` | 4 |
| `Documentacion/Archivo/SEGURIDAD_AUDITORIA_V-01.05.md` | 1 |
| `Documentacion/Archivo/AUDITORIA_GENERAL_MAVIS_V-02-02.md` | 1 |

Menciones que siguen citando el nombre antiguo y **no** se han tocado, por estar fuera
del alcance de M-17:

- `Documentacion/Versiones/*.md` - historico de versiones, no se modifica.
- `Documentacion/INFORME_REVISION_INTEGRAL_2026-11-16.md` - el informe fuente de este
  lote, no se modifica.
- `Documentacion/Diseno/INFORME_IMPECCABLE_CRITIQUE_AUDIT_V-03.02.md` (linea 13) - cita en
  prosa de `AUDITORIA_UI_UX_GLOBAL_V-02-02.md`; el fichero no se puede tocar en este lote.
- `Atlas Balance/backend/src/AtlasBalance.API/Caching/CacheService.cs` (linea 19) -
  comentario de codigo que cita `AUDITORIA_CONCURRENCIA_2026-07-10.md`; `backend/**` y
  `frontend/**` no se tocan en este lote.
- `Documentacion/DOCUMENTACION_CAMBIOS_ARCHIVO.md` - historico de la bitacora: las menciones
  describen el estado de los ficheros en el momento en que se escribieron.
