import { Link } from 'react-router';
import { money } from '../lib/format';
import { useLocale, useT } from '../lib/i18n';
import type { BankrollSummary } from '../types/domain';

interface Props {
    /** The amount stakes are sized against right now. */
    value: number;
    /** The account's balance, or null until it has loaded. */
    account: BankrollSummary | null;
    /** True when the box holds something other than the balance. */
    overridden: boolean;
    onChange: (next: number) => void;
    onUseAccount: () => void;
}

/**
 * The bankroll the stake is sized against. It is the account's balance unless the user types a
 * different number for one calculation; the real balance is only ever changed on the bets page.
 */
export function BankrollInput({ value, account, overridden, onChange, onUseAccount }: Props) {
    const t = useT();
    const locale = useLocale();

    return (
        <section className="card card-compact">
            <label className="field field-narrow">
                <span>{t.bankroll.label}</span>
                <input
                    type="number"
                    step="1"
                    min="0"
                    value={Number.isFinite(value) ? value : 0}
                    onChange={(e) => onChange(Number(e.target.value))}
                />
            </label>

            {account && !account.hasStartingBalance ? (
                <p className="warning">
                    <Link to="/bets#bankroll">{t.bankroll.noStart}</Link>
                </p>
            ) : overridden ? (
                <p className="note">
                    {t.bankroll.overridden}{' '}
                    <button type="button" className="link-button" onClick={onUseAccount}>
                        {t.bankroll.useAccount}
                        {account ? ` (${money(account.balance, locale)})` : ''}
                    </button>
                </p>
            ) : (
                <p className="note">
                    {account ? `${t.bankroll.account(money(account.balance, locale))}. ` : ''}
                    {t.bankroll.note}
                </p>
            )}
        </section>
    );
}
