import { useEffect, useId, useRef, useState } from 'react';

interface EditableCellProps {
  value: string;
  editable: boolean;
  onSave: (next: string) => Promise<void> | void;
  displayValue?: string;
  displayClassName?: string;
}

export default function EditableCell({ value, editable, onSave, displayValue, displayClassName }: EditableCellProps) {
  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState(value);
  const [saving, setSaving] = useState(false);
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle');
  const feedbackId = useId();
  const savingRef = useRef(false);

  useEffect(() => {
    setDraft(value);
  }, [value]);

  useEffect(() => {
    if (saveState !== 'saved') {
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
    return <span className={displayClassName}>{displayValue || value || '-'}</span>;
  }

  if (isEditing) {
    return (
      <input
        autoFocus
        disabled={saving}
        value={draft}
        aria-invalid={saveState === 'error'}
        aria-describedby={saveState === 'error' ? feedbackId : undefined}
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
        onClick={() => {
          setSaveState('idle');
          setIsEditing(true);
        }}
        onDoubleClick={() => {
          setSaveState('idle');
          setIsEditing(true);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Enter' || event.key === 'F2') {
            event.preventDefault();
            setSaveState('idle');
            setIsEditing(true);
          }
        }}
        aria-describedby={saveState === 'error' ? feedbackId : undefined}
        aria-label={`Editar celda ${value || 'sin valor'}`}
      >
        {displayValue || value || '-'}
      </button>
      {saving ? <small className="cell-save-state" role="status">Guardando</small> : null}
      {saveState === 'saved' ? <small className="cell-save-state cell-save-state--ok" role="status">Guardado</small> : null}
      {saveState === 'error' ? (
        <small id={feedbackId} className="cell-save-state cell-save-state--error" role="alert">
          No guardado. Revisa el dato e inténtalo otra vez.
        </small>
      ) : null}
    </span>
  );
}
