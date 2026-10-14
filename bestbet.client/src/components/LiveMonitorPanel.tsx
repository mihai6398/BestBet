import { useCallback, useMemo, useState } from 'react';
import { bookmarkletFor, PUSH_INTERVAL_SECONDS, STALE_AFTER_SECONDS } from '../lib/liveFeed';
import { useT } from '../lib/i18n';
import type { ParsedStats } from '../lib/statsPaste';
import type { LiveFeedState } from '../types/domain';

interface Props {
    /** Null until the first poll has answered. */
    feed: LiveFeedState | null;
    /** What the last reading parsed to, or null when nothing usable has arrived. */
    parsed: ParsedStats | null;
    watching: boolean;
    onWatchingChange: (next: boolean) => void;
    /** Open positions this reading is allowed to re-price, by their own label. */
    tracked: string[];
    error: string | null;
    onClear: () => void;
}

/**
 * Keeps an open bet priced against a match that is still being played.
 *
 * The honest version of "fetch it automatically": the reading comes from the tab the user already
 * has the match open in, pushed by a bookmarklet, because Sofascore answers a programmatic request
 * with 403 and the only way around that is to pretend to be a browser. So we use a real one.
 *
 * Everything the panel shows is about whether to believe the number: how old the reading is, which
 * match it came from, and which saved rows it is being applied to. A monitor that silently goes
 * stale is worse than no monitor, because the numbers keep looking live.
 */
export function LiveMonitorPanel({
    feed,
    parsed,
    watching,
    onWatchingChange,
    tracked,
    error,
    onClear,
}: Props) {
    const t = useT();
    const [open, setOpen] = useState(false);
    const [copied, setCopied] = useState(false);

    const bookmarklet = useMemo(
        () => (feed ? bookmarkletFor(window.location.origin, feed.token) : null),
        [feed],
    );

    /**
     * React refuses to render a javascript: href, and a bookmarklet has to be one to be draggable.
     * A callback ref sets it the moment the link appears — which matters because the panel starts
     * collapsed, so the link mounts long after the token arrived.
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

    const age = feed?.ageSeconds ?? null;
    const stale = age !== null && age > STALE_AFTER_SECONDS;

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
                    {watching && age !== null
                        ? t.monitor.lastReading(Math.round(age))
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
                    {age === null ? (
                        <p className="hint">{t.monitor.waiting}</p>
                    ) : (
                        <>
                            <p className="hint">
                                {t.monitor.lastReading(Math.round(age))}
                                {feed?.source && ` · ${hostOf(feed.source)}`}
                            </p>

                            {parsed && (
                                <p className="monitor-reading">
                                    {parsed.matchLabel ?? t.monitor.unknownMatch}
                                    {' · '}
                                    {t.monitor.reading(
                                        parsed.minute ?? 0,
                                        parsed.goalsHome ?? 0,
                                        parsed.goalsAway ?? 0,
                                        parsed.xgHome ?? 0,
                                        parsed.xgAway ?? 0,
                                    )}
                                </p>
                            )}

                            {parsed && parsed.missing.length > 0 && (
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

            <div className="card-actions card-actions-tight">
                <button type="button" onClick={onClear} disabled={!feed?.receivedUtc}>
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
