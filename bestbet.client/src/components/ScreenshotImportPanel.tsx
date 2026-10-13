import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { DragEvent } from 'react';
import { candidateId, candidatesFrom } from '../lib/batch';
import type { BatchCandidate } from '../lib/batch';
import { odds as formatOdds, percent } from '../lib/format';
import { describeIssue, marketTitle, useT } from '../lib/i18n';
import { imageFrom, readOddsFromImage, releaseOcr } from '../lib/ocr';
import type { InvertMode } from '../lib/ocr';
import { normaliseOcrText } from '../lib/ocrText';
import { isPlausibleOverround, overroundOf, parseOddsPaste } from '../lib/oddsPaste';

interface Props {
    /** Every candidate currently ticked, sent off to be priced. */
    onAnalyseAll: (candidates: BatchCandidate[]) => void;
    /** One outcome, put into the form the way the paste panel does it. */
    onPickOne: (candidate: BatchCandidate) => void;
    /** Why a batch cannot run right now, already worded. Null when it can. */
    blockedReason: string | null;
    busy: boolean;
}

/**
 * Import a market from a screenshot.
 *
 * The bookmaker this is used against ships an app whose odds cannot be selected, so there is nothing
 * to copy and nothing to paste — the prices exist on screen and nowhere else. A capture is the only
 * way they come out, and reading it here beats reading it aloud to yourself while typing.
 *
 * The transcript is always shown and always editable. Recognition is good on a clean capture and
 * merely plausible on a blurry one, and the difference between those two cases is invisible from the
 * inside — so the user gets the text, not just the conclusion, and the margin on every market is
 * printed next to it as a second opinion on whether the pairing even makes sense.
 */
export function ScreenshotImportPanel({ onAnalyseAll, onPickOne, blockedReason, busy }: Props) {
    const t = useT();

    const [open, setOpen] = useState(true);
    const [text, setText] = useState('');
    const [preview, setPreview] = useState<string | null>(null);
    const [fileName, setFileName] = useState<string | null>(null);
    const [reading, setReading] = useState(false);
    const [progress, setProgress] = useState(0);
    const [loadingEngine, setLoadingEngine] = useState(false);
    const [confidence, setConfidence] = useState<number | null>(null);
    const [inverted, setInverted] = useState(false);
    const [invert, setInvert] = useState<InvertMode>('auto');
    const [error, setError] = useState<string | null>(null);
    const [dragging, setDragging] = useState(false);
    const [unticked, setUnticked] = useState<ReadonlySet<string>>(new Set());

    const fileInput = useRef<HTMLInputElement>(null);
    // Kept so "read again" can re-run the same capture with the inversion decided by hand.
    const lastImage = useRef<Blob | null>(null);

    // Normalising here rather than in the box: what the user typed stays exactly as they typed it,
    // and the parser still sees "1.27" when the engine handed back "l.27".
    const parsed = useMemo(() => parseOddsPaste(normaliseOcrText(text)), [text]);
    const candidates = useMemo(() => candidatesFrom(parsed.markets), [parsed.markets]);
    const ticked = useMemo(
        () => candidates.filter((candidate) => !unticked.has(candidate.id)),
        [candidates, unticked],
    );

    const read = useCallback(
        async (image: Blob, name: string | null, mode: InvertMode) => {
            lastImage.current = image;
            setFileName(name);
            setError(null);
            setReading(true);
            setProgress(0);
            setLoadingEngine(true);

            setPreview((previous) => {
                if (previous) URL.revokeObjectURL(previous);
                return URL.createObjectURL(image);
            });

            try {
                const transcript = await readOddsFromImage(image, {
                    invert: mode,
                    onProgress: (update) => {
                        setProgress(update.progress);
                        setLoadingEngine(update.loading);
                    },
                });

                setText(transcript.text);
                setConfidence(transcript.confidence);
                setInverted(transcript.inverted);
                setUnticked(new Set());
            } catch (failure) {
                setConfidence(null);
                setError(
                    failure instanceof Error ? `${t.shot.failed} ${failure.message}` : t.shot.failed,
                );
            } finally {
                setReading(false);
                setLoadingEngine(false);
            }
        },
        [t],
    );

    // Ctrl+V anywhere on the page while the panel is open. Pasting into a text box still goes to the
    // text box — the transcript is meant to be edited, and an image there would be nonsense anyway.
    useEffect(() => {
        if (!open) return undefined;

        const onPaste = (event: ClipboardEvent) => {
            const target = event.target as HTMLElement | null;
            if (target && (target.tagName === 'TEXTAREA' || target.tagName === 'INPUT')) return;

            const image = imageFrom(event.clipboardData);
            if (!image) return;

            event.preventDefault();
            void read(image, image.name, invert);
        };

        document.addEventListener('paste', onPaste);
        return () => document.removeEventListener('paste', onPaste);
    }, [open, invert, read]);

    // The WebAssembly instance is a few tens of megabytes; letting it go when the panel is taken off
    // screen matters on a laptop with a match, a stats page and a bookmaker already open.
    useEffect(
        () => () => {
            void releaseOcr();
        },
        [],
    );

    const drop = (event: DragEvent<HTMLDivElement>) => {
        event.preventDefault();
        setDragging(false);

        const image = imageFrom(event.dataTransfer);
        if (!image) {
            setError(t.shot.notAnImage);
            return;
        }

        void read(image, image.name, invert);
    };

    const chooseFile = () => fileInput.current?.click();

    const changeInvert = (mode: InvertMode) => {
        setInvert(mode);
        if (lastImage.current) void read(lastImage.current, fileName, mode);
    };

    const toggle = (id: string) =>
        setUnticked((current) => {
            const next = new Set(current);
            if (next.has(id)) next.delete(id);
            else next.add(id);
            return next;
        });

    const tickAll = (on: boolean) =>
        setUnticked(on ? new Set() : new Set(candidates.map((candidate) => candidate.id)));

    const clear = () => {
        setText('');
        setConfidence(null);
        setError(null);
        setFileName(null);
        setUnticked(new Set());
        lastImage.current = null;
        setPreview((previous) => {
            if (previous) URL.revokeObjectURL(previous);
            return null;
        });
    };

    if (!open) {
        return (
            <section className="card card-compact">
                <button
                    type="button"
                    className="link-button link-button-strong"
                    onClick={() => setOpen(true)}
                >
                    {t.shot.open}
                </button>
                <p className="note note-tight">{t.shot.openNote}</p>
            </section>
        );
    }

    return (
        <section className="card">
            <div className="card-head">
                <h2>{t.shot.title}</h2>
                <button type="button" className="link-button" onClick={() => setOpen(false)}>
                    {t.shot.hide}
                </button>
            </div>

            <p className="note">{t.shot.intro}</p>

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
                    <img className="shot-preview" src={preview} alt={fileName ?? ''} />
                ) : (
                    <p className="dropzone-text">
                        {dragging ? (
                            t.shot.dropActive
                        ) : (
                            <>
                                {t.shot.dropHere}{' '}
                                <button type="button" className="link-button" onClick={chooseFile}>
                                    {t.shot.chooseFile}
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
                        if (file) void read(file, file.name, invert);
                        // Picking the same file twice in a row must still fire a change event.
                        event.target.value = '';
                    }}
                />
            </div>

            <div className="shot-controls">
                <label className="field field-narrow">
                    <span>{t.shot.invert}</span>
                    <select
                        value={invert}
                        onChange={(event) => changeInvert(event.target.value as InvertMode)}
                        disabled={reading}
                    >
                        <option value="auto">{t.shot.invertAuto}</option>
                        <option value="always">{t.shot.invertAlways}</option>
                        <option value="never">{t.shot.invertNever}</option>
                    </select>
                </label>

                <div className="shot-buttons">
                    <button type="button" onClick={chooseFile} disabled={reading}>
                        {t.shot.chooseFile}
                    </button>
                    <button
                        type="button"
                        onClick={() => lastImage.current && void read(lastImage.current, fileName, invert)}
                        // Keyed off the preview rather than off the ref: the ref changing does not
                        // re-render, so a button disabled by it would stay disabled after a read.
                        disabled={reading || preview === null}
                    >
                        {t.shot.again}
                    </button>
                    <button type="button" onClick={clear} disabled={reading || (text === '' && !preview)}>
                        {t.shot.clear}
                    </button>
                </div>
            </div>

            <p className="hint">{t.shot.tips}</p>

            {reading && (
                <div className="shot-progress">
                    <progress value={progress} max={1} />
                    <span className="hint">
                        {loadingEngine
                            ? t.shot.loadingEngine
                            : t.shot.readingStatus(Math.round(progress * 100))}
                    </span>
                </div>
            )}

            {error && <p className="error error-tight">{error}</p>}

            {confidence !== null && (
                <p className="hint">
                    {t.shot.confidence(confidence)}
                    {inverted && ` · ${t.shot.invertedNote}`}
                </p>
            )}

            {confidence !== null && confidence < 70 && (
                <p className="warning">{t.shot.lowConfidence}</p>
            )}

            {(text !== '' || confidence !== null) && (
                <>
                    <label className="field">
                        <span>{t.shot.recognised}</span>
                        <textarea
                            className="json-input"
                            rows={8}
                            spellCheck={false}
                            value={text}
                            onChange={(event) => setText(event.target.value)}
                        />
                    </label>
                    <p className="hint">{t.shot.recognisedNote}</p>
                </>
            )}

            {text.trim() !== '' && parsed.markets.length === 0 && (
                <p className="error error-tight">{t.shot.noMarkets}</p>
            )}

            {parsed.markets.length > 0 && (
                <div className="parse-preview">
                    <div className="card-head">
                        <h3>{t.shot.marketsFound(parsed.markets.length)}</h3>
                        <span className="shot-buttons">
                            <button type="button" className="link-button" onClick={() => tickAll(true)}>
                                {t.shot.selectAll}
                            </button>
                            <button type="button" className="link-button" onClick={() => tickAll(false)}>
                                {t.shot.selectNone}
                            </button>
                        </span>
                    </div>

                    {parsed.markets.map((market) => {
                        const overround = overroundOf(market);
                        const plausible = isPlausibleOverround(overround);

                        return (
                            <div className="market-found" key={`${market.kind}:${market.line ?? '-'}`}>
                                <div className="market-found-title">
                                    {marketTitle(t, market.kind, market.line)}
                                    <span className={plausible ? 'muted' : 'margin-odd'}>
                                        {' · '}
                                        {plausible
                                            ? t.shot.margin(percent(overround - 1))
                                            : `${t.shot.margin(percent(overround - 1))} — ${t.shot.marginOdd}`}
                                    </span>
                                </div>

                                <div className="market-found-outcomes">
                                    {market.outcomes.map((outcome) => {
                                        const id = candidateId(market.kind, market.line, outcome.selection);
                                        const candidate = candidates.find((one) => one.id === id);

                                        return (
                                            <span className="outcome-pick" key={outcome.selection}>
                                                <label className="outcome-tick">
                                                    <input
                                                        type="checkbox"
                                                        checked={!unticked.has(id)}
                                                        onChange={() => toggle(id)}
                                                    />
                                                    <span className="outcome-label">{outcome.label}</span>
                                                    <span className="outcome-odds">
                                                        {formatOdds(outcome.odds)}
                                                    </span>
                                                </label>
                                                <button
                                                    type="button"
                                                    className="outcome-open"
                                                    title={t.batch.openOneHint}
                                                    onClick={() => candidate && onPickOne(candidate)}
                                                >
                                                    &rsaquo;
                                                </button>
                                            </span>
                                        );
                                    })}
                                </div>
                            </div>
                        );
                    })}
                </div>
            )}

            {parsed.issues.length > 0 && (
                <ul className="paste-notes">
                    {parsed.issues.map((issue, index) => (
                        <li key={index}>{describeIssue(t, issue)}</li>
                    ))}
                </ul>
            )}

            {blockedReason && candidates.length > 0 && <p className="warning">{blockedReason}</p>}

            {candidates.length > 0 && (
                <div className="card-actions card-actions-tight">
                    <button
                        type="button"
                        className="primary"
                        onClick={() => onAnalyseAll(ticked)}
                        disabled={busy || ticked.length === 0 || blockedReason !== null}
                    >
                        {t.shot.analyseSelected(ticked.length)}
                    </button>
                </div>
            )}
        </section>
    );
}
