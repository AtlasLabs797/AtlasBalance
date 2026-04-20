# Documentacion de usuario

## Ubicacion principal

La aplicacion esta en la carpeta `Atlas Balance`.

## Paquetes de instalacion

Los paquetes de release estan en:

```text
Atlas Balance/Atlas Balance Release
```

Para instalar o actualizar desde una build local, usa los archivos del paquete generado para la version correspondiente.

Scripts principales del paquete:

- `install.cmd`: instala dependencias, PostgreSQL, servicios y configuracion.
- `start.cmd`: arranca PostgreSQL gestionado, Watchdog y API; el frontend va servido por la API.
- `update.cmd`: actualiza binarios y aplica migraciones al arrancar.
- `uninstall.cmd`: elimina la instalacion y la base gestionada si fue creada por el instalador.

La distribucion oficial de paquetes se publica como asset en GitHub Releases:

```text
https://github.com/AtlasLabs797/AtlasBalance/releases
```

## Documentacion util

- Instalacion y actualizaciones: `Documentacion/documentacion.md`
- Version actual: `Documentacion/Versiones/version_actual.md`
- Cambios tecnicos: `Documentacion/DOCUMENTACION_CAMBIOS.md`

## Notas de seguridad

- No guardes contrasenas en documentos.
- No pegues tokens ni credenciales en tickets, logs o notas.
- Las credenciales iniciales de instalacion deben tratarse como temporales y cambiarse en el primer acceso.
- Los archivos `appsettings.Development.json`, `appsettings.Production.json` y `.env` son locales del servidor o del entorno de desarrollo. No van a Git.
- Para desarrollo local, copia las plantillas `appsettings.*.json.template`, rellena secretos propios y define `ATLAS_BALANCE_POSTGRES_PASSWORD` en un `.env` local.
- Si la aplicacion arranca por primera vez con una base vacia, `SeedAdmin:Password` debe estar configurado; ya no existe una password admin por defecto. Bien. Eso era una mala idea.
- Las claves SMTP y de Exchange Rate API guardadas desde Configuracion se protegen automaticamente; las existentes en claro se migran al siguiente arranque.
- No borres, muevas ni copies a otra maquina `%ProgramData%/AtlasBalance/keys` en produccion sin plan de rotacion: ahi vive el keyring protegido que permite leer secretos cifrados.
- Las descargas de exportaciones solo sirven `.xlsx` generados dentro de la ruta `export_path`.
- Los scripts manuales de backup/restauracion piden la password en consola segura y no deben ejecutarse con passwords pegadas en comandos o documentos.
- En produccion, `AllowedHosts` debe contener el hostname real. `*` ya no arranca; comodo, pero inseguro.
