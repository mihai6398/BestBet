import { useEffect, useRef } from 'react';
import type { ReactNode } from 'react';
import { useT } from '../lib/i18n';

interface ModalProps {
    title: string;
    onClose: () => void;
    children: ReactNode;
    /** Wider for forms with two columns of fields. */
    wide?: boolean;
}

/**
 * A modal on the browser's own <dialog>: focus moves into it, Escape closes it, and the page behind
 * cannot be clicked — none of which has to be rebuilt by hand.
 */
export function Modal({ title, onClose, children, wide = false }: ModalProps) {
    const ref = useRef<HTMLDialogElement>(null);
    const t = useT();

    useEffect(() => {
        const dialog = ref.current;
        if (dialog && !dialog.open) dialog.showModal();
        return () => dialog?.close();
    }, []);

    return (
        <dialog
            ref={ref}
            className={wide ? 'modal modal-wide' : 'modal'}
            aria-label={title}
            onCancel={(e) => {
                e.preventDefault();
                onClose();
            }}
        >
            <div className="modal-head">
                <h2>{title}</h2>
                <button type="button" className="link-button" onClick={onClose} aria-label={t.common.close}>
                    ×
                </button>
            </div>
            {children}
        </dialog>
    );
}

interface ConfirmProps {
    title: string;
    message: string;
    confirmLabel: string;
    onConfirm: () => void;
    onCancel: () => void;
    busy?: boolean;
    /** Styles the confirm button as destructive. */
    danger?: boolean;
}

/** "Are you sure?" for anything that cannot be undone. */
export function ConfirmDialog({ title, message, confirmLabel, onConfirm, onCancel, busy = false, danger = false }: ConfirmProps) {
    const t = useT();

    return (
        <Modal title={title} onClose={onCancel}>
            <p>{message}</p>
            <div className="modal-actions">
                <button type="button" onClick={onCancel} disabled={busy}>
                    {t.common.cancel}
                </button>
                <button type="button" className={danger ? 'danger' : 'primary'} onClick={onConfirm} disabled={busy}>
                    {confirmLabel}
                </button>
            </div>
        </Modal>
    );
}
