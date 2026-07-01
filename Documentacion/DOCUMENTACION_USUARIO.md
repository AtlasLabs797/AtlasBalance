# Documentacion de usuario

## Ubicacion principal

La aplicacion esta en la carpeta `Atlas Balance`.

## Datos demo en desarrollo

En entorno `Development`, Atlas Balance puede cargar datos demo sinteticos para revisar la interfaz con contenido: paises, titulares, cuentas, extractos, alertas y plazo fijo.

La plantilla `appsettings.Development.json.template` lo deja activado con:

```json
"DemoData": {
  "Enabled": true
}
```

Para trabajar con una base limpia, cambia `DemoData.Enabled` a `false` antes del primer arranque. En produccion no se cargan datos demo.

## Proxy inverso y login

Si Atlas Balance se publica detras de IIS, Nginx, HAProxy u otro proxy inverso, configura en `appsettings.Production.json` las IPs o redes de proxy confiables:

```json
"ForwardedHeaders": {
  "KnownProxies": ["127.0.0.1"],
  "KnownNetworks": []
}
```

Usa `KnownProxies` para IPs concretas y `KnownNetworks` para rangos CIDR, por ejemplo `10.0.0.0/24`. No confies `X-Forwarded-For` desde cualquier cliente: eso es dejar que cualquiera escriba su propia matricula.

## Navegacion y dashboard

Desde `V-01.05`, el menu se organiza en tres bloques:

- `Operacion`: Dashboard, Titulares, Cuentas, Extractos e Importacion.
- `Control`: Alertas y Exportaciones.
- `Sistema`: Usuarios, Auditoria, Formatos, Backups, Configuracion y Papelera.

En movil, la barra inferior muestra los accesos que mas se usan: Inicio si tienes Dashboard, Cuentas, Extractos, Importar y Mas. Si tu usuario no tiene Dashboard, Extractos pasa a ser el primer acceso. El boton `Mas` abre el resto de secciones, incluido `IA` si esta disponible.

El dashboard principal prioriza la lectura financiera:

- Saldo total en la divisa base dentro de un panel superior amplio.
- Variacion del saldo junto al periodo comparado.
- Saldos por divisa en tarjetas compactas. La divisa base aparece siempre primero.
- Filtro por pais y bloque `Saldos por pais` cuando las cuentas tienen pais asociado.
- Evolucion del saldo en una grafica ancha de area azul, con lineas de ingresos y egresos del periodo.
- KPIs de ingresos, egresos, disponible e inmovilizado debajo del panel principal.
- Saldos por titular y plazos fijos juntos en desktop; en movil se apilan.

## Desglose de cuenta

En el dashboard de una cuenta, el panel `Periodo / Volver al titular / Ver en extractos / Importar movimientos` queda a la derecha del titulo y alineado con la ficha de cuenta para que el encabezado no salte visualmente.

En el desglose, la seleccion por checkbox esta en la primera columna. Si tu usuario tiene permiso para agregar lineas, el icono `+` aparece al pasar el cursor entre dos filas y abre el formulario justo en esa posicion.

La nueva linea se rellena en el formulario que aparece entre filas. Al guardar, Atlas Balance renumera las lineas necesarias y conserva ese orden en la base de datos. La vista de cuenta se ordena por numero de fila para respetar esa secuencia.

El flag se aplica desde el boton superior con icono de banderola y solo afecta a las filas seleccionadas. La eliminacion de lineas tambien se hace desde la accion superior sobre seleccion. Estas acciones actualizan la tabla sin recargar la pagina ni mover el scroll.

Si la cuenta tiene muchos movimientos, el desglose usa paginacion. Ya no se corta silenciosamente en las primeras 500 lineas.

## Extractos

La vista `Extractos` usa una reticula de celdas tipo hoja de calculo. Las columnas visibles mantienen ancho estable y la cabecera queda alineada con las filas aunque haya muchas columnas extra.

Si falla la carga de movimientos, preferencias de columnas o auditoria de una celda, la pantalla muestra el error y permite reintentar. Si intentas ocultar columnas, siempre queda al menos una visible.

El boton `Columnas` permite elegir que columnas se muestran. La preferencia se guarda para el scope actual: cuenta si hay una seleccionada, titular si filtraste por titular, pais si estas en un pais, o vista general si no hay filtros de cuenta/titular. No hace falta elegir una cuenta para guardar columnas en la vista general.

El selector tambien muestra columnas extra disponibles en el resultado filtrado aunque no aparezcan en la fila visible actual. Si una preferencia queda demasiado recortada, usa `Mostrar todas` para recuperar todas las columnas disponibles de esa vista.

En la columna `Alerta`, marca o desmarca el checkbox y escribe la nota si hace falta. La tabla ya no muestra el texto `Marcada/Sin marca` porque era redundante.

El boton `Historial` aparece en la columna `Fila`, no repetido por toda la tabla.

Con teclado, la tabla de Extractos permite moverse por celdas con flechas, Home/End y PageUp/PageDown. Enter o F2 abre la edicion de una celda editable. En movil y tablet tactil conserva scroll local para no romper la comparacion por columnas.

## Acceso con Google Authenticator

Atlas Balance usa MFA con aplicaciones compatibles tipo Google Authenticator.

La primera vez que entras, despues de email y contrasena, aparece un QR. Escanealo con Google Authenticator y escribe el codigo de 6 digitos. Si el QR no se puede escanear, usa la clave manual que aparece debajo.

Despues de verificarlo, la casilla `Recordar este dispositivo durante 90 dias` aparece si el recuerdo de dispositivo esta habilitado en `Configuracion > General y SMTP > Autenticacion`. Si no marcas esa casilla, el codigo MFA se pedira en el siguiente login.

Cerrar sesion ya no borra el dispositivo recordado. Se volvera a pedir MFA cuando pasen esos 90 dias, borres cookies, cambies de navegador/equipo, un administrador revoque el Authenticator, cambie la contrasena o rote la seguridad del usuario. Si necesitas cortar todos los dispositivos recordados de un usuario, un administrador puede revocar el Authenticator desde `Usuarios`.

## Paises en cuentas

En `Cuentas`, cada cuenta puede tener un pais opcional. Las cuentas antiguas quedan sin pais para no romper datos existentes.

El selector `Organizacion` de la barra lateral es ahora el scope global por pais:

- `General` muestra todo, incluidas cuentas sin pais.
- Un pais concreto muestra solo cuentas, saldos, movimientos, titulares y datos derivados de ese pais.
- Las cuentas sin pais no aparecen cuando eliges un pais concreto.

El campo `Pais` en alta/edicion de cuenta solo asigna esa etiqueta a la cuenta. No cambia el scope de la app.

El dashboard muestra `Saldos por pais`, para que no tengas que adivinar si el scope esta haciendo algo.

Los paises se gestionan desde el catalogo `/api/paises` por administradores. Borrar un pais es soft delete: las cuentas existentes no se rompen, pero el pais deja de estar disponible para nuevas asignaciones normales.

Importante: el pais ya no es solo un filtro visual. En permisos de usuario y tokens de integracion, un administrador puede limitar el acceso a un pais concreto. Si ademas se elige titular o cuenta, Atlas Balance exige que todas esas condiciones coincidan a la vez.

## Copias de seguridad y Google Drive

En `Sistema > Backups`, un administrador puede configurar:

- Si las copias automaticas estan activas.
- Frecuencia: cada X horas, diaria, semanal o mensual.
- Hora UTC, dia semanal, dia mensual o intervalo horario segun la frecuencia.
- Destino: `Solo local` o `Local + Google Drive`.

La copia local sigue siendo el archivo restaurable principal. Si eliges `Local + Google Drive`, Atlas Balance crea la copia local y despues sube a Google Drive una version cifrada `.enc`.

Para usar Google Drive:

1. Crea credenciales OAuth en Google Cloud para la app.
2. Copia `OAuth Client ID` y `OAuth Client Secret` en `Backups`.
3. Pulsa `Guardar`.
4. Pulsa `Vincular` y abre la URL que muestra la pantalla.
5. Introduce el codigo de Google y concede acceso.
6. Pulsa `Probar` para validar que el refresh token sigue funcionando.

Si dejas vacia la carpeta Drive ID, Atlas Balance intentara crear una carpeta `Atlas Balance Backups`. Si quieres usar una carpeta concreta, pega su ID.

Desde la misma pantalla puedes:

- Crear una copia manual.
- Ver si cada copia quedo solo local o subida a Drive.
- Reintentar la subida a Drive de una copia local correcta.
- Listar las copias creadas por Atlas Balance en Drive.
- Importar una copia cifrada desde Drive para convertirla otra vez en copia local restaurable.

Aviso importante: las copias en Drive dependen de la clave local `backup_cloud_encryption_key`. Si pierdes esa clave o reinstalas sin conservar la configuracion protegida, los `.enc` de Drive no se podran descifrar. Drive sera almacenamiento, no magia.

## IA y modelos OpenRouter

En `Configuracion > Revision e IA`, OpenRouter permite escribir cualquier model id valido, por ejemplo `openrouter/auto` o `proveedor/modelo`. Las sugerencias vienen de OpenRouter, pero no son una jaula.

Si OpenRouter rechaza un modelo por saldo, privacidad, proveedor no disponible o ID inexistente, Atlas Balance muestra un error limpio. Si escribes un ID con formato invalido, el backend lo rechaza antes de llamar al proveedor.

## Paquetes de instalacion

Los paquetes de release estan en:

```text
Atlas Balance/Atlas Balance Release
```

Ultimo paquete publicado documentado antes de `V-02-02`:

```text
AtlasBalance-V-01.09-win-x64.zip
AtlasBalance-V-01.09-win-x64.zip.sig
```

SHA256 del ZIP firmado de `V-01.09`:

```text
4E3256141498450775AB581FC5DFF38F066867592D38F3123CAEED8940B38128
```

No reutilices hashes ni paquetes de `V-01.09` para publicar `V-02-02`. Cuando se genere `V-02-02`, debe tener ZIP y `.sig` propios.

Para instalar o actualizar desde una build local, usa los archivos del paquete generado para la version correspondiente.

No instales desde el ZIP `main` de GitHub ni desde una carpeta fuente. El paquete instalable debe llamarse como `AtlasBalance-V-01.09-win-x64.zip` y contener `api\AtlasBalance.API.exe`, `watchdog\AtlasBalance.Watchdog.exe`, `scripts` y wrappers `.cmd`.

Para actualizacion desde la app, el release de GitHub debe incluir tambien `AtlasBalance-V-01.09-win-x64.zip.sig`. Si falta la firma, el actualizador online lo rechazara. Desde `V-01.06`, el script de release tambien falla si no hay clave de firma, salvo que se use `-AllowUnsignedLocal` para una prueba local que no se debe publicar. Bien rechazado: actualizar una app financiera sin firma es jugar con cerillas al lado de gasolina.

Nota dura de `V-01.09`: el codigo ya prepara la actualizacion online completa desde GitHub `latest`, incluyendo API, Watchdog, scripts, wrappers y metadatos raiz. Una instalacion que todavia tenga un Watchdog anterior a este cambio puede necesitar un primer `update.cmd` manual o una ruta puente; esperar que el Watchdog viejo ejecute el flujo nuevo es magia barata, no ingenieria.

## Limpieza antes de publicar

Antes de publicar o entregar una base local, ejecuta la purga de entrega desde la carpeta `Atlas Balance`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Purge-DeliveryData.ps1" -ConfirmDeliveryPurge
```

Esto borra usuarios, titulares, cuentas, extractos, tokens, auditorias, backups/exportaciones registradas y consumo IA. Tambien deja vacias las claves SMTP, OpenRouter, OpenAI, MiniMax y tipos de cambio externos.

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

Si el backup falla con RLS en `AUDITORIAS` y el mensaje dice que faltan credenciales owner/migracion, no uses `-SkipBackup` como primera salida. Ejecuta la actualizacion manual pidiendo la credencial owner:

```powershell
.\update.cmd -InstallPath C:\AtlasBalance -PromptForDbOwnerCredentials
```

Si el usuario owner no es `atlas_balance_owner`, indica el nombre:

```powershell
.\update.cmd -InstallPath C:\AtlasBalance -PromptForDbOwnerCredentials -DbOwnerUser nombre_owner
```

El prompt pedira la password en consola segura. No la pegues en comandos, chats ni documentos.

Si la instalacion ya tiene los scripts actualizados, tambien vale:

```powershell
C:\AtlasBalance\update.cmd -PackagePath C:\Temp\AtlasBalance-V-01.09-win-x64 -InstallPath C:\AtlasBalance
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
5. Si hay version nueva y el preflight dice `Instalable`, pulsa `Actualizar ahora`.

Tambien puedes activar `Actualizar automaticamente desde GitHub`. La app revisa una vez al dia desde la hora UTC indicada y, si hay version superior, descarga y aplica el release firmado sin pulsar `Actualizar ahora`. Dejamos esto desactivado por defecto porque una actualizacion silenciosa tambien reinicia servicios; usarlo fuera de una ventana razonable es pegarse un tiro en el pie con interfaz bonita.

La app descarga el ZIP oficial `win-x64`, verifica digest y firma `.zip.sig`, limita tamano/contenido del paquete, crea backup PostgreSQL previo, rollback de binarios y comprueba `/api/health`. Si falta ZIP, firma, digest, clave publica o Watchdog disponible, el boton queda bloqueado con el motivo. En `V-02-02`, `Actualizacion disponible` no significa `Instalable`; esa diferencia evita actualizaciones a medias.

El limite maximo de descarga del paquete es 300 MB. Si una instalacion necesita un limite mas bajo, configura `UpdateSecurity:MaxUpdatePackageBytes`; no sirve para subir el maximo por encima de 300 MB. Si el servidor no declara tamano, Atlas Balance corta igualmente la descarga al superar el limite.

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

Los formatos de importacion permiten hasta 64 columnas extra y nombres de hasta 80 caracteres. Cada celda pegada puede tener como maximo 4096 caracteres. Las columnas extra vacias no se guardan. Si el banco ni siquiera trae esa columna en alguna fila pegada, se deja en blanco y la fila puede importarse. Esto evita que un formato mal hecho convierta una importacion normal en basura multiplicada en base de datos.

Si falla la carga de cuentas o formato durante la importacion, la pantalla muestra el error real y no lo disfraza como "sin cuentas". La confirmacion ignora dobles clics mientras ya hay una importacion en curso.

Si la cuenta seleccionada es de `Plazo fijo`, no hay formato de importacion. Solo puedes:

- `Anadir dinero`.
- `Sacar dinero`.

Indica fecha, monto y concepto opcional. Atlas Balance calcula el saldo nuevo desde el ultimo saldo registrado.

## Usuarios y permisos

Atlas Balance usa tres roles:

- `Admin`: acceso total al sistema.
- `Gerente`: acceso financiero asignado por permisos. Puede trabajar por todos los paises/titulares/cuentas o solo por los seleccionados. Ve dashboards, alertas activas y revision dentro de su alcance, y puede hacer exportaciones manuales. No crea titulares ni cuentas y no administra sistema.
- `Empleado`: rol base por defecto. Hace lo que indiquen sus permisos granulares.

En `Usuarios`, el modal de alta/edicion incluye `Acceso a todas las cuentas`. Ese ajuste crea un permiso global para ver todas las cuentas sin conceder automaticamente edicion, eliminacion ni importacion.

Para permisos manuales, marca `Pais` si el usuario solo debe operar en un pais. Luego puedes reducir mas con `Titular` y `Cuenta`. Un permiso con pais y titular no significa "todo el pais o todo el titular"; significa la interseccion exacta.

Marca `Ver cuentas` cuando el usuario necesite abrir cuentas o extractos. Las acciones `Puede Agregar`, `Puede Editar`, `Puede Eliminar` y `Puede Importar` siguen siendo permisos separados.

Las columnas visibles/editables tambien respetan ese alcance. Cambiar columnas visibles en `Extractos` no concede permiso de edicion; la edicion de columnas se decide por los permisos configurados en `Usuarios`.

Para `Gerente`, el dashboard se habilita cuando tiene al menos un permiso de datos. Para `Empleado`, `Puede ver dashboard` solo permite dashboard si la fila tambien tiene algun permiso operativo de datos dentro de ese alcance. No abre cuentas ni extractos por si solo.

La tabla de `Usuarios` muestra si el Authenticator del usuario esta activo. Si alguien pierde el movil o hay que cortarle el acceso MFA, usa `Revocar Authenticator`. Atlas Balance cerrara sus sesiones activas y en el siguiente acceso tendra que configurar MFA desde cero.

## Titulares, cuentas y plazos fijos

Los titulares pueden ser `Empresa`, `Autonomo` o `Particular`. En `Titulares` puedes filtrar por tipo.

Las cuentas pueden ser:

- `Normal`: cuenta bancaria operativa.
- `Efectivo`: caja o saldo manual sin datos bancarios, con formato de importacion opcional.
- `Plazo fijo`: dinero inmovilizado hasta una fecha de vencimiento.

En cuentas normales y de efectivo puedes asociar un `Formato de importacion` desde `Cuentas`. Las normales ademas permiten banco, numero de cuenta e IBAN; las de efectivo no, porque ponerle IBAN a una caja es teatro administrativo.

Tambien puedes asignar `Pais` a cualquier cuenta. Es opcional y se usa para filtros y agregados del dashboard.

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

Las importaciones de extractos y los movimientos manuales de plazo fijo tambien evaluan estas alertas al terminar. Antes solo lo hacian algunas ediciones manuales; eso era demasiado facil de olvidar.

## Revision bancaria

El menu lateral incluye `Revision` con dos apartados:

- `Comisiones`: busca movimientos con conceptos de comision, mantenimiento, administracion, reclamacion, descubierto o gastos bancarios. `Tarjeta`, `cuota`, `leasing`, `prestamo`, `servicio` o `transferencia` por si solos no cuentan como comision.
- `Seguros`: busca cargos negativos con conceptos de seguro, poliza, prima y aseguradoras habituales. Quedan fuera Seguridad Social, Seguro Social, Seguros Sociales, TGSS, Tesoreria General, Generalitat, transferencias, anulaciones, devoluciones y reembolsos.

En comisiones puedes marcar una linea como `Devuelta`. En seguros puedes marcarla como `Correcto`. Si la deteccion automatica se equivoca, usa `No es comision` o `No es seguro`; la linea queda como `Descartada` y puedes recuperarla con `Restaurar`.

El estado queda guardado y puedes filtrar por pendientes, revisadas o descartadas. La vista `Todas/Todos` no muestra descartadas; para verlas, usa el filtro `Descartadas/Descartados`. Para cambiar estados necesitas permiso de escritura sobre la cuenta o titular de esa linea; si solo tienes lectura, veras `Solo lectura`.

El importe minimo de comisiones se configura en `Configuracion > Revision e IA`. Se compara por valor absoluto: con umbral `1`, aparecen `-1,20` y `1,20`.

En movil, `Revision` muestra cada movimiento como tarjeta etiquetada para que puedas leer titular, cuenta, importe, concepto y estado sin arrastrar una tabla ancha.

## IA

El menu lateral incluye `IA` y la barra superior incluye un boton de IA para abrir un chat flotante cuando la IA esta habilitada globalmente y tu usuario tiene permiso. En movil, el boton flotante no aparece: entra desde `Mas > IA` para no tapar la navegacion ni los formularios.

La IA responde usando contexto financiero real minimizado: saldos, agregados y movimientos relevantes cuando aplican. El chat IA requiere permiso explicito por usuario, interruptor global activo, proveedor/modelo configurados, limites de uso disponibles y presupuesto no agotado. Si no tiene datos suficientes, debe decirlo. Si falta configurar proveedor, modelo, API key o permisos, el chat muestra un error claro en vez de inventar.

En consultas de comisiones y seguros, Atlas Balance filtra ruido antes de llamar al proveedor. Un cargo normal de tarjeta, una cuota/leasing, una transferencia, Seguridad Social/TGSS, Generalitat, anulaciones, devoluciones y reembolsos no deben inflar los totales de seguros o comisiones que recibe la IA.

Algunas preguntas de ranking financiero se calculan directamente en Atlas Balance, sin mandar la consulta al proveedor. Por ejemplo, `Que cuentas han tenido mas gastos este trimestre?` devuelve ranking por cuenta; `Que titulares han tenido mas gastos este trimestre?` agrupa por titular y divisa. En esas respuestas veras coste y tokens `0`.

Las respuestas del chat se muestran como texto legible. Si el proveedor devuelve una tabla Markdown, Atlas Balance la convierte en datos simples para que no veas pipes, asteriscos ni filas raras. Los detalles tecnicos de modelo, tokens y coste quedan plegados en `Detalles de IA`.

Atlas Balance tambien filtra razonamiento interno del proveedor. No deberias ver textos como `We need to answer`, bloques `<think>`, notas de analisis ni placeholders tipo `[PERSON_NAME]`; si un dato no viene en el contexto accesible, la respuesta debe decir que no consta.

Si el proveedor externo devuelve algo que Atlas Balance no puede usar, el error debe indicar una categoria tecnica corta, por ejemplo `invalid_json` o `unsupported_content`, en vez de repetir un mensaje generico de respuesta malformada.

Si falla la conexion con OpenRouter, OpenAI o MiniMax, el chat muestra un error generico. El administrador puede revisar la auditoria, donde solo queda una categoria tecnica segura como `tls_certificate`, `proxy_unavailable`, `dns_resolution_failed`, `connection_refused` o `network_error`; no se muestran hostnames internos, proxy, puertos, certificados, prompt, respuesta completa ni API key.

En el chat, `Enter` envia la pregunta y `Shift+Enter` inserta una linea nueva. El selector de modelo queda discreto en la cabecera junto al proveedor y cambia el modelo solo para las siguientes consultas de esa conversacion; no modifica la configuracion global de la app.

El chat esta limitado a Atlas Balance, funcionamiento de la app y datos financieros disponibles. Puede responder sobre gastos, ingresos, importes, montos, Seguridad Social, impuestos, comisiones, seguros, recibos, facturas, nominas, cuotas, cargos y cobros si esos datos estan en el contexto financiero accesible para tu usuario. Si preguntas por recetas, cocina, programacion, noticias, ocio, salud, asesoramiento legal externo o cualquier asunto externo, la app debe rechazar la consulta.

En `Configuracion > Revision e IA` puedes activar o desactivar la IA, elegir proveedor `OpenRouter`, `OpenAI` o `MiniMax`, guardar la API key correspondiente, elegir modelo, definir limites por minuto/hora/dia, limite global, presupuesto mensual/total, coste estimado por token y limites de contexto/respuesta.

Para OpenRouter, puedes dejar `Auto (gratis permitido)`. Atlas Balance guarda `openrouter/auto`, pero no usa el Auto Router abierto de OpenRouter porque puede chocar con las restricciones de modelos de tu cuenta. En su lugar, usa fallback con un maximo de 3 modelos por consulta, que es el limite efectivo de OpenRouter: `Nemotron 3 Super (free)`, `Gemma 4 31B (free)` y `MiniMax M2.5 (free)`. Si quieres forzar otro modelo gratis permitido, el selector del chat y el de Configuracion tambien muestran `gpt-oss-120b (free)`, `GLM 4.5 Air (free)` y `Qwen3 Coder 480B A35B (free)`.

Para MiniMax, los modelos soportados son `MiniMax-M3` y `MiniMax-M2.7`. Atlas Balance llama directamente a `https://api.minimax.io/v1/chat/completions` con API key de servidor; no lo trata como slug de OpenRouter. `MiniMax-M3` se envia con `thinking` desactivado y `reasoning_split=true`; `MiniMax-M2.7` mantiene el comportamiento del proveedor porque MiniMax no permite desactivar thinking en la familia M2.x.

Aviso serio: Atlas Balance envia a OpenRouter `zdr=true` y `data_collection=deny` en cada consulta. Si un modelo gratis no puede cumplir esa politica de privacidad, la consulta debe fallar. Eso es molesto, pero sacar finanzas a un proveedor con retencion seria peor.

El chat interno usa una API key de servidor para llamar a OpenRouter, OpenAI o MiniMax.

Si el servidor necesita proxy corporativo para salir a internet, configuralo en `appsettings.Production.json` con `Ia:UseSystemProxy=true` o con `Ia:ProxyUrl`. Por defecto Atlas Balance no usa proxies heredados de variables de entorno para la IA, porque ya provocaron errores falsos de OpenRouter.

En `Usuarios`, un administrador puede marcar `Puede usar IA` para cada usuario. Ese permiso se valida tambien en backend: esconder el boton en la interfaz no es la seguridad, solo la parte amable.

## Dashboard

El dashboard principal muestra:

- Saldo consolidado en el panel principal.
- Saldos por divisa dentro del panel superior; la divisa base aparece primero.
- Grafica de evolucion de saldo en la misma zona principal del dashboard, con ingresos y egresos visibles como lineas.
- KPIs de ingresos, egresos, disponible e inmovilizado cuando hay datos suficientes.
- Plazos fijos: monto total, intereses aproximados y dias hasta el proximo vencimiento.
- Saldos por pais, concentracion por banco/titular y saldos por titular.
- En `Cuentas > Saldos y evolucion`, la grafica de `Evolucion` se muestra antes del listado de cuentas.

En desktop, los saldos por titular aparecen junto a `Plazos fijos`; en movil se apilan. Los titulares se agrupan en Empresa, Autonomo y Particular.

El periodo se elige con tabs (`1m`, `3m`, `6m`, `9m`, `12m`, `18m`, `24m`) y la divisa principal con el selector de divisa. Ambos siguen quedando reflejados en la URL del dashboard.

### Desglose de cuenta

En el dashboard de una cuenta, la tabla de movimientos permite seleccionar filas desde la primera columna.

- Para marcar movimientos con flag, selecciona una o varias filas y pulsa el boton superior con icono de banderola.
- Para eliminar movimientos, selecciona una o varias filas y pulsa la papelera superior. La confirmacion de borrado se mantiene.
- Para insertar una linea intermedia, pasa el cursor entre filas y pulsa el icono `+` que aparece.
- Marcar checks, seleccionar filas, insertar, eliminar o aplicar flag no debe recargar la pagina ni mandarte arriba.

## Interfaz

La interfaz mantiene el mismo funcionamiento, pero ahora los botones, campos, pestanas, tarjetas, tablas y estados de foco usan un sistema visual comun. No cambia el flujo de trabajo: solo debe sentirse mas consistente al pasar de dashboard a cuentas, extractos, importacion, configuracion o administracion.

El menu lateral queda oscuro aunque uses tema claro. Agrupa operacion, control y sistema, mantiene el selector global de pais/organizacion y conserva los avisos de alertas, exportaciones pendientes y actualizacion disponible.

La barra superior queda fija al desplazarte. Desde ahi puedes contraer el menu, cambiar tema, abrir/cerrar el chat IA si tienes permiso y cerrar sesion.

La pantalla de login ahora tiene un panel de marca y una tarjeta de acceso. El flujo no cambia: email, password, MFA, QR de configuracion, recordar dispositivo y primer cambio de password siguen funcionando igual.

Los campos de fecha usan un selector propio de Atlas Balance en ordenador. Al abrirlo veras el mes, los dias, la fecha seleccionada, el dia actual y las acciones `Hoy` y `Limpiar`. Si no cabe debajo del campo, se abre hacia arriba. En movil y tablet tactil se usa el selector nativo del dispositivo para evitar solapes con la navegacion inferior.

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
- Generar exportaciones manuales exige permiso operativo sobre la cuenta; descargarlas exige lectura. Las descargas solo sirven `.xlsx` generados dentro de la ruta `export_path`.
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

## Actualizacion visual V-02-02

En la parte inferior del menu lateral veras la version activa y la hora local. Sirve para comprobar rapido que estas mirando la build correcta.

La pantalla de login y el cambio obligatorio de password usan el mismo layout visual: panel de marca y tarjeta de accion. El flujo no cambia: email, password, MFA, QR de configuracion, recordar dispositivo y primer cambio de password siguen funcionando igual.

Si `recordar dispositivo MFA` no esta configurado explicitamente, Atlas Balance lo trata como desactivado. Es la opcion correcta: recordar dispositivos por accidente es una mala idea.

Los desplegables de la app usan `<select>` nativo estilizado. Funcionan con teclado, lector de pantalla y controles del sistema operativo; menos teatro visual, mas fiabilidad.

En movil el dashboard no debe crear scroll horizontal. Las tablas ocultas usadas para accesibilidad de graficas no ocupan ancho visible.

La navegacion por teclado de `Extractos` mantiene una celda activa. Los botones internos de cada celda no llenan la tabulacion; entra en la celda y usa `Enter` o `F2` para editar cuando corresponda.

En `Backups`, los campos de frecuencia, dia y destino usan los mismos desplegables visuales que el resto de la app.

El resumen muestra `Ultima copia correcta en esta pagina` porque ese dato se calcula sobre la pagina cargada. Si necesitas una verdad global, revisa el listado completo o cambia la paginacion; la app no debe fingir precision que no tiene.

Si un codigo de vinculacion de Google Drive expira o falla, Atlas Balance deja de mostrar ese codigo viejo y ofrece generar uno nuevo.

## Importacion por lotes V-02-02

La pantalla `Importacion` se divide en `Nueva`, `Historial` y `Lote`.

- `Nueva`: elige cuenta, pega datos o carga archivo, revisa mapeo, valida y confirma.
- `Historial`: lista lotes importados, validados o revertidos.
- `Lote`: muestra evidencia del lote, SHA-256, resumen de filas, advertencias y acciones.

Las filas con advertencias no se seleccionan por defecto. Si decides importarlas, tienes que marcar la aceptacion de advertencias. Esto no es burocracia: evita meter lineas dudosas por accidente.

Revertir un lote borra logicamente los extractos importados por ese lote. La evidencia original queda en la base de datos para auditoria y backups.

## Conciliacion V-02-02

La nueva pantalla `Conciliacion` compara movimientos esperados contra extractos reales.

- Crea movimientos esperados con cuenta, fecha, importe, divisa, referencia y concepto.
- Genera sugerencias con ventana configurable, por defecto mas/menos 3 dias.
- Atlas Balance solo sugiere matches de misma cuenta, importe exacto y score suficiente.
- Puedes confirmar, marcar excepcion o resolver conciliaciones.

Estados disponibles: `pendiente`, `sugerida`, `conciliada`, `excepcion` y `resuelta`.

## Extractos

`Extractos` tiene dos modos:

- `Revision`: modo por defecto para revisar sin editar accidentalmente.
- `Edicion avanzada`: habilita la edicion inline de celdas.

Si vas con prisa y editas en caliente, ese modo separado existe para salvarte de ti mismo.

## Tokens OpenClaw

Los tokens de integracion tienen expiracion por defecto de 90 dias. Tambien muestran scopes, ultimo uso, IP reciente, rotacion y revocacion.

Un token sin expiracion requiere confirmacion explicita y queda auditado. Es comodo, pero tambien es peor seguridad; usalo solo si tienes un motivo real.

## Secretos locales de desarrollo

Los secretos reales de desarrollo ya no viven en el repo ni en `Documentacion`. Deben estar en `%APPDATA%\AtlasBalance\dev-secrets`.

No pegues valores de tokens, passwords ni connection strings reales en capturas, tickets, documentos o logs. Si alguien te pide hacerlo, esa persona esta pidiendo crear una incidencia.

## Logo Atlas Balance V-02-03

Atlas Balance usa el nuevo simbolo de marca en login, cambio obligatorio de password, menu lateral, favicon y activos de instalacion.

El logo se adapta automaticamente a modo claro y oscuro. No cambia ningun flujo de uso.
