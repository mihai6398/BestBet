import { useEffect, useState } from 'react';
import { money, odds as formatOdds, percent, percentagePoints, signedPercent } from '../lib/format';
import { useLocale, useT } from '../lib/i18n';
import { secondsBetween } from '../lib/liveRefresh';
import type { BetRefresh } from '../lib/liveRefresh';
import type { WinReading } from '../lib/winChart';
import type { Bet, RepricedBet } from '../types/domain';
import { WinProbabilityChart } from './WinProbabilityChart';

interface Props {
    positions: Bet[];
    /** Every open bet's readings so far, by id. */
    histories: Readonly<Record<string, WinReading[]>>;
    /** The latest re-price of each, for the numbers the chart does not carry. */
    repriced: Readonly<Record<number, RepricedBet>>;
    /** When each was last re-priced and when it is due again, while the monitor is on. */
    refresh: Readonly<Record<number, BetRefresh>>;
    watching: boolean;
    onClearHistory: (id: number) => void;
}

/** How often the "updated 12 s ago" line moves. Often enough to look alive, rarely enough to cost nothing. */
const CLOCK_SECONDS = 5;

/**
 * Every open bet that is being followed through its match, each with its win chance drawn over time.
 *
 * Sits at the top of the results column because that is where the eye is while a match is on: the
 * log below has the same bets as rows, which is the right shape for a record and the wrong one for
 * watching — a column of numbers says where a bet is, a line says which way it is going.
 *
 * While the monitor is on, every bet here is re-priced once a minute against its own match, and
 * each card says when it last was, from which match, and when it will be again. The match is found
 * by name, so showing which one was found is not decoration: it is the check only the user can make.
 */
export function LiveBetsPanel({ positions, histories, repriced, refresh, watching, onClearHistory }: Props) {
    const t = useT();
    const [now, setNow] = useState(() => Date.now());

    useEffect(() => {
        if (!watching) return undefined;
        const timer = window.setInterval(() => setNow(Date.now()), CLOCK_SECONDS * 1000);
        return () => window.clearInterval(timer);
    }, [watching]);

    const watched = positions.filter(
        (position) =>
            position.status === 'Open' &&
            ((histories[String(position.id)]?.length ?? 0) > 0 || refresh[position.id] !== undefined),
    );

    if (watched.length === 0) return null;

    return (
        <section className="card">
            <h2>{t.chart.title}</h2>
            <p className="note">{t.chart.intro}</p>
            <p className={watching ? 'hint' : 'warning'}>{watching ? t.chart.auto : t.chart.paused}</p>

            {watched.map((position) => (
                <LiveBet
                    key={position.id}
                    position={position}
                    history={histories[String(position.id)] ?? []}
                    latest={repriced[position.id] ?? null}
                    refresh={watching ? refresh[position.id] ?? null : null}
                    now={now}
                    onClear={() => onClearHistory(position.id)}
                />
            ))}
        </section>
    );
}

function LiveBet({
    position,
    history,
    latest,
    refresh,
    now,
    onClear,
}: {
    position: Bet;
    history: WinReading[];
    latest: RepricedBet | null;
    refresh: BetRefresh | null;
    now: number;
    onClear: () => void;
}) {
    const t = useT();
    const locale = useLocale();
    const last = history[history.length - 1];
    // A bet imported from a slip has no probability of its own from the moment it was struck.
    const entryKnown = position.estimateMethod !== 'slip-import';

    return (
        <article className="live-bet">
            <header className="live-bet-head">
                <strong>{position.matchLabel || '—'}</strong>
                <span className="cell-sub">
                    {position.marketLabel || position.selection} · {t.track.odds} {formatOdds(position.entryDecimalOdds)} ·{' '}
                    {t.track.stake} {money(position.stakeAmount, locale)}
                </span>
            </header>

            {refresh && <RefreshLine refresh={refresh} now={now} />}

            {last && (
                <>
                    <dl className="metrics metrics-compact">
                        <div className="metric">
                            <dt>{t.reprice.probabilityNow}</dt>
                            <dd>
                                {percent(last.win)}
                                {entryKnown && (
                                    <span className="metric-sub">
                                        {t.reprice.atEntry}: {percent(position.entryOwnProbability)}
                                    </span>
                                )}
                            </dd>
                        </div>
                        {latest && entryKnown && (
                            <div className={`metric ${latest.probabilityPoints >= 0 ? 'metric-good' : 'metric-bad'}`}>
                                <dt>{t.reprice.change}</dt>
                                <dd>{percentagePoints(latest.probabilityPoints)}</dd>
                            </div>
                        )}
                        {latest && (
                            <div className={`metric ${latest.expectedValuePerUnit >= 0 ? 'metric-good' : 'metric-bad'}`}>
                                <dt>{t.reprice.evNow}</dt>
                                <dd>
                                    {signedPercent(latest.expectedValuePerUnit)}
                                    <span className="metric-sub">
                                        {t.reprice.evAmount}: {money(latest.expectedValueAmount, locale)}
                                    </span>
                                </dd>
                            </div>
                        )}
                        {latest && (
                            <div className="metric">
                                <dt>{t.reprice.fairNow}</dt>
                                <dd>
                                    {latest.fairOddsNow === null ? '—' : formatOdds(latest.fairOddsNow)}
                                    <span className="metric-sub">{t.chart.left(Math.round(last.remainingMinutes))}</span>
                                </dd>
                            </div>
                        )}
                    </dl>

                    <WinProbabilityChart
                        history={history}
                        entryOdds={position.entryDecimalOdds}
                        entryMinute={position.wasLive ? position.minutePlayed : null}
                        entryProbability={entryKnown ? position.entryOwnProbability : null}
                    />

                    <div className="card-actions card-actions-tight">
                        <button type="button" className="link-button" onClick={onClear}>
                            {t.chart.clear}
                        </button>
                    </div>
                </>
            )}
        </article>
    );
}

/**
 * When the bet was last re-priced, from which match, and when it will be again — or why it cannot be.
 * The source line links to the match, because a match found by name is one the user should be able
 * to check in a click.
 */
function RefreshLine({ refresh, now }: { refresh: BetRefresh; now: number }) {
    const t = useT();
    const source = refresh.source;
    // Before the first price there is nothing to date — and once the server has said why it cannot
    // price the bet, "looking for the match" would contradict the line under it.
    const when =
        refresh.lastAt !== null
            ? t.chart.updated(secondsBetween(refresh.lastAt, now))
            : refresh.unavailable === null && refresh.error === null
              ? t.chart.notYet
              : null;

    return (
        <div className="live-bet-refresh">
            <p className="hint">
                {when}
                {source && (
                    <>
                        {when ? ` · ${t.chart.from(source.provider)}` : t.chart.foundAt(source.provider)}{' '}
                        {source.url ? (
                            <a href={source.url} target="_blank" rel="noreferrer noopener">
                                {source.matchLabel ?? source.url}
                            </a>
                        ) : (
                            source.matchLabel
                        )}
                    </>
                )}
                {refresh.nextAt !== null &&
                    refresh.unavailable === null &&
                    refresh.error === null &&
                    ` · ${t.chart.next(secondsBetween(now, refresh.nextAt))}`}
            </p>

            {refresh.unavailable && <p className="warning">{t.chart.unavailable[refresh.unavailable]}</p>}
            {refresh.error && <p className="error error-tight">{t.chart.failed(refresh.error)}</p>}
        </div>
    );
}
