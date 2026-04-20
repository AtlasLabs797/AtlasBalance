import { useEffect, useRef, useState } from 'react';

interface EditableCellProps {
  value: string;
  editable: boolean;
  onSave: (next: string) => Promise<void> | void;
  displayClassName?: string;
}

export default function EditableCell({ value, editable, onSave, displayClassName }: EditableCellProps) {
  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState(value);
  const [saving, setSaving] = useState(false);
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle');
  const savingRef = useRef(false);

  useEffect(() => {
    setDraft(value);
  }, [value]);

  useEffect(() => {
    if (saveState === 'idle') {
      return undefined;
    }

    const timer = window.setTimeout(() => setSaveState('idle'), 1800);
    return () => window.clearTimeout(timer);
  }, [saveState]);

  const commit = async () => {
    if (!editable || savingRef.current) return;
    if (draft === value) {
      setIsEditing(false);
      return;
    }

    savingRef.current = true;
    setSaving(true);
    try {
      await onSave(draft);
      setSaveState('saved');
      setIsEditing(false);
    } catch {
      setDraft(value);
      setSaveState('error');
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  if (!editable) {
    return <span className={displayClassName}>{value || '-'}</span>;
  }

  if (isEditing) {
    return (
      <input
        autoFocus
        disabled={saving}
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={() => void commit()}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault();
            void commit();
          }
          if (e.key === 'Escape') {
            setDraft(value);
            setIsEditing(false);
          }
        }}
        aria-label="Editar celda"
      />
    );
  }

  return (
    <span className="cell-edit-shell">
      <button
        type="button"
        className={['cell-edit-button', displayClassName].filter(Boolean).join(' ')}
        onClick={() => setIsEditing(true)}
        onDoubleClick={() => setIsEditing(true)}
        onKeyDown={(event) => {
          if (event.key === 'Enter' || event.key === 'F2') {
            event.preventDefault();
            setIsEditing(true);
          }
        }}
        aria-label={`Editar celda ${value || 'sin valor'}`}
      >
        {value || '-'}
      </button>
      {saving ? <small className="cell-save-state">Guardando</small> : null}
      {saveState === 'saved' ? <small className="cell-save-state cell-save-state--ok">Guardado</small> : null}
      {saveState === 'error' ? <small className="cell-save-state cell-save-state--error">Error</small> : null}
    </span>
  );
}
