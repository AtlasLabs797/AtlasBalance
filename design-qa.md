# Design QA - Canal IA

## Source visual truth

- Principal: `C:\Users\usuario\Downloads\IA\Captura de pantalla 2026-09-20 190746.png`
  (870 x 860 px), con apoyo de `190731.png` (866 x 856 px), `190922.png`
  (639 x 742 px), `190954.png` (1621 x 926 px) y los dos vídeos de la misma
  carpeta.
- El vídeo muestra tres estados de la cara: `idle`, `listening` y `thinking`.

## Rendered implementation

- URL: `http://localhost:5173/ia`
- Captura: pestaña Chrome local 1413039478, 1920 x 864 px CSS, densidad 1.
- Estado capturado: tema claro, sesión autenticada, IA no configurada.
- La captura se inspeccionó en navegador junto con la referencia; no se guardó
  como archivo local porque la superficie de navegador conectada solo entrega
  la captura inline.

## Comparison

La cabecera implementada conserva la jerarquía de la referencia (`Asistente`,
`Solo ve lo que tú puedes ver`) y muestra la nueva pieza morada con ojos
blancos. La página `/ia` ya no añade la cabecera redundante `IA` dentro del
contenido. El panel, los bordes violetas y el compositor siguen los tokens del
producto; las respuestas configuradas añaden la cara junto a cada mensaje y
los tres estados de la animación están implementados en `IconAiFace`.

No es una comparación válida de la conversación completa: la referencia
principal contiene mensajes, sugerencias y composer activo, mientras que el
entorno local devolvió `IA no disponible`. No se modificó configuración ni se
usaron credenciales para forzar ese estado.

### Required fidelity surfaces

- Typography: cabecera y subtítulo usan la tipografía y escala existentes del
  sistema; pendiente comparar mensajes activos en el mismo estado.
- Spacing/layout: panel completo, cabecera, borde, composer y chat flotante se
  ajustaron a la composición de referencia; pendiente verificar con mensajes.
- Colors/tokens: canal IA usa `--ai-*` violeta, con fondo y bordes derivados de
  los tokens existentes.
- Image/assets: la cara es una marca vectorial interactiva derivada de la
  referencia; no se copian capturas ni vídeos de usuario al producto.
- Copy/content: `Asistente` y `Solo ve lo que tú puedes ver` coinciden con la
  referencia; el contenido financiero real solo puede revisarse con IA activa.

## Findings

- No se observó un P0/P1/P2 en la cabecera visible.
- P2 de verificación: no se pudo comparar el estado activo de conversación,
  porque la API local tiene la IA desactivada/no configurada.

## Comparison history

- Pass 1: se corrigió la cara circular y la cabecera redundante. La captura
  posterior mostró la cara morada y la nueva cabecera en `/ia`; el estado activo
  siguió sin estar disponible.

## Final result

blocked

Blocker: falta una sesión local con IA configurada para capturar y comparar el
estado de conversación, composer activo, `Pensando` y widget flotante contra
las referencias.

## Latest structural check

La pantalla actual `/ia` renderiza dos instancias de `AiFace` (48 px en el
botón flotante y 34 px en la cabecera), ocho chips de sugerencias y el
`ai-chat-composer-box`. El panel calcula un radio de 18 px en el navegador.
Esta comprobación confirma la composición vacía y sus medidas, pero no cambia
el resultado: sigue faltando una conversación real con IA configurada para
validar mensajes, respuesta, `Pensando` y el widget abierto contra el vídeo.
