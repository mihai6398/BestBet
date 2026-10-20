import { useState } from 'react';
import type { FormEvent } from 'react';
import { bankrollClient } from '../api/accountClient';
import { ApiError } from '../api/analysisClient';
import { parseNumber } from '../lib/bets';
import { dateTime, money, signedMoney } from '../lib/format';
import { useLocale, useT } from '../lib/i18n';
import type { BankrollSummary, BankrollTransaction, BankrollTransactionKind } from '../types/domain';
import { ConfirmDialog } from './Modal';

interface Props {
    summary: BankrollSummary | null;
    /** Called after any change, so the page can reload the balance and the statistics together. */
    onChanged: () => void;
}

/**
 * The user's money: the balance, where it came from, and the movements behind it.
 *
 * The balance is never typed in. It is the deposits less the withdrawals plus every settled bet's
 * profit, worked out by the server on each read, so it cannot drift away from the bets it describes.
 */
export function BankrollPanel({ summary, onChanged }: Props) {
    const t = useT();
    const locale = useLocale();
    const [kind, setKind] = useState<BankrollTransactionKind>('Deposit');
    const [amount, setAmount] = useState('');
    const [note, setNote] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [deleting, setDeleting] = useState<BankrollTransaction | null>(null);

    const firstDeposit = summary !== null && !summary.hasStartingBalance;

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        const value = parseNumber(amount);
        if (!(value > 0)) {
            setError(t.bankroll.amountError);
            return;
        }

        setBusy(true);
        setError(null);
        try {
            await bankrollClient.add({
                kind,
                amount: value,
                note: note.trim() || (firstDeposit && kind === 'Deposit' ? t.bankroll.startingNote : undefined),
            });
            setAmount('');
            setNote('');
            onChanged();
        } catch (failure) {
            setError(failure instanceof ApiError ? failure.message : t.common.failed);
        } finally {
            setBusy(false);
        }
    };

    const remove = async () => {
        if (!deleting) return;
        setBusy(true);
        try {
            await bankrollClient.remove(deleting.id);
            setDeleting(null);
            onChanged();
        } catch (failure) {
            setError(failure instanceof ApiError ? failure.message : t.common.failed);
            setDeleting(null);
        } finally {
            setBusy(false);
        }
    };

    return (
        <section className="card" id="bankroll">
            <h2>{t.bankroll.title}</h2>

            {summary && (
                <dl className="metrics">
                    <div className="metric" title={t.bankroll.balanceHint}>
                        <dt>{t.bankroll.balance}</dt>
                        <dd>{money(summary.balance, locale)}</dd>
                    </div>
                    <div className="metric" title={t.bankroll.availableHint}>
                        <dt>{t.bankroll.available}</dt>
                        <dd>{money(summary.available, locale)}</dd>
                    </div>
                    <div className="metric">
                        <dt>{t.bankroll.deposits}</dt>
                        <dd>{money(summary.deposits, locale)}</dd>
                    </div>
                    <div className="metric">
                        <dt>{t.bankroll.withdrawals}</dt>
                        <dd>{money(summary.withdrawals, locale)}</dd>
                    </div>
                    <div className={`metric ${summary.settledProfitLoss >= 0 ? 'metric-good' : 'metric-bad'}`}>
                        <dt>{t.bankroll.settled}</dt>
                        <dd>{signedMoney(summary.settledProfitLoss, locale)}</dd>
                    </div>
                </dl>
            )}

            {firstDeposit && <p className="warning">{t.bankroll.empty}</p>}

            <form className="inline-form" onSubmit={submit}>
                <label className="field">
                    <span>{t.bankroll.kind}</span>
                    <select value={kind} onChange={(e) => setKind(e.target.value as BankrollTransactionKind)}>
                        <option value="Deposit">{t.bankroll.kinds.Deposit}</option>
                        <option value="Withdrawal">{t.bankroll.kinds.Withdrawal}</option>
                    </select>
                </label>
                <label className="field">
                    <span>{t.bankroll.amount}</span>
                    <input inputMode="decimal" required value={amount} onChange={(e) => setAmount(e.target.value)} />
                </label>
                <label className="field field-grow">
                    <span>{t.bankroll.movementNote}</span>
                    <input
                        maxLength={200}
                        value={note}
                        placeholder={firstDeposit ? t.bankroll.startingNote : undefined}
                        onChange={(e) => setNote(e.target.value)}
                    />
                </label>
                <button type="submit" className="primary" disabled={busy}>
                    {busy ? t.bankroll.adding : t.bankroll.add}
                </button>
            </form>
            {error && (
                <p className="error" role="alert">
                    {error}
                </p>
            )}

            <details className="working">
                <summary>{t.bankroll.history}</summary>
                {summary && summary.transactions.length === 0 ? (
                    <p className="note">{t.bankroll.empty}</p>
                ) : (
                    <ul className="movement-list">
                        {summary?.transactions.map((movement) => (
                            <li key={movement.id}>
                                <span>{dateTime(movement.createdUtc, locale)}</span>
                                <span>{t.bankroll.kinds[movement.kind]}</span>
                                <strong className={movement.kind === 'Deposit' ? 'good' : 'bad'}>
                                    {movement.kind === 'Deposit' ? '+' : '−'}
                                    {money(movement.amount, locale)}
                                </strong>
                                <span className="hint">{movement.note}</span>
                                <button type="button" className="link-button" onClick={() => setDeleting(movement)}>
                                    {t.bets.delete}
                                </button>
                            </li>
                        ))}
                    </ul>
                )}
            </details>

            {deleting && (
                <ConfirmDialog
                    title={t.bets.delete}
                    message={t.bankroll.confirmDelete(
                        `${t.bankroll.kinds[deleting.kind]} ${money(deleting.amount, locale)}${deleting.note ? ` — ${deleting.note}` : ''}`,
                    )}
                    confirmLabel={t.bets.confirmDeleteButton}
                    danger
                    busy={busy}
                    onConfirm={() => void remove()}
                    onCancel={() => setDeleting(null)}
                />
            )}
        </section>
    );
}
