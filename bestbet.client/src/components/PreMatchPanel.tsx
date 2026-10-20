import { useEffect, useState } from 'react';
import { analysisClient, ApiError } from '../api/analysisClient';
import { useLocalStorage } from '../hooks/useLocalStorage';
import { odds as formatOdds, percent, percentagePoints } from '../lib/format';
import { useT } from '../lib/i18n';
import { assessScorer, splitMatchLabel } from '../lib/preMatch';
import type {
    PlayerScoringChance,
    PreMatchRates,
    PreMatchResponse,
    PreMatchStatus,
    PreMatchTeam,
} from '../types/domain';

interface Props {
    /** Seeds the two team boxes, because the match is usually already typed into the form. */
    matchLabel: string;
    /** The rates the analysis is currently using, or null when it is not using the model. */
    active: PreMatchRates | null;
    /**
     * Hands the rates to the analysis; null stops using them. `teams` names the match they were
     * worked out for, as the data source spells it, for when the form has no match typed in.
     */
    onApply: (rates: PreMatchRates | null, teams: string) => void;
}

/**
 * A probability for a match that has not started, from how the two teams have scored and conceded
 * this season and from who is actually going to play.
 *
 * The panel does not decide anything. It produces two numbers — expected goals for each side — and
 * the analysis below prices whichever market is on the form from them, so the verdict, the edge test
 * and the stake are the ones the rest of the app already uses. The scorer table is the one place it
 * judges a price itself, because the form has no anytime-scorer market; it is deliberately strict.
 */
export function PreMatchPanel({ matchLabel, active, onApply }: Props) {
    const t = useT();

    const [open, setOpen] = useState(false);
    const [status, setStatus] = useState<PreMatchStatus | null>(null);
    const [leagueId, setLeagueId] = useLocalStorage('bestbet.prematch.league', 39);
    const [home, setHome] = useState('');
    const [away, setAway] = useState('');
    const [homeAbsent, setHomeAbsent] = useState<number[]>([]);
    const [awayAbsent, setAwayAbsent] = useState<number[]>([]);
    const [result, setResult] = useState<PreMatchResponse | null>(null);
    const [running, setRunning] = useState(false);
    const [error, setError] = useState<string | null>(null);
    // Prices typed per player, by id. Never sent anywhere: they only feed the on-screen verdict.
    const [prices, setPrices] = useState<Record<number, string>>({});

    useEffect(() => {
        if (!open || status) return undefined;

        let cancelled = false;
        void (async () => {
            try {
                const next = await analysisClient.preMatchStatus();
                if (!cancelled) setStatus(next);
            } catch {
                // Leaves the panel without a status; the first run reports the real problem.
            }
        })();

        return () => {
            cancelled = true;
        };
    }, [open, status]);

    // Fills the team boxes from the match label, once, and only while they are still empty.
    useEffect(() => {
        if (!open || home !== '' || away !== '') return;
        const teams = splitMatchLabel(matchLabel);
        if (teams) {
            // Seeding a form from another form is a one-off synchronisation, not derived state.
            // eslint-disable-next-line react/set-state-in-effect
            setHome(teams.home);
            setAway(teams.away);
        }
    }, [open, matchLabel, home, away]);

    const run = async (nextHomeAbsent: number[], nextAwayAbsent: number[]) => {
        setRunning(true);
        setError(null);
        try {
            const next = await analysisClient.preMatch({
                leagueId,
                homeTeam: home,
                awayTeam: away,
                homeAbsent: nextHomeAbsent,
                awayAbsent: nextAwayAbsent,
            });
            setResult(next);
            // A model already in use follows the numbers on screen, so toggling a player never
            // leaves the analysis pricing something the table no longer shows.
            if (active) onApply(next.rates, `${next.home.name} - ${next.away.name}`);
        } catch (failure) {
            setResult(null);
            setError(
                failure instanceof ApiError || failure instanceof Error
                    ? failure.message
                    : t.prematch.failed,
            );
        } finally {
            setRunning(false);
        }
    };

    const calculate = () => {
        setHomeAbsent([]);
        setAwayAbsent([]);
        setPrices({});
        void run([], []);
    };

    const toggle = (side: 'home' | 'away', id: number) => {
        const current = side === 'home' ? homeAbsent : awayAbsent;
        const next = current.includes(id) ? current.filter((x) => x !== id) : [...current, id];
        if (side === 'home') {
            setHomeAbsent(next);
            void run(next, awayAbsent);
        } else {
            setAwayAbsent(next);
            void run(homeAbsent, next);
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
                    {t.prematch.open}
                </button>
                <p className="note note-tight">
                    {active
                        ? t.prematch.applied
                        : t.prematch.openNote}
                </p>
            </section>
        );
    }

    const lacksKey = status !== null && !status.configured;
    const canRun = !running && !lacksKey && home.trim() !== '' && away.trim() !== '';

    return (
        <section className="card">
            <div className="card-head">
                <h2>{t.prematch.title}</h2>
                <button type="button" className="link-button" onClick={() => setOpen(false)}>
                    {t.prematch.hide}
                </button>
            </div>

            <p className="note">{t.prematch.intro}</p>
            {lacksKey && <p className="warning">{t.prematch.notConfigured}</p>}

            <div className="field-row">
                <label className="field">
                    <span>{t.prematch.league}</span>
                    <select value={leagueId} onChange={(e) => setLeagueId(Number(e.target.value))}>
                        {(status?.leagues ?? [{ id: leagueId, name: String(leagueId) }]).map((league) => (
                            <option key={league.id} value={league.id}>
                                {league.name}
                            </option>
                        ))}
                    </select>
                </label>
            </div>
            <div className="field-row">
                <label className="field">
                    <span>{t.prematch.home}</span>
                    <input
                        type="text"
                        placeholder={t.prematch.homePlaceholder}
                        value={home}
                        onChange={(e) => setHome(e.target.value)}
                    />
                </label>
                <label className="field">
                    <span>{t.prematch.away}</span>
                    <input
                        type="text"
                        placeholder={t.prematch.awayPlaceholder}
                        value={away}
                        onChange={(e) => setAway(e.target.value)}
                    />
                </label>
                <button type="button" className="search-button" onClick={calculate} disabled={!canRun}>
                    {running ? t.prematch.running : t.prematch.run}
                </button>
            </div>

            <p className="hint">{t.prematch.privacy}</p>
            {error && <p className="error error-tight">{error}</p>}

            {result && (
                <>
                    <h3 className="prematch-heading">
                        {t.prematch.season(result.season, result.leagueName)}
                    </h3>

                    <Summary result={result} />

                    {result.warnings.map((warning) => (
                        <p key={warning} className="warning">
                            {warning}
                        </p>
                    ))}

                    <TeamBlock
                        team={result.home}
                        absent={homeAbsent}
                        busy={running}
                        onToggle={(id) => toggle('home', id)}
                        prices={prices}
                        onPrice={(id, value) => setPrices((current) => ({ ...current, [id]: value }))}
                    />
                    <TeamBlock
                        team={result.away}
                        absent={awayAbsent}
                        busy={running}
                        onToggle={(id) => toggle('away', id)}
                        prices={prices}
                        onPrice={(id, value) => setPrices((current) => ({ ...current, [id]: value }))}
                    />

                    <p className="hint">{t.prematch.scorerNote}</p>

                    <div className="reference-active">
                        <div>
                            <strong>{active ? t.prematch.applied : t.prematch.apply}</strong>
                            <div className="cell-sub">{t.prematch.applyNote}</div>
                        </div>
                        {active ? (
                            <button type="button" className="link-button" onClick={() => onApply(null, '')}>
                                {t.prematch.remove}
                            </button>
                        ) : (
                            <button type="button" onClick={() => onApply(result.rates, `${result.home.name} - ${result.away.name}`)}>
                                {t.prematch.apply}
                            </button>
                        )}
                    </div>

                    <p className="hint">{t.prematch.limits}</p>
                </>
            )}
        </section>
    );
}

function Summary({ result }: { result: PreMatchResponse }) {
    const t = useT();
    const { probabilities: p, home, away } = result;
    const line = (value: number) => value.toFixed(2);
    const over25 = p.totals.find((row) => row.line === 2.5);

    return (
        <dl className="metrics metrics-compact">
            <div className="metric">
                <dt>{t.prematch.expectedGoals}</dt>
                <dd>
                    {line(home.expectedGoals)} – {line(away.expectedGoals)}
                    <span className="metric-sub">
                        {home.name} – {away.name}
                    </span>
                </dd>
            </div>
            <div className="metric">
                <dt>
                    {t.prematch.homeWin} / {t.prematch.draw} / {t.prematch.awayWin}
                </dt>
                <dd>
                    {percent(p.homeWin, 0)} / {percent(p.draw, 0)} / {percent(p.awayWin, 0)}
                </dd>
            </div>
            <div className="metric">
                <dt>{t.prematch.btts}</dt>
                <dd>{percent(p.bothTeamsToScore, 0)}</dd>
            </div>
            {over25 && (
                <div className="metric">
                    <dt>{t.prematch.over('2.5')}</dt>
                    <dd>{percent(over25.over, 0)}</dd>
                </div>
            )}
        </dl>
    );
}

interface TeamBlockProps {
    team: PreMatchTeam;
    absent: number[];
    busy: boolean;
    onToggle: (id: number) => void;
    prices: Record<number, string>;
    onPrice: (id: number, value: string) => void;
}

function TeamBlock({ team, absent, busy, onToggle, prices, onPrice }: TeamBlockProps) {
    const t = useT();
    const f = team.form;
    const avg = (value: number) => value.toFixed(2);

    return (
        <div className="prematch-team">
            <h3 className="prematch-heading">{team.name}</h3>
            <p className="cell-sub">
                {t.prematch.expectedGoals}: <strong>{avg(team.expectedGoals)}</strong>
                {team.missingShare > 0 && (
                    <>
                        {' · '}
                        {t.prematch.beforeAbsences(avg(team.baseExpectedGoals))}
                        {' · '}
                        {t.prematch.missing(percent(team.missingShare, 0))}
                    </>
                )}
            </p>
            <p className="cell-sub">
                {t.prematch.form}: {t.prematch.formHome}{' '}
                {t.prematch.formLine(avg(f.goalsForHome), avg(f.goalsAgainstHome), f.homePlayed)}
                {' · '}
                {t.prematch.formAway}{' '}
                {t.prematch.formLine(avg(f.goalsForAway), avg(f.goalsAgainstAway), f.awayPlayed)}
            </p>

            {team.players.length > 0 && (
                <>
                    <p className="hint">{t.prematch.absentHint}</p>
                    <div className="table-scroll">
                        <table>
                            <thead>
                                <tr>
                                    <th>{t.prematch.absent}</th>
                                    <th>{t.prematch.player}</th>
                                    <th>{t.prematch.goals}</th>
                                    <th>{t.prematch.shots}</th>
                                    <th>{t.prematch.chance}</th>
                                    <th>{t.prematch.fairOdds}</th>
                                    <th>{t.prematch.yourOdds}</th>
                                    <th>{t.prematch.verdict}</th>
                                </tr>
                            </thead>
                            <tbody>
                                {team.players.map((player) => (
                                    <PlayerRow
                                        key={player.id}
                                        player={player}
                                        absent={absent.includes(player.id)}
                                        busy={busy}
                                        onToggle={() => onToggle(player.id)}
                                        price={prices[player.id] ?? ''}
                                        onPrice={(value) => onPrice(player.id, value)}
                                    />
                                ))}
                            </tbody>
                        </table>
                    </div>
                </>
            )}
        </div>
    );
}

interface PlayerRowProps {
    player: PlayerScoringChance;
    absent: boolean;
    busy: boolean;
    onToggle: () => void;
    price: string;
    onPrice: (value: string) => void;
}

function PlayerRow({ player, absent, busy, onToggle, price, onPrice }: PlayerRowProps) {
    const t = useT();
    const assessment = absent ? null : assessScorer(player.scoringProbability, Number(price));
    const label = {
        worth: t.prematch.worth,
        marginal: t.prematch.borderline,
        'not-worth': t.prematch.notWorth,
    };

    return (
        <tr className={absent ? 'row-muted' : undefined}>
            <td>
                <input type="checkbox" checked={absent} disabled={busy} onChange={onToggle} />
            </td>
            <td>
                {player.name}
                <div className="cell-sub">{player.position ?? ''}</div>
            </td>
            <td>{player.goals}</td>
            <td>{player.shots}</td>
            <td>{absent ? '—' : percent(player.scoringProbability, 0)}</td>
            <td>{absent || player.fairOdds <= 0 ? '—' : formatOdds(player.fairOdds)}</td>
            <td>
                <input
                    className="input-narrow"
                    type="number"
                    step="0.01"
                    min="1"
                    inputMode="decimal"
                    disabled={absent}
                    value={price}
                    onChange={(e) => onPrice(e.target.value)}
                />
            </td>
            <td>
                {assessment && (
                    <>
                        <span className={`pill pill-${assessment.call}`}>{label[assessment.call]}</span>
                        <div className="cell-sub">{t.prematch.edgeOf(percentagePoints(assessment.edgePp))}</div>
                    </>
                )}
            </td>
        </tr>
    );
}
