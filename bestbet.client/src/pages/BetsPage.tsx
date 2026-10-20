import { useCallback, useEffect, useState } from 'react';
import { bankrollClient } from '../api/accountClient';
import { analysisClient, ApiError } from '../api/analysisClient';
import { BankrollPanel } from '../components/BankrollPanel';
import { EditBetDialog, ManualBetDialog, SettleDialog } from '../components/BetDialogs';
import { ConfirmDialog } from '../components/Modal';
import { dateTime, money, odds as formatOdds, percent, percentagePoints, signedMoney } from '../lib/format';
import { useLocale, useT } from '../lib/i18n';
import type { BankrollSummary, Bet, BetStats } from '../types/domain';

type Tab = 'open' | 'closed';

type Dialog =
    | { kind: 'add' }
    | { kind: 'edit'; bet: Bet }
    | { kind: 'settle'; bet: Bet }
    | { kind: 'delete'; bet: Bet }
    | null;

/**
 * "My bets": the scoreboard, the bankroll, and every bet the user saved — open ones to settle,
 * settled ones to look back on. Everything here is the signed-in user's own; the server makes sure
 * of that on every request, so this page has nothing to filter.
 */
export function BetsPage() {
    const t = useT();
    const locale = useLocale();

    const [tab, setTab] = useState<Tab>('open');
    const [bets, setBets] = useState<Bet[]>([]);
    const [stats, setStats] = useState<BetStats | null>(null);
    const [bankroll, setBankroll] = useState<BankrollSummary | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [dialog, setDialog] = useState<Dialog>(null);
    const [busy, setBusy] = useState(false);

    // One counter drives every reload, as on the calculator page: a change anywhere bumps it, and a
    // single effect fetches the bets, the statistics and the balance together so they never disagree.
    const [version, setVersion] = useState(0);
    const reload = useCallback(() => setVersion((v) => v + 1), []);

    useEffect(() => {
        let cancelled = false;

        void (async () => {
            try {
                const [rows, totals, balance] = await Promise.all([
                    analysisClient.listBetsByStatus(tab),
                    analysisClient.betStats(),
                    bankrollClient.get(),
                ]);
                if (cancelled) return;
                setBets(rows);
                setStats(totals);
                setBankroll(balance);
                setError(null);
            } catch (e) {
                if (!cancelled) setError(e instanceof ApiError ? e.message : t.bets.loadError);
            }
        })();

        return () => {
            cancelled = true;
        };
    }, [tab, version, t]);

    /** Runs a change, closes the dialog and reloads. Errors are left to the dialog that asked. */
    const apply = async (change: () => Promise<unknown>) => {
        await change();
        setDialog(null);
        reload();
    };

    const remove = async (bet: Bet) => {
        setBusy(true);
        try {
            await analysisClient.deleteBet(bet.id);
            setDialog(null);
            reload();
        } catch (e) {
            setError(e instanceof ApiError ? e.message : t.bets.actionFailed);
            setDialog(null);
        } finally {
            setBusy(false);
        }
    };

    const reopen = async (bet: Bet) => {
        try {
            await analysisClient.updateBet(bet.id, { status: 'Open' });
            reload();
        } catch (e) {
            setError(e instanceof ApiError ? e.message : t.bets.actionFailed);
        }
    };

    const settled = stats?.settledBets ?? 0;
    const open = stats?.openBets ?? 0;

    return (
        <main className="page">
            <header className="page-head page-head-row">
                <h1>{t.bets.title}</h1>
                <button type="button" className="primary" onClick={() => setDialog({ kind: 'add' })}>
                    {t.bets.add}
                </button>
            </header>

            {error && (
                <p className="error" role="alert">
                    {error}
                </p>
            )}

            {stats && (
                <section className="card">
                    <h2>{t.bets.stats.title}</h2>
                    <dl className="metrics">
                        <Metric label={t.bets.stats.bets} value={String(stats.totalBets)} />
                        <Metric label={t.bets.stats.open} value={String(stats.openBets)} />
                        <Metric label={t.bets.stats.openStake} value={money(stats.openStake, locale)} />
                        <Metric label={t.bets.stats.staked} value={money(stats.totalStaked, locale)} />
                        <Metric
                            label={t.bets.stats.profit}
                            value={signedMoney(stats.totalProfitLoss, locale)}
                            tone={stats.totalProfitLoss >= 0 ? 'good' : 'bad'}
                        />
                        <Metric
                            label={t.bets.stats.yield}
                            hint={t.bets.stats.yieldHint}
                            value={stats.yield === null ? t.bets.stats.none : percent(stats.yield)}
                        />
                        <Metric
                            label={t.bets.stats.roi}
                            hint={t.bets.stats.roiHint}
                            value={stats.roi === null ? t.bets.stats.none : percent(stats.roi)}
                        />
                        <Metric
                            label={t.bets.stats.beatClose}
                            value={stats.beatCloseRate === null ? t.bets.stats.none : percent(stats.beatCloseRate)}
                        />
                    </dl>
                    <p className="note">{stats.readout}</p>
                </section>
            )}

            <BankrollPanel summary={bankroll} onChanged={reload} />

            <section className="card">
                <div className="tabs" role="tablist">
                    <button
                        type="button"
                        role="tab"
                        aria-selected={tab === 'open'}
                        className={tab === 'open' ? 'tab tab-active' : 'tab'}
                        onClick={() => setTab('open')}
                    >
                        {t.bets.open(open)}
                    </button>
                    <button
                        type="button"
                        role="tab"
                        aria-selected={tab === 'closed'}
                        className={tab === 'closed' ? 'tab tab-active' : 'tab'}
                        onClick={() => setTab('closed')}
                    >
                        {t.bets.closed(settled)}
                    </button>
                </div>

                {bets.length === 0 ? (
                    <p className="note">{tab === 'open' ? t.bets.emptyOpen : t.bets.emptyClosed}</p>
                ) : (
                    <div className="table-scroll">
                        <table>
                            <thead>
                                <tr>
                                    <th>{t.bets.columns.placed}</th>
                                    <th>{t.bets.columns.event}</th>
                                    <th>{t.bets.columns.market}</th>
                                    <th>{t.bets.columns.bookmaker}</th>
                                    <th>{t.bets.columns.odds}</th>
                                    <th>{t.bets.columns.stake}</th>
                                    <th>{t.bets.columns.probability}</th>
                                    <th>{t.bets.columns.edge}</th>
                                    {tab === 'closed' && (
                                        <>
                                            <th>{t.bets.columns.status}</th>
                                            <th>{t.bets.columns.payout}</th>
                                            <th>{t.bets.columns.profit}</th>
                                        </>
                                    )}
                                    <th>{t.bets.columns.actions}</th>
                                </tr>
                            </thead>
                            <tbody>
                                {bets.map((bet) => (
                                    <tr key={bet.id}>
                                        <td className="nowrap">{dateTime(bet.createdUtc, locale)}</td>
                                        <td>
                                            <div>{bet.matchLabel || '—'}</div>
                                            {(bet.competition || bet.eventStartUtc) && (
                                                <div className="hint">
                                                    {[bet.competition, bet.eventStartUtc ? dateTime(bet.eventStartUtc, locale) : null]
                                                        .filter(Boolean)
                                                        .join(' · ')}
                                                </div>
                                            )}
                                        </td>
                                        <td>{bet.marketLabel || bet.selection}</td>
                                        <td>{bet.bookmaker ?? '—'}</td>
                                        <td>{formatOdds(bet.entryDecimalOdds)}</td>
                                        <td>{money(bet.stakeAmount, locale)}</td>
                                        <td>{percent(bet.entryOwnProbability)}</td>
                                        <td>{percentagePoints(bet.entryEdgePercentagePoints)}</td>
                                        {tab === 'closed' && (
                                            <>
                                                <td>{t.outcome[bet.status]}</td>
                                                <td>{bet.payout === null ? '—' : money(bet.payout, locale)}</td>
                                                <td className={(bet.profitLoss ?? 0) >= 0 ? 'good' : 'bad'}>
                                                    {bet.profitLoss === null ? '—' : signedMoney(bet.profitLoss, locale)}
                                                </td>
                                            </>
                                        )}
                                        <td className="row-actions">
                                            {tab === 'open' ? (
                                                <button type="button" onClick={() => setDialog({ kind: 'settle', bet })}>
                                                    {t.bets.settle}
                                                </button>
                                            ) : (
                                                <button type="button" onClick={() => void reopen(bet)}>
                                                    {t.bets.reopen}
                                                </button>
                                            )}
                                            <button type="button" onClick={() => setDialog({ kind: 'edit', bet })}>
                                                {t.bets.edit}
                                            </button>
                                            <button type="button" onClick={() => setDialog({ kind: 'delete', bet })}>
                                                {t.bets.delete}
                                            </button>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </section>

            {dialog?.kind === 'add' && (
                <ManualBetDialog
                    bankroll={bankroll?.balance ?? 0}
                    onSave={(request) => apply(() => analysisClient.saveBet(request))}
                    onClose={() => setDialog(null)}
                />
            )}
            {dialog?.kind === 'edit' && (
                <EditBetDialog
                    bet={dialog.bet}
                    onSave={(update) => apply(() => analysisClient.updateBet(dialog.bet.id, update))}
                    onClose={() => setDialog(null)}
                />
            )}
            {dialog?.kind === 'settle' && (
                <SettleDialog
                    bet={dialog.bet}
                    onSettle={(request) => apply(() => analysisClient.settleBet(dialog.bet.id, request))}
                    onClose={() => setDialog(null)}
                />
            )}
            {dialog?.kind === 'delete' && (
                <ConfirmDialog
                    title={t.bets.delete}
                    message={t.bets.confirmDelete(dialog.bet.matchLabel || dialog.bet.selection)}
                    confirmLabel={t.bets.confirmDeleteButton}
                    danger
                    busy={busy}
                    onConfirm={() => void remove(dialog.bet)}
                    onCancel={() => setDialog(null)}
                />
            )}
        </main>
    );
}

function Metric({ label, value, hint, tone }: { label: string; value: string; hint?: string; tone?: 'good' | 'bad' }) {
    return (
        <div className={tone ? `metric metric-${tone}` : 'metric'} title={hint}>
            <dt>{label}</dt>
            <dd>{value}</dd>
        </div>
    );
}
