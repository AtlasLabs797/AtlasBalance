import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

// V-03.02 (LOTE 8, D-9).
//
// D-9: el "Modal unico" estaba a medias: 5 usos de <Modal> y 8 modales
// montados a mano (backdrop + panel + role="dialog" escritos a pelo en cada
// pagina). Este test es de FUENTE, no de DOM: en este repo los tests unitarios
// no montan React, asi que se comprueba que nadie vuelve a escribir un modal
// a mano y que los modales migrados usan el componente compartido.
//
// El componente comun es src/components/common/Modal.tsx: ahi vive el fondo
// (modal-backdrop), el panel con role="dialog", la trampa de foco y el Escape.

const SRC = fileURLToPath(new URL('../../src/', import.meta.url));
// D-6 (LOTE 8): el CSS del modal salio de layout/extractos.css a su propia
// hoja de componente, src/styles/components/modal.css.
const CSS_MODALES = fileURLToPath(new URL('../../src/styles/components/modal.css', import.meta.url));

/** Modales que D-9 migro al componente comun (rutas relativas a src/). */
const MODALES_MIGRADOS = [
  'components/common/Modal.tsx',
  'components/usuarios/UsuarioModal.tsx',
  'components/auth/SessionTimeoutWarning.tsx',
  'pages/UsuariosPage.tsx',
  'pages/TitularesPage.tsx',
  'pages/CuentasPage.tsx',
  'pages/configuracion/PaisesPage.tsx',
  'pages/CuentaDetailPage.tsx',
  'pages/BackupsPage.tsx',
];

/** El componente comun es el unico sitio autorizado para pintar el fondo. */
const COMPONENTE_MODAL = 'components/common/Modal.tsx';

/**
 * Dialogos que NO son modales y se dejan como estan: el popup del calendario,
 * el chat flotante de la barra superior y los menus desplegables.
 */
const DIALOGOS_QUE_NO_SON_MODALES = [
  'components/common/DatePickerField.tsx',
  'components/layout/TopBar.tsx',
  'components/layout/CommandPalette.tsx',
  'components/layout/BottomNav.tsx',
];

function fuente(relativa: string): string {
  return readFileSync(path.join(SRC, relativa), 'utf8');
}

/** Ficheros .tsx/.ts de src/ (para el barrido completo). */
function ficherosSrc(): string[] {
  const encontrados: string[] = [];
  const recorrer = (directorio: string) => {
    for (const entrada of readdirSync(directorio, { withFileTypes: true })) {
      const completa = path.join(directorio, entrada.name);
      if (entrada.isDirectory()) {
        recorrer(completa);
        continue;
      }
      if (entrada.isFile() && (entrada.name.endsWith('.tsx') || entrada.name.endsWith('.ts'))) {
        encontrados.push(path.relative(SRC, completa).split(path.sep).join('/'));
      }
    }
  };
  recorrer(SRC);
  return encontrados.sort();
}

// Clases de fondo que delatan un modal montado a mano.
const CLASES_FONDO_MANUALES = [
  'className="modal-backdrop',
  'className={`modal-backdrop',
  "className='modal-backdrop",
  'users-modal-backdrop',
  'config-modal-backdrop',
  'import-modal-backdrop',
];

test('ningun modal se monta a mano fuera del componente comun', () => {
  const fallos: string[] = [];

  for (const fichero of ficherosSrc()) {
    if (fichero === COMPONENTE_MODAL) {
      continue;
    }
    const contenido = fuente(fichero);
    for (const clase of CLASES_FONDO_MANUALES) {
      if (contenido.includes(clase)) {
        fallos.push(`${fichero} sigue pintando el fondo a mano ("${clase}").`);
      }
    }
  }

  assert.deepEqual(fallos, [], `Hay modales montados a mano:\n${fallos.join('\n')}`);
});

test('ningun dialogo manual queda con role="dialog" fuera del componente comun', () => {
  const fallos: string[] = [];

  for (const fichero of ficherosSrc()) {
    if (fichero === COMPONENTE_MODAL || DIALOGOS_QUE_NO_SON_MODALES.includes(fichero)) {
      continue;
    }
    const contenido = fuente(fichero);
    if (contenido.includes('role="dialog"') || contenido.includes("role='dialog'")) {
      fallos.push(`${fichero} sigue pintando un dialogo a mano en lugar de usar <Modal>.`);
    }
  }

  assert.deepEqual(fallos, [], `Hay dialogos montados a mano:\n${fallos.join('\n')}`);
});

test('los nueve modales migrados usan el componente comun <Modal>', () => {
  const fallos: string[] = [];

  for (const fichero of MODALES_MIGRADOS) {
    if (fichero === COMPONENTE_MODAL) {
      continue;
    }
    const contenido = fuente(fichero);
    if (!contenido.includes('<Modal')) {
      fallos.push(`${fichero} no usa <Modal>.`);
    }
    if (!contenido.includes("from '@/components/common/Modal'")) {
      fallos.push(`${fichero} no importa el componente comun <Modal>.`);
    }
  }

  assert.deepEqual(fallos, [], `Modales sin migrar:\n${fallos.join('\n')}`);
});

test('ningun modal migrado gestiona el Escape por su cuenta', () => {
  // El Escape y la trampa de foco los lleva useDialogFocus dentro de <Modal>:
  // duplicarlos haria que el Escape cerrase el modal dos veces.
  const fallos: string[] = [];

  for (const fichero of MODALES_MIGRADOS) {
    if (fichero === COMPONENTE_MODAL) {
      continue;
    }
    const contenido = fuente(fichero);
    if (contenido.includes('useDialogFocus')) {
      fallos.push(`${fichero} sigue usando useDialogFocus: el Escape lo gestiona <Modal>.`);
    }
  }

  assert.deepEqual(fallos, [], `Escape gestionado por duplicado:\n${fallos.join('\n')}`);
});

test('cada modal migrado tiene un titleId unico', () => {
  const vistos = new Map<string, string>();

  for (const fichero of MODALES_MIGRADOS) {
    if (fichero === COMPONENTE_MODAL) {
      continue;
    }
    const contenido = fuente(fichero);
    const ids = [...contenido.matchAll(/titleId="([^"]+)"/g)].map((coincidencia) => coincidencia[1]);

    assert.ok(ids.length > 0, `${fichero} debe pasar un titleId a <Modal>.`);

    for (const id of ids) {
      const anterior = vistos.get(id);
      assert.equal(anterior, undefined, `El titleId "${id}" esta repetido en ${anterior} y ${fichero}.`);
      vistos.set(id, fichero);
    }
  }
});

test('el componente comun declara la semantica de dialogo y el cierre', () => {
  const contenido = fuente(COMPONENTE_MODAL);

  assert.ok(contenido.includes('role="dialog"'), 'El panel debe seguir siendo role="dialog".');
  assert.ok(contenido.includes('aria-modal="true"'), 'El panel debe seguir siendo aria-modal="true".');
  assert.ok(
    contenido.includes('aria-labelledby={titleId}'),
    'El panel debe seguir enlazando aria-labelledby con el titleId del titulo.'
  );
  assert.ok(contenido.includes('useDialogFocus'), 'El modal debe seguir usando la trampa de foco y el Escape.');
  assert.ok(!contenido.includes('<dialog'), 'El modal no debe usar el elemento <dialog> nativo.');
});

test('las variantes de backdrop que usan los modales migrados tienen CSS', () => {
  const css = readFileSync(CSS_MODALES, 'utf8');
  const componente = fuente(COMPONENTE_MODAL);

  assert.ok(css.includes('.modal-backdrop--fullscreen-mobile'), 'Falta el CSS de --fullscreen-mobile.');
  assert.ok(css.includes('.modal-backdrop--stack-top'), 'Falta el CSS de --stack-top.');
  assert.ok(componente.includes('fullscreen-mobile'), 'El componente comun debe exponer la variante fullscreen-mobile.');
  assert.ok(componente.includes('stack-top'), 'El componente comun debe exponer la variante stack-top.');
});
