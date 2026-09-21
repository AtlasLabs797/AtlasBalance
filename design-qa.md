# Design QA - Canal IA

## Source visual truth

- Principal: `C:\Users\usuario\Downloads\IA\Captura de pantalla 2026-09-20 190746.png`.
- Compacto: `190922.png` y `190731.png`.
- Refinamiento solicitado del popup: `C:\Users\usuario\AppData\Local\Temp\codex-clipboard-e4115b9d-9da3-4390-9f6a-d9390c1fd864.png` (492 x 642 px).
- Conversación y estados: `190954.png` y los dos vídeos de la misma carpeta.
- Animación exacta de estados: `C:\Users\usuario\Downloads\AiFace - Estados (standalone).html`.
- Estado de mensaje enviado: `C:\Users\usuario\Downloads\Chat - Mensaje Enviado (standalone).html`.
- La cara de referencia se implementa con los estados `idle`, `listening` y
  `thinking` del design system local.

## Rendered implementation

- Página: `http://localhost:5173/ia`.
- Widget: `http://localhost:5173/dashboard?periodo=1m&divisa=EUR`.
- Capturas inspeccionadas en Chrome local, pestaña 1413039478, 1920 x 864 px
  CSS, tema claro y sesión autenticada. La superficie conectada entrega las
  capturas inline, no como archivo local.

## Comparison

- `/ia` conserva la jerarquía `Asistente / Solo ve lo que tú puedes ver`, la
  cara morada, los ocho chips de sugerencias y el composer anidado.
- El widget usa una superficie compacta de 420 px, saludo, dos sugerencias,
  composer y botón circular de cierre independiente; la altura revisada es de
  560 px y el círculo conserva 56 px con una X interior de 24 px.
- Los menús de modelo y pensamiento se abren sobre el composer y son visibles;
  usan iconos Lucide y semántica de menú accesible.
- La página `/ia` no duplica el widget flotante. Al cambiar de ruta, el estado
  abierto se cierra.
- La cara reutiliza `IconAiFace` con `idle`, `listening` y `thinking`; el CSS
  conserva los keyframes del HTML adjunto y sus ritmos de 9/14 s, 7 s y
  2,8/5,2 s. `prefers-reduced-motion` la deja estable cuando el sistema lo
  solicita.
- El estado de mensaje enviado reproduce la burbuja derecha del usuario, su
  avatar y metadato horario. La fila `Pensando` usa la cara `thinking` y una
  etiqueta con pulso de 1,5 s dentro de la burbuja del asistente.

## Comparison history

- Pass 1: se validó la composición completa, el widget y los menús; resultado
  `passed` sin hallazgos P0/P1/P2.
- Pass 2: se corrigió el tamaño visual de la X, se mantuvo el círculo de cierre,
  se aumentó el popup a 560 px y se aceleraron los ciclos de `AiFace`. La nueva
  captura del widget confirmó las proporciones solicitadas.
- Pass 3: se comparó directamente la captura adjunta de 492 x 642 px con el
  popup renderizado en Chrome; no quedaron diferencias P0/P1/P2 en la X, el
  círculo contenedor o la altura del panel.
- Pass 4: se compararon los keyframes y duraciones de `AiFace` con el HTML
  standalone. Chrome tenía `prefers-reduced-motion` activo, así que la
  reproducción visual en movimiento queda pendiente de repetir con esa opción
  desactivada.
- Pass 5: se comparó estáticamente el markup y CSS del estado enviado con
  `Chat - Mensaje Enviado (standalone).html`; la secuencia `mensaje de usuario
  -> Pensando -> respuesta` quedó cubierta en `AiChatPanel`. El navegador no
  permitió abrir el adjunto como `file://`, por lo que no se declara una
  comparación visual dinámica de esta referencia.
- Pass 6: Chrome devolvió `prefers-reduced-motion: reduce`; los keyframes
  estaban cargados, pero la media query de accesibilidad ganó con
  `animation: none !important`. Se confirma bloqueo de entorno, no regresión
  de CSS ni de React.

## Findings

- No se observó ningún P0, P1 o P2 visual en las superficies inspeccionadas.
- No se ejecutó una pregunta real contra OpenRouter: la validación de este
  cambio se limitó al UI/UX para no alterar datos ni consumo externo.

## Final result

passed

## Latest structural check

La implementación renderiza la composición completa, el widget compacto, el
cierre independiente y ambos menús. El cambio reutiliza los tokens y las
animaciones del HTML adjunto; no copia capturas ni vídeos al producto. La
validación de movimiento queda condicionada a la preferencia del navegador.
