import { useEffect, useMemo, useState } from 'react';
import { analysisClient, ApiError } from '../api/analysisClient';
import { useLocalStorage } from '../hooks/useLocalStorage';
import type { LiveFormState } from '../lib/forms';
import { money, odds as formatOdds, percent, signedPercent } from '../lib/format';
import { useLocale, useT } from '../lib/i18n';
import {
    initialTrackForm,
    loseProbability,
    stateKey,
    trackFromLiveForm,
    trackLiveState,
    trend,
    withReading,
} from '../lib/tracking';
import type { TrackForm } from '../lib/tracking';
import type { Bet, BetStatus, RepricedBet } from '../types/domain';

interface Props {
    positions: Bet[];
    /** The match state in the main form, which a bet can borrow instead of retyping it. */
    live: LiveFormState;
    onSettle: (id: number, outcome: BetStatus) => void;
}

/** How long to wait after the last keystroke before asking the server, so typing "1.25" is one call. */
const SETTLE_MS = 450;

/** Past this the score has settled the bet, whatever the rest of the match does. */
const DECIDED = 0.9995;

/**
 * Open bets, each followed through the match with the latest data typed in for it.
 *
 * The question this answers is "what is my chance right now", asked again every time the minute, the
 * score or the xG changes — so the answer is recomputed as the numbers are typed instead of waiting
 * for a button. It reuses the server's re-pricing rather than a second implementation, which is the
 * only way the number here can never disagree with the one in the log.
 */
export function OpenBetsTracker({ positions, live, onSettle }: Props) {
    const t = useT();
    const open = positions.filter((position) => position.status === 'Open');

    return (
        <section className="card">
            <h2>{t.track.title}</h2>
            <p className="note">{t.track.intro}</p>

            {open.length === 0 ? (
                <p className="note">{t.track.empty}</p>
            ) : (
                open.map((position) => (
                    <OpenBet key={position.id} position={position} live={live} onSettle={onSettle} />
                ))
            )}
        </section>
    );
}

function OpenBet({
    position,
    live,
    onSettle,
}: {
    position: Bet;
    live: LiveFormState;
    onSettle: (id: number, outcome: BetStatus) => void;
}) {
    const t = useT();
    const locale = useLocale();

    // Per bet, and kept across reloads: a match runs for two hours and the tab will be closed in it.
    const [form, setForm] = useLocalStorage<TrackForm>(
        `bestbet.track.${position.id}`,
        initialTrackForm(position),
    );

    const [reading, setReading] = useState<RepricedBet | null>(null);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const state = useMemo(() => trackLiveState(form), [form]);
    const key = stateKey(state);
    const fromForm = trackFromLiveForm(live);

    useEffect(() => {
        if (state === null) return undefined;

        let cancelled = false;
        const timer = window.setTimeout(() => {
            setBusy(true);
            setError(null);

            analysisClient
                .reprice(position.id, { liveState: state })
                .then((now) => {
                    if (cancelled) return;
                    setReading(now);
                    setForm({
                        ...form,
                        history: withReading(form.history, {
                            minute: state.minutePlayed,
                            goalsHome: state.currentGoalsHome,
                            goalsAway: state.currentGoalsAway,
                            win: now.model.rawWinProbability,
                            push: now.model.pushProbability,
                        }),
                    });
                })
                .catch((failure: unknown) => {
                    if (cancelled) return;
                    setReading(null);
                    setError(
                        failure instanceof ApiError || failure instanceof Error
                            ? failure.message
                            : t.track.failed,
                    );
                })
                .finally(() => {
                    if (!cancelled) setBusy(false);
                });
        }, SETTLE_MS);

        return () => {
            cancelled = true;
            window.clearTimeout(timer);
        };
        // Re-run on the numbers, not on `form` as a whole: the history changing after a reading must
        // not start another reading, which would be an endless loop of identical requests.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [key, position.id]);

    const change = (patch: Partial<TrackForm>) => setForm({ ...form, ...patch });

    const win = reading?.model.rawWinProbability ?? null;
    const push = reading?.model.pushProbability ?? 0;
    const lose = win === null ? null : loseProbability(win, push);
    const line = position.selection.split(' ')[1];
    const entryUnknown = position.estimateMethod === 'slip-import';

    return (
        <article className="open-bet">
            <header className="open-bet-head">
                <div>
                    <strong>{position.matchLabel || '—'}</strong>
                    <div className="cell-sub">
                        {position.marketLabel || position.selection} · {t.track.odds}{' '}
                        {formatOdds(position.entryDecimalOdds)} · {t.track.stake}{' '}
                        {money(position.stakeAmount, locale)}
                    </div>
                </div>

                <div className="open-bet-settle">
                    <span className="hint">{t.track.settle}:</span>
                    {(['Won', 'Lost', 'Void'] as const).map((outcome) => (
                        <button
                            key={outcome}
                            type="button"
                            className="link-button"
                            onClick={() => onSettle(position.id, outcome)}
                        >
                            {t.outcome[outcome]}
                        </button>
                    ))}
                </div>
            </header>

            <div className="field-row">
                <label className="field field-narrow">
                    <span>{t.track.minute}</span>
                    <input
                        type="number"
                        min="1"
                        max="130"
                        value={form.minute}
                        onChange={(e) => change({ minute: e.target.value })}
                    />
                </label>
                <label className="field field-narrow">
                    <span>{t.track.goalsHome}</span>
                    <input
                        type="number"
                        min="0"
                        value={form.goalsHome}
                        onChange={(e) => change({ goalsHome: e.target.value })}
                    />
                </label>
                <label className="field field-narrow">
                    <span>{t.track.goalsAway}</span>
                    <input
                        type="number"
                        min="0"
                        value={form.goalsAway}
                        onChange={(e) => change({ goalsAway: e.target.value })}
                    />
                </label>
                <label className="field field-narrow">
                    <span>{t.track.xgHome}</span>
                    <input
                        type="number"
                        step="0.01"
                        min="0"
                        value={form.xgHome}
                        onChange={(e) => change({ xgHome: e.target.value })}
                    />
                </label>
                <label className="field field-narrow">
                    <span>{t.track.xgAway}</span>
                    <input
                        type="number"
                        step="0.01"
                        min="0"
                        value={form.xgAway}
                        onChange={(e) => change({ xgAway: e.target.value })}
                    />
                </label>
            </div>

            <div className="open-bet-actions">
                <button
                    type="button"
                    className="link-button"
                    disabled={fromForm === null}
                    title={fromForm === null ? t.track.fromFormNote : undefined}
                    onClick={() => fromForm && change(fromForm)}
                >
                    {t.track.fromForm}
                </button>
            </div>

            {state === null && <p className="hint">{t.track.needState}</p>}
            {busy && <p className="hint">{t.track.updating}</p>}
            {error && <p className="error error-tight">{`${t.track.failed} ${error}`}</p>}

            {state !== null && reading !== null && win !== null && lose !== null && (
                <>
                    <dl className="metrics metrics-compact">
                        <div className="metric metric-good">
                            <dt>{t.track.chanceWin}</dt>
                            <dd className="metric-hero">
                                {percent(win)}
                                {!entryUnknown && (
                                    <span className="metric-sub">
                                        {t.track.atEntry}: {percent(position.entryOwnProbability)}
                                    </span>
                                )}
                            </dd>
                        </div>
                        {push > 0 && (
                            <div className="metric">
                                <dt>{t.track.chancePush}</dt>
                                <dd>{percent(push)}</dd>
                            </div>
                        )}
                        <div className="metric metric-bad">
                            <dt>{t.track.chanceLose}</dt>
                            <dd>{percent(lose)}</dd>
                        </div>
                        <div
                            className={`metric ${
                                reading.expectedValuePerUnit >= 0 ? 'metric-good' : 'metric-bad'
                            }`}
                        >
                            <dt>{t.track.evNow}</dt>
                            <dd>
                                {signedPercent(reading.expectedValuePerUnit)}
                                <span className="metric-sub">
                                    {t.track.evAmount}: {money(reading.expectedValueAmount, locale)}
                                </span>
                            </dd>
                        </div>
                        <div className="metric">
                            <dt>{t.track.fairNow}</dt>
                            <dd>{reading.fairOddsNow === null ? '—' : formatOdds(reading.fairOddsNow)}</dd>
                        </div>
                        <div className="metric">
                            <dt>{t.track.ifWin}</dt>
                            <dd>
                                +{money(position.stakeAmount * (position.entryDecimalOdds - 1), locale)}
                                <span className="metric-sub">
                                    {t.track.ifLose}: −{money(position.stakeAmount, locale)}
                                </span>
                            </dd>
                        </div>
                    </dl>

                    {win >= DECIDED && <p className="notice notice-good">{t.track.decidedWon}</p>}
                    {lose >= DECIDED && <p className="notice notice-bad">{t.track.decidedLost}</p>}

                    {push > 0 && line !== undefined && <p className="hint">{t.track.pushNote(line)}</p>}
                </>
            )}

            {form.history.length > 0 && (
                <div className="open-bet-history">
                    <div className="card-head">
                        <h3>{t.track.history}</h3>
                        <button
                            type="button"
                            className="link-button"
                            onClick={() => change({ history: [] })}
                        >
                            {t.track.clearHistory}
                        </button>
                    </div>

                    <Trend values={trend(form.history)} />

                    <p className="hint">{t.track.historyNote(form.history.length)}</p>
                    <ul className="history-list">
                        {form.history.slice(0, 8).map((entry, index) => (
                            <li key={`${entry.minute}-${entry.goalsHome}-${entry.goalsAway}-${index}`}>
                                <span className="nowrap">
                                    {entry.minute}&apos; · {entry.goalsHome}-{entry.goalsAway}
                                </span>
                                <strong>{percent(entry.win)}</strong>
                                {entry.push > 0 && (
                                    <span className="muted">
                                        {' '}
                                        (+{percent(entry.push)} {t.track.chancePush.toLowerCase()})
                                    </span>
                                )}
                            </li>
                        ))}
                    </ul>
                </div>
            )}

            <p className="hint">{t.track.note}</p>
        </article>
    );
}

/** The win chance across the readings, oldest to newest. A trend line, so no axes: the list has numbers. */
function Trend({ values }: { values: number[] }) {
    if (values.length < 2) return null;

    const width = 240;
    const height = 44;
    const step = width / (values.length - 1);
    const points = values
        .map((value, index) => `${(index * step).toFixed(1)},${((1 - value) * (height - 4) + 2).toFixed(1)}`)
        .join(' ');

    return (
        <svg
            className="trend"
            viewBox={`0 0 ${width} ${height}`}
            role="img"
            aria-label={values.map((v) => percent(v, 0)).join(' → ')}
            preserveAspectRatio="none"
        >
            <polyline points={points} fill="none" stroke="currentColor" strokeWidth="2" />
        </svg>
    );
}
