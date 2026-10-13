import { useState } from 'react';
import type { LiveFormState, OddsFormState } from '../lib/forms';
import { QUICK_IMPORT_EXAMPLE, parseQuickImport, toQuickImportJson } from '../lib/quickImport';
import type { QuickImportResult } from '../lib/quickImport';
import { useT } from '../lib/i18n';

interface Props {
    odds: OddsFormState;
    live: LiveFormState;
    bankroll: number;
    onImport: (result: QuickImportResult) => void;
}

/**
 * Pasting one JSON object instead of filling eight fields. The panel starts closed so it does not
 * get in the way of the normal form, and opens sticky once used.
 */
export function QuickImportPanel({ odds, live, bankroll, onImport }: Props) {
    const t = useT();
    const [open, setOpen] = useState(false);
    const [text, setText] = useState('');
    const [error, setError] = useState<string | null>(null);
    const [copied, setCopied] = useState(false);

    const applyImport = () => {
        try {
            onImport(parseQuickImport(text));
            setError(null);
        } catch (e) {
            setError(e instanceof Error ? e.message : t.quickImport.readError);
        }
    };

    const loadExample = () => {
        setText(QUICK_IMPORT_EXAMPLE);
        setError(null);
    };

    const copyCurrent = async () => {
        const json = toQuickImportJson(odds, live, bankroll);
        setText(json);
        setError(null);
        try {
            await navigator.clipboard.writeText(json);
            setCopied(true);
            window.setTimeout(() => setCopied(false), 2000);
        } catch {
            // Clipboard access can be refused; the JSON is in the box either way, ready to select.
        }
    };

    if (!open) {
        return (
            <section className="card card-compact">
                <button type="button" className="link-button link-button-strong" onClick={() => setOpen(true)}>
                    {t.quickImport.open}
                </button>
                <p className="note note-tight">{t.quickImport.openNote}</p>
            </section>
        );
    }

    return (
        <section className="card">
            <div className="card-head">
                <h2>{t.quickImport.title}</h2>
                <button type="button" className="link-button" onClick={() => setOpen(false)}>
                    {t.quickImport.hide}
                </button>
            </div>

            <p className="note">{t.quickImport.note}</p>

            <textarea
                className="json-input"
                rows={11}
                spellCheck={false}
                placeholder={QUICK_IMPORT_EXAMPLE}
                value={text}
                onChange={(e) => setText(e.target.value)}
            />

            {error && <p className="error error-tight">{error}</p>}

            <div className="card-actions card-actions-tight">
                <button type="button" className="primary" onClick={applyImport}>
                    {t.quickImport.fill}
                </button>
                <button type="button" onClick={loadExample}>
                    {t.quickImport.loadExample}
                </button>
                <button type="button" onClick={copyCurrent}>
                    {copied ? t.quickImport.copied : t.quickImport.exportCurrent}
                </button>
            </div>
        </section>
    );
}
