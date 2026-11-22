import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

// V-03.02 (LOTE 8, D-2 y D-8).
//
// D-2: las pantallas de configuracion (Paises, Configuracion, Alertas e
// Importacion) tenian textos visibles sin tilde y errores genericos sin causa
// ni siguiente paso.
// D-8: los colores del grafico de Configuracion eran texto libre: se podia
// guardar cualquier cosa y el grafico se rompia en silencio.
//
// No hay DOM ni render en este repo: los textos se comprueban sobre el fuente
// y el validador de color se extrae del propio archivo (es la expresion
// regular que usa la pagina al guardar).
//
// Solo se miran los textos VISIBLES (literales de cadena y texto JSX). Los
// comentarios del codigo se dejan sin tilde por costumbre del repo y no se
// comprueban.

const RUTAS = {
  paises: '../../src/pages/configuracion/PaisesPage.tsx',
  configuracion: '../../src/pages/ConfiguracionPage.tsx',
  alertas: '../../src/pages/AlertasPage.tsx',
  importacion: '../../src/pages/ImportacionPage.tsx',
} as const;

type Clave = keyof typeof RUTAS;

const fuentes: Record<Clave, string> = {
  paises: readFileSync(new URL(RUTAS.paises, import.meta.url), 'utf8'),
  configuracion: readFileSync(new URL(RUTAS.configuracion, import.meta.url), 'utf8'),
  alertas: readFileSync(new URL(RUTAS.alertas, import.meta.url), 'utf8'),
  importacion: readFileSync(new URL(RUTAS.importacion, import.meta.url), 'utf8'),
};

const sourceFile = ts.createSourceFile(
  'ConfiguracionPage.tsx',
  fuentes.configuracion,
  ts.ScriptTarget.ES2020,
  true,
  ts.ScriptKind.TSX,
);

function normalizar(texto: string): string {
  return texto.replace(/\s+/g, ' ').trim();
}

// Textos visibles de un archivo: literales de cadena (incluidas las partes de
// una plantilla) y texto JSX. Los comentarios quedan fuera.
function textosVisibles(clave: Clave): string[] {
  const file = ts.createSourceFile(
    `${clave}.tsx`,
    fuentes[clave],
    ts.ScriptTarget.ES2020,
    true,
    ts.ScriptKind.TSX,
  );
  const textos: string[] = [];
  const visit = (node: ts.Node) => {
    if (
      ts.isStringLiteral(node) ||
      ts.isNoSubstitutionTemplateLiteral(node) ||
      ts.isTemplateHead(node) ||
      ts.isTemplateMiddle(node) ||
      ts.isTemplateTail(node)
    ) {
      const texto = normalizar(node.text);
      if (texto) {
        textos.push(texto);
      }
    } else if (ts.isJsxText(node)) {
      const texto = normalizar(node.text);
      if (texto) {
        textos.push(texto);
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(file);
  return textos;
}

const visibles: Record<Clave, string[]> = {
  paises: textosVisibles('paises'),
  configuracion: textosVisibles('configuracion'),
  alertas: textosVisibles('alertas'),
  importacion: textosVisibles('importacion'),
};

const blob: Record<Clave, string> = {
  paises: visibles.paises.join('\n'),
  configuracion: visibles.configuracion.join('\n'),
  alertas: visibles.alertas.join('\n'),
  importacion: visibles.importacion.join('\n'),
};

// ---------------------------------------------------------------- D-2 copy

// Textos sin tilde citados en el informe de revision: no pueden volver a
// aparecer en ninguna de las cuatro pantallas.
const SIN_TILDE_DEL_INFORME = ['Nuevo pais', 'Cargando paises', 'Anadir', 'podran iniciar sesion'];

test('ninguna de las cuatro pantallas repite los textos sin tilde del informe', () => {
  for (const clave of Object.keys(visibles) as Clave[]) {
    for (const texto of SIN_TILDE_DEL_INFORME) {
      assert.ok(!blob[clave].includes(texto), `${clave} sigue mostrando "${texto}" sin tilde.`);
    }
  }
});

// Barrido de tildes por pantalla: texto antes -> texto despues.
// `exacto` compara el mensaje completo (para textos que son subcadena de otro
// visible, como "Valida" dentro de "Validando...").
const CAMBIOS: Array<{ pagina: Clave; antes: string; despues: string; exacto?: boolean }> = [
  // Paises
  { pagina: 'paises', antes: 'Nuevo pais', despues: 'Nuevo país' },
  { pagina: 'paises', antes: 'Cargando paises...', despues: 'Cargando países...' },
  { pagina: 'paises', antes: 'No hay paises con estos filtros.', despues: 'No hay países con estos filtros.' },
  {
    pagina: 'paises',
    antes: 'Ajusta la busqueda o crea un pais nuevo.',
    despues: 'Ajusta la búsqueda o crea un país nuevo.',
  },
  {
    pagina: 'paises',
    antes: 'Catalogo de paises disponibles para asignar a cuentas y permisos por pais.',
    despues: 'Catálogo de países disponibles para asignar a cuentas y permisos por país.',
  },
  { pagina: 'paises', antes: 'Escribe el nombre del pais.', despues: 'Escribe el nombre del país.' },
  {
    pagina: 'paises',
    antes: 'El codigo ISO2 debe tener exactamente dos letras.',
    despues: 'El código ISO2 debe tener exactamente dos letras.',
  },
  {
    pagina: 'paises',
    antes: 'Dos letras en mayusculas. Opcional pero recomendado para el selector de organizacion.',
    despues: 'Dos letras en mayúsculas. Opcional pero recomendado para el selector de organización.',
  },
  { pagina: 'paises', antes: 'Eliminar pais', despues: 'Eliminar país' },
  { pagina: 'paises', antes: 'Editar pais', despues: 'Editar país' },
  { pagina: 'paises', antes: 'Restaurar pais', despues: 'Restaurar país' },
  { pagina: 'paises', antes: 'Confirmar eliminacion', despues: 'Confirmar eliminación' },
  {
    pagina: 'paises',
    antes: 'Tienes cambios sin guardar en este pais. Si cierras, se perderan.',
    despues: 'Tienes cambios sin guardar en este país. Si cierras, se perderán.',
  },
  {
    pagina: 'paises',
    antes: 'Las cuentas con este pais conservaran su etiqueta y la accion quedara auditada.',
    despues: 'Las cuentas con este país conservarán su etiqueta y la acción quedará auditada.',
  },
  // Configuracion
  {
    pagina: 'configuracion',
    antes: 'Los gerentes y empleados podran iniciar sesion solo con contrasena.',
    despues: 'Los gerentes y empleados podrán iniciar sesión solo con contraseña.',
  },
  {
    pagina: 'configuracion',
    antes: 'Los administradores seguiran obligados a usar Authenticator. El cambio se aplica al siguiente inicio de sesion y queda registrado en auditoria.',
    despues:
      'Los administradores seguirán obligados a usar Authenticator. El cambio se aplica al siguiente inicio de sesión y queda registrado en auditoría.',
  },
  // Importacion
  { pagina: 'importacion', antes: 'Anadir dinero', despues: 'Añadir dinero' },
  { pagina: 'importacion', antes: 'Formato automatico aplicado', despues: 'Formato automático aplicado' },
  { pagina: 'importacion', antes: 'Aun no hay datos pegados.', despues: 'Aún no hay datos pegados.' },
  { pagina: 'importacion', antes: 'Filas validas', despues: 'Filas válidas' },
  { pagina: 'importacion', antes: 'Preview (primeras 3 filas)', despues: 'Vista previa (primeras 3 filas)' },
  { pagina: 'importacion', antes: 'El lote esta', despues: 'El lote está' },
  {
    pagina: 'importacion',
    antes: 'los importes quedaran registrados con tu declaracion.',
    despues: 'los importes quedarán registrados con tu declaración.',
  },
  { pagina: 'importacion', antes: 'Atras', despues: 'Atrás', exacto: true },
  { pagina: 'importacion', antes: 'Valida', despues: 'Válida', exacto: true },
];

test('cada texto sin tilde se ha sustituido por su version acentuada', () => {
  for (const { pagina, antes, despues, exacto } of CAMBIOS) {
    if (exacto) {
      assert.ok(
        !visibles[pagina].includes(antes),
        `${pagina} sigue mostrando el texto "${antes}".`
      );
      assert.ok(
        visibles[pagina].includes(despues),
        `${pagina} deberia mostrar el texto "${despues}".`
      );
      continue;
    }
    assert.ok(!blob[pagina].includes(antes), `${pagina} sigue mostrando "${antes}".`);
    assert.ok(blob[pagina].includes(despues), `${pagina} deberia mostrar "${despues}".`);
  }
});

// ---------------------------------------------------------------- D-2 copy unificado

// El mismo concepto con la misma formula en las cuatro pantallas.
const UNIFICADOS: Array<{ pagina: Clave; concepto: string; texto: string }> = [
  { pagina: 'paises', concepto: 'carga de países', texto: 'Cargando países...' },
  { pagina: 'alertas', concepto: 'carga de alertas', texto: 'Cargando alertas activas...' },
  { pagina: 'importacion', concepto: 'carga de lotes', texto: 'Cargando lotes...' },
  { pagina: 'configuracion', concepto: 'titulo de configuración', texto: 'Configuración' },
];

test('las pantallas cargan con la misma formula "Cargando ..."', () => {
  for (const { pagina, concepto, texto } of UNIFICADOS) {
    assert.ok(blob[pagina].includes(texto), `${pagina} deberia usar "${texto}" (${concepto}).`);
  }
});

// Errores genericos: ahora dicen que fallo y que hacer a continuacion.
const ERRORES_ACCIONABLES: Array<{ pagina: Clave; generico: string; accionable: string }> = [
  {
    pagina: 'paises',
    generico: 'No se pudieron cargar paises.',
    accionable: 'No se pudieron cargar los países. Revisa la conexión con el servidor e inténtalo de nuevo.',
  },
  {
    pagina: 'paises',
    generico: 'No se pudo guardar país',
    accionable: 'No se pudo guardar el país. Revisa el nombre y el código ISO2 e inténtalo de nuevo.',
  },
  {
    pagina: 'paises',
    generico: 'No se pudo eliminar pais',
    accionable: 'No se pudo enviar el país a la papelera. Recarga la lista e inténtalo de nuevo.',
  },
  {
    pagina: 'configuracion',
    generico: 'No se pudo guardar.',
    accionable: 'No se pudo guardar la configuración. Revisa los campos marcados e inténtalo de nuevo.',
  },
  {
    pagina: 'configuracion',
    generico: 'No se pudo cargar configuración.',
    accionable: 'No se pudo cargar la configuración. Revisa la conexión con el servidor e inténtalo de nuevo.',
  },
  {
    pagina: 'alertas',
    generico: 'No se pudo cargar la configuración de alertas.',
    accionable:
      'No se pudo cargar la configuración de alertas. Revisa la conexión con el servidor e inténtalo de nuevo.',
  },
  {
    pagina: 'alertas',
    generico: 'No se pudo guardar la alerta global.',
    accionable: 'No se pudo guardar la alerta global. Revisa el saldo mínimo y los destinatarios e inténtalo de nuevo.',
  },
  {
    pagina: 'importacion',
    generico: 'No se pudo validar la importación',
    accionable:
      'No se pudo validar la importación. Revisa los datos pegados y el formato de importación de la cuenta e inténtalo de nuevo.',
  },
  {
    pagina: 'importacion',
    generico: 'No se pudo confirmar la importación',
    accionable: 'No se pudo confirmar la importación. Revisa las filas seleccionadas e inténtalo de nuevo.',
  },
];

test('los errores genericos dicen que fallo y que hacer a continuacion', () => {
  for (const { pagina, generico, accionable } of ERRORES_ACCIONABLES) {
    assert.ok(
      !visibles[pagina].includes(generico),
      `${pagina} sigue mostrando el error generico "${generico}".`
    );
    assert.ok(
      visibles[pagina].includes(accionable),
      `${pagina} deberia mostrar "${accionable}".`
    );
  }
});

test('los cuatro pantallas siguen usando el helper de errores de la app', () => {
  for (const pagina of Object.keys(fuentes) as Clave[]) {
    assert.ok(
      fuentes[pagina].includes('extractErrorMessage'),
      `${pagina} debe usar extractErrorMessage (muestra el mensaje del backend cuando existe).`
    );
  }
});

// ---------------------------------------------------------------- D-8 color

// Expresion regular del validador, extraida del fuente: es la que usa la
// pagina al guardar, no una copia del test.
function patronValidadorColor(): RegExp {
  let patron: string | null = null;
  const visit = (node: ts.Node) => {
    if (patron !== null) {
      return;
    }
    if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && node.name.text === 'COLOR_HEX_PATTERN') {
      if (node.initializer && ts.isRegularExpressionLiteral(node.initializer)) {
        // El texto del nodo incluye las barras: /patron/flags
        const bruto = node.initializer.getText(sourceFile);
        const ultimo = bruto.lastIndexOf('/');
        patron = bruto.slice(1, ultimo);
      }
      return;
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  assert.ok(patron, 'No se encuentra el patron COLOR_HEX_PATTERN en ConfiguracionPage.tsx.');
  return new RegExp(patron);
}

test('el validador de color acepta solo hexadecimales validos', () => {
  const patron = patronValidadorColor();

  for (const valido of ['#1A2B3C', '#fff', '#FFF', '#AbC', '#000000']) {
    assert.ok(patron.test(valido), `"${valido}" deberia ser un color valido.`);
  }

  for (const invalido of ['rojo', '#12345', 'rgb(1,2,3)', '#', '1A2B3C', '#GGG', 'red']) {
    assert.ok(!patron.test(invalido), `"${invalido}" no deberia ser un color valido.`);
  }
});

test('el campo de color explica el formato esperado y usa el color del tema por defecto', () => {
  const fuente = fuentes.configuracion;

  assert.ok(
    fuente.includes('El color no es válido. Escribe un hexadecimal como #1A2B3C (o #fff)'),
    'El campo de color debe explicar junto al campo cual es el formato esperado.'
  );
  assert.ok(
    fuente.includes('Vacío = color del tema'),
    'El campo de color debe indicar que vacio significa color del tema.'
  );
  assert.ok(
    !fuente.includes("'#43B430'"),
    'El color por defecto no puede ser un hex escrito a mano: debe salir del token del tema.'
  );
  assert.ok(
    fuente.includes("'--chart-5'") && fuente.includes("'--chart-7'") && fuente.includes("'--chart-1'"),
    'Los colores por defecto deben salir de los tokens de grafico.'
  );
});

// El guardado no puede enviar un color invalido.
test('el guardado comprueba el color antes de enviar la configuracion', () => {
  const fuente = fuentes.configuracion;
  const inicio = fuente.indexOf('const saveConfig = async');
  assert.ok(inicio > 0, 'No se encuentra saveConfig en ConfiguracionPage.tsx.');
  const cuerpo = fuente.slice(inicio, fuente.indexOf('const handleSaveConfig', inicio));

  const validacion = cuerpo.indexOf('COLOR_HEX_PATTERN');
  const guardado = cuerpo.indexOf("api.put('/configuracion'");

  assert.ok(validacion > 0, 'saveConfig debe validar el color antes de guardar.');
  assert.ok(guardado > 0, 'saveConfig debe seguir guardando en /configuracion.');
  assert.ok(
    validacion < guardado,
    'La validacion del color debe ejecutarse antes de api.put: un color invalido no se puede guardar.'
  );
});

// El patron se usa tambien al escribir, para marcar el campo como invalido.
test('el campo de color marca aria-invalid cuando el valor no es valido', () => {
  const fuente = fuentes.configuracion;
  assert.ok(fuente.includes('aria-invalid={invalid}'), 'El input de color debe marcar aria-invalid.');
  assert.ok(fuente.includes('role="alert"'), 'El error del campo de color debe anunciarse como alerta.');
});
