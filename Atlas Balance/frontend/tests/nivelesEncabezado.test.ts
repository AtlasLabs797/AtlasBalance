import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';

// V-03.02 (LOTE 8, D-10).
//
// `<EmptyState>` y `<KpiCard` pintan un encabezado. Si se renderizan
// directamente bajo el `h1` de la pagina, el DOM salta de `h1` a `h3`.
//
// El componente acepta `headingLevel="h2"` para esos casos, y el CSS mantiene
// el mismo tamano (21px) en los dos niveles, de modo que el aspecto no
// cambia: solo se corrige la jerarquia.
//
// Este test comprueba, sobre el fuente, que ningun componente se queda sin
// `headingLevel="h2"` cuando le corresponde.

const PAGINAS = [
  'pages/AuditoriaPage.tsx',
  'pages/CuentaDetailPage.tsx',
  'pages/DashboardPage.tsx',
  'pages/DashboardTitularPage.tsx',
  'pages/DispositivosMfaPage.tsx',
  'pages/ExportacionesPage.tsx',
  'pages/FormatosImportacionPage.tsx',
  'pages/ImportacionPage.tsx',
  'pages/PapeleraPage.tsx',
  'pages/RevisionPage.tsx',
  'pages/configuracion/PaisesPage.tsx',
];

function ruta(rel: string): string {
  return join('src', rel);
}

function lineaDeApertura(linea: string): 'h1' | 'h2' | 'h3' | 'componente' | null {
  if (/<h1(\s|>|$)/.test(linea)) return 'h1';
  if (/<h2(\s|>|$)/.test(linea)) return 'h2';
  if (/<h3(\s|>|$)/.test(linea)) return 'h3';
  if (/<(EmptyState|KpiCard)(\s|\/|>|$)/.test(linea)) return 'componente';
  return null;
}

test('D-10: ningun EmptyState ni KpiCard se queda sin headingLevel cuando cuelga del h1', () => {
  const sinNivel: string[] = [];

  for (const pagina of PAGINAS) {
    const lineas = readFileSync(ruta(pagina), 'utf8').split(/\r?\n/);
    let h1Visto = false;
    let h2DesdeElH1 = false;

    lineas.forEach((linea, indice) => {
      const tipo = lineaDeApertura(linea);
      if (tipo === 'h1') {
        h1Visto = true;
        h2DesdeElH1 = false;
        return;
      }
      if (tipo === 'h2') {
        if (h1Visto) h2DesdeElH1 = true;
        return;
      }
      if (tipo !== 'componente') return;
      // Sin h1 previo es un retorno temprano que se dibuja sin encabezado de
      // pagina: el h3 se queda. Con un h2 intermedio la secuencia ya es
      // h1 -> h2 -> h3 y tampoco se toca.
      if (!h1Visto || h2DesdeElH1) return;
      // La prop puede estar en la linea de apertura o en la siguiente.
      const ventana = lineas.slice(indice, indice + 4).join('\n');
      if (!ventana.includes('headingLevel="h2"')) {
        sinNivel.push(`${pagina}:${indice + 1}`);
      }
    });
  }

  assert.deepEqual(sinNivel, [], `componentes con salto de nivel: ${sinNivel.join(', ')}`);
});

test('D-10: el componente acepta headingLevel y el CSS conserva el tamano del h3', () => {
  const emptyState = readFileSync(ruta('components/common/EmptyState.tsx'), 'utf8');
  const kpiCard = readFileSync(ruta('components/dashboard/KpiCard.tsx'), 'utf8');
  assert.match(emptyState, /headingLevel\?: 'h2' \| 'h3'/);
  assert.match(kpiCard, /headingLevel\?: 'h2' \| 'h3'/);
  assert.match(emptyState, /const Heading = headingLevel;/);
  assert.match(kpiCard, /const Heading = headingLevel;/);

  // El h2 global mide 28px y el h3 21px: sin una regla propia, cambiar el
  // nivel habria cambiado el tamano del texto.
  const shell = readFileSync(ruta('styles/layout/shell.css'), 'utf8');
  assert.match(shell, /\.empty-state h2 \{\s*font-size: var\(--text-21\);/);

  const dashboard = readFileSync(ruta('styles/layout/dashboard.css'), 'utf8');
  assert.match(dashboard, /\.dashboard-kpi h3,\s*\.dashboard-kpi h2 \{/);
});
