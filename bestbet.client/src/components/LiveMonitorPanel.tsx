import { useCallback, useMemo, useState } from 'react';
import { bookmarkletFor, PUSH_INTERVAL_SECONDS, STALE_AFTER_SECONDS } from '../lib/liveFeed';
import { missingXg, parseMatchRef } from '../lib/providerFeed';
import { useT } from '../lib/i18n';
import type { ParsedStats } from '../lib/statsPaste';
import type { LiveFeedState, ProviderReading } from '../types/domain';

interface Props {
    /** Null until the first poll has answered. */
    feed: LiveFeedState | null;
    /** What the reading in force parsed to, or null when nothing usable has arrived. */
    parsed: ParsedStats | null;
    /** How old that reading is, in seconds, or null when there has not been one. */
    ageSeconds: number | null;
    /** Where it came from, so the panel can say which of the two routes is answering. */
    via: 'provider' | 'push' | null;
    watching: boolean;
    onWatchingChange: (next: boolean) => void;

    /** The link or id being followed, '' when none is. Persisted by the caller. */
    match: string;
    onMatchChange: (next: string) => void;
    /** The last fetched reading, or null when the link route is idle or failing. */
    reading: ProviderReading | null;
    /** True between pressing the button and the first answer. */
    resolving: boolean;

    /** Open positions this reading is allowed to re-price, by their own label. */
    tracked: string[];
    error: string | null;
    onClear: () => void;
}

/**
 * Keeps an open bet priced against a match that is still being played.
 *
 * Two routes in, and the order they appear in is the point. Pasting the match link is first because
 * it is what the user actually wanted: the server asks FotMob itself, so there is nothing to drag
 * and no tab to leave open. The bookmarklet is folded away underneath — not retired, because
 * FotMob has no xG for the smaller leagues and without xG the model has nothing to run on, which
 * leaves reading the page the user is looking at as the only way to watch those matches at all.
 *
 * Everything the panel shows is about whether to believe the number: how old the reading is, which
 * match it came from, which route answered, and which saved rows it is being applied to. A monitor
 * that silently goes stale is worse than no monitor, because the numbers keep looking live.
 */
export function LiveMonitorPanel({
    feed,
    parsed,
    ageSeconds,
    via,
    watching,
    onWatchingChange,
    match,
    onMatchChange,
    reading,
    resolving,
    tracked,
    error,
    onClear,
}: Props) {
    const t = useT();
    const [open, setOpen] = useState(false);
    const [copied, setCopied] = useState(false);
    const [fallbackOpen, setFallbackOpen] = useState(false);

    // What is in the box, which is only the same as `match` once the button has been pressed. Typing
    // must not start fetching: a half-pasted URL is a request for a match that does not exist.
    const [draft, setDraft] = useState(match);

    const draftRef = useMemo(() => parseMatchRef(draft), [draft]);
    const bookmarklet = useMemo(
        () => (feed ? bookmarkletFor(window.location.origin, feed.token) : null),
        [feed],
    );

    /**
     * React refuses to render a javascript: href, and a bookmarklet has to be one to be draggable.
     * A callback ref sets it the moment the link appears — which matters because the panel starts
     * collapsed and the fallback inside it starts collapsed too, so the link mounts long after the
     * token arrived.
     */
    const setAnchor = useCallback(
        (node: HTMLAnchorElement | null) => {
            if (node && bookmarklet) node.setAttribute('href', bookmarklet);
        },
        [bookmarklet],
    );

    const copy = async () => {
        if (!bookmarklet) return;
        try {
            await navigator.clipboard.writeText(bookmarklet);
            setCopied(true);
            window.setTimeout(() => setCopied(false), 2000);
        } catch {
            // Clipboard access can be refused; dragging the link still works.
        }
    };

    const start = () => {
        if (!draftRef) return;
        onMatchChange(draft.trim());
        onWatchingChange(true);
    };

    const stop = () => {
        onMatchChange('');
        setDraft('');
    };

    const stale = ageSeconds !== null && ageSeconds > STALE_AFTER_SECONDS;

    if (!open) {
        return (
            <section className="card card-compact">
                <button
                    type="button"
                    className="link-button link-button-strong"
                    onClick={() => setOpen(true)}
                >
                    {t.monitor.open}
                </button>
                <p className="note note-tight">
                    {watching && ageSeconds !== null
                        ? t.monitor.lastReading(Math.round(ageSeconds))
                        : t.monitor.openNote}
                </p>
            </section>
        );
    }

    return (
        <section className="card">
            <div className="card-head">
                <h2>{t.monitor.title}</h2>
                <button type="button" className="link-button" onClick={() => setOpen(false)}>
                    {t.monitor.hide}
                </button>
            </div>

            <p className="note">{t.monitor.why}</p>

            <div className="monitor-link">
                <label className="field">
                    <span>{t.monitor.linkLabel}</span>
                    <input
                        type="text"
                        inputMode="url"
                        spellCheck={false}
                        placeholder={t.monitor.linkPlaceholder}
                        value={draft}
                        onChange={(event) => setDraft(event.target.value)}
                        onKeyDown={(event) => {
                            if (event.key === 'Enter') {
                                event.preventDefault();
                                start();
                            }
                        }}
                    />
                </label>
                {match === '' ? (
                    <button type="button" onClick={start} disabled={!draftRef}>
                        {t.monitor.linkStart}
                    </button>
                ) : (
                    <button type="button" onClick={stop}>
                        {t.monitor.linkStop}
                    </button>
                )}
            </div>

            {/* Only once there is something to judge: an empty box is not a mistake. */}
            {draft.trim() !== '' && !draftRef && <p className="warning">{t.monitor.linkInvalid}</p>}

            <p className="hint">{t.monitor.linkHint}</p>

            {match !== '' && (
                <div className={`monitor-status ${stale && via === 'provider' ? 'monitor-stale' : ''}`}>
                    {resolving && !reading && <p className="hint">{t.monitor.linkWorking}</p>}

                    {reading && (
                        <>
                            <p className="monitor-reading">
                                {t.monitor.linkFound(
                                    reading.provider,
                                    reading.matchLabel ?? t.monitor.unknownMatch,
                                )}
                            </p>

                            {/* The one failure that is nobody's fault and has a specific answer:
                                the league has no xG at this provider, so this match needs the
                                bookmarklet or a paste. Said as a plain error it would read as a bug. */}
                            {missingXg(reading) ? (
                                <p className="warning">{t.monitor.linkNoXg}</p>
                            ) : (
                                reading.minute === null && (
                                    <p className="warning">{t.monitor.linkNotStarted}</p>
                                )
                            )}

                            {reading.finished && <p className="warning">{t.monitor.linkFinished}</p>}
                            {stale && via === 'provider' && <p className="warning">{t.monitor.linkStale}</p>}
                        </>
                    )}
                </div>
            )}

            <label className="toggle toggle-block">
                <input
                    type="checkbox"
                    checked={watching}
                    onChange={(event) => onWatchingChange(event.target.checked)}
                />
                <span>{t.monitor.watching}</span>
            </label>

            {error && <p className="error error-tight">{error}</p>}

            {watching && (
                <div className={`monitor-status ${stale ? 'monitor-stale' : ''}`}>
                    {ageSeconds === null ? (
                        <p className="hint">{t.monitor.waiting}</p>
                    ) : (
                        <>
                            <p className="hint">
                                {t.monitor.lastReading(Math.round(ageSeconds))}
                                {via === 'provider' && reading?.provider && ` · ${reading.provider}`}
                                {via === 'push' && feed?.source && ` · ${hostOf(feed.source)}`}
                            </p>

                            {parsed && (
                                <p className="monitor-reading">
                                    {parsed.matchLabel ?? t.monitor.unknownMatch}
                                    {' · '}
                                    {t.monitor.reading(
                                        parsed.minute,
                                        parsed.goalsHome,
                                        parsed.goalsAway,
                                        parsed.xgHome,
                                        parsed.xgAway,
                                    )}
                                </p>
                            )}

                            {/* The link route explains its own gaps above, in terms of the league it
                                found. This one is about a page that was read and came up short. */}
                            {via === 'push' && parsed && parsed.missing.length > 0 && (
                                <p className="warning">{t.monitor.unusable}</p>
                            )}

                            {stale && <p className="warning">{t.monitor.stale}</p>}
                        </>
                    )}

                    <p className="hint">
                        {tracked.length === 0
                            ? t.monitor.nothingTracked
                            : t.monitor.tracking(tracked.join(' · '))}
                    </p>
                </div>
            )}

            {/* Folded away, because it is the answer to a question most matches do not raise — and
                kept, because for a league without xG at the provider it is the only answer there is. */}
            {fallbackOpen ? (
                <div className="monitor-fallback">
                    <p className="note">{t.monitor.fallbackWhy}</p>

                    <ol className="monitor-steps">
                        <li>
                            {t.monitor.step1}
                            <div className="monitor-bookmarklet">
                                {/* eslint-disable-next-line jsx-a11y/anchor-is-valid */}
                                <a ref={setAnchor} className="bookmarklet-link" onClick={(e) => e.preventDefault()}>
                                    {t.monitor.bookmarkName}
                                </a>
                                <button type="button" onClick={() => void copy()} disabled={!bookmarklet}>
                                    {copied ? t.monitor.copied : t.monitor.copy}
                                </button>
                            </div>
                        </li>
                        <li>{t.monitor.step2}</li>
                        <li>{t.monitor.step3(PUSH_INTERVAL_SECONDS)}</li>
                    </ol>
                </div>
            ) : (
                <button
                    type="button"
                    className="link-button monitor-fallback-open"
                    onClick={() => setFallbackOpen(true)}
                >
                    {t.monitor.fallback}
                </button>
            )}

            <div className="card-actions card-actions-tight">
                <button type="button" onClick={onClear} disabled={!feed?.receivedUtc && !reading}>
                    {t.monitor.clear}
                </button>
                <span className="hint">{t.monitor.privacy}</span>
            </div>
        </section>
    );
}

/** The page the reading came from, shortened to the bit worth showing. */
function hostOf(url: string): string {
    try {
        return new URL(url).hostname.replace(/^www\./, '');
    } catch {
        return url.slice(0, 40);
    }
}
