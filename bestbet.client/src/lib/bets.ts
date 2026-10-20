import type { Bet, BetStatus, MarketKind, SaveBetRequest, UpdateBetRequest } from '../types/domain';

/**
 * The bet forms' arithmetic and parsing, kept out of the components so the place a typo turns into a
 * wrong stake has one home and its own tests.
 */

/** The descriptive fields every bet form shares. All optional, all free text except the kick-off. */
export interface BetDetailsForm {
    sport: string;
    competition: string;
    bookmaker: string;
    /** As an <input type="datetime-local"> holds it: local time, no zone. Empty when unknown. */
    eventStart: string;
    notes: string;
}

/** A bet typed in by hand on the bets page, rather than saved from an analysis. */
export interface ManualBetForm extends BetDetailsForm {
    matchLabel: string;
    marketKind: MarketKind;
    marketLabel: string;
    selection: string;
    odds: string;
    /** The other prices in the same market, comma-separated, for the de-vig. Optional. */
    otherOdds: string;
    stake: string;
    /** The user's own estimate, in percent. Optional: without it the edge is zero. */
    probability: string;
}

/** What can be corrected on a saved bet. */
export interface EditBetForm extends BetDetailsForm {
    matchLabel: string;
    odds: string;
    stake: string;
    closingOdds: string;
}

export type BetFormError = 'match' | 'odds' | 'otherOdds' | 'stake' | 'probability' | 'closingOdds';

export type Parsed<T> = { ok: true; value: T } | { ok: false; error: BetFormError };

export const emptyDetails = (): BetDetailsForm => ({ sport: '', competition: '', bookmaker: '', eventStart: '', notes: '' });

export const emptyManualBet = (): ManualBetForm => ({
    ...emptyDetails(),
    sport: 'Fotbal',
    matchLabel: '',
    marketKind: 'TotalGoals',
    marketLabel: '',
    selection: '',
    odds: '',
    otherOdds: '',
    stake: '',
    probability: '',
});

/** Reads a number the way people type it here: a comma works as the decimal point. */
export const parseNumber = (text: string): number => {
    const trimmed = text.trim().replace(',', '.');
    return trimmed === '' ? Number.NaN : Number(trimmed);
};

/** Money to the cent, half away from zero — the same rounding the server stores. */
export const roundMoney = (value: number): number =>
    Math.sign(value) * Math.round((Math.abs(value) + Number.EPSILON) * 100) / 100;

/**
 * The fair probability of one outcome: its implied probability over the whole market's. The same
 * proportional de-vig the server uses, so a bet typed in here and one saved from an analysis agree.
 */
export const deVig = (odds: number, siblings: readonly number[]): number => {
    const implied = 1 / odds;
    return implied / (implied + siblings.reduce((sum, price) => sum + 1 / price, 0));
};

/** The datetime-local value as UTC ISO, or null when empty or unreadable. */
export const localToUtcIso = (local: string): string | null => {
    if (local.trim() === '') return null;
    const date = new Date(local);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
};

/** A UTC ISO timestamp as a datetime-local value in the browser's own time zone. */
export const utcIsoToLocal = (iso: string | null): string => {
    if (!iso) return '';
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return '';
    const pad = (n: number) => String(n).padStart(2, '0');
    return (
        `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
        `T${pad(date.getHours())}:${pad(date.getMinutes())}`
    );
};

const optional = (text: string): string | null => (text.trim() === '' ? null : text.trim());

const detailsOf = (form: BetDetailsForm) => ({
    sport: optional(form.sport),
    competition: optional(form.competition),
    bookmaker: optional(form.bookmaker),
    eventStartUtc: localToUtcIso(form.eventStart),
});

/**
 * Builds the request for a bet typed in by hand.
 *
 * With the other prices in the market the fair probability is de-vigged; without them it is the
 * price's own implied probability, so any edge still contains the bookmaker's margin — the same
 * convention the slip import uses. Without an estimate of their own the edge is exactly zero rather
 * than a number somebody made up.
 */
export function manualBetRequest(form: ManualBetForm, bankroll: number): Parsed<SaveBetRequest> {
    if (form.matchLabel.trim() === '') return { ok: false, error: 'match' };

    const odds = parseNumber(form.odds);
    if (!(odds > 1)) return { ok: false, error: 'odds' };

    // With a dot anywhere the dot is the decimal point, so commas separate prices ("1.85, 3.40");
    // without one the comma is the decimal point and only spaces or semicolons separate ("1,85 3,40").
    const separators = form.otherOdds.includes('.') ? /[\s;,/]+/ : /[\s;/]+/;
    const siblings = form.otherOdds
        .split(separators)
        .map((part) => part.trim())
        .filter((part) => part !== '')
        .map(parseNumber);
    if (siblings.some((price) => !(price > 1))) return { ok: false, error: 'otherOdds' };

    const stake = parseNumber(form.stake);
    if (!(stake >= 0)) return { ok: false, error: 'stake' };

    const typed = form.probability.trim() === '' ? null : parseNumber(form.probability) / 100;
    if (typed !== null && !(typed >= 0 && typed <= 1)) return { ok: false, error: 'probability' };

    const fair = siblings.length > 0 ? deVig(odds, siblings) : 1 / odds;
    const own = typed ?? fair;

    return {
        ok: true,
        value: {
            matchLabel: form.matchLabel.trim(),
            marketKind: form.marketKind,
            marketLabel: form.marketLabel.trim() || form.selection.trim(),
            selection: form.selection.trim(),
            entryDecimalOdds: odds,
            entrySiblingOdds: siblings,
            entryFairProbability: fair,
            entryOwnProbability: own,
            entryEdgePercentagePoints: (own - fair) * 100,
            estimateMethod: typed === null ? 'slip-import' : 'manual-input',
            bankrollAtEntry: bankroll,
            stakeAmount: roundMoney(stake),
            stakeFraction: bankroll > 0 ? stake / bankroll : 0,
            verdict: 'Marginal',
            wasLive: false,
            minutePlayed: null,
            cumulativeXgHome: null,
            cumulativeXgAway: null,
            goalsHome: null,
            goalsAway: null,
            notes: optional(form.notes),
            ...detailsOf(form),
        },
    };
}

/**
 * An analysis's request with what the analysis could not know: the stake actually placed, where, and
 * the match's details. The price and the estimate stay exactly as the analysis produced them.
 */
export function draftWithDetails(
    draft: SaveBetRequest,
    stakeText: string,
    details: BetDetailsForm,
): Parsed<SaveBetRequest> {
    const stake = parseNumber(stakeText);
    if (!(stake >= 0)) return { ok: false, error: 'stake' };

    return {
        ok: true,
        value: {
            ...draft,
            stakeAmount: roundMoney(stake),
            stakeFraction: draft.bankrollAtEntry > 0 ? stake / draft.bankrollAtEntry : 0,
            notes: optional(details.notes) ?? draft.notes,
            ...detailsOf(details),
        },
    };
}

export const editFormFor = (bet: Bet): EditBetForm => ({
    sport: bet.sport ?? '',
    competition: bet.competition ?? '',
    bookmaker: bet.bookmaker ?? '',
    eventStart: utcIsoToLocal(bet.eventStartUtc),
    notes: bet.notes ?? '',
    matchLabel: bet.matchLabel,
    odds: String(bet.entryDecimalOdds),
    stake: String(bet.stakeAmount),
    closingOdds: bet.closingDecimalOdds === null ? '' : String(bet.closingDecimalOdds),
});

/**
 * Only what actually changed, so an edit never re-sends a price the user did not touch — the server
 * re-derives the fair line and the edge whenever the price arrives.
 */
export function editRequest(bet: Bet, form: EditBetForm): Parsed<UpdateBetRequest> {
    const update: UpdateBetRequest = {};

    if (form.matchLabel.trim() === '') return { ok: false, error: 'match' };
    if (form.matchLabel.trim() !== bet.matchLabel) update.matchLabel = form.matchLabel.trim();

    const odds = parseNumber(form.odds);
    if (!(odds > 1)) return { ok: false, error: 'odds' };
    if (Math.abs(odds - bet.entryDecimalOdds) > 1e-9) update.entryDecimalOdds = odds;

    const stake = parseNumber(form.stake);
    if (!(stake >= 0)) return { ok: false, error: 'stake' };
    if (Math.abs(roundMoney(stake) - bet.stakeAmount) > 1e-9) update.stakeAmount = roundMoney(stake);

    if (form.closingOdds.trim() === '') {
        // An emptied box clears the closing price; the server reads anything not above 1 as "none".
        if (bet.closingDecimalOdds !== null) update.closingDecimalOdds = 0;
    } else {
        const closing = parseNumber(form.closingOdds);
        if (!(closing > 1)) return { ok: false, error: 'closingOdds' };
        if (closing !== bet.closingDecimalOdds) update.closingDecimalOdds = closing;
    }

    // Sent as empty strings when cleared: the server stores an emptied field as missing.
    const text = (value: string, current: string | null): string | undefined =>
        value.trim() === (current ?? '') ? undefined : value.trim();

    const sport = text(form.sport, bet.sport);
    if (sport !== undefined) update.sport = sport;
    const competition = text(form.competition, bet.competition);
    if (competition !== undefined) update.competition = competition;
    const bookmaker = text(form.bookmaker, bet.bookmaker);
    if (bookmaker !== undefined) update.bookmaker = bookmaker;
    const notes = text(form.notes, bet.notes);
    if (notes !== undefined) update.notes = notes;

    const start = localToUtcIso(form.eventStart);
    if (start !== null && start !== bet.eventStartUtc) update.eventStartUtc = start;

    return { ok: true, value: update };
}

/**
 * What a result would pay, shown before it is confirmed. The server works it out again and its
 * number is the one that is stored; this is the same arithmetic so the two never disagree on screen.
 */
export function settlementPreview(
    status: BetStatus,
    stake: number,
    odds: number,
    cashOut: number | null = null,
): { payout: number; profitLoss: number } | null {
    const half = stake / 2;
    let payout: number;

    switch (status) {
        case 'Won':
            payout = stake * odds;
            break;
        case 'Lost':
            payout = 0;
            break;
        case 'Void':
            payout = stake;
            break;
        case 'HalfWon':
            payout = half * odds + half;
            break;
        case 'HalfLost':
            payout = half;
            break;
        case 'CashedOut':
            if (cashOut === null || !(cashOut >= 0)) return null;
            payout = cashOut;
            break;
        default:
            return null;
    }

    const rounded = roundMoney(payout);
    return { payout: rounded, profitLoss: roundMoney(rounded - stake) };
}
