import { useEffect, useMemo, useRef, useState } from 'react';
import type { DragEvent } from 'react';
import { analysisClient, ApiError } from '../api/analysisClient';
import { buildImportRequest, parseBetSlip, slipFormProblem, slipToForm } from '../lib/betSlip';
import type { SlipFormState } from '../lib/betSlip';
import { marketKindConfig } from '../lib/forms';
import { useT } from '../lib/i18n';
import { imageFrom, readOddsFromImage, releaseOcr } from '../lib/ocr';
import { MARKET_KINDS } from '../types/domain';
import type { MarketKind, MarketSelection } from '../types/domain';

interface Props {
    /** Called once a bet has been saved, so the log and the open-bets list pick it up. */
    onSaved: () => void;
}

/**
 * Bring a bet that is already placed into the app, from its slip.
 *
 * Reuses the same in-browser reader as the odds screenshot panel, because the bookmaker's app lets
 * nothing be copied from the slip either. What comes back is a transcript the user can edit and a
 * form they must look at: a stake or a price read wrongly is saved as a bet nobody placed, and every
 * number tracked from then on inherits the mistake.
 *
 * Deliberately no paste-an-image shortcut: the odds screenshot panel already listens for Ctrl+V on
 * the whole page, and one pasted capture must not be read twice.
 */
export function BetSlipImportPanel({ onSaved }: Props) {
    const t = useT();

    const [open, setOpen] = useState(false);
    const [text, setText] = useState('');
    const [form, setForm] = useState<SlipFormState | null>(null);
    const [issues, setIssues] = useState<string[]>([]);

    const [reading, setReading] = useState(false);
    const [progress, setProgress] = useState(0);
    const [loadingEngine, setLoadingEngine] = useState(false);
    const [dragging, setDragging] = useState(false);
    const [preview, setPreview] = useState<string | null>(null);

    const [saving, setSaving] = useState(false);
    const [saved, setSaved] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const fileInput = useRef<HTMLInputElement>(null);

    // The engine is a few tens of megabytes of WebAssembly; let it go when the panel is.
    useEffect(
        () => () => {
            void releaseOcr();
        },
        [],
    );

    const problem = useMemo(() => (form ? slipFormProblem(form) : null), [form]);

    const interpret = (transcript: string) => {
        const slip = parseBetSlip(transcript);
        setForm(slipToForm(slip));
        setIssues(slip.issues);
        setSaved(false);
        setError(null);
    };

    const read = async (image: Blob) => {
        setError(null);
        setSaved(false);
        setReading(true);
        setProgress(0);
        setLoadingEngine(true);

        setPreview((previous) => {
            if (previous) URL.revokeObjectURL(previous);
            return URL.createObjectURL(image);
        });

        try {
            const transcript = await readOddsFromImage(image, {
                onProgress: (update) => {
                    setProgress(update.progress);
                    setLoadingEngine(update.loading);
                },
            });
            setText(transcript.text);
            interpret(transcript.text);
        } catch (failure) {
            setError(failure instanceof Error ? `${t.slip.failed} ${failure.message}` : t.slip.failed);
        } finally {
            setReading(false);
            setLoadingEngine(false);
        }
    };

    const drop = (event: DragEvent<HTMLDivElement>) => {
        event.preventDefault();
        setDragging(false);

        const image = imageFrom(event.dataTransfer);
        if (!image) {
            setError(t.slip.notAnImage);
            return;
        }
        void read(image);
    };

    const clear = () => {
        setText('');
        setForm(null);
        setIssues([]);
        setError(null);
        setSaved(false);
        setPreview((previous) => {
            if (previous) URL.revokeObjectURL(previous);
            return null;
        });
    };

    const changeKind = (kind: MarketKind) => {
        if (!form) return;
        const config = marketKindConfig(kind);
        // The side list and the need for a line both depend on the market; a stale side would point
        // at a market it does not belong to.
        setForm({
            ...form,
            kind,
            selection: config.selections[0].value,
            line: config.needsLine ? (form.line || '2.5') : '',
        });
    };

    const save = async () => {
        if (!form || problem !== null) return;

        setSaving(true);
        setError(null);
        try {
            await analysisClient.importBet(buildImportRequest(form));
            setSaved(true);
            setForm(null);
            setText('');
            setIssues([]);
            onSaved();
        } catch (failure) {
            setError(
                failure instanceof ApiError || failure instanceof Error
                    ? `${t.slip.saveFailed} ${failure.message}`
                    : t.slip.saveFailed,
            );
        } finally {
            setSaving(false);
        }
    };

    if (!open) {
        return (
            <section className="card card-compact">
                <button
                    type="button"
                    className="link-button link-button-strong"
                    onClick={() => setOpen(true)}
                >
                    {t.slip.open}
                </button>
                <p className="note note-tight">{saved ? t.slip.saved : t.slip.openNote}</p>
            </section>
        );
    }

    const config = form ? marketKindConfig(form.kind) : null;
    const set = (patch: Partial<SlipFormState>) => form && setForm({ ...form, ...patch });

    return (
        <section className="card">
            <div className="card-head">
                <h2>{t.slip.title}</h2>
                <button type="button" className="link-button" onClick={() => setOpen(false)}>
                    {t.slip.hide}
                </button>
            </div>

            <p className="note">{t.slip.intro}</p>
            {saved && <p className="hint">{t.slip.saved}</p>}

            <div
                className={`dropzone ${dragging ? 'dropzone-active' : ''}`}
                onDragOver={(event) => {
                    event.preventDefault();
                    setDragging(true);
                }}
                onDragLeave={() => setDragging(false)}
                onDrop={drop}
            >
                {preview ? (
                    <img className="shot-preview" src={preview} alt="" />
                ) : (
                    <p className="dropzone-text">
                        {dragging ? (
                            t.slip.dropActive
                        ) : (
                            <>
                                {t.slip.dropHere}{' '}
                                <button
                                    type="button"
                                    className="link-button"
                                    onClick={() => fileInput.current?.click()}
                                >
                                    {t.slip.chooseFile}
                                </button>
                            </>
                        )}
                    </p>
                )}

                <input
                    ref={fileInput}
                    type="file"
                    accept="image/*"
                    className="hidden-input"
                    onChange={(event) => {
                        const file = event.target.files?.[0];
                        if (file) void read(file);
                        // Picking the same file twice in a row must still fire a change event.
                        event.target.value = '';
                    }}
                />
            </div>

            {reading && (
                <div className="shot-progress">
                    <progress value={progress} max={1} />
                    <span className="hint">
                        {loadingEngine ? t.slip.loadingEngine : t.slip.reading(Math.round(progress * 100))}
                    </span>
                </div>
            )}

            <label className="field">
                <span>{t.slip.textLabel}</span>
                <textarea
                    className="json-input"
                    rows={6}
                    spellCheck={false}
                    placeholder={t.slip.textPlaceholder}
                    value={text}
                    onChange={(event) => setText(event.target.value)}
                />
            </label>
            <p className="hint">{t.slip.textNote}</p>

            <div className="card-actions card-actions-tight">
                <button type="button" onClick={() => interpret(text)} disabled={reading || text.trim() === ''}>
                    {t.slip.read}
                </button>
                <button type="button" onClick={clear} disabled={reading || (text === '' && form === null)}>
                    {t.slip.clear}
                </button>
            </div>

            {error && <p className="error error-tight">{error}</p>}

            {form && config && (
                <div className="parse-preview">
                    <h3>{t.slip.formTitle}</h3>

                    {issues.length > 0 && (
                        <ul className="paste-notes">
                            {issues.map((code) => (
                                <li key={code}>{t.slip.issues[code] ?? code}</li>
                            ))}
                        </ul>
                    )}

                    <label className="field">
                        <span>{t.slip.match}</span>
                        <input
                            type="text"
                            placeholder={t.slip.matchPlaceholder}
                            value={form.matchLabel}
                            onChange={(e) => set({ matchLabel: e.target.value })}
                        />
                    </label>

                    <div className="field-row">
                        <label className="field">
                            <span>{t.slip.kind}</span>
                            <select value={form.kind} onChange={(e) => changeKind(e.target.value as MarketKind)}>
                                {MARKET_KINDS.map((m) => (
                                    <option key={m.kind} value={m.kind}>
                                        {t.marketKind[m.kind]}
                                    </option>
                                ))}
                            </select>
                        </label>

                        <label className="field">
                            <span>{t.slip.side}</span>
                            <select
                                value={form.selection}
                                onChange={(e) => set({ selection: e.target.value as MarketSelection })}
                            >
                                {config.selections.map((s) => (
                                    <option key={s.value} value={s.value}>
                                        {t.selection[s.value]}
                                    </option>
                                ))}
                            </select>
                        </label>

                        {config.needsLine && (
                            <label className="field field-narrow">
                                <span>{t.slip.line}</span>
                                <input
                                    type="number"
                                    step="0.25"
                                    value={form.line}
                                    onChange={(e) => set({ line: e.target.value })}
                                />
                            </label>
                        )}
                    </div>

                    <div className="field-row">
                        <label className="field">
                            <span>{t.slip.bookLabel}</span>
                            <input
                                type="text"
                                value={form.label}
                                onChange={(e) => set({ label: e.target.value })}
                            />
                        </label>
                        <label className="field field-narrow">
                            <span>{t.slip.odds}</span>
                            <input
                                type="number"
                                step="0.01"
                                min="1.01"
                                value={form.odds}
                                onChange={(e) => set({ odds: e.target.value })}
                            />
                        </label>
                        <label className="field field-narrow">
                            <span>{t.slip.stake}</span>
                            <input
                                type="number"
                                step="0.01"
                                min="0"
                                value={form.stake}
                                onChange={(e) => set({ stake: e.target.value })}
                            />
                        </label>
                    </div>

                    <fieldset className="subgroup">
                        <legend>{t.slip.stateTitle}</legend>
                        <div className="field-row">
                            <label className="field field-narrow">
                                <span>{t.slip.minute}</span>
                                <input
                                    type="number"
                                    min="1"
                                    max="130"
                                    value={form.minute}
                                    onChange={(e) => set({ minute: e.target.value })}
                                />
                            </label>
                            <label className="field field-narrow">
                                <span>{t.slip.scoreHome}</span>
                                <input
                                    type="number"
                                    min="0"
                                    value={form.goalsHome}
                                    onChange={(e) => set({ goalsHome: e.target.value })}
                                />
                            </label>
                            <label className="field field-narrow">
                                <span>{t.slip.scoreAway}</span>
                                <input
                                    type="number"
                                    min="0"
                                    value={form.goalsAway}
                                    onChange={(e) => set({ goalsAway: e.target.value })}
                                />
                            </label>
                            <label className="field field-narrow">
                                <span>{t.slip.xgHome}</span>
                                <input
                                    type="number"
                                    step="0.01"
                                    min="0"
                                    value={form.xgHome}
                                    onChange={(e) => set({ xgHome: e.target.value })}
                                />
                            </label>
                            <label className="field field-narrow">
                                <span>{t.slip.xgAway}</span>
                                <input
                                    type="number"
                                    step="0.01"
                                    min="0"
                                    value={form.xgAway}
                                    onChange={(e) => set({ xgAway: e.target.value })}
                                />
                            </label>
                        </div>
                        <p className="hint">{t.slip.stateNote}</p>
                    </fieldset>

                    {problem && <p className="warning">{t.slip.problems[problem]}</p>}

                    <div className="card-actions card-actions-tight">
                        <button
                            type="button"
                            className="primary"
                            onClick={() => void save()}
                            disabled={saving || problem !== null}
                        >
                            {saving ? t.slip.saving : t.slip.save}
                        </button>
                    </div>
                </div>
            )}
        </section>
    );
}
