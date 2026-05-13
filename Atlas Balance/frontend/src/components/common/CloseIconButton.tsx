import type { ButtonHTMLAttributes } from 'react';
import { X } from 'lucide-react';

interface CloseIconButtonProps
  extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'aria-label' | 'children' | 'type'> {
  ariaLabel: string;
}

export function CloseIconButton({ ariaLabel, className, title, ...buttonProps }: CloseIconButtonProps) {
  const classes = ['close-icon-button', className].filter(Boolean).join(' ');

  return (
    <button type="button" className={classes} aria-label={ariaLabel} title={title ?? ariaLabel} {...buttonProps}>
      <X size={18} strokeWidth={2} aria-hidden="true" />
    </button>
  );
}
