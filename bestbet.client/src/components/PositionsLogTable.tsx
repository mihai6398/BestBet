import { useState } from 'react';
import type {
    Position,
    PositionOutcome,
    PositionsSummary,
    RepricedPosition,
} from '../types/domain';
import { dateTime, money, odds, percent, percentagePoints, signedPercent } from '../lib/format';
import { useLocale, useT } from '../lib/i18n';
import type { Dictionary } from '../lib/i18n';
import { VerdictBadge } from './VerdictBadge';

const OUTCOMES: PositionOutcome[] = ['Pending', 'Won', 'Lost', 'Void'];

/** A stake in an editable box, at the precision anybody actually places one. */
const toMoneyInput = (value: number) => String(Math.round(value * 100) / 100);

interface Props {
    positions: Position[];
    summary: PositionsSummary | null;
    onUpdateClosingOdds: (id: number, closingOdds: number | null) => void;
    onUpdateEntryOdds: (id: number, entryOdds: number) => void;
    onUpdateStake: (id: number, stake: number) => void;
    onUpdateOutcome: (id: number, outcome: PositionOutcome) => void;
    onDelete: (id: number) => void;
    /** Re-prices an open row against the match state in the form above. */
    onReprice: (id: number) => void;
    /** Why re-pricing cannot run right now, already worded. Null when it can. */
    repriceBlocked: string | null;
    repricing: number | null;
    repriced: Readonly<Record<number, RepricedPosition>>;
    onClearReprice: (id: number) => void;
}

export function PositionsLogTable({
    positions,
    summary,
    onUpdateClosingOdds,
    onUpdateEntryOdds,
    onUpdateStake,
    onUpdateOutcome,
    onDelete,
    onReprice,
    repriceBlocked,
    repricing,
    repriced,
    onClearReprice,
}: Props) {
    const t = useT();
    const locale = useLocale();

    return (
        <section className="card">
            <h2>{t.log.title}</h2>

            {summary && (
                <div className="summary">
                    <p className="summary-readout">{summary.readout}</p>
                    <dl className="metrics metrics-compact">
                        <div className="metric">
                            <dt>{t.log.savedCount}</dt>
                            <dd>{summary.totalPositions}</dd>
                        </div>
                        <div className="metric">
                            <dt>{t.log.pricedAtClose}</dt>
                            <dd>
                                {summary.positionsWithClosingPrice}
                                <span className="metric-sub">{t.log.of(summary.totalPositions)}</span>
                            </dd>
                        </div>
                        <div className="metric">
                            <dt>{t.log.beatClose}</dt>
                            <dd>
                                {summary.beatCloseRate === null ? '—' : percent(summary.beatCloseRate)}
                                <span className="metric-sub">
                                    {t.log.rows(summary.positionsBeatingClose)}
                                </span>
                            </dd>
                        </div>
                        <div className="metric">
                            <dt>{t.log.averageClv}</dt>
                            <dd>
                                {summary.averageClvPercent === null
                                    ? '—'
                                    : signedPercent(summary.averageClvPercent, 2)}
                            </dd>
                        </div>
                        <div className="metric">
                            <dt>{t.log.settledPl}</dt>
                            <dd>
                                {money(summary.totalProfitLoss, locale)}
                                <span className="metric-sub">
                                    {t.log.settled(summary.settledPositions)}
                                </span>
                            </dd>
                        </div>
                    </dl>
                </div>
            )}

            {positions.length === 0 ? (
                <p className="note">{t.log.empty}</p>
            ) : (
                <div className="table-scroll">
                    <table>
                        <thead>
                            <tr>
                                <th>{t.log.columns.saved}</th>
                                <th>{t.log.columns.match}</th>
                                <th>{t.log.columns.market}</th>
                                <th className="numeric">{t.log.columns.entry}</th>
                                <th className="numeric">{t.log.columns.fair}</th>
                                <th className="numeric">{t.log.columns.yours}</th>
                                <th className="numeric">{t.log.columns.edge}</th>
                                <th className="numeric">{t.log.columns.stake}</th>
                                <th>{t.log.columns.verdict}</th>
                                <th className="numeric">{t.log.columns.close}</th>
                                <th className="numeric">{t.log.columns.clv}</th>
                                <th>{t.log.columns.outcome}</th>
                                <th className="numeric">{t.log.columns.pl}</th>
                                <th />
                            </tr>
                        </thead>
                        <tbody>
                            {positions.map((position) => (
                                <PositionRow
                                    key={position.id}
                                    t={t}
                                    locale={locale}
                                    position={position}
                                    onUpdateClosingOdds={onUpdateClosingOdds}
                                    onUpdateEntryOdds={onUpdateEntryOdds}
                                    onUpdateStake={onUpdateStake}
                                    onUpdateOutcome={onUpdateOutcome}
                                    onDelete={onDelete}
                                    onReprice={onReprice}
                                    repriceBlocked={repriceBlocked}
                                    repricing={repricing === position.id}
                                    repriced={repriced[position.id] ?? null}
                                    onClearReprice={onClearReprice}
                                />
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {positions.length > 0 && <p className="hint">{t.log.editHint}</p>}
            {positions.length > 0 && repriceBlocked && <p className="hint">{repriceBlocked}</p>}
        </section>
    );
}

function PositionRow({
    t,
    locale,
    position,
    onUpdateClosingOdds,
    onUpdateEntryOdds,
    onUpdateStake,
    onUpdateOutcome,
    onDelete,
    onReprice,
    repriceBlocked,
    repricing,
    repriced,
    onClearReprice,
}: {
    t: Dictionary;
    locale: string;
    position: Position;
    onUpdateClosingOdds: (id: number, closingOdds: number | null) => void;
    onUpdateEntryOdds: (id: number, entryOdds: number) => void;
    onUpdateStake: (id: number, stake: number) => void;
    onUpdateOutcome: (id: number, outcome: PositionOutcome) => void;
    onDelete: (id: number) => void;
    onReprice: (id: number) => void;
    repriceBlocked: string | null;
    repricing: boolean;
    repriced: RepricedPosition | null;
    onClearReprice: (id: number) => void;
}) {
    const [closing, setClosing] = useState(
        position.closingDecimalOdds === null ? '' : String(position.closingDecimalOdds),
    );

    // Both of these are what was ACTUALLY bet, which is not always what the analysis suggested: a
    // live price moves between reading it and clicking it, and the stake gets rounded to whatever
    // the slip would take. Editing them in place keeps the log describing the bet that happened.
    const [entry, setEntry] = useState(String(position.entryDecimalOdds));
    const [stake, setStake] = useState(toMoneyInput(position.stakeAmount));

    // What the server last said this row holds. An edited price makes the server re-derive the fair
    // line and the edge, and the row comes back changed — so the boxes have to follow the stored
    // value rather than keep whatever was typed. Without this a rejected or adjusted value would
    // stay on screen looking saved, which is the one way an editable log can lie.
    const [stored, setStored] = useState({
        entry: position.entryDecimalOdds,
        stake: position.stakeAmount,
        closing: position.closingDecimalOdds,
    });

    if (
        stored.entry !== position.entryDecimalOdds ||
        stored.stake !== position.stakeAmount ||
        stored.closing !== position.closingDecimalOdds
    ) {
        setStored({
            entry: position.entryDecimalOdds,
            stake: position.stakeAmount,
            closing: position.closingDecimalOdds,
        });
        setEntry(String(position.entryDecimalOdds));
        setStake(toMoneyInput(position.stakeAmount));
        setClosing(position.closingDecimalOdds === null ? '' : String(position.closingDecimalOdds));
    }

    const commitEntry = () => {
        const parsed = Number(entry);
        if (!Number.isFinite(parsed) || parsed <= 1) {
            setEntry(String(position.entryDecimalOdds));
            return;
        }
        if (parsed !== position.entryDecimalOdds) onUpdateEntryOdds(position.id, parsed);
    };

    const commitStake = () => {
        const parsed = Number(stake);
        if (!Number.isFinite(parsed) || parsed < 0) {
            setStake(toMoneyInput(position.stakeAmount));
            return;
        }

        // Compared against the rounded value, because that is what the box was showing. A Kelly
        // stake of 4.4390921 displays as 4.44, and tabbing past it must not be taken for an edit.
        if (toMoneyInput(parsed) !== toMoneyInput(position.stakeAmount)) {
            onUpdateStake(position.id, parsed);
        }
    };

    const commitClosing = () => {
        const parsed = Number(closing);
        const next = closing.trim() === '' || !Number.isFinite(parsed) || parsed <= 1 ? null : parsed;
        if (next !== position.closingDecimalOdds) {
            onUpdateClosingOdds(position.id, next);
        }
    };

    const clvClass =
        position.clvPercent === null ? '' : position.clvPercent > 0 ? 'cell-good' : 'cell-bad';

    const row = (
        <tr>
            <td className="nowrap">{dateTime(position.createdUtc, locale)}</td>
            <td>{position.matchLabel || <span className="muted">—</span>}</td>
            <td>
                <div>{position.marketLabel || position.selection}</div>
                <div className="cell-sub">
                    {position.selection}
                    {position.wasLive && position.minutePlayed !== null && ` · ${position.minutePlayed}'`}
                </div>
            </td>
            <td className="numeric">
                <input
                    className="cell-input"
                    type="number"
                    step="0.01"
                    min="1.01"
                    value={entry}
                    onChange={(e) => setEntry(e.target.value)}
                    onBlur={commitEntry}
                    onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
                />
            </td>
            <td className="numeric">{percent(position.entryFairProbability)}</td>
            <td className="numeric">{percent(position.entryOwnProbability)}</td>
            <td className="numeric">{percentagePoints(position.entryEdgePercentagePoints)}</td>
            <td className="numeric">
                <input
                    className="cell-input"
                    type="number"
                    step="0.01"
                    min="0"
                    value={stake}
                    onChange={(e) => setStake(e.target.value)}
                    onBlur={commitStake}
                    onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
                />
            </td>
            <td>
                <VerdictBadge verdict={position.verdict} />
            </td>
            <td className="numeric">
                {/* The whole point of the log: this cell is filled in by hand once the market settles. */}
                <input
                    className="cell-input"
                    type="number"
                    step="0.01"
                    min="1.01"
                    placeholder="—"
                    value={closing}
                    onChange={(e) => setClosing(e.target.value)}
                    onBlur={commitClosing}
                    onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
                />
            </td>
            <td className={`numeric ${clvClass}`}>
                {position.clvPercent === null ? '—' : signedPercent(position.clvPercent, 2)}
            </td>
            <td>
                <select
                    className="cell-input"
                    value={position.outcome}
                    onChange={(e) => onUpdateOutcome(position.id, e.target.value as PositionOutcome)}
                >
                    {OUTCOMES.map((outcome) => (
                        <option key={outcome} value={outcome}>
                            {t.outcome[outcome]}
                        </option>
                    ))}
                </select>
            </td>
            <td className="numeric">
                {position.profitLoss === null ? '—' : money(position.profitLoss, locale)}
            </td>
            <td className="nowrap row-actions">
                {position.outcome === 'Pending' && (
                    <button
                        type="button"
                        className="link-button"
                        title={repriceBlocked ?? t.reprice.note}
                        onClick={() => onReprice(position.id)}
                        disabled={repricing || repriceBlocked !== null}
                    >
                        {t.reprice.action}
                    </button>
                )}
                <button type="button" className="link-button" onClick={() => onDelete(position.id)}>
                    {t.log.delete}
                </button>
            </td>
        </tr>
    );

    return repriced ? (
        <>
            {row}
            <tr className="reprice-row">
                <td colSpan={14}>
                    <RepricePanel t={t} locale={locale} repriced={repriced} onClose={onClearReprice} />
                </td>
            </tr>
        </>
    ) : (
        row
    );
}

/**
 * What an open bet is worth now.
 *
 * Nothing here is written back to the row. The entry snapshot is the evidence that the decision was
 * reasonable when the stake went down, and replacing it with a later reading would destroy the only
 * thing the log exists to prove — so this is a question asked of the row, not a change to it.
 */
function RepricePanel({
    t,
    locale,
    repriced,
    onClose,
}: {
    t: Dictionary;
    locale: string;
    repriced: RepricedPosition;
    onClose: (id: number) => void;
}) {
    return (
        <div className="reprice-panel">
            <div className="card-head">
                <h3>{t.reprice.title}</h3>
                <button type="button" className="link-button" onClick={() => onClose(repriced.id)}>
                    {t.reprice.close}
                </button>
            </div>

            <dl className="metrics metrics-compact">
                <div className="metric">
                    <dt>{t.reprice.probabilityNow}</dt>
                    <dd>{percent(repriced.ownProbability)}</dd>
                </div>
                <div className="metric">
                    <dt>{t.reprice.atEntry}</dt>
                    <dd>{percent(repriced.entryOwnProbability)}</dd>
                </div>
                <div
                    className={`metric ${repriced.probabilityPoints >= 0 ? 'metric-good' : 'metric-bad'}`}
                >
                    <dt>{t.reprice.change}</dt>
                    <dd>{percentagePoints(repriced.probabilityPoints)}</dd>
                </div>
                <div
                    className={`metric ${
                        repriced.expectedValuePerUnit >= 0 ? 'metric-good' : 'metric-bad'
                    }`}
                >
                    <dt>{t.reprice.evNow}</dt>
                    <dd>
                        {signedPercent(repriced.expectedValuePerUnit)}
                        <span className="metric-sub">
                            {t.reprice.evAmount}: {money(repriced.expectedValueAmount, locale)}
                        </span>
                    </dd>
                </div>
                <div className="metric">
                    <dt>{t.reprice.fairNow}</dt>
                    <dd>
                        {repriced.fairOddsNow === null ? '—' : odds(repriced.fairOddsNow)}
                        <span className="metric-sub">{t.reprice.fairNowHint}</span>
                    </dd>
                </div>
            </dl>

            <p className="hint">{t.reprice.note}</p>
        </div>
    );
}
