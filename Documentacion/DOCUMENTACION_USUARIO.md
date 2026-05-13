# Documentacion de usuario

## Ubicacion principal

La aplicacion esta en la carpeta `Atlas Balance`.

## Navegacion y dashboard

Desde `V-01.05`, el menu se organiza en tres bloques:

- `Operacion`: Dashboard, Titulares, Cuentas, Extractos e Importacion.
- `Control`: Alertas y Exportaciones.
- `Sistema`: Usuarios, Auditoria, Formatos, Backups, Configuracion y Papelera.

En movil, la barra inferior muestra solo los accesos principales: Inicio, Titulares, Cuentas, Importar y Mas. El boton `Mas` abre el resto de secciones.

El dashboard principal prioriza la lectura financiera:

- Saldo total en la divisa base.
- Porcentajes de variacion compactos bajo los KPIs principales, sin texto comparativo adicional.
- Saldos por divisa, separando disponible e inmovilizado. La divisa base aparece siempre primero.
- Plazos fijos debajo del resumen de saldo, ingresos y egresos.
- Evolucion del periodo en una grafica ancha para leer la tendencia sin pelearse con tarjetas laterales.
- Saldos por titular en la parte inferior, agrupados en tres columnas: Empresa, Autonomo y Particular.

## Desglose de cuenta

En el dashboard de una cuenta, el panel `Periodo / Volver al titular / Ver en extractos / Importar movimientos` queda a la derecha del titulo y alineado con la ficha de cuenta para que el encabezado no salte visualmente.

En el desglose, la seleccion por checkbox esta en la primera columna. Si tu usuario tiene permiso para agregar lineas, el icono `+` aparece al pasar el cursor entre dos filas y abre el formulario justo en esa posicion.

La nueva linea se rellena en el formulario que aparece entre filas. Al guardar, Atlas Balance renumera las lineas necesarias y conserva ese orden en la base de datos. La vista de cuenta se ordena por numero de fila para respetar esa secuencia.

El flag se aplica desde el boton superior con icono de banderola y solo afecta a las filas seleccionadas. La eliminacion de lineas tambien se hace desde la accion superior sobre seleccion. Estas acciones actualizan la tabla sin recargar la pagina ni mover el scroll.

Si la cuenta tiene muchos movimientos, el desglose usa paginacion. Ya no se corta silenciosamente en las primeras 500 lineas.

## Extractos

La vista `Extractos` usa una reticula de celdas tipo hoja de calculo. Las columnas visibles mantienen ancho estable y la cabecera queda alineada con las filas aunque haya muchas columnas extra.

Si falla la carga de movimientos, preferencias de columnas o auditoria de una celda, la pantalla muestra el error y permite reintentar. Si intentas ocultar columnas, siempre queda al menos una visible.

## Acceso con Google Authenticator

Atlas Balance usa MFA con aplicaciones compatibles tipo Google Authenticator.

La primera vez que entras, despues de email y contrasena, aparece un QR. Escanealo con Google Authenticator y escribe el codigo de 6 digitos. Si el QR no se puede escanear, usa la clave manual que aparece debajo.

Despues de verificarlo, puedes marcar `Recordar este dispositivo durante 30 dias`. Si no marcas esa casilla, el codigo MFA se pedira en el siguiente login. Se volvera a pedir tambien cuando pasen esos 30 dias, cierres sesion y borres cookies, cambie la seguridad del usuario o uses otro navegador/equipo.

## Paquetes de instalacion

Los paquetes de release estan en:

```text
Atlas Balance/Atlas Balance Release
```

Paquete esperado para la version actual `V-01.06`:

```text
AtlasBalance-V-01.06-win-x64.zip
AtlasBalance-V-01.06-win-x64.zip.sig
```

SHA256 del ZIP firmado generado el 2026-05-13:

```text
95DCA977E145DE07BF41E5B6478AD856BF803E4938A0A98480ABB043F51781E1
```

No reutilices hashes ni paquetes de `V-01.05` para publicar `V-01.06`.

Para instalar o actualizar desde una build local, usa los archivos del paquete generado para la version correspondiente.

No instales desde el ZIP `main` de GitHub ni desde una carpeta fuente. El paquete instalable debe llamarse como `AtlasBalance-V-01.06-win-x64.zip` y contener `api\AtlasBalance.API.exe`, `watchdog\AtlasBalance.Watchdog.exe`, `scripts` y wrappers `.cmd`.

Para actualizacion desde la app, el release de GitHub debe incluir tambien `AtlasBalance-V-01.06-win-x64.zip.sig`. Si falta la firma, el actualizador online lo rechazara. Desde `V-01.06`, el script de release tambien falla si no hay clave de firma, salvo que se use `-AllowUnsignedLocal` para una prueba local que no se debe publicar. Bien rechazado: actualizar una app financiera sin firma es jugar con cerillas al lado de gasolina.

## Limpieza antes de publicar

Antes de publicar o entregar una base local, ejecuta la purga de entrega desde la carpeta `Atlas Balance`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Purge-DeliveryData.ps1" -ConfirmDeliveryPurge
```

Esto borra usuarios, titulares, cuentas, extractos, tokens, auditorias, backups/exportaciones registradas y consumo IA. Tambien deja vacias las claves SMTP, OpenRouter, OpenAI y tipos de cambio externos.

No ejecutes esta purga contra una base de cliente en produccion salvo que quieras dejarla vacia. Su nombre no es decorativo.

Tras purgar, el siguiente primer arranque creara el admin inicial solo si `SeedAdmin:Password` esta configurado. Si no lo esta, el backend fallara cerrado, que es exactamente lo correcto.

Scripts principales del paquete:

- `install.cmd`: instala dependencias, PostgreSQL, servicios y configuracion.
- `start.cmd`: arranca PostgreSQL gestionado, Watchdog y API; el frontend va servido por la API.
- `update.cmd`: actualiza binarios y aplica migraciones al arrancar.
- `uninstall.cmd`: elimina la instalacion y la base gestionada si fue creada por el instalador.

En Windows Server 2019, instala PostgreSQL 16+ manualmente si `winget` falla. PostgreSQL 17 es compatible. Para comprobar la API, usa:

```powershell
curl.exe -k -v https://NOMBRE_DEL_SERVIDOR/api/health
```

Si el navegador falla pero `curl.exe -k` responde, instala `C:\AtlasBalance\certs\atlas-balance.cer` como raiz confiable en el cliente.

Si reinstalas sobre una base existente, las credenciales iniciales no se regeneran. Usa el admin existente o ejecuta:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\AtlasBalance\scripts\Reset-AdminPassword.ps1" -InstallPath C:\AtlasBalance -AdminEmail admin@atlasbalance.local -GeneratePassword
```

El reset de password debe ejecutarse como Administrador. Si genera password temporal, la escribe en `C:\AtlasBalance\config\RESET_ADMIN_CREDENTIALS_ONCE.txt` con acceso limitado a Administrators/SYSTEM.

Para actualizar una instalacion ya existente, descomprime el paquete nuevo y ejecuta:

```powershell
.\update.cmd -InstallPath C:\AtlasBalance
```

Desde `V-01.05`, ese wrapper acepta `-InstallPath` directamente y crea backup antes de reemplazar binarios. Si una actualizacion anterior dejo la API parada por formatos de importacion duplicados, actualiza con un paquete `V-01.06` o posterior; el arranque ya no intenta duplicar esos formatos por ID fijo.

Si la instalacion ya tiene los scripts actualizados, tambien vale:

```powershell
C:\AtlasBalance\update.cmd -PackagePath C:\Temp\AtlasBalance-V-01.06-win-x64 -InstallPath C:\AtlasBalance
```

La distribucion oficial de paquetes se publica como asset en GitHub Releases:

```text
https://github.com/AtlasLabs797/AtlasBalance/releases
```

Tambien puedes actualizar desde la propia app:

1. Entra como admin.
2. Ve a `Configuracion > Sistema`.
3. Deja el repo `https://github.com/AtlasLabs797/AtlasBalance`.
4. Pulsa `Verificar actualizacion`.
5. Si hay version nueva, pulsa `Actualizar ahora`.

La app descarga el ZIP oficial `win-x64`, verifica su firma `.zip.sig`, crea backup PostgreSQL previo, rollback de binarios y comprueba `/api/health`. Si no puede verificar firma, crear backup o levantar la API despues, no deja la actualizacion como buena.

Para que la actualizacion online funcione, la instalacion debe tener configurada la clave publica de firma en `UpdateSecurity:ReleaseSigningPublicKeyPem` o en `ATLAS_RELEASE_SIGNING_PUBLIC_KEY_PEM`. Desde el paquete firmado `V-01.06`, el instalador escribe una clave publica por defecto si no se proporciona override. Sin clave publica valida, la app rechaza paquetes online. Es incomodo una vez; confiar en ZIPs sin firma seria peor.

## Seguridad de PostgreSQL

Desde `V-01.05`, Atlas Balance activa Row Level Security en PostgreSQL para las tablas sensibles de titulares, cuentas, extractos, plazos fijos, exportaciones, auditoria, backups y notificaciones.

Esto no cambia lo que ves en la app. Cambia lo importante: si una consulta backend sale mal filtrada, la base tambien aplica aislamiento por fila. Antes no lo hacia; eso era un agujero claro.

En instalaciones nuevas, PostgreSQL usa dos credenciales: una de migracion/owner y otra de aplicacion runtime. La app normal usa runtime, sin superusuario, sin ownership de tablas y sin `BYPASSRLS`. El contexto RLS va firmado; falsificar `atlas.system=true` a mano no basta.

En instalaciones existentes, la migracion activa RLS y firma de contexto. Si esa base antigua fue creada con el usuario de aplicacion como owner, merece migracion manual de ownership para dejar la frontera igual de fuerte que una instalacion nueva.

## Documentacion util

- Instalacion y actualizaciones: `Documentacion/documentacion.md`
- Version actual: `Documentacion/Versiones/version_actual.md`
- Cambios tecnicos: `Documentacion/DOCUMENTACION_CAMBIOS.md`

## Importacion de extractos

Para extractos grandes, la validacion muestra la tabla por paginas. No significa que se hayan perdido filas: usa `Anterior` y `Siguiente` para revisar el resto antes de confirmar.

En la pantalla de validacion, las filas con concepto pero sin fecha ni monto ya no bloquean la importacion. Se muestran como avisos y quedan seleccionables.

Cuando se importan, Atlas Balance usa la fecha de la ultima fila valida anterior y guarda el monto como `0`. Si la fila trae saldo, conserva ese saldo; si tambien falta el saldo, usa el saldo de la ultima fila valida anterior. Si una fila tiene datos mezclados o ambiguos, sigue apareciendo como error y no se importa.

Al confirmar, Atlas Balance respeta el orden del extracto pegado. La linea superior queda como la ultima del lote (`fila_numero` mas alto), sin reordenar por fecha durante la importacion.

Los formatos de importacion permiten hasta 64 columnas extra y nombres de hasta 80 caracteres. Las columnas extra vacias no se guardan. Esto evita que un formato mal hecho convierta una importacion normal en basura multiplicada en base de datos.

Si falla la carga de cuentas o formato durante la importacion, la pantalla muestra el error real y no lo disfraza como "sin cuentas". La confirmacion ignora dobles clics mientras ya hay una importacion en curso.

Si la cuenta seleccionada es de `Plazo fijo`, no hay formato de importacion. Solo puedes:

- `Anadir dinero`.
- `Sacar dinero`.

Indica fecha, monto y concepto opcional. Atlas Balance calcula el saldo nuevo desde el ultimo saldo registrado.

## Usuarios y permisos

En `Usuarios`, el modal de alta/edicion incluye `Acceso a todas las cuentas`. Ese ajuste crea un permiso global para ver todas las cuentas sin conceder automaticamente edicion, eliminacion ni importacion.

Para permisos manuales, marca `Ver cuentas` cuando el usuario necesite abrir cuentas o extractos. Las acciones `Puede Agregar`, `Puede Editar`, `Puede Eliminar` y `Puede Importar` siguen siendo permisos separados.

## Titulares, cuentas y plazos fijos

Los titulares pueden ser `Empresa`, `Autonomo` o `Particular`. En `Titulares` puedes filtrar por tipo.

Las cuentas pueden ser:

- `Normal`: cuenta bancaria operativa.
- `Efectivo`: caja o saldo manual sin datos bancarios, con formato de importacion opcional.
- `Plazo fijo`: dinero inmovilizado hasta una fecha de vencimiento.

En cuentas normales y de efectivo puedes asociar un `Formato de importacion` desde `Cuentas`. Las normales ademas permiten banco, numero de cuenta e IBAN; las de efectivo no, porque ponerle IBAN a una caja es teatro administrativo.

Al crear una cuenta de plazo fijo debes indicar fecha de inicio, fecha de vencimiento y si es renovable. Opcionalmente puedes informar interes previsto, cuenta de referencia y notas.

En el dashboard de una cuenta de plazo fijo, la fecha de vencimiento aparece bajo el nombre de la cuenta, junto con los dias restantes o el aviso de vencido y el estado actual.

En ese mismo dashboard de cuenta, dentro de `Desglose de la cuenta`, ahora puedes seleccionar varias lineas y borrarlas de una vez:

- Marca las filas que quieras eliminar o usa `Seleccionar todas`.
- Pulsa `Eliminar seleccionadas`.
- Confirma la accion para enviarlas a papelera.

La renovacion de un plazo fijo es manual desde `Cuentas` con la accion `Renovar`. Atlas Balance no mueve dinero ni crea transferencias por ti. Bien: una app de tesoreria que inventa movimientos automaticamente es una bomba.

## Alertas

Las alertas de saldo bajo pueden configurarse con tres alcances:

- `Global`: aplica si no hay una alerta mas especifica.
- `Tipo de titular`: aplica a Empresa, Autonomo o Particular.
- `Cuenta`: aplica solo a una cuenta concreta.

La prioridad es cuenta > tipo de titular > global.

El aviso por email se envia cuando el saldo actual de la cuenta queda por debajo del umbral de la alerta aplicable. Para no bombardear, Atlas Balance respeta la ventana antiduplicados configurada en `Configuracion > Revision e IA`. Si no hay destinatarios validos o falla SMTP, no se marca como enviado y se reintentara en la siguiente evaluacion.

## Revision bancaria

El menu lateral incluye `Revision` con dos apartados:

- `Comisiones`: busca movimientos con conceptos de comision, cuota, mantenimiento, administracion, servicio, reclamacion, descubierto, tarjeta, transferencia o gastos bancarios.
- `Seguros`: busca movimientos con conceptos de seguro, poliza, prima y aseguradoras habituales.

En comisiones puedes marcar una linea como `Devuelta`. En seguros puedes marcarla como `Correcto`. Si la deteccion automatica se equivoca, usa `No es comision` o `No es seguro`; la linea queda como `Descartada` y puedes recuperarla con `Restaurar`.

El estado queda guardado y puedes filtrar por pendientes, revisadas o descartadas. La vista `Todas/Todos` no muestra descartadas; para verlas, usa el filtro `Descartadas/Descartados`. Para cambiar estados necesitas permiso de escritura sobre la cuenta o titular de esa linea; si solo tienes lectura, veras `Solo lectura`.

El importe minimo de comisiones se configura en `Configuracion > Revision e IA`. Se compara por valor absoluto: con umbral `1`, aparecen `-1,20` y `1,20`.

## IA

El menu lateral incluye `IA` y la barra superior incluye un boton de IA para abrir un chat flotante cuando la IA esta habilitada globalmente y tu usuario tiene permiso.

La IA responde usando contexto financiero real minimizado: saldos, agregados y movimientos relevantes cuando aplican. El chat IA requiere permiso explicito por usuario, interruptor global activo, proveedor/modelo configurados, limites de uso disponibles y presupuesto no agotado. Si no tiene datos suficientes, debe decirlo. Si falta configurar proveedor, modelo, API key o permisos, el chat muestra un error claro en vez de inventar.

Algunas preguntas de ranking financiero se calculan directamente en Atlas Balance, sin mandar la consulta al proveedor. Por ejemplo, `Que cuentas han tenido mas gastos este trimestre?` devuelve un ranking por cuenta, titular y divisa calculado con los movimientos accesibles para tu usuario. En esas respuestas veras coste y tokens `0`.

Las respuestas del chat se muestran como texto legible. Si el proveedor devuelve una tabla Markdown, Atlas Balance la convierte en datos simples para que no veas pipes, asteriscos ni filas raras. Los detalles tecnicos de modelo, tokens y coste quedan plegados en `Detalles de IA`.

Atlas Balance tambien filtra razonamiento interno del proveedor. No deberias ver textos como `We need to answer`, bloques `<think>`, notas de analisis ni placeholders tipo `[PERSON_NAME]`; si un dato no viene en el contexto accesible, la respuesta debe decir que no consta.

Si el proveedor externo devuelve algo que Atlas Balance no puede usar, el error debe indicar una categoria tecnica corta, por ejemplo `invalid_json` o `unsupported_content`, en vez de repetir un mensaje generico de respuesta malformada.

En el chat, `Enter` envia la pregunta y `Shift+Enter` inserta una linea nueva. El selector de modelo queda discreto en la cabecera junto al proveedor y cambia el modelo solo para las siguientes consultas de esa conversacion; no modifica la configuracion global de la app.

El chat esta limitado a Atlas Balance, funcionamiento de la app y datos financieros disponibles. Puede responder sobre gastos, ingresos, importes, montos, Seguridad Social, impuestos, comisiones, seguros, recibos, facturas, nominas, cuotas, cargos y cobros si esos datos estan en el contexto financiero accesible para tu usuario. Si preguntas por recetas, cocina, programacion, noticias, ocio, salud, asesoramiento legal externo o cualquier asunto externo, la app debe rechazar la consulta.

En `Configuracion > Revision e IA` puedes activar o desactivar la IA, elegir proveedor `OpenRouter` u `OpenAI`, guardar la API key correspondiente, elegir modelo, definir limites por minuto/hora/dia, limite global, presupuesto mensual/total, coste estimado por token y limites de contexto/respuesta.

Para OpenRouter, puedes dejar `Auto (gratis permitido)`. Atlas Balance guarda `openrouter/auto`, pero no usa el Auto Router abierto de OpenRouter porque puede chocar con las restricciones de modelos de tu cuenta. En su lugar, usa fallback con un maximo de 3 modelos por consulta, que es el limite efectivo de OpenRouter: `Nemotron 3 Super (free)`, `Gemma 4 31B (free)` y `MiniMax M2.5 (free)`. Si quieres forzar otro modelo gratis permitido, el selector del chat y el de Configuracion tambien muestran `gpt-oss-120b (free)`, `GLM 4.5 Air (free)` y `Qwen3 Coder 480B A35B (free)`.

Aviso serio: en esta version, esos modelos gratis no se tratan como endpoints ZDR. Atlas Balance envia contexto financiero minimizado, restringe Auto a esos modelos y no guarda prompts ni respuestas completas, pero el proveedor externo sigue viendo la consulta. Si necesitas Zero Data Retention de verdad, anade en OpenRouter un modelo ZDR permitido y no uses los modelos gratis para datos sensibles.

El chat interno usa una API key de servidor para llamar a OpenAI u OpenRouter.

Si el servidor necesita proxy corporativo para salir a internet, configuralo en `appsettings.Production.json` con `Ia:UseSystemProxy=true` o con `Ia:ProxyUrl`. Por defecto Atlas Balance no usa proxies heredados de variables de entorno para la IA, porque ya provocaron errores falsos de OpenRouter.

En `Usuarios`, un administrador puede marcar `Puede usar IA` para cada usuario. Ese permiso se valida tambien en backend: esconder el boton en la interfaz no es la seguridad, solo la parte amable.

## Dashboard

El dashboard principal muestra:

- Saldo disponible: cuentas normales y efectivo.
- Saldo inmovilizado: cuentas de plazo fijo.
- Saldo total: disponible + inmovilizado.
- Saldos por divisa: la divisa base aparece primero y el resto debajo/despues segun el espacio disponible.
- Plazos fijos: monto total, intereses aproximados y dias hasta el proximo vencimiento, ubicados bajo los KPIs de saldo, ingresos y egresos.
- La grafica de evolucion se muestra en una franja ancha propia, despues del resumen superior de KPIs y saldos por divisa.
- En `Cuentas > Saldos y evolucion`, la grafica de `Evolucion` se muestra antes del listado de cuentas.

Los saldos por titular ocupan la parte inferior completa del dashboard y se agrupan en tres columnas: Empresa, Autonomo y Particular.

### Desglose de cuenta

En el dashboard de una cuenta, la tabla de movimientos permite seleccionar filas desde la primera columna.

- Para marcar movimientos con flag, selecciona una o varias filas y pulsa el boton superior con icono de banderola.
- Para eliminar movimientos, selecciona una o varias filas y pulsa la papelera superior. La confirmacion de borrado se mantiene.
- Para insertar una linea intermedia, pasa el cursor entre filas y pulsa el icono `+` que aparece.
- Marcar checks, seleccionar filas, insertar, eliminar o aplicar flag no debe recargar la pagina ni mandarte arriba.

## Interfaz

La interfaz mantiene el mismo funcionamiento, pero ahora los botones, campos, pestanas, tarjetas, tablas y estados de foco usan un sistema visual comun. No cambia el flujo de trabajo: solo debe sentirse mas consistente al pasar de dashboard a cuentas, extractos, importacion, configuracion o administracion.

Los campos de fecha usan un selector propio de Atlas Balance. Al abrirlo veras el mes, los dias, la fecha seleccionada, el dia actual y las acciones `Hoy` y `Limpiar`. Si no cabe debajo del campo, se abre hacia arriba.

En tablets y pantallas pequenas se conservan los targets tactiles amplios y la navegacion inferior. Si algun texto largo o tabla concreta se desborda, hay que reportarlo con pantalla y ruta exacta; los fallos de UI vagos no se arreglan solos.

## Notas de seguridad

- Al iniciar sesion, Atlas Balance puede pedir un codigo MFA de 6 digitos.
- En el primer acceso con MFA, la pantalla muestra una clave para guardarla en una app de autenticacion. Despues hay que escribir el codigo generado para terminar el login.
- Si se cambian permisos, email o datos de un usuario, sus sesiones abiertas se cierran y tendra que entrar de nuevo.
- No guardes contrasenas en documentos.
- No pegues tokens ni credenciales en tickets, logs o notas.
- Las credenciales iniciales de instalacion deben tratarse como temporales y cambiarse en el primer acceso.
- El instalador escribe `C:\AtlasBalance\config\INSTALL_CREDENTIALS_ONCE.txt` con acceso limitado a Administrators/SYSTEM e intenta borrarlo automaticamente a las 24 horas. Si sigue ahi despues, borrarlo no es opcional.
- Los archivos `appsettings.Development.json`, `appsettings.Production.json` y `.env` son locales del servidor o del entorno de desarrollo. No van a Git.
- Para desarrollo local, copia las plantillas `appsettings.*.json.template`, rellena secretos propios y define `ATLAS_BALANCE_POSTGRES_PASSWORD` en un `.env` local.
- No configures `VITE_API_URL` para Atlas Balance. El frontend debe llamar a `/api` en el mismo origen; poner `localhost` ahi rompe el login en cuanto entras desde otro equipo.
- Si la aplicacion arranca por primera vez con una base vacia, `SeedAdmin:Password` debe estar configurado y tener al menos 12 caracteres; ya no existe una password admin por defecto. Bien. Eso era una mala idea.
- Las claves SMTP y de Exchange Rate API guardadas desde Configuracion se protegen automaticamente; las existentes en claro se migran al siguiente arranque.
- No borres, muevas ni copies a otra maquina `%ProgramData%/AtlasBalance/keys` en produccion sin plan de rotacion: ahi vive el keyring protegido que permite leer secretos cifrados.
- Las descargas de exportaciones solo sirven `.xlsx` generados dentro de la ruta `export_path`.
- `backup_path` y `export_path` deben ser rutas absolutas sin `..`.
- La URL de actualizaciones debe apuntar por HTTPS al repositorio oficial de Atlas Balance en GitHub.
- Los scripts manuales de backup/restauracion piden la password en consola segura y no deben ejecutarse con passwords pegadas en comandos o documentos.
- En produccion, `AllowedHosts` debe contener el hostname real. `*` ya no arranca; comodo, pero inseguro.

## Extractos - vista tipo hoja de calculo

La tabla de `Extractos` ahora se lee mas como una hoja de calculo:

- En la parte superior puedes filtrar por titular, cuenta y periodo.
- El periodo se elige con dos fechas: `Desde` y `Hasta`.
- Si dejas una fecha vacia, el filtro queda abierto por ese lado.
- El periodo elegido queda en la URL, asi que puedes recargar o compartir esa vista sin perder el rango.
- La cabecera queda fija al desplazarte.
- La columna `Fila` queda fija al mover la tabla horizontalmente.
- Las celdas tienen bordes mas claros y foco visible al editar.
- Los importes y saldos usan alineacion derecha y numeros tabulares para comparar cifras rapido.
- Las columnas tecnicas se muestran con nombres legibles, por ejemplo `Importe` en vez de `monto`.

El funcionamiento no cambia: puedes filtrar, ordenar, editar celdas, abrir historial y cambiar columnas visibles igual que antes.
