# Documentacion de usuario

En Extractos y en el detalle de cuenta, las filas con alerta tienen fondo amarillo y se pueden desplazar las columnas desde las barras superior e inferior. En «Columnas» se puede mostrar u ocultar cada columna y cambiar su posicion con las flechas; la aplicacion guarda la preferencia para el usuario y el ambito. La primera vez tras actualizar, las columnas adicionales con datos pueden volver a aparecer aunque antes estuvieran ocultas; puedes ocultarlas de nuevo. El detalle incluye busqueda por concepto o comentarios y fechas; los filtros bajo las cabeceras actuan sobre la pagina cargada. La tabla de cuenta comienza en «Revision»; usa «Edicion avanzada» para modificar movimientos si tienes permiso. Al volver a revision se deshace la seleccion de movimientos.

Documento vigente para `V-03.02`. Las menciones `Desde V-XX.YY` conservan la
version en la que se introdujo cada comportamiento; no son una referencia a una
version anterior instalada.

## Novedades de uso (Impeccable, V-03.02)

- **Densidad de la tabla de Extractos:** arriba de la tabla hay un selector "Comodo / Compacto". Compacto muestra mas filas a la vez. Se recuerda por usuario. En pantallas tactiles las filas mantienen un tamano comodo.
- **Dashboard:** si falla una parte (por ejemplo la evolucion), el resto sigue visible y esa tarjeta ofrece "Reintentar". Los vencimientos de plazos fijos a 7 dias o menos aparecen como aviso arriba.
- **Confirmaciones:** las acciones se distinguen por nivel: normales, reversibles (borde de aviso) e irreversibles (boton rojo y la frase "Esta accion no se puede deshacer").
- **Movil:** Copias, Usuarios, Alertas, Exportaciones y Papelera caben en 390 px; las listas se muestran como tarjetas.
- El reloj del menu lateral se ha quitado.

## Novedades de accesibilidad y uso (V-03.02)

- **Tema:** la primera vez, la aplicacion usa el modo claro u oscuro de tu sistema. Si lo cambias con el boton de la barra superior, se recuerda tu eleccion.
- **Tamano de texto:** respeta el tamano de letra de tu navegador. Si lo aumentas, la aplicacion crece con el.
- **Foco con teclado:** al navegar con Tab se ve un contorno azul alrededor del elemento activo, tambien en Alto Contraste de Windows.
- **Titulos de pestana:** la pestana del navegador muestra la pantalla en la que estas (por ejemplo, `Extractos | Atlas Balance`).
- **Importes y tasas:** en saldo minimo, tasa manual, interes previsto y presupuestos de IA puedes escribir `1,085` o `1.085` (un solo separador cuenta como decimal). Los importes con miles, como `1.234,50`, no se aceptan en estos campos: escribe `1234,50`.
- **Pestanas con teclado:** en Importacion y Configuracion usa las flechas izquierda/derecha, Inicio y Fin.
- **Tablas anchas:** puedes enfocarlas con Tab y desplazarlas con las flechas.

## Permisos de usuario y Configuracion (V-03.02)

- **Permisos de un usuario:** al editar un usuario, cada fila de permisos tiene ahora diez casillas. Las cuatro nuevas son "Revisar movimientos", "Aprobar importaciones", "Conciliar cuentas" y "Cerrar conciliacion". Antes no se podian marcar y, al guardar el usuario, se perdian: si un gerente necesitaba conciliar o revisar, habia que volver a concederselo cada vez que se editaba el usuario.
- **Configuracion:** guardar una tasa, sincronizar los tipos de cambio, crear o modificar una divisa, o revocar, rotar o borrar un token ya no recarga la pagina entera. Solo se actualiza la seccion afectada, asi que lo que tengas escrito en otra seccion (por ejemplo la clave SMTP o el proveedor de IA) ya no se borra al guardar otra cosa. La pantalla tampoco se queda en blanco esqueleto mientras recarga.
- **Auditoria:** cuando un movimiento no tiene valor anterior o nuevo, la celda muestra "Sin valor" o "-" en lugar de un importe en blanco.

## Novedades de uso del LOTE 4 (IA e integraciones, V-03.02)

- **El chat de IA respeta los filtros que le pides.** Si preguntas "¿cuanto he gastado en seguros?", la respuesta ya sale con el filtro de concepto aplicado; antes podia devolverte el total de todos los gastos sin avisar. Tambien se aplican los importes minimo y maximo, y se comparan por valor absoluto (un gasto de 120 EUR cuenta como 120, no como -120).
- **Cuando la pregunta no se puede filtrar, la IA lo dice.** Si pides un saldo filtrado por concepto, o un pendiente por importe, el chat responde que no puede aplicar ese filtro en vez de mostrarte una cifra que no es la que preguntaste.
- **Las cifras ya no mezclan divisas.** Las comparaciones entre periodos y los rankings (top de cuentas o titulares por gasto) se calculan por separado para cada divisa. Si la pregunta abarca mas de una divisa sin concretarla, el chat te pide que la concretes en vez de sumar euros y dolares juntos.
- **Los pagos recurrentes ya no se marcan como duplicados.** Un cargo que se repite cada mes (una prima, una suscripcion) solo se avisa como posible duplicado si dos cargos del mismo concepto e importe estan a tres dias o menos.
- **Los seguimientos mantienen los filtros.** Si preguntas "¿cuanto he gastado en seguros?" y despues "¿y el mes anterior?", la segunda respuesta tambien viene filtrada por seguros y por las cuentas y titulares de la primera.
- **El chat de IA deja rastro.** Las respuestas que se resuelven en local (saldos, totales, rankings, sin llamar a ningun proveedor) ahora se auditan y cuentan contra tus limites de uso, igual que las que si llaman al proveedor. Repetir la misma pregunta hasta agotar el limite ya no sale gratis.
- **Los dias y los meses se cuentan en hora local.** Entre las 00:00 y las 02:00 (verano) o la 01:00 (invierno), "hoy" y "este mes" ya no se refieren al dia anterior.
- **Solo los administradores ven el presupuesto y el coste global de la IA.** El resto de usuarios ven su propio consumo (peticiones, tokens, coste estimado) y si pueden usar la IA. Si tu rol no es ADMIN, la pantalla de Configuracion sigue mostrando todo: ahi la informacion es para el administrador.
- **Los tokens de integracion duran como maximo un ano.** Al crear o al editar un token no se puede pedir una caducidad de mas de 365 dias, ni una fecha ya pasada. Si editas un token y no cambias la fecha, **conserva la que tenia**: antes se renovaba sola a 90 dias.
- **Rotar un token no resucita uno revocado.** Si el token esta revocado o ya ha caducado, «Rotar» responde con un error y no emite un token nuevo. Al rotar, el nuevo token hereda los permisos y los alcances del anterior: si el anterior no tenia ninguno, el nuevo tampoco.
- **Los tokens nuevos ya no reciben por defecto los alcances "vencimientos" y "auditoria".** "Vencimientos" no abria ninguna direccion y "auditoria" permite ver el valor anterior y el nuevo de los movimientos modificados. Si un cliente los necesita, se piden a mano al crear el token. Los tokens que ya tenian esos alcances no cambian.

## Novedades de uso del LOTE 2 (acceso, sesiones y correo, V-03.02)

- **Una cuenta bloqueada ya no se queda fuera para siempre.** El bloqueo por contrasena incorrecta dura 30 minutos y, al cumplirse, el contador de intentos vuelve a cero: un fallo nuevo cuenta como uno, no como el sexto. Antes el contador se quedaba en cinco, de modo que cualquier fallo volvia a bloquear y el usuario podia quedarse fuera indefinidamente.
- **El bloqueo ya no cierra las sesiones abiertas.** Mientras la cuenta esta bloqueada no se puede iniciar sesion ni completar el doble factor, pero las sesiones que ya tenias abiertas siguen funcionando. Asi, quien conozca un email no puede dejarte fuera de la aplicacion.
- **Los administradores pueden desbloquear a un usuario.** En `Usuarios`, cada fila tiene ahora una columna "Bloqueo" con el estado de la cuenta y el numero de intentos fallidos. Cuando la cuenta esta bloqueada aparece el boton "Desbloquear", que pide confirmacion, retira el bloqueo y deja constancia en la auditoria.
- **Las sesiones tienen una vida maxima de 30 dias.** Al renovarse, la sesion no reinicia su reloj: pasados 30 dias desde que la abriste, tienes que volver a iniciar sesion (con doble factor). El plazo se cambia con `JwtSettings:MaxSessionDays` en `appsettings.json`.
- **Dos pestanas abiertas ya no cierran tu sesion.** Si dos pestanas renuevan el token a la vez, la segunda ya no se interpreta como un robo y no se cierran todas tus sesiones. El aviso de robo por reutilizacion sigue vigente si el intento llega desde otra IP o mas de 30 segundos despues de la renovacion.
- **En produccion solo valen las cookies con prefijo `__Host-`.** Al actualizar la aplicacion, las sesiones abiertas que usaban las cookies antiguas dejan de valer: vuelve a iniciar sesion. Las cookies antiguas del navegador se ignoran mientras sigan ahi.
- **Un fallo de red ya no cierra la sesion.** Si al renovar la sesion se cae la conexion, el backend no responde o hay demasiadas peticiones a la vez, la aplicacion te avisa y conserva la sesion. Solo se cierra cuando el servidor dice que la sesion ya no vale.
- **Las respuestas de la aplicacion no se guardan en cache.** Los saldos y los extractos se piden siempre al servidor: un proxy o el navegador no pueden quedarse con una copia antigua.
- **El cifrado del correo SMTP se decide de forma explicita.** En `Configuracion > General y SMTP > Servidor SMTP` hay un selector "Modo TLS" con tres opciones: "Obligatorio (STARTTLS)", "Automatico (STARTTLS con usuario; sin usuario, si el servidor lo admite)" y "Sin cifrar (servidor sin TLS)". Elige "Sin cifrar" solo si tu servidor no admite TLS: en ese caso los correos (alertas de saldo, avisos de vencimientos y alertas de seguridad) viajan sin cifrar, y el servidor deja un aviso en el log. "Obligatorio" es el valor por defecto en las instalaciones nuevas. "Automatico" (el valor de las instalaciones anteriores) exige STARTTLS cuando hay usuario SMTP, para no enviar nunca la contrasena en claro; sin usuario, cifra si el servidor lo permite. Si tu servidor tiene usuario y no admite STARTTLS, el envio fallara hasta que elijas "Sin cifrar" de forma explicita.
- **El registro de auditoria de seguridad ya no se llena con un escaneo.** Los rechazos de acceso (401, 403) se agrupan por tipo, IP y usuario durante un minuto, en vez de escribir una linea por cada ruta intentada.

## Novedades de uso del LOTE 6 (permisos, columnas e importacion, V-03.02)

- **Ver el panel de inicio exige dos permisos.** Para ver saldos y cuentas hacen falta "Ver cuentas" **y** "Ver dashboard" en la misma linea de permisos. Antes, tener permiso solo para anadir, editar, importar o borrar movimientos ya era suficiente, y un gerente sin "Ver dashboard" veia el panel igualmente. Si un gerente o un empleado se queda sin panel, es que le falta "Ver dashboard": lo marca el administrador en la matriz de permisos.
- **Deshacer una importacion exige "Eliminar lineas".** Revertir un lote borra todos sus extractos, asi que ahora necesita ese permiso: quien solo puede importar ya no puede deshacer su propia importacion. En la pantalla de Importacion el boton de revertir solo aparece si lo tienes.
- **Guardar un usuario ya no pierde datos por accidente.** Ahora hay que enviar todos los datos del usuario (rol, si esta activo, si es su primer acceso, si puede usar la IA, emails y permisos). Si falta alguno, el servidor responde con un error que dice cual falta y **no cambia nada**: antes, un guardado con un cuerpo incompleto desactivaba al usuario, lo bajaba a EMPLEADO y le borraba los permisos y los emails.
- **Las columnas que el administrador esconde ya no se pueden volver a mostrar.** Si un administrador fija que solo se vean algunas columnas, el servidor deja de devolver las demas y el usuario no puede reactivarlas: solo puede esconder mas. Cada usuario sigue eligiendo las suyas dentro de las permitidas.
- **Las acciones que no puedes hacer ya no se ven.** En Conciliacion y en Revision, los botones de conciliar, generar sugerencias, resolver y revisar solo aparecen si tienes el permiso correspondiente: antes se veian y al pulsarlos el servidor respondia con un error.
- **La exportacion del registro de auditoria deja rastro.** Quien exporta la auditoria a CSV queda anotado, con el numero de filas exportadas y los filtros usados.
- **Puedes ver y revocar los equipos con MFA recordado.** En el menu lateral, grupo *Sistema*, esta **Dispositivos MFA**: la lista de los equipos y navegadores a los que diste permiso para no repetir el codigo de doble factor. De cada uno se ve el dispositivo, la IP, cuando se concedio, cuando se uso por ultima vez y hasta cuando es valida esa confianza. Puedes revocar uno concreto o el que estas usando ahora mismo (pedira confirmacion). Los equipos revocados se quedan listados, marcados como "Revocado". Al revocar uno, ese equipo tendra que meter el codigo MFA la proxima vez.
- **Ya puedes deshacer una importacion.** En **Importacion > Historial**, cada lote confirmado tiene el boton **Revertir**, que pide confirmacion y explica que se borran los extractos de ese lote, que sus conciliaciones pasan a "excepcion" y que la importacion se puede volver a hacer. Solo te aparece si tienes el permiso **eliminar lineas** en esa cuenta (los administradores siempre). Si el lote no esta en estado reversible, el servidor avisa con el motivo.
## Ubicacion principal

La aplicacion esta en la carpeta `Atlas Balance`.

## Seguridad antes de ponerla en produccion

La aplicacion no debe publicarse solo porque compile. Antes del primer uso real,
el tecnico debe comprobar en el servidor:

- entorno `Production`, HTTPS con certificado confiable y redireccion desde HTTP;
- BitLocker activo y permisos de `config`, `backups` y `exports` limitados a
  Administradores y SYSTEM;
- una copia y restauracion completas con la base real;
- que el ZIP instalado no contiene `.env`, logs, dumps, sourcemaps, certificados
  privados ni credenciales de instalacion.

Las importaciones desde Google Drive admiten como maximo 10 GiB por defecto. El
limite puede cambiarse por configuracion, pero subirlo sin calcular primero el
espacio para el fichero cifrado, el dump descifrado y la restauracion es una mala
idea. Los ficheros parciales se eliminan si se supera el limite.

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

Desde V-02.09, el proxy tambien debe enviar `X-Forwarded-Proto` (con el esquema real del visitante, normalmente `https`). Con esa cabecera, si alguien entra por HTTP la API responde una redireccion 308 a HTTPS; sin ella, exigir HTTPS externo sigue siendo responsabilidad del proxy (Caddy e IIS con la regla de redireccion bien configurada ya lo hacen). Dos claves nuevas en `Security` completan el comportamiento:

```json
"Security": {
  "HttpsRedirect": true,
  "HttpsPort": null
}
```

`HttpsPort` solo hace falta si el HTTPS publico no esta en el puerto 443 (por ejemplo 8443): pon ahi ese numero para que la redireccion apunte al sitio correcto. La base de datos remota sin cifrar (`SslMode=Disable`) ahora impide el arranque; si tu topologia lo exige expresamente, `Security.AllowInsecureDatabaseTransport = true` lo acepta dejando aviso en el log.

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

La columna `Desglose` sirve para recibos domiciliados u otros movimientos que agrupan varias personas o terceros. Abre el modal, anade nombre, importe y notas de cada linea, y revisa el total contra el importe del extracto. Si no cuadra, la fila queda marcada como `descuadrado`; el saldo bancario no cambia.

Si otra persona cambia el mismo desglose antes de que guardes, Atlas Balance avisa del conflicto, no pisa sus cambios y recarga la version vigente.

Para insertar una fila manual en `Extractos`, pasa por la columna `Fila` y pulsa el boton `+` que aparece entre esa fila y la siguiente. Se abre un formulario dentro de la tabla; al guardar, Atlas Balance inserta la fila en esa posicion y desplaza la numeracion.

El boton `Historial` aparece en la columna `Fila`, no repetido por toda la tabla.

Con teclado, la tabla de Extractos permite moverse por celdas con flechas, Home/End y PageUp/PageDown. Enter o F2 abre la edicion de una celda editable. En movil y tablet tactil conserva scroll local para no romper la comparacion por columnas.

## Novedades de uso (V-02-04)

- **Confirmaciones antes de acciones importantes.** Ahora se pide confirmar antes de:
  confirmar una importacion, conciliar un movimiento, pulsar "Actualizar ahora",
  revocar/rotar/eliminar un token de integracion, fijar la divisa base o desactivar
  una divisa, y desvincular Google Drive. Evita clics accidentales con consecuencias.
- **Aviso de cambios sin guardar.** Si intentas recargar o cerrar el navegador con una
  importacion pegada sin confirmar, o cerrar el formulario de usuario con cambios sin
  guardar, la aplicacion te avisa antes de perderlos.
- **Importes ambiguos al importar.** Si un importe con un unico separador puede leerse de
  dos formas (por ejemplo `1,234` como 1234 o como 1.234), la validacion muestra un aviso
  en esa fila para que revises el formato antes de confirmar. No bloquea, solo avisa.
- **Edicion de celdas mas fluida.** Al editar una celda del extracto ya no se recarga toda
  la tabla; solo se actualiza esa fila (salvo si cambias la fecha). Si otra persona edito
  la misma fila a la vez, veras un aviso y la fila se recargara con el dato actualizado en
  pantalla en lugar de un error generico.
- **Desglose de recibos domiciliados.** Cada extracto puede tener lineas informativas por
  persona/tercero e importe. Sirve para explicar un recibo agrupado sin duplicar movimientos
  ni alterar saldos.
- **Caducidad de tokens en tu hora local.** La fecha de expiracion que eliges para un token
  se interpreta como el final de ese dia en tu zona horaria.
- **Integracion OpenClaw.** Hoy es de solo lectura; el permiso de escritura de un token no
  habilita ninguna operacion todavia (se indica en la pantalla de creacion de tokens).

## Acceso con Google Authenticator

Atlas Balance usa MFA con aplicaciones compatibles tipo Google Authenticator.

La primera vez que entras, despues de email y contrasena, aparece un QR. Escanealo con Google Authenticator y escribe el codigo de 6 digitos. Si el QR no se puede escanear, usa la clave manual que aparece debajo.

Despues de verificarlo, la casilla `Recordar este dispositivo durante 90 dias` aparece si el recuerdo de dispositivo esta habilitado en `Configuracion > General y SMTP > Autenticacion`. Si no marcas esa casilla, el codigo MFA se pedira en el siguiente login.

Cerrar sesion ya no borra el dispositivo recordado. Se volvera a pedir MFA cuando pasen esos 90 dias, borres cookies, cambies de navegador/equipo, un administrador revoque el Authenticator, cambie la contrasena o rote la seguridad del usuario. Si necesitas cortar todos los dispositivos recordados de un usuario, un administrador puede revocar el Authenticator desde `Usuarios`.

Desde V-02.07, al cerrar sesion se cierran TODAS las sesiones abiertas de ese usuario en todos los dispositivos, no solo la actual. El dispositivo recordado para el codigo de Authenticator se conserva igualmente: cerrar sesion no te va a volver a pedir el QR ni la clave manual en el siguiente acceso desde ese mismo dispositivo.

## Paises en cuentas

En `Cuentas`, cada cuenta puede tener un pais opcional. Las cuentas antiguas quedan sin pais para no romper datos existentes.

El selector `Pais` de la barra lateral define el scope global por pais:

- `General` muestra todo, incluidas cuentas sin pais.
- Un pais concreto muestra solo cuentas, saldos, movimientos, titulares y datos derivados de ese pais.
- Las cuentas sin pais no aparecen cuando eliges un pais concreto.

El campo `Pais` en alta/edicion de cuenta solo asigna esa etiqueta a la cuenta. No cambia el scope de la app.

El dashboard muestra `Saldos por pais` cuando estas en `General`. Al elegir un
pais concreto, ese bloque se oculta porque el resto del dashboard ya esta
filtrado a ese unico pais.

Los paises se gestionan desde el catalogo `/api/paises` por administradores. Borrar un pais es soft delete: las cuentas existentes no se rompen, pero el pais deja de estar disponible para nuevas asignaciones normales.

Para usuarios no administradores, el selector solo muestra países donde existe
alguna cuenta a la que tienen acceso efectivo. Esto incluye cuentas concedidas
por un permiso global, por país, por titular repartido entre varios países o
por una cuenta concreta. Un país nuevo queda visible automáticamente cuando
una cuenta accesible se crea allí.

Importante: el pais ya no es solo un filtro visual. En permisos de usuario y tokens de integracion, un administrador puede limitar el acceso a un pais concreto. Si ademas se elige titular o cuenta, Atlas Balance exige que todas esas condiciones coincidan a la vez.

## Copias de seguridad y Google Drive

En `Sistema > Backups`, un administrador puede configurar:

- Si las copias automaticas estan activas.
- Frecuencia: cada X horas, diaria, semanal o mensual.
- Hora UTC, dia semanal, dia mensual o intervalo horario segun la frecuencia.
- Destino: `Solo local` o `Local + Google Drive`.

El nombre del fichero de copia lleva fecha y hora (`backup_AAAA-MM-DD_HHMMSS.dump`), de modo que dos copias automaticas del mismo dia nunca se pisan. La copia se escribe primero en un fichero temporal y solo se publica con su nombre definitivo cuando el volcado termina bien: si la copia se interrumpe, no queda un volcado a medias dando la sensacion de que la copia es buena. Ese temporal a medias se borra solo en la siguiente copia correcta.

Si la copia automatica esta activa y el ultimo volcado correcto es mas viejo de lo que permite la frecuencia configurada, el administrador recibe un aviso en el panel de notificaciones: 1 dia con frecuencia horaria, 2 con diaria, 8 con semanal y 32 con mensual. Si no hay ninguna copia correcta registrada, el aviso tambien aparece. Con la copia automatica desactivada no hay avisos: esa decision es del administrador.

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

En `Configuracion > Revision e IA`, OpenRouter usa `openrouter/auto` como
modelo predeterminado desde `V-03.01` (correcciones del 2026-09-25). Ese
modelo exige retencion cero de datos (ZDR) al proveedor, ademas de la
seudonimizacion del contexto financiero que ya hace Atlas Balance antes de
enviarlo.

Bajo el selector de proveedor hay una nueva opcion para administradores:
**Permitir modelos gratuitos de OpenRouter**. Esta desactivada por defecto.
Si la activas, podras guardar y usar modelos gratuitos (`openrouter/free` o
cualquier modelo `:free`); Atlas Balance te avisa expresamente de que esos
modelos gratuitos pueden guardar y usar para entrenamiento los datos que se
les envian (importes, saldos y conceptos), asi que solo debes activarla si
tu empresa lo acepta. Si tu instalacion ya tenia guardado un modelo gratuito
de una version anterior, la aplicacion lo bloqueara hasta que un
administrador entre a `Configuracion` y active esta opcion explicitamente.

Guarda tu API key de OpenRouter en esa pantalla. La clave se protege en el
backend y nunca se muestra en la interfaz ni se escribe en la auditoria.

Si OpenRouter rechaza un modelo por saldo, privacidad, proveedor no disponible o ID inexistente, Atlas Balance muestra un error limpio. Si escribes un ID con formato invalido, el backend lo rechaza antes de llamar al proveedor.

El chat se puede abrir desde `IA` para trabajar a pantalla completa o desde la
cara morada de la esquina inferior derecha en cualquier pantalla compatible.
Ambas vistas comparten el mismo historial y compositor. La cara cambia de
animacion cuando esta en reposo, mientras escribes o mientras la IA esta
pensando, siguiendo el ritmo visual de los tres estados de la referencia; si
el sistema reduce el movimiento, permanece estable.

Al enviar una pregunta, esta aparece alineada a la derecha con la burbuja
violeta, el avatar del usuario y la hora. Mientras se prepara la respuesta,
veras la cara en estado de pensamiento y la etiqueta `Pensando` animada dentro
de una burbuja del asistente. Cuando llega la respuesta, ese estado desaparece
y se muestra el contenido con sus citas y detalles habituales.

El popup flotante deja espacio para una conversación algo más alta y su botón
de cierre mantiene el círculo completo con una X discreta.

Si las caras aparecen quietas, revisa que Windows permita las animaciones:
`Configuración > Accesibilidad > Efectos visuales > Efectos de animación`.
Atlas Balance respeta esa preferencia y deja la cara estática cuando está
desactivada.

En el compositor puedes elegir el modelo disponible y el esfuerzo de
pensamiento. Desde `V-03.01` (correcciones del 2026-09-25), el selector de
modelo solo ofrece los modelos que el administrador permite en
`Configuracion > Revision e IA`; no es posible pedir un modelo distinto al
configurado. Si solo hay un modelo permitido, el selector no se muestra
porque no hay nada entre lo que elegir. Las sugerencias de la pantalla
inicial rellenan la pregunta sin obligarte a memorizar el formato. Las
respuestas muestran sus citas y los detalles del registro cuando existen;
revisalos antes de operar sobre los datos.

## Paquetes de instalacion

Los paquetes de release estan en:

```text
Atlas Balance/Atlas Balance Release
```

Ultimo paquete publicado documentado antes de `V-02.08`:

```text
AtlasBalance-V-02.07-win-x64.zip
AtlasBalance-V-02.07-win-x64.zip.sig
```

SHA256 del ZIP firmado de `V-02.07`:

```text
B2EEE529B4B3A16C05E077162E6D6510945485B98E4944FC6A3305840A5E5101
```

No reutilices hashes ni paquetes de `V-02.07` para publicar `V-02.08`. Cuando se genere `V-02.08`, debe tener ZIP y `.sig` propios.

Para instalar o actualizar desde una build local, usa los archivos del paquete generado para la version correspondiente.

No instales desde el ZIP `main` de GitHub ni desde una carpeta fuente. El paquete instalable debe llamarse como `AtlasBalance-V-02.08-win-x64.zip` y contener `api\AtlasBalance.API.exe`, `watchdog\AtlasBalance.Watchdog.exe`, `scripts` y wrappers `.cmd`.

Para actualizacion desde la app, el release de GitHub debe incluir tambien `AtlasBalance-V-02.08-win-x64.zip.sig`. Si falta la firma, el actualizador online lo rechazara. Desde `V-01.06`, el script de release tambien falla si no hay clave de firma, salvo que se use `-AllowUnsignedLocal` para una prueba local que no se debe publicar. Bien rechazado: actualizar una app financiera sin firma es jugar con cerillas al lado de gasolina.

Nota vigente de `V-02.08`: el codigo prepara la actualizacion online completa desde GitHub `latest`, incluyendo API, Watchdog, scripts, wrappers y metadatos raiz. Una instalacion que todavia tenga un Watchdog anterior a este flujo puede necesitar un primer `update.cmd` manual o una ruta puente; esperar que el Watchdog viejo ejecute el flujo nuevo es magia barata, no ingenieria.

Nota de `V-03.01` (correcciones del 2026-09-25): si tu instalacion viene de
`V-02.09` o anterior, los servicios de Atlas Balance corrian con una cuenta
integrada de Windows (`LocalSystem`). Al actualizar a `V-03.01`, el proceso
detecta esa situacion y migra automaticamente los servicios a cuentas
dedicadas (`AtlasBalanceApiSvc` para la API, `AtlasBalanceWatchdogSvc` para
el Watchdog) antes de continuar; no hace falta ninguna accion manual, pero
debes ejecutar `Actualizar Atlas Balance.cmd` como Administrador para que la
migracion tenga permisos suficientes. Si la migracion no puede completarse,
el script restaura los servicios a como estaban y aborta sin dejar la
instalacion a medias.

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
C:\AtlasBalance\update.cmd -PackagePath C:\Temp\AtlasBalance-V-02-03-win-x64 -InstallPath C:\AtlasBalance
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

Tambien puedes activar `Actualizar automaticamente desde GitHub`. La app revisa una vez al dia dentro de una ventana de madrugada en hora del servidor (01:00-05:00 por defecto) y, si hay version superior, descarga y aplica el release firmado sin pulsar `Actualizar ahora`. Si el servidor estaba apagado durante esa ventana, la actualizacion se aplaza al dia siguiente: la app no se reinicia nunca en horario laboral. Dejamos esto desactivado por defecto porque una actualizacion silenciosa tambien reinicia servicios; usarlo fuera de una ventana razonable es pegarse un tiro en el pie con interfaz bonita.

La app descarga el ZIP oficial `win-x64`, verifica digest y firma `.zip.sig`, limita tamano/contenido del paquete, crea backup PostgreSQL previo, rollback de binarios y comprueba `/api/health`. Si falta ZIP, firma, digest, clave publica o Watchdog disponible, el boton queda bloqueado con el motivo. En `V-02-03`, `Actualizacion disponible` no significa `Instalable`; esa diferencia evita actualizaciones a medias.

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

### Jerarquia Pais > Titular > Cuenta

La jerarquia opera asi: cada dimension se acota cuando se selecciona. Si una dimension esta en blanco, el alcance se ampla a todas sus opciones. Una cuenta solo puede ir acompanada de al menos un titular o pais; una fila "solo cuenta" no se acepta.

| Pais        | Titular        | Cuenta        | Alcance efectivo                                |
|-------------|----------------|---------------|-------------------------------------------------|
| Todos       | Todos          | (vacio)       | Todas las cuentas de todos los titulares        |
| Todos       | Uno            | (vacio)       | Todas las cuentas de ese titular, en cualquier pais |
| Todos       | Uno            | Una especifica | Solo esa cuenta                                 |
| Uno         | Todos          | (vacio)       | Todas las cuentas de cualquier titular en ese pais |
| Uno         | Uno            | (vacio)       | Todas las cuentas de ese titular en ese pais     |
| Uno         | Uno            | Una especifica | Solo esa cuenta                                 |

Si una cuenta se asigna acompanada de titular o pais distintos al real, el backend rechaza el guardado y el modal muestra la incoherencia en vivo con tres botones: "Mantener solo la cuenta", "Mantener pais y titular" y "Corregir a la realidad de la cuenta". El modal tambien calcula y muestra "Afecta a N cuenta(s)" en cada fila antes de guardar.

Si envias dos o mas filas con los mismos flags y una cubre a la otra en todas las dimensiones, el backend devuelve `409 Conflict` con la lista de redundancias y no guarda nada. Quita las restrictivas o ampla las generales antes de reintentar.

Marca `Ver cuentas` cuando el usuario necesite abrir cuentas o extractos. Las acciones `Puede Agregar`, `Puede Editar`, `Puede Eliminar` y `Puede Importar` siguen siendo permisos separados.

Desde V-03.02 se corrige el falso «Sin acceso» de permisos globales o con
cuenta en blanco. Para dar lectura de todas las cuentas a un gerente o empleado,
deja pais y titular en «Todos», cuenta en blanco y marca `Ver cuentas`.
Un pais o titular concreto limita las cuentas visibles a ese alcance.
El cambio requiere instalar el frontend actualizado; no hace falta enumerar
todas las cuentas ni cambiar el rol a Admin.

Las columnas visibles/editables tambien respetan ese alcance. Cambiar columnas visibles en `Extractos` no concede permiso de edicion; la edicion de columnas se decide por los permisos configurados en `Usuarios`.

Para `Gerente`, el dashboard se habilita cuando tiene al menos un permiso de datos. Para `Empleado`, `Puede ver dashboard` solo permite dashboard si la fila tambien tiene algun permiso operativo de datos dentro de ese alcance. No abre cuentas ni extractos por si solo.

La tabla de `Usuarios` muestra si el Authenticator del usuario esta activo. Si alguien pierde el movil o hay que cortarle el acceso MFA, usa `Revocar MFA` (antes se llamaba `Revocar Authenticator`). Atlas Balance cerrara sus sesiones activas y en el siguiente acceso tendra que configurar MFA desde cero.

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

Del aviso de vencimiento de un plazo fijo se ocupa el sistema, cada dia. Si el correo electronico no sale (servidor de correo caido, destinatario rejecting), el aviso **se reintenta al dia siguiente**: un plazo proximo a vencer no se queda sin avisar porque fallara un envio. El administrador recibe una notificacion por cada plazo afectado, no una sola por grupo de vencimientos.

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

- `Comisiones`: busca cargos (importe negativo) con conceptos de comision, mantenimiento, administracion, reclamacion, descubierto o gastos bancarios. `Tarjeta`, `cuota`, `leasing`, `prestamo`, `servicio` o `transferencia` por si solos no cuentan como comision. Los abonos en positivo no aparecen como lineas: son las devoluciones de esas comisiones.
- `Seguros`: busca cargos negativos con conceptos de seguro, poliza, prima y aseguradoras habituales. Quedan fuera Seguridad Social, Seguro Social, Seguros Sociales, TGSS, Tesoreria General, Generalitat, transferencias, anulaciones, devoluciones y reembolsos.

En comisiones puedes marcar una linea como `Devuelta`. En seguros puedes marcarla como `Correcto`. Si la deteccion automatica se equivoca, usa `No es comision` o `No es seguro`; la linea queda como `Descartada` y puedes recuperarla con `Restaurar`.

### Devolucion automatica de comisiones

Cuando una comision tiene su bonificacion en el mismo banco y cuenta, la columna `Devolucion` muestra automaticamente la fecha del abono emparejado y aparece un boton verde de `Verificar`. Un clic marca la comision como `Devuelta` y guarda que abono concreto la cubre; si desmarcas la linea, el abono queda libre de nuevo.

El emparejamiento sigue tres reglas: mismo importe exacto (la bonificacion invierte el cargo), misma cuenta y fecha posterior a la comision. Si hay varias comisiones iguales, el abono siempre se asigna a la mas antigua; mientras esa siga pendiente, Atlas Balance no deja verificar la mas reciente con ese mismo abono. Los abonos que descartaste como `No es comision` nunca se usan.

Si no hay ningun abono candidato, la columna muestra `—` y puedes seguir marcando la devolucion a mano.

El estado queda guardado y puedes filtrar por pendientes, revisadas o descartadas. La vista `Todas/Todos` no muestra descartadas; para verlas, usa el filtro `Descartadas/Descartados`. Para cambiar estados necesitas permiso de escritura sobre la cuenta o titular de esa linea; si solo tienes lectura, veras `Solo lectura`.

El importe minimo de comisiones se configura en `Configuracion > Revision e IA`. Con umbral `1` aparecen los cargos a partir de `-1,20`; los abonos equivalentes ya no se listan, se reservan para el emparejamiento automatico.

En movil, `Revision` muestra cada movimiento como tarjeta etiquetada para que puedas leer titular, cuenta, importe, concepto, fecha de devolucion y estado sin arrastrar una tabla ancha.

## IA

El menu lateral incluye `IA` y la barra superior incluye un boton de IA para abrir un chat flotante cuando la IA esta habilitada globalmente y tu usuario tiene permiso. En movil, el boton flotante no aparece: entra desde `Mas > IA` para no tapar la navegacion ni los formularios.

La IA responde usando contexto financiero real minimizado: saldos, agregados y movimientos relevantes cuando aplican. El chat IA requiere permiso explicito por usuario, interruptor global activo, proveedor/modelo configurados, limites de uso disponibles y presupuesto no agotado. Si no tiene datos suficientes, debe decirlo. Si falta configurar proveedor, modelo, API key o permisos, el chat muestra un error claro en vez de inventar.

En consultas de comisiones y seguros, Atlas Balance filtra ruido antes de llamar al proveedor. Un cargo normal de tarjeta, una cuota/leasing, una transferencia, Seguridad Social/TGSS, Generalitat, anulaciones, devoluciones y reembolsos no deben inflar los totales de seguros o comisiones que recibe la IA.

Desde V-03.01 solo puedes tener una pregunta a la IA en curso a la vez. Si envias otra (por ejemplo desde otra pestana) antes de que termine la primera, veras un aviso de demasiadas peticiones: espera a que llegue la respuesta y vuelve a enviarla.

Algunas preguntas de ranking financiero se calculan directamente en Atlas Balance, sin mandar la consulta al proveedor. Por ejemplo, `Que cuentas han tenido mas gastos este trimestre?` devuelve ranking por cuenta; `Que titulares han tenido mas gastos este trimestre?` agrupa por titular y divisa. En esas respuestas veras coste y tokens `0`.

Las respuestas del chat se muestran como texto legible dentro de una burbuja violeta junto a la cara animada del asistente. Si el proveedor devuelve una tabla Markdown, Atlas Balance la convierte en datos simples para que no veas pipes, asteriscos ni filas raras. Los detalles tecnicos de modelo, tokens y coste quedan plegados en `Detalles de IA`.

Tu pregunta aparece como burbuja a la derecha con fondo suave; la respuesta de la IA aparece a la izquierda con la hora y el modelo debajo. La cara cambia suavemente entre reposo, escritura y pensamiento; si tienes activado `prefers-reduced-motion`, no se mueve. Cuando cambia el dia entre mensajes se muestra un divisor `Hoy` / `Ayer` / `DD MMM`.

El composer (la caja de texto inferior) es una tarjeta. En la fila inferior veras el **modo de pensamiento** (a la izquierda) y el modelo activo + el boton de enviar (a la derecha). El modo de pensamiento cambia la profundidad de razonamiento que pide al proveedor:

- `OPENAI`: `Esfuerzo automatico / bajo / medio / alto` (mapeado a `reasoning_effort`).
- `MINIMAX`: `Esfuerzo automatico / Pensamiento activado / Pensamiento desactivado` (mapeado a `thinking.type`).
- `OPENROUTER`: solo `Esfuerzo automatico` (los modelos concretos pueden aceptar `reasoning.effort`, pero el `openrouter/auto` lo ignora).

El modelo activo se sigue eligiendo desde `Configuracion` (no hay selector de modelo dentro del chat). En la cabecera del chat veras el chip del provider (`OpenAI` / `MiniMax` / `OpenRouter`); el boton de la flecha circular es `Nueva conversacion` (limpia los mensajes pero mantiene el modo de pensamiento elegido).

Atlas Balance tambien filtra razonamiento interno del proveedor. No deberias ver textos como `We need to answer`, bloques `<think>`, notas de analisis ni placeholders tipo `[PERSON_NAME]`; si un dato no viene en el contexto accesible, la respuesta debe decir que no consta.

Si el proveedor externo devuelve algo que Atlas Balance no puede usar, el error debe indicar una categoria tecnica corta, por ejemplo `invalid_json` o `unsupported_content`, en vez de repetir un mensaje generico de respuesta malformada.

Si falla la conexion con OpenRouter, OpenAI o MiniMax, el chat muestra un error generico. El administrador puede revisar la auditoria, donde solo queda una categoria tecnica segura como `tls_certificate`, `proxy_unavailable`, `dns_resolution_failed`, `connection_refused` o `network_error`; no se muestran hostnames internos, proxy, puertos, certificados, prompt, respuesta completa ni API key.

En el chat, `Enter` envia la pregunta y `Shift+Enter` inserta una linea nueva.

El chat esta limitado a Atlas Balance, funcionamiento de la app y datos financieros disponibles. Puede responder sobre gastos, ingresos, importes, montos, Seguridad Social, impuestos, comisiones, seguros, recibos, facturas, nominas, cuotas, cargos y cobros si esos datos estan en el contexto financiero accesible para tu usuario. Si preguntas por recetas, cocina, programacion, noticias, ocio, salud, asesoramiento legal externo o cualquier asunto externo, la app debe rechazar la consulta.

En `Configuracion > Revision e IA` puedes activar o desactivar la IA, elegir proveedor `OpenRouter`, `OpenAI` o `MiniMax`, guardar la API key correspondiente, elegir modelo, definir limites por minuto/hora/dia, limite global, presupuesto mensual/total, coste estimado por token y limites de contexto/respuesta. Para OpenRouter gratuito, el coste por tokens del proveedor es cero, pero siguen aplicando sus limites de uso y la disponibilidad de sus modelos.

Para OpenRouter, puedes dejar `Auto (gratis permitido)`. Atlas Balance guarda `openrouter/auto`, pero no usa el Auto Router abierto de OpenRouter porque puede chocar con las restricciones de modelos de tu cuenta. En su lugar, usa fallback con un maximo de 3 modelos por consulta, que es el limite efectivo de OpenRouter: `Nemotron 3 Super (free)`, `Gemma 4 31B (free)` y `MiniMax M2.5 (free)`. Si quieres forzar otro modelo gratis permitido, el selector del chat y el de Configuracion tambien muestran `gpt-oss-120b (free)`, `GLM 4.5 Air (free)` y `Qwen3 Coder 480B A35B (free)`.

Para MiniMax, el modelo predeterminado es `MiniMax-M3.1-Flash`; tambien estan disponibles `MiniMax-M3` y `MiniMax-M2.7`. En Configuracion > IA selecciona MiniMax o M3.1 Flash y guarda: se propone un limite de 1.000.000 de tokens de contexto, reservando los tokens de salida configurados. Las instalaciones existentes conservan su configuracion hasta guardarla. En el chat se puede elegir esfuerzo bajo, medio, alto, muy alto o maximo; automatico usa el maximo del proveedor. El razonamiento no se puede desactivar. El limite de tokens es aproximado; el numero de movimientos y el periodo de datos siguen limitados por la aplicacion. Atlas Balance llama directamente a `https://api.minimax.io/v1/chat/completions` con la clave API del servidor.

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
- Saldos por pais cuando el scope esta en `General`, concentracion por banco/titular y saldos por titular.
- En `Cuentas > Saldos y evolucion`, la grafica de `Evolucion` se muestra antes del listado de cuentas.

En desktop, los saldos por titular aparecen junto a `Plazos fijos`; en movil se apilan. Los titulares se agrupan en Empresa, Autonomo y Particular.

El periodo se elige con tabs (`1m`, `3m`, `6m`, `9m`, `12m`, `18m`, `24m`) y la divisa principal con el selector de divisa. Ambos siguen quedando reflejados en la URL del dashboard.

### Desglose de cuenta

En el dashboard de una cuenta, la tabla de movimientos permite seleccionar filas desde la primera columna.

- Para marcar movimientos con flag, selecciona una o varias filas y pulsa el boton superior con icono de banderola. El mismo boton alterna entre marcar y quitar la alerta: si todas las seleccionadas ya estan amarillas, las desmarca; si hay alguna sin alerta, marca solo las que faltan. El icono y el permiso son los mismos, solo cambia el texto accesible del boton ("Marcar seleccion con alerta" / "Quitar alerta de la seleccion").
- Para eliminar movimientos, selecciona una o varias filas y pulsa la papelera superior. La confirmacion de borrado se mantiene.
- Para insertar una linea intermedia, pasa el cursor entre filas y pulsa el icono `+` que aparece.
- Marcar checks, seleccionar filas, insertar, eliminar o aplicar flag no debe recargar la pagina ni mandarte arriba.

## Interfaz

La interfaz mantiene el mismo funcionamiento, pero ahora los botones, campos, pestanas, tarjetas, tablas y estados de foco usan un sistema visual comun. No cambia el flujo de trabajo: solo debe sentirse mas consistente al pasar de dashboard a cuentas, extractos, importacion, configuracion o administracion.

El menu lateral sigue el tema que tengas puesto: claro con tema claro, oscuro con tema oscuro. Agrupa operacion, control y sistema, mantiene el selector global de pais y conserva los avisos de alertas, exportaciones pendientes y actualizacion disponible.

La barra superior queda fija al desplazarte. Desde ahi puedes contraer el menu, cambiar tema, abrir/cerrar el chat IA si tienes permiso y cerrar sesion.

La pantalla de login ahora tiene un panel de marca y una tarjeta de acceso. El flujo no cambia: email, password, MFA, QR de configuracion, recordar dispositivo y primer cambio de password siguen funcionando igual.

Los campos de fecha usan un selector propio de Atlas Balance en ordenador. Al abrirlo veras el mes, los dias, la fecha seleccionada, el dia actual y las acciones `Hoy` y `Limpiar`. Si no cabe debajo del campo, se abre hacia arriba. En movil y tablet tactil se usa el selector nativo del dispositivo para evitar solapes con la navegacion inferior.

En tablets y pantallas pequenas se conservan los targets tactiles amplios y la navegacion inferior. Si algun texto largo o tabla concreta se desborda, hay que reportarlo con pantalla y ruta exacta; los fallos de UI vagos no se arreglan solos.

## Notas de seguridad

- Al iniciar sesion, Atlas Balance puede pedir un codigo MFA de 6 digitos.
- En el primer acceso con MFA, la pantalla muestra una clave para guardarla en una app de autenticacion. Despues hay que escribir el codigo generado para terminar el login.
- Si se cambian permisos, email o datos de un usuario, sus sesiones abiertas se cierran y tendra que entrar de nuevo.
- Desde V-02.07, cerrar sesion cierra todas las sesiones abiertas del usuario en todos los dispositivos, no solo la sesion actual. El dispositivo recordado para el codigo de Authenticator no se pierde al cerrar sesion.
- Desde V-02.07, si al cambiar la contrasena escribes 5 veces mal la contrasena actual, la cuenta se bloquea 30 minutos, igual que en el login.
- Desde V-02.07, las contrasenas nuevas se comparan contra una lista mas amplia de contrasenas filtradas comunes. Alguna contrasena que antes se aceptaba puede rechazarse ahora por "demasiado comun"; en ese caso, elige otra que no sea una variante obvia de una palabra o frase habitual.
- Desde V-03.01, una contrasena nueva no puede contener tu nombre ni la parte de tu email antes de la @ (palabras de 4 letras o mas), ni ser una palabra muy comun con numeros o simbolos al final (por ejemplo `Tesoreria2026!` o `P@ssw0rd2024`). Una frase larga de varias palabras funciona mejor.
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

- La pantalla se organiza en una cabecera, un bloque de ámbito de consulta y la
  tabla de movimientos, para distinguir qué conjunto estás consultando de los
  filtros de cada columna.
- El bloque superior permite elegir titular, cuenta y periodo con `Desde` y
  `Hasta`. Cuando hay algún filtro activo aparece `Restablecer` y devuelve la
  vista general.
- La cabecera separa claramente los nombres de columna de la fila de filtros;
  ambos quedan alineados incluso al desplazar la tabla horizontalmente.
- Los filtros de columna siempre están visibles. Los campos de texto buscan
  dentro de la página cargada, la fecha se selecciona con calendario y los
  estados ofrecen `Todos`, `Sí` y `No`.
- `Borrar filtros` limpia los filtros de columna y también los filtros de
  titular, cuenta y periodo de la pantalla.
- En la parte superior puedes filtrar por titular, cuenta y periodo.
- El periodo se elige con dos fechas: `Desde` y `Hasta`.
- Si dejas una fecha vacia, el filtro queda abierto por ese lado.
- El periodo elegido queda en la URL, asi que puedes recargar o compartir esa vista sin perder el rango.
- La cabecera queda fija al desplazarte.
- En escritorio, la rejilla reserva espacio para ver 17 movimientos a la vez en
  densidad compacta; en movil conserva un viewport mas corto para no romper la
  navegacion.
- Al seleccionar una celda, la franja superior muestra su columna y el contenido
  completo para poder leer valores largos sin perder la referencia de la fila.
- `Fila` deja de mostrarse como columna: el numero sigue siendo interno para
  ordenar, insertar y auditar sin ocupar espacio de lectura.
- La columna `Cuenta` muestra el nombre real de la cuenta. Tambien se muestran
  por defecto `Banco`, `Titular`, `Divisa` y las columnas extra que llegan con
  los extractos importados.
- Para insertar una fila en `Edicion avanzada`, usa el `+` que aparece en la
  celda `Revisada`; el borrador se abre dentro de la tabla, no en un formulario
  separado encima.
- Las celdas tienen bordes mas claros y foco visible al editar.
- Los importes y saldos usan alineacion derecha y numeros tabulares para comparar cifras rapido.
- Las columnas tecnicas se muestran con nombres legibles, por ejemplo `Importe` en vez de `monto`.
- La tabla usa siempre densidad `Compacta`; no hay un selector de densidad que
  cambie el tamaño de las filas.
- Los filtros `Titular` y `Cuenta` usan desplegables con el mismo estilo visual
  que el resto de controles y permiten escribir para filtrar sus opciones. Al
  abrirlos, las opciones aparecen en una lista rectangular legible, no en una
  burbuja elíptica.

El funcionamiento no cambia: puedes filtrar, ordenar, editar celdas, abrir historial y cambiar columnas visibles igual que antes. El historial se abre desde el icono de la celda `Revisada` o con el menu contextual de cualquier celda.

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

## Rutas de backups y exportaciones (vigente en V-02.08; introducido en V-02.07)

Las carpetas de backups y de exportaciones deben estar en un disco de la propia maquina. Se escriben asi:

```
C:\atlas-balance\backups
C:\atlas-balance\exports
```

Ya no se admiten carpetas de red del tipo `\servidor\carpeta`. Si guardas una, la aplicacion la rechaza y te avisa en pantalla.

El motivo es simple: un backup contiene toda la base de datos, con numeros de cuenta, saldos y datos personales. Si esa carpeta apunta a un equipo de fuera, cada copia sale de tu red sin que nadie se entere. Obligando a que sea una carpeta local, esa fuga no puede ocurrir por un cambio de configuracion.

Si necesitas que las copias acaben en un NAS o en un servidor de la oficina, monta ese recurso como una unidad de red con su letra (por ejemplo `Z:`) y usa esa letra en la ruta. Para copias fuera de la oficina, la via recomendada sigue siendo la integracion de Google Drive, que ya cifra el archivo antes de subirlo.

Nada mas cambia: el resto del funcionamiento de backups y exportaciones es identico.

## Avisos de seguridad y estado del sistema (vigente en V-02.08; introducido en V-02.07)

Solo para administradores.

### Avisos automaticos de seguridad

Atlas Balance vigila la actividad y avisa cuando detecta algo raro. Los avisos
llegan por tres vias a la vez:

- La campana de notificaciones de la barra superior.
- Un correo, si el servidor de correo esta configurado. Por defecto va a todos
  los administradores activos.
- Slack, si el tecnico lo ha configurado (no viene activado).

Los seis avisos que existen:

| Aviso | Que significa |
|-------|---------------|
| Muchos fallos de acceso a una cuenta | Alguien esta probando contrasenas contra una cuenta concreta. |
| Una direccion tocando muchas cuentas | Un mismo equipo esta probando el acceso a muchas cuentas de golpe. |
| Descarga masiva de datos | Alguien ha pedido muchisimos registros de una vez, o esta haciendo cientos de consultas seguidas. |
| Acceso desde un sitio nuevo | Un usuario ha entrado desde un equipo o red que nunca habia usado. Puede ser normal (teletrabajo, portatil nuevo) o no. |
| Cambios de contrasena repetidos | Se ha cambiado la contrasena de la misma cuenta varias veces en pocos minutos. |
| Muchos accesos denegados | Se estan rechazando muchas peticiones, muy por encima de lo habitual. |

La vigilancia no se salta ningun tramo: cada comprobacion empieza donde termino
la anterior, asi que un servidor parado o una tarea que se retrasa no dejan
eventos sin mirar (antes se perdian justo los tramos con mas actividad). Por lo
mismo, si el aviso llega con mas eventos de los habituales, es que se estaba
acumulando actividad desde la ultima comprobacion.

**Que hacer cuando llega un aviso.** Casi todos tienen explicacion inocente: un
companero que ha olvidado la contrasena, alguien trabajando desde casa, una
exportacion grande. Lo importante es confirmarlo con la persona implicada. Si
nadie reconoce la actividad, cambia la contrasena de esa cuenta y avisa al
tecnico.

Un mismo aviso no se repite durante una hora, para que un incidente largo no
llene el buzon de correos identicos.

### Que se guarda de cada accion

La pantalla de Auditoria registra, para cada accion sobre datos sensibles:

- Quien la hizo y cuando.
- Que cambio exactamente (valor anterior y valor nuevo).
- Desde que direccion de red y con que navegador.
- Un identificador de sesion, que permite seguir todo lo que hizo una persona
  desde que entro hasta que salio.
- Si la accion entro por la aplicacion web o directamente por la API de
  integracion.

**Este registro no se puede modificar ni borrar**, ni siquiera desde la propia
aplicacion. Cada linea va firmada, asi que si alguien intentara alterarla desde
la base de datos, el sistema lo detecta. Hay una comprobacion automatica cada
madrugada que avisa si encuentra algo.

Se conserva **un ano** (antes eran 28 dias). Las lineas anteriores a esta version
no llevan firma: aparecen como "sin firma", que no es un problema, solo significa
que se crearon antes de que existiera el mecanismo.

### Estado del sistema

El tecnico dispone de una comprobacion de estado que verifica de verdad que la
base de datos responde, que queda espacio en disco y que la aplicacion no va
lenta. Si algo esta mal, llega un aviso por los mismos canales que los de
seguridad.

Si el disco baja del 15% libre aparece un aviso, y por debajo del 5% se considera
critico: a partir de ahi los backups y los informes pueden empezar a fallar.


## Nueva imagen de Atlas Balance

La aplicacion tiene un aspecto nuevo. **No cambia como se trabaja**: los mismos
menus, las mismas pantallas, los mismos botones en los mismos sitios. Lo que
cambia es como se ve y como se lee.

### Que vas a notar

- **Se lee mejor.** El texto general es mas grande. Las tablas de extractos y de
  administracion se quedan en un tamano compacto a proposito, para que sigan
  cabiendo muchas lineas en pantalla sin tener que hacer scroll continuamente.
- **Todas las cifras van en la misma tipografia de numeros**, alineadas a la
  derecha y con las columnas cuadradas, para poder compararlas de un vistazo.
  El euro va detras del numero: `1.284.560,12 €`. Los negativos usan el signo
  menos tipografico: `−84.120,50 €`.
- **Los colores dicen lo que significan, no lo que marca el signo.** Un gasto
  que sube se ve en rojo aunque el numero sea positivo.
- **Menos sombras, mas orden.** Las tarjetas ya no "flotan": se separan por su
  borde y por el color del fondo. Solo tienen sombra las cosas que de verdad se
  superponen a la pantalla: menus, avisos emergentes, ventanas y paneles
  laterales.
- **Nada se mueve al pasar el raton por encima.** Antes algunos elementos se
  levantaban ligeramente; ahora solo cambian de color. Al pulsar, el boton se
  hunde un poco, que es lo que confirma que has hecho clic.
- **Los botones son ovalados.** El azul relleno es la accion principal de cada
  pantalla; el que solo tiene el borde azul es la accion secundaria.
- **El asistente de IA es violeta**, en las dos modalidades de tema. Es un
  codigo de color deliberado: todo lo que veas en violeta lo ha generado la IA,
  no la aplicacion.

### Tema claro y tema oscuro

Los dos temas estan revisados por igual. El interruptor sigue donde estaba, en
la barra superior, y tu eleccion se recuerda en ese navegador. El menu lateral,
la barra superior y la navegacion inferior en movil tienen un acabado
translucido que deja intuir el contenido que hay debajo.

### Alertas

El aviso de saldo bajo de la parte superior ahora **distingue la gravedad**: si
solo hay cuentas por debajo de su minimo, sale en ambar; si alguna cuenta esta
directamente en negativo, sale en rojo y lo dice en el texto.

En la pantalla de Alertas, el listado de alertas activas ha dejado de ser una
tabla y es una lista con un icono de gravedad por linea. La configuracion de las
reglas de alerta (global, por tipo de titular y por cuenta) no ha cambiado.

### Movimientos marcados

Un movimiento marcado ya **no pinta la fila entera de color**. Ahora lleva un
punto de aviso al principio de la linea. La razon es practica: tenir la fila
tapaba el color de los importes, que es la informacion que de verdad importa en
un extracto.

### Indicadores del dashboard

Cada indicador lleva ahora su comparacion debajo ("+3,4% vs. anterior"). Cuando
la aplicacion no tiene con que comparar todavia (un titular recien creado, un
periodo sin historico), no muestra un cero enganoso: simplemente no muestra la
comparacion.

En los graficos de concentracion se representan como maximo cinco entidades; el
resto se agrupa en "Otros", con el numero de entidades agrupadas.

### Si algo se ve mal

Reporta la **pantalla exacta, el tema (claro u oscuro) y el tamano de ventana**,
con captura si puedes. Un aviso de "se ve raro" sin esos tres datos no se puede
reproducir ni corregir.

## Cambios de seguridad visibles (V-03.02)

- **Contrasenas:** siguen pidiendo 12 caracteres como minimo. Ahora tambien se rechazan las que aparecen en listas de contrasenas filtradas conocidas, con el mensaje "La contrasena es demasiado comun". Elige una frase larga que solo conozcas tu.
- **Aviso de bloqueo:** si tu cuenta se bloquea por demasiados intentos fallidos, recibiras un correo con la hora y la IP aproximada del intento. El bloqueo se levanta solo pasado el tiempo indicado; si no fuiste tu, avisa al administrador. El correo no incluye enlaces.
- **IBAN de las cuentas:** solo el administrador ve el IBAN completo al abrir el detalle o al editar una cuenta. El resto de usuarios lo ve enmascarado, y tampoco puede buscar cuentas por IBAN. El numero de cuenta todavia no ha cambiado.
- **Registro de auditoria:** los registros nuevos ya no guardan el IBAN completo; si un cambio afecta al IBAN, el registro lo indica. Los registros anteriores no se modifican y caducan a su plazo normal.
- **Limpieza automatica:** cada dia se borran los dispositivos de confianza de MFA caducados o revocados. En las importaciones cerradas hace mas de 180 dias (plazo configurable) se vacian las filas intermedias del archivo; los movimientos ya confirmados no se tocan.
# Buscar titulares y cuentas (V-03.02)

Pulsa Ctrl+K (⌘K en Mac) o usa el buscador superior y escribe el nombre de un titular o una cuenta. Selecciona el resultado para abrir su detalle. Se muestran hasta diez resultados por tipo, correspondientes al pais activo y a tus permisos. Escribe mas caracteres si necesitas concretar la busqueda.

# Lo que cambia en el LOTE 7 (V-03.02, 2026-11-17)

- **Papelera:** ahora muestra de 50 en 50 con paginacion, y aparecen tambien los elementos eliminados hace mas tiempo, que antes no cabian en la lista y no se podian restaurar. Los importes de los extractos se muestran en la divisa de su cuenta, no siempre en euros. Si cambias de pestana o de pagina mientras se esta cargando, ya no se cuela una respuesta antigua: solo se muestra la que pediste.
- **Avisos de error repetidos:** durante una actualizacion del sistema o un backup, el servidor esta caido a proposito. Antes salia un aviso de error cada dos segundos y medio; ahora los sondeos no pintan avisos y se detienen al salir de la pagina.
- **Banner de alertas:** si lo ocultas, sigue oculto al recargar o al cambiar de pais. Solo vuelve a salir cuando hay una alerta distinta de la que ocultaste.
- **Nota de alerta:** entrar y salir del campo de la nota sin escribirla ya no guarda nada.
- **Borrar o marcar en lote:** en el detalle de cuenta se procesan de cuatro en cuatro, todas las filas se intentan y, si alguna falla, te dicen cuantas se hicieron y cuantas no ("Se eliminaron 12 de 15 movimientos. Fallaron 3."). Antes se paraban en el primer error.
- **Campos numericos de Configuracion:** ahora puedes vaciarlos. Si dejas el campo vacio, se conserva el ultimo valor que estaba bien, porque el servidor todavia no admite "sin valor" en estos campos.
- **Celda editable:** si el guardado falla, lo que habias escrito se queda, aparece un aviso debajo ("No guardado. Revisa el dato e intentalo otra vez.") y el campo sigue abierto: puedes reintentar con Enter o salir con Escape para descartar.
- **Detalle de cuenta:** al cambiar de pagina o de periodo ya no se hacen peticiones duplicadas. El dato se repide solo cuando toca (cada 10 segundos en los movimientos, cada 5 en el resumen) o al volver a la ventana.
- **Buscadores:** los de Formatos, Usuarios y el campo "Modelo" de Configuracion esperan 300 ms antes de buscar, asi que ya no lanzan una peticion por cada tecla. El texto que ves en pantalla se filtra al instante.
- **Configuracion de la IA:** el navegador ya no la consulta cada 30 segundos. Si otro usuario cambia la configuracion de la IA, tu navegador la recoge al volver a la pestana (si han pasado mas de 5 minutos) o al recargar la pagina.
- **Importacion:** si cambias de pais o de cuenta mientras preparas una importacion, ya no falla con "la clave de idempotencia ya se uso con otro contenido".

# Lo que cambia en el LOTE 5 (V-03.02, 2026-11-17)

- **Totales en divisa:** si alguna cuenta esta en una divisa sin tipo de cambio, el total ya no parece mas bajo sin explicacion. El dashboard y la integracion OpenClaw dicen que divisas quedan fuera ("El total no incluye GBP y USD. No hay tipo de cambio para esas divisas.") y el total sigue siendo la suma de las cuentas convertibles.
- **Tipos de cambio:** las tasas que has fijado a mano **no se sobrescriben** al sincronizar. Si alguna se ha quedado sin actualizar, el administrador recibe un aviso y la pantalla de Configuracion te dice cuantas se han omitido. Si la sincronizacion falla (la API no responde, devuelve error o no devuelve tasas), tambien deja aviso al administrador: antes solo quedaba en el registro del servidor.
- **Cambiar la divisa de una cuenta:** si la cuenta ya tiene movimientos, no se puede cambiar su divisa. El mensaje es "La cuenta tiene extractos: no se puede cambiar su divisa. Crea una cuenta nueva o elimina los extractos de esta cuenta antes de cambiarla." Los movimientos que estan en la papelera no bloquean el cambio.
- **Revertir una importacion:** al revertir un lote, todo se deshace o se aplica entero: si algo falla a mitad, no quedan movimientos borrados con el lote sin revertir. Ahora solo se pueden revertir los lotes ya confirmados (antes en un lote sin confirmar se aceptaba la peticion y se devolvia un error). Ademas, los movimientos de ese lote que estaban conciliados pasan a "excepcion" (se ven y se pueden revisar) y el movimiento esperado vuelve a quedar pendiente, para que se pueda volver a sugerir cuando se reimporte.
- **Reimportar un lote revertido:** ya se puede. Antes el sistema daba los movimientos del lote revertido por duplicados y no dejaba volver a importarlos.
- **Confirmar una importacion:** el boton de confirmar un lote ya no responde siempre con "El lote esta siendo procesado por otra operacion". Ese error hacia imposible confirmar cualquier importacion en una base de datos real.
- **Conciliacion:** un extracto no se puede conciliar dos veces (te avisa con "El extracto ya esta conciliado"), y ya no se propone un extracto cuyo importe se aleja del movimiento mas alla de la tolerancia. Los estados tienen su recorrido: una conciliacion se puede pasar a "Excepcion" o a "Resuelta", y una excepcion se puede volver a conciliar o cerrar. Las conciliaciones resueltas quedan cerradas.
- **Orden de las filas pegadas:** Atlas Balance toma como saldo actual la ultima fila del extracto pegado, que es la linea de ARRIBA del bloque. Si pegas un fichero del banco que va del mas antiguo al mas reciente, la pantalla de validacion te avisa antes de confirmar, porque el saldo actual se calcularia con la fila mas antigua. Si el aviso te aparece, pega el extracto al reves o consulta con el equipo: el criterio de que fila es la actual depende de como exporta tu banco.
- **Avisos de saldo bajo:** con dos importaciones simultaneas ya no llegan dos correos de saldo bajo. El aviso se comprueba y se registra dentro de una transaccion, asi que las dos importaciones esperan su turno.
- **Importes con coma:** "0,500" se lee como 0,5 y no como 500, tanto al importar como en la interfaz. Lo mismo con el punto: "0.500" es 0,5.
- **Avisos de plazo fijo:** el aviso de "proximo a vencer" ya no falla por un error del servidor. Si el envio del correo falla, se vuelve a intentar en la siguiente pasada sin duplicar la notificacion.

## Cambios visuales de noviembre de 2026

- La barra superior muestra la seccion en la que estas ("Operación", "Control" o "Sistema"). El nombre de la pantalla aparece en el titulo grande.
- En "Dispositivos MFA de confianza" cada dispositivo aparece como "Navegador · Sistema", por ejemplo "Chrome 157 · Windows". Pasa el raton por encima para ver el detalle tecnico completo.
- Al editar un pais en Configuración › Países ya se conserva su codigo ISO2.

## Aviso de saldos

Cuando una cuenta queda por debajo del minimo, aparece un aviso flotante sobre la pagina. Puedes abrir «Revisar alertas» u ocultarlo con su boton.

## Signos de cifras (V-03.02)

Los importes y porcentajes positivos se muestran sin signo; los negativos llevan el signo menos. Los datos guardados no cambian.
# Aviso de alertas

El aviso de cuentas con alertas aparece encima del contenido sin tapar los botones de la pantalla. Se puede ocultar con el boton del propio aviso.
