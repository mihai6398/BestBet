import { useMemo, useState } from 'react';
import type { ClipboardEvent } from 'react';
import type { LiveFormState } from '../lib/forms';
import { readPastedHtml } from '../lib/htmlPaste';
import { parseStatsPaste } from '../lib/statsPaste';
import { fieldLabel, statLabel, useT } from '../lib/i18n';
import type { Dictionary } from '../lib/i18n';

interface Props {
    live: LiveFormState;
    onApply: (patch: { live: LiveFormState; matchLabel: string | null }) => void;
}

/**
 * Import from a live-score site by pasting its stats panel.
 *
 * The parse is always shown before it is applied. A provider can reorder its rows or rename a stat
 * overnight, and the failure mode of a misread xG is a real stake on a number nobody checked — so
 * this asks for a glance rather than trusting itself.
 */
export function StatsPastePanel({ live, onApply }: Props) {
    const t = useT();
    const [open, setOpen] = useState(false);
    const [text, setText] = useState('');

    const parsed = useMemo(() => (text.trim() === '' ? null : parseStatsPaste(text)), [text]);
    const usable = parsed !== null && parsed.missing.length === 0;

    /**
     * A stats panel copied from a page keeps its row structure in the clipboard's HTML flavour. The
     * plain-text flavour sometimes flattens a whole grid onto one line, which is exactly the case the
     * parser has the least to work with.
     */
    const handlePaste = (event: ClipboardEvent<HTMLTextAreaElement>) => {
        const fromHtml = readPastedHtml(event.clipboardData);
        if (!fromHtml) return;

        event.preventDefault();
        setText(fromHtml);
    };

    const apply = () => {
        if (!parsed || !usable) return;

        onApply({
            matchLabel: parsed.matchLabel,
            live: {
                ...live,
                isLive: true,
                minutePlayed: String(parsed.minute),
                cumulativeXgHome: String(parsed.xgHome),
                cumulativeXgAway: String(parsed.xgAway),
                // An absent score means the header was not copied, not that it is nil-nil — so the
                // form keeps whatever is already in it rather than quietly resetting to zero.
                currentGoalsHome: parsed.goalsHome === null ? live.currentGoalsHome : String(parsed.goalsHome),
                currentGoalsAway: parsed.goalsAway === null ? live.currentGoalsAway : String(parsed.goalsAway),
            },
        });
        setText('');
    };

    if (!open) {
        return (
            <section className="card card-compact">
                <button type="button" className="link-button link-button-strong" onClick={() => setOpen(true)}>
                    {t.statsPaste.open}
                </button>
                <p className="note note-tight">{t.statsPaste.openNote}</p>
            </section>
        );
    }

    return (
        <section className="card">
            <div className="card-head">
                <h2>{t.statsPaste.title}</h2>
                <button type="button" className="link-button" onClick={() => setOpen(false)}>
                    {t.statsPaste.hide}
                </button>
            </div>

            <p className="note">{t.statsPaste.note}</p>
            <p className="warning">{t.statsPaste.tabTip}</p>

            <textarea
                className="json-input"
                rows={8}
                spellCheck={false}
                placeholder={'Barcelona 3 - 1 Racing Club\n35:57\nPosesia mingii\n68%\n32%\nGoluri estimate (xG)\n2.38\n0.30'}
                value={text}
                onPaste={handlePaste}
                onChange={(e) => setText(e.target.value)}
            />

            {parsed && (
                <div className="parse-preview">
                    <h3>{t.statsPaste.whatWasRead}</h3>
                    <dl className="parse-grid">
                        <ParsedField t={t} label={t.statsPaste.match} value={parsed.matchLabel} optional />
                        <ParsedField t={t} label={t.statsPaste.minute} value={parsed.minute} />
                        <ParsedField t={t} label={t.statsPaste.xgHome} value={parsed.xgHome} />
                        <ParsedField t={t} label={t.statsPaste.xgAway} value={parsed.xgAway} />
                        <ParsedField
                            t={t}
                            label={t.statsPaste.score}
                            value={score(parsed.goalsHome, parsed.goalsAway)}
                            optional
                        />
                    </dl>

                    {parsed.extras.length > 0 && (
                        <p className="hint">
                            {t.statsPaste.alsoRecognised}
                            {parsed.extras.map((row) => `${statLabel(t, row)} ${row.home}–${row.away}`).join(' · ')}
                        </p>
                    )}

                    {!usable && (
                        <p className="error error-tight">
                            {t.statsPaste.missing(parsed.missing.map((field) => fieldLabel(t, field)).join(', '))}
                        </p>
                    )}
                </div>
            )}

            <p className="hint">{t.statsPaste.windowTip}</p>

            <div className="card-actions card-actions-tight">
                <button type="button" className="primary" onClick={apply} disabled={!usable}>
                    {t.statsPaste.use}
                </button>
                <button type="button" onClick={() => setText('')} disabled={text === ''}>
                    {t.statsPaste.clear}
                </button>
            </div>
        </section>
    );
}

function ParsedField({
    t,
    label,
    value,
    optional = false,
}: {
    t: Dictionary;
    label: string;
    value: string | number | null;
    optional?: boolean;
}) {
    const missing = value === null;
    return (
        <div className={`parse-field ${missing && !optional ? 'parse-field-missing' : ''}`}>
            <dt>{label}</dt>
            <dd>
                {missing ? (
                    <span className="muted">
                        {optional ? t.statsPaste.notInPaste : t.statsPaste.notFound}
                    </span>
                ) : (
                    value
                )}
            </dd>
        </div>
    );
}

const score = (home: number | null, away: number | null) =>
    home === null || away === null ? null : `${home} – ${away}`;
