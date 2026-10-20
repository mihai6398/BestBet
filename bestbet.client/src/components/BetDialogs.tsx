import { useState } from 'react';
import type { FormEvent } from 'react';
import { ApiError } from '../api/analysisClient';
import {
    draftWithDetails,
    editFormFor,
    editRequest,
    emptyDetails,
    emptyManualBet,
    manualBetRequest,
    parseNumber,
    settlementPreview,
} from '../lib/bets';
import type { BetDetailsForm, EditBetForm, ManualBetForm, Parsed } from '../lib/bets';
import { money, odds as formatOdds, percent, percentagePoints, signedMoney } from '../lib/format';
import { useLocale, useT } from '../lib/i18n';
import { MARKET_KINDS, SETTLED_STATUSES } from '../types/domain';
import type { Bet, BetStatus, SaveBetRequest, SettleBetRequest, UpdateBetRequest } from '../types/domain';
import { Modal } from './Modal';

/** Runs a save, keeping the form open with the server's own message when it refuses. */
function useSubmit() {
    const t = useT();
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const run = async <T,>(parsed: Parsed<T>, save: (value: T) => Promise<void>) => {
        if (!parsed.ok) {
            setError(t.bets.errors[parsed.error]);
            return;
        }

        setBusy(true);
        setError(null);
        try {
            await save(parsed.value);
        } catch (failure) {
            setError(failure instanceof ApiError ? failure.message : t.bets.actionFailed);
        } finally {
            setBusy(false);
        }
    };

    return { busy, error, run };
}

function FormFooter({ busy, error, onClose, label }: { busy: boolean; error: string | null; onClose: () => void; label?: string }) {
    const t = useT();
    return (
        <>
            {error && (
                <p className="error" role="alert">
                    {error}
                </p>
            )}
            <div className="modal-actions">
                <button type="button" onClick={onClose} disabled={busy}>
                    {t.common.cancel}
                </button>
                <button type="submit" className="primary" disabled={busy}>
                    {busy ? t.bets.form.saving : (label ?? t.bets.form.save)}
                </button>
            </div>
        </>
    );
}

/** Sport, competition, bookmaker, kick-off and notes: what every bet form asks the same way. */
function BetDetailsFields<T extends BetDetailsForm>({ value, onChange }: { value: T; onChange: (next: T) => void }) {
    const t = useT();
    const set = (field: keyof BetDetailsForm) => (e: { target: { value: string } }) =>
        onChange({ ...value, [field]: e.target.value });

    return (
        <>
            <div className="form-grid">
                <label className="field">
                    <span>{t.bets.form.sport}</span>
                    <input value={value.sport} maxLength={64} onChange={set('sport')} />
                </label>
                <label className="field">
                    <span>{t.bets.form.competition}</span>
                    <input value={value.competition} maxLength={128} onChange={set('competition')} />
                </label>
                <label className="field">
                    <span>{t.bets.form.bookmaker}</span>
                    <input value={value.bookmaker} maxLength={64} onChange={set('bookmaker')} />
                </label>
                <label className="field">
                    <span>{t.bets.form.kickoff}</span>
                    <input type="datetime-local" value={value.eventStart} onChange={set('eventStart')} />
                </label>
            </div>
            <label className="field">
                <span>{t.bets.form.notes}</span>
                <textarea rows={2} maxLength={2000} value={value.notes} onChange={set('notes')} />
            </label>
        </>
    );
}

/**
 * "Save as a bet" from the calculator: the analysis already knows the price, the probability and the
 * edge, so those are shown rather than asked for. What it cannot know — the stake actually placed,
 * where, and the match's details — is what the form asks.
 */
export function SaveBetDialog({
    draft,
    onSave,
    onClose,
}: {
    draft: SaveBetRequest;
    onSave: (request: SaveBetRequest) => Promise<void>;
    onClose: () => void;
}) {
    const t = useT();
    const [stake, setStake] = useState(String(draft.stakeAmount));
    const [details, setDetails] = useState<BetDetailsForm>({ ...emptyDetails(), sport: 'Fotbal' });
    const { busy, error, run } = useSubmit();

    const submit = (e: FormEvent) => {
        e.preventDefault();
        void run(draftWithDetails(draft, stake, details), onSave);
    };

    return (
        <Modal title={t.bets.form.titleSave} onClose={onClose} wide>
            <form onSubmit={submit} className="modal-form">
                <p className="note">
                    <strong>{draft.matchLabel || '—'}</strong>{' '}
                    {t.bets.form.fromAnalysis(
                        draft.marketLabel || draft.selection,
                        formatOdds(draft.entryDecimalOdds),
                        percent(draft.entryOwnProbability),
                        percentagePoints(draft.entryEdgePercentagePoints),
                    )}
                </p>
                <label className="field field-narrow">
                    <span>{t.bets.form.stake}</span>
                    <input inputMode="decimal" required value={stake} onChange={(e) => setStake(e.target.value)} />
                </label>
                <BetDetailsFields value={details} onChange={setDetails} />
                <FormFooter busy={busy} error={error} onClose={onClose} />
            </form>
        </Modal>
    );
}

/** A bet typed in by hand, for one placed without an analysis behind it. */
export function ManualBetDialog({
    bankroll,
    onSave,
    onClose,
}: {
    bankroll: number;
    onSave: (request: SaveBetRequest) => Promise<void>;
    onClose: () => void;
}) {
    const t = useT();
    const [form, setForm] = useState<ManualBetForm>(emptyManualBet);
    const { busy, error, run } = useSubmit();
    const set = (field: keyof ManualBetForm) => (e: { target: { value: string } }) =>
        setForm({ ...form, [field]: e.target.value });

    const submit = (e: FormEvent) => {
        e.preventDefault();
        void run(manualBetRequest(form, bankroll), onSave);
    };

    return (
        <Modal title={t.bets.form.titleAdd} onClose={onClose} wide>
            <form onSubmit={submit} className="modal-form">
                <label className="field">
                    <span>{t.bets.form.match}</span>
                    <input
                        required
                        maxLength={200}
                        placeholder={t.bets.form.matchPlaceholder}
                        value={form.matchLabel}
                        onChange={set('matchLabel')}
                    />
                </label>
                <div className="form-grid">
                    <label className="field">
                        <span>{t.bets.form.marketKind}</span>
                        <select value={form.marketKind} onChange={set('marketKind')}>
                            {MARKET_KINDS.map((m) => (
                                <option key={m.kind} value={m.kind}>
                                    {t.marketKind[m.kind]}
                                </option>
                            ))}
                        </select>
                    </label>
                    <label className="field">
                        <span>{t.bets.form.selection}</span>
                        <input
                            required
                            maxLength={64}
                            placeholder={t.bets.form.selectionPlaceholder}
                            value={form.selection}
                            onChange={set('selection')}
                        />
                    </label>
                    <label className="field">
                        <span>{t.bets.form.marketLabel}</span>
                        <input maxLength={200} value={form.marketLabel} onChange={set('marketLabel')} />
                    </label>
                    <label className="field">
                        <span>{t.bets.form.odds}</span>
                        <input inputMode="decimal" required value={form.odds} onChange={set('odds')} />
                    </label>
                    <label className="field">
                        <span>{t.bets.form.stake}</span>
                        <input inputMode="decimal" required value={form.stake} onChange={set('stake')} />
                    </label>
                    <label className="field">
                        <span>{t.bets.form.probability}</span>
                        <input inputMode="decimal" value={form.probability} onChange={set('probability')} />
                    </label>
                </div>
                <label className="field">
                    <span>{t.bets.form.otherOdds}</span>
                    <input inputMode="decimal" value={form.otherOdds} onChange={set('otherOdds')} />
                </label>
                <p className="hint">
                    {t.bets.form.otherOddsHint} {t.bets.form.probabilityHint}
                </p>
                <BetDetailsFields value={form} onChange={setForm} />
                <FormFooter busy={busy} error={error} onClose={onClose} />
            </form>
        </Modal>
    );
}

/** Corrects a saved bet. Only the fields that changed are sent. */
export function EditBetDialog({
    bet,
    onSave,
    onClose,
}: {
    bet: Bet;
    onSave: (update: UpdateBetRequest) => Promise<void>;
    onClose: () => void;
}) {
    const t = useT();
    const [form, setForm] = useState<EditBetForm>(() => editFormFor(bet));
    const { busy, error, run } = useSubmit();
    const set = (field: keyof EditBetForm) => (e: { target: { value: string } }) =>
        setForm({ ...form, [field]: e.target.value });

    const submit = (e: FormEvent) => {
        e.preventDefault();
        void run(editRequest(bet, form), onSave);
    };

    return (
        <Modal title={t.bets.form.titleEdit} onClose={onClose} wide>
            <form onSubmit={submit} className="modal-form">
                <label className="field">
                    <span>{t.bets.form.match}</span>
                    <input required maxLength={200} value={form.matchLabel} onChange={set('matchLabel')} />
                </label>
                <div className="form-grid">
                    <label className="field">
                        <span>{t.bets.form.odds}</span>
                        <input inputMode="decimal" required value={form.odds} onChange={set('odds')} />
                    </label>
                    <label className="field">
                        <span>{t.bets.form.stake}</span>
                        <input inputMode="decimal" required value={form.stake} onChange={set('stake')} />
                    </label>
                    <label className="field">
                        <span>{t.bets.form.closingOdds}</span>
                        <input inputMode="decimal" value={form.closingOdds} onChange={set('closingOdds')} />
                    </label>
                </div>
                <BetDetailsFields value={form} onChange={setForm} />
                <FormFooter busy={busy} error={error} onClose={onClose} />
            </form>
        </Modal>
    );
}

/** Records how a bet ended, with what it pays shown before it is confirmed. */
export function SettleDialog({
    bet,
    onSettle,
    onClose,
}: {
    bet: Bet;
    onSettle: (request: SettleBetRequest) => Promise<void>;
    onClose: () => void;
}) {
    const t = useT();
    const locale = useLocale();
    const [status, setStatus] = useState<BetStatus>('Won');
    const [cashOut, setCashOut] = useState('');
    const [closing, setClosing] = useState(bet.closingDecimalOdds === null ? '' : String(bet.closingDecimalOdds));
    const { busy, error, run } = useSubmit();

    const cashOutAmount = cashOut.trim() === '' ? null : parseNumber(cashOut);
    const preview = settlementPreview(status, bet.stakeAmount, bet.entryDecimalOdds, cashOutAmount);

    const submit = (e: FormEvent) => {
        e.preventDefault();
        const closingOdds = closing.trim() === '' ? null : parseNumber(closing);
        if (closingOdds !== null && !(closingOdds > 1)) {
            void run({ ok: false, error: 'closingOdds' }, async () => {});
            return;
        }

        void run(
            { ok: true, value: { status, payout: status === 'CashedOut' ? cashOutAmount : null, closingDecimalOdds: closingOdds } },
            onSettle,
        );
    };

    return (
        <Modal title={t.bets.settleTitle(bet.matchLabel || bet.selection)} onClose={onClose}>
            <form onSubmit={submit} className="modal-form">
                <p className="note">
                    {bet.marketLabel || bet.selection} · {formatOdds(bet.entryDecimalOdds)} · {money(bet.stakeAmount, locale)}
                </p>
                <label className="field">
                    <span>{t.bets.result}</span>
                    <select value={status} onChange={(e) => setStatus(e.target.value as BetStatus)}>
                        {SETTLED_STATUSES.map((s) => (
                            <option key={s} value={s}>
                                {t.outcome[s]}
                            </option>
                        ))}
                    </select>
                </label>
                {status === 'CashedOut' && (
                    <label className="field">
                        <span>{t.bets.cashOut}</span>
                        <input inputMode="decimal" required value={cashOut} onChange={(e) => setCashOut(e.target.value)} />
                    </label>
                )}
                <label className="field">
                    <span>{t.bets.closingOdds}</span>
                    <input inputMode="decimal" value={closing} onChange={(e) => setClosing(e.target.value)} />
                </label>
                <p className="hint">
                    {preview
                        ? t.bets.preview(money(preview.payout, locale), signedMoney(preview.profitLoss, locale))
                        : t.bets.previewNeedsAmount}
                </p>
                <FormFooter busy={busy} error={error} onClose={onClose} label={t.bets.confirmSettle} />
            </form>
        </Modal>
    );
}
