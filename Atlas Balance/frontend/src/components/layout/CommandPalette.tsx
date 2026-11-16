import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Building2, WalletCards } from 'lucide-react';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import api from '@/services/api';
import { getVisibleNavigationItems, navigationGroups, type NavigationGroup } from '@/utils/navigation';
import { useAuthStore } from '@/stores/authStore';
import { useIaAvailabilityStore } from '@/stores/iaAvailabilityStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import type { Cuenta, PaginatedResponse, Titular } from '@/types';

interface CommandPaletteProps {
  open: boolean;
  onClose: () => void;
}

const GROUP_ORDER: NavigationGroup[] = ['operacion', 'control', 'sistema'];

// Busqueda tolerante a tildes y mayusculas: el usuario escribe "auditoria" y
// tiene que encontrar "Auditoría".
function normalize(value: string) {
  return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

export function CommandPalette({ open, onClose }: CommandPaletteProps) {
  const navigate = useNavigate();
  const usuario = useAuthStore((state) => state.usuario);
  const aiAvailable = useIaAvailabilityStore((state) => state.available);
  const canViewDashboard = usePermisosStore((state) => state.canViewDashboard());
  const paisId = usePaisScopeStore((state) => state.selectedPaisId);
  const [query, setQuery] = useState('');
  const search = useDebouncedValue(query.trim());
  const [activeIndex, setActiveIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);

  // Mismo conjunto y mismos permisos que la navegacion lateral: la paleta no
  // puede ofrecer una pantalla que el usuario no tiene derecho a abrir.
  const items = useMemo(
    () =>
      getVisibleNavigationItems(usuario?.rol, {
        aiAvailable,
        dashboardAvailable: usuario?.rol === 'ADMIN' || canViewDashboard,
      }),
    [usuario?.rol, aiAvailable, canViewDashboard]
  );

  const results = useMemo(() => {
    const term = normalize(query.trim());
    const matched = term
      ? items.filter((item) => normalize(item.label).includes(term))
      : items;
    return GROUP_ORDER.flatMap((group) => {
      const grupo = matched.filter((item) => item.group === group);
      return grupo.length > 0 ? [{ group, items: grupo }] : [];
    });
  }, [items, query]);

  const entityQuery = useQuery({
    queryKey: ['command-palette', usuario?.id, paisId, search],
    enabled: open && search.length > 0 && search === query.trim() && Boolean(usuario?.id),
    queryFn: async ({ signal }) => {
      const params = { search, paisId: paisId || undefined, pageSize: 10 };
      const [titulares, cuentas] = await Promise.all([
        api.get<PaginatedResponse<Titular>>('/titulares', { params, signal }),
        api.get<PaginatedResponse<Cuenta & { titular_nombre: string }>>('/cuentas', { params, signal }),
      ]);
      return { titulares: titulares.data.data, cuentas: cuentas.data.data };
    },
    staleTime: 0,
  });

  const entitySections = useMemo(() => {
    if (!open || !query.trim() || search !== query.trim() || entityQuery.isFetching || entityQuery.isError || !entityQuery.data) return [];
    return [
      { label: 'Titulares', items: entityQuery.data.titulares.map((item) => ({ to: `/titulares/${item.id}`, label: item.nombre, icon: <Building2 size={20} />, meta: 'Titular' })) },
      { label: 'Cuentas', items: entityQuery.data.cuentas.map((item) => ({ to: `/cuentas/${item.id}`, label: item.nombre, icon: <WalletCards size={20} />, meta: item.titular_nombre })) },
    ].filter((section) => section.items.length > 0);
  }, [open, query, search, entityQuery.data, entityQuery.isFetching, entityQuery.isError]);

  const flat = useMemo(() => [...results.flatMap((section) => section.items), ...entitySections.flatMap((section) => section.items)], [results, entitySections]);

  useEffect(() => {
    if (open) {
      setQuery('');
      setActiveIndex(0);
      inputRef.current?.focus();
    }
  }, [open]);

  useEffect(() => {
    setActiveIndex(0);
  }, [query]);

  useEffect(() => {
    setActiveIndex((current) => Math.min(current, Math.max(0, flat.length - 1)));
  }, [flat.length]);

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onClose();
        return;
      }
      if (event.key === 'ArrowDown') {
        event.preventDefault();
        setActiveIndex((current) => (flat.length === 0 ? 0 : (current + 1) % flat.length));
        return;
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault();
        setActiveIndex((current) => (flat.length === 0 ? 0 : (current - 1 + flat.length) % flat.length));
        return;
      }
      if (event.key === 'Enter') {
        const destino = flat[activeIndex];
        if (destino) {
          event.preventDefault();
          navigate(destino.to);
          onClose();
        }
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, flat, activeIndex, navigate, onClose]);

  useEffect(() => {
    listRef.current
      ?.querySelector('[data-active="true"]')
      ?.scrollIntoView({ block: 'nearest' });
  }, [activeIndex]);

  if (!open) {
    return null;
  }

  let index = -1;

  return (
    <div
      className="app-palette-scrim"
      role="presentation"
      onClick={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <div className="app-palette" role="dialog" aria-modal="true" aria-label="Buscar en Atlas Balance">
        <div className="app-palette-field">
          <input
            ref={inputRef}
            className="app-palette-input"
            type="text"
            autoComplete="off"
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            enterKeyHint="search"
            value={query}
            placeholder="Buscar pantalla, titular o cuenta..."
            aria-label="Buscar pantalla, titular o cuenta"
            onChange={(event) => setQuery(event.target.value)}
          />
          <kbd className="app-kbd">Esc</kbd>
        </div>
        <div className="app-palette-body" ref={listRef}>
          {flat.length === 0 && !entityQuery.isFetching && search === query.trim() && !entityQuery.isError ? (
            <p className="app-palette-empty">No hay resultados para «{query}».</p>
          ) : null}
          {entityQuery.isError ? <p className="app-palette-empty">No se pudieron buscar titulares y cuentas.</p> : null}
          {query.trim() && (entityQuery.isFetching || search !== query.trim()) ? <p className="app-palette-empty">Buscando titulares y cuentas...</p> : null}
          {
            results.map((section) => (
              <div key={section.group}>
                <p className="app-palette-group">{navigationGroups[section.group].label}</p>
                {section.items.map((item) => {
                  index += 1;
                  const current = index;
                  return (
                    <button
                      key={item.to}
                      type="button"
                      className={`app-palette-item${current === activeIndex ? ' app-palette-item--active' : ''}`}
                      data-active={current === activeIndex}
                      onMouseEnter={() => setActiveIndex(current)}
                      onClick={() => {
                        navigate(item.to);
                        onClose();
                      }}
                    >
                      <span className="app-palette-item-icon" aria-hidden="true">{item.icon}</span>
                      <span>{item.label}</span>
                      <span className="app-palette-item-meta">{item.to}</span>
                    </button>
                  );
                })}
              </div>
            ))
          }
          {entitySections.map((section) => (
            <div key={section.label}>
              <p className="app-palette-group">{section.label}</p>
              {section.items.map((item) => {
                index += 1;
                const current = index;
                return (
                  <button key={item.to} type="button" className={`app-palette-item${current === activeIndex ? ' app-palette-item--active' : ''}`} data-active={current === activeIndex} onMouseEnter={() => setActiveIndex(current)} onClick={() => { navigate(item.to); onClose(); }}>
                    <span className="app-palette-item-icon" aria-hidden="true">{item.icon}</span>
                    <span>{item.label}</span>
                    <span className="app-palette-item-meta">{item.meta}</span>
                  </button>
                );
              })}
            </div>
          ))}
        </div>
        <div className="app-palette-foot">
          <span><kbd className="app-kbd">↑</kbd><kbd className="app-kbd">↓</kbd> moverse</span>
          <span><kbd className="app-kbd">Enter</kbd> abrir</span>
          <span><kbd className="app-kbd">Esc</kbd> cerrar</span>
        </div>
      </div>
    </div>
  );
}
