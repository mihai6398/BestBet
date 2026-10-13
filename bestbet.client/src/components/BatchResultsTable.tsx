import type { BatchRow } from '../lib/batch';
import { money, odds as formatOdds, percent, percentagePoints, signedPercent } from '../lib/format';
import type { LiveFormState } from '../lib/forms';
import { marketTitleShort, useLocale, useT } from '../lib/i18n';
import { VerdictBadge } from './VerdictBadge';

interface Props {
    rows: BatchRow[];
    running: boolean;
    completed: number;
    total: number;
    matchLabel: string;
    live: LiveFormState;
    onOpen: (row: BatchRow) => void;
    onSave: (row: BatchRow) => void;
    savedIds: ReadonlySet<string>;
    savingId: string | null;
    onClear: () => void;
}

/**
 * Every outcome from one capture, priced and lined up.
 *
 * Sorted by edge rather than filtered by verdict, and the losers are left in. A table that only
 * showed the bets would quietly hide the thing worth knowing most of the time — that the book has
 * the whole ladder about right, and there is nothing here.
 */
export function BatchResultsTable({
    rows,
    running,
    completed,
    total,
    matchLabel,
    live,
    onOpen,
    onSave,
    savedIds,
    savingId,
    onClear,
}: Props) {
    const t = useT();
    const locale = useLocale();

    const priced = rows.filter((row) => row.result !== null);
    const bestEdge = priced.reduce(
        (best, row) => Math.max(best, row.result!.edge.edgePercentagePoints),
        Number.NEGATIVE_INFINITY,
    );
    const nothingPositive = priced.length > 0 && bestEdge <= 0;

    return (
        <section className="card card-result">
            <div className="card-head">
                <h2>{t.batch.title}</h2>
                <button type="button" className="link-button" onClick={onClear}>
                    {t.batch.clear}
                </button>
            </div>

            <p className="note note-tight">{t.batch.subtitle(rows.length, matchLabel)}</p>

            {running && (
                <div className="shot-progress">
                    <progress value={completed} max={total} />
                    <span className="hint">{t.batch.running(completed, total)}</span>
                </div>
            )}

            <div className="table-scroll">
                <table>
                    <thead>
                        <tr>
                            <th>{t.batch.columns.market}</th>
                            <th>{t.batch.columns.selection}</th>
                            <th className="numeric">{t.batch.columns.odds}</th>
                            <th className="numeric">{t.batch.columns.fair}</th>
                            <th className="numeric">{t.batch.columns.yours}</th>
                            <th className="numeric">{t.batch.columns.edge}</th>
                            <th className="numeric">{t.batch.columns.ev}</th>
                            <th className="numeric">{t.batch.columns.stake}</th>
                            <th>{t.batch.columns.verdict}</th>
                            <th />
                        </tr>
                    </thead>
                    <tbody>
                        {rows.map((row) => {
                            const { candidate, result } = row;
                            const isBest =
                                result !== null &&
                                bestEdge > 0 &&
                                result.edge.edgePercentagePoints === bestEdge;

                            return (
                                <tr key={candidate.id} className={isBest ? 'row-best' : undefined}>
                                    <td className="row-market">
                                        {marketTitleShort(t, candidate.kind, candidate.line)}
                                        {isBest && <span className="best-flag">{t.batch.best}</span>}
                                    </td>
                                    <td>
                                        <div>{candidate.label}</div>
                                        <div className="cell-sub">{t.selection[candidate.selection]}</div>
                                    </td>
                                    <td className="numeric">{formatOdds(candidate.odds)}</td>

                                    {result === null ? (
                                        <td className="numeric" colSpan={6}>
                                            <span className="cell-bad">
                                                {t.batch.failed}
                                                {row.error ? ` — ${row.error}` : ''}
                                            </span>
                                        </td>
                                    ) : (
                                        <>
                                            <td className="numeric">
                                                {percent(result.edge.fairMarketProbability)}
                                            </td>
                                            <td className="numeric">
                                                {percent(result.edge.ownProbability)}
                                            </td>
                                            <td
                                                className={`numeric ${
                                                    result.edge.edgePercentagePoints >= 0
                                                        ? 'cell-good'
                                                        : 'cell-bad'
                                                }`}
                                            >
                                                {percentagePoints(result.edge.edgePercentagePoints)}
                                            </td>
                                            <td
                                                className={`numeric ${
                                                    result.edge.expectedValuePerUnit >= 0
                                                        ? 'cell-good'
                                                        : 'cell-bad'
                                                }`}
                                            >
                                                {signedPercent(result.edge.expectedValuePerUnit)}
                                            </td>
                                            <td className="numeric">
                                                {money(result.stake.suggestedStakeAmount, locale)}
                                            </td>
                                            <td>
                                                <VerdictBadge verdict={result.verdict} />
                                            </td>
                                        </>
                                    )}

                                    <td className="nowrap row-actions">
                                        <button
                                            type="button"
                                            className="link-button"
                                            onClick={() => onOpen(row)}
                                            disabled={result === null}
                                        >
                                            {t.batch.openOne}
                                        </button>
                                        <button
                                            type="button"
                                            className="link-button"
                                            onClick={() => onSave(row)}
                                            disabled={
                                                result === null ||
                                                savedIds.has(candidate.id) ||
                                                savingId === candidate.id
                                            }
                                        >
                                            {savedIds.has(candidate.id) ? '✓' : t.batch.save}
                                        </button>
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
            </div>

            {nothingPositive && <p className="note">{t.batch.nothingPositive}</p>}

            <p className="hint">{t.batch.sortedBy}</p>

            {live.isLive && (
                <p className="hint">
                    {t.batch.disclaimerLive(
                        live.minutePlayed || '—',
                        live.cumulativeXgHome || '—',
                        live.cumulativeXgAway || '—',
                    )}
                </p>
            )}
        </section>
    );
}
