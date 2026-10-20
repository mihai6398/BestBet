import { describe, expect, it } from 'vitest';
import type { Bet, SaveBetRequest } from '../types/domain';
import {
    deVig,
    draftWithDetails,
    editFormFor,
    editRequest,
    emptyDetails,
    emptyManualBet,
    localToUtcIso,
    manualBetRequest,
    roundMoney,
    settlementPreview,
    utcIsoToLocal,
} from './bets';

const manual = (overrides: Partial<ReturnType<typeof emptyManualBet>> = {}) => ({
    ...emptyManualBet(),
    matchLabel: 'Bayern - Köln',
    marketLabel: 'Peste 3.5',
    selection: 'Over 3.5',
    odds: '2.10',
    stake: '10',
    ...overrides,
});

describe('a bet typed in by hand', () => {
    it('de-vigs against the other prices when they are given', () => {
        const parsed = manualBetRequest(manual({ otherOdds: '1.75', probability: '52' }), 200);
        if (!parsed.ok) throw new Error(parsed.error);

        expect(parsed.value.entryFairProbability).toBeCloseTo(deVig(2.1, [1.75]), 10);
        expect(parsed.value.entryOwnProbability).toBeCloseTo(0.52, 10);
        expect(parsed.value.entryEdgePercentagePoints).toBeCloseTo((0.52 - deVig(2.1, [1.75])) * 100, 10);
        expect(parsed.value.estimateMethod).toBe('manual-input');
        expect(parsed.value.stakeFraction).toBeCloseTo(0.05, 10);
    });

    it('has an edge of exactly zero without an estimate of its own', () => {
        const parsed = manualBetRequest(manual(), 100);
        if (!parsed.ok) throw new Error(parsed.error);

        expect(parsed.value.entryOwnProbability).toBe(parsed.value.entryFairProbability);
        expect(parsed.value.entryEdgePercentagePoints).toBe(0);
    });

    it('reads a comma as the decimal point', () => {
        const parsed = manualBetRequest(manual({ odds: '2,10', otherOdds: '1,75 3,40', stake: '7,5' }), 100);
        if (!parsed.ok) throw new Error(parsed.error);

        expect(parsed.value.entryDecimalOdds).toBe(2.1);
        expect(parsed.value.entrySiblingOdds).toEqual([1.75, 3.4]);
        expect(parsed.value.stakeAmount).toBe(7.5);
    });

    it('separates prices on commas when the dot is the decimal point', () => {
        const parsed = manualBetRequest(manual({ otherOdds: '1.75, 3.40' }), 100);
        if (!parsed.ok) throw new Error(parsed.error);

        expect(parsed.value.entrySiblingOdds).toEqual([1.75, 3.4]);
    });

    it.each([
        [{ matchLabel: '  ' }, 'match'],
        [{ odds: '1' }, 'odds'],
        [{ odds: 'abc' }, 'odds'],
        [{ otherOdds: '0.9' }, 'otherOdds'],
        [{ stake: '-1' }, 'stake'],
        [{ probability: '120' }, 'probability'],
    ] as const)('refuses %o', (overrides, error) => {
        expect(manualBetRequest(manual(overrides), 100)).toEqual({ ok: false, error });
    });

    it('turns the kick-off into UTC', () => {
        const parsed = manualBetRequest(manual({ eventStart: '2026-10-20T21:00' }), 100);
        if (!parsed.ok) throw new Error(parsed.error);

        expect(parsed.value.eventStartUtc).toBe(new Date('2026-10-20T21:00').toISOString());
    });
});

describe('saving an analysis as a bet', () => {
    const draft = {
        stakeAmount: 3.88,
        bankrollAtEntry: 100,
        notes: null,
        entryDecimalOdds: 2.3,
        entryOwnProbability: 0.52,
    } as SaveBetRequest;

    it('keeps the price and the estimate and takes the stake actually placed', () => {
        const parsed = draftWithDetails(draft, '5', { ...emptyDetails(), bookmaker: 'Superbet' });
        if (!parsed.ok) throw new Error(parsed.error);

        expect(parsed.value.entryDecimalOdds).toBe(2.3);
        expect(parsed.value.entryOwnProbability).toBe(0.52);
        expect(parsed.value.stakeAmount).toBe(5);
        expect(parsed.value.stakeFraction).toBeCloseTo(0.05, 10);
        expect(parsed.value.bookmaker).toBe('Superbet');
        expect(parsed.value.sport).toBeNull();
    });

    it('refuses a negative stake', () => {
        expect(draftWithDetails(draft, '-3', emptyDetails())).toEqual({ ok: false, error: 'stake' });
    });
});

describe('editing a saved bet', () => {
    const bet = {
        matchLabel: 'A - B',
        entryDecimalOdds: 2.3,
        stakeAmount: 10,
        closingDecimalOdds: null,
        sport: 'Fotbal',
        competition: null,
        bookmaker: null,
        eventStartUtc: null,
        notes: null,
    } as Bet;

    it('sends nothing when nothing changed', () => {
        expect(editRequest(bet, editFormFor(bet))).toEqual({ ok: true, value: {} });
    });

    it('sends only what changed', () => {
        const form = { ...editFormFor(bet), odds: '2.20', bookmaker: 'Betano', closingOdds: '2,05' };

        expect(editRequest(bet, form)).toEqual({
            ok: true,
            value: { entryDecimalOdds: 2.2, bookmaker: 'Betano', closingDecimalOdds: 2.05 },
        });
    });

    it('clears a field that was emptied', () => {
        const form = { ...editFormFor(bet), sport: '' };

        expect(editRequest(bet, form)).toEqual({ ok: true, value: { sport: '' } });
    });

    it('refuses a closing price that is not a price', () => {
        expect(editRequest(bet, { ...editFormFor(bet), closingOdds: '1' })).toEqual({ ok: false, error: 'closingOdds' });
    });
});

describe('what a result pays', () => {
    it.each([
        ['Won', 25, 15],
        ['Lost', 0, -10],
        ['Void', 10, 0],
        ['HalfWon', 17.5, 7.5],
        ['HalfLost', 5, -5],
    ] as const)('%s on 10 at 2.50', (status, payout, profitLoss) => {
        expect(settlementPreview(status, 10, 2.5)).toEqual({ payout, profitLoss });
    });

    it('a cash-out pays what the bookmaker paid, and has no preview without it', () => {
        expect(settlementPreview('CashedOut', 10, 2.5, 12.3)).toEqual({ payout: 12.3, profitLoss: 2.3 });
        expect(settlementPreview('CashedOut', 10, 2.5)).toBeNull();
    });

    it('rounds to the cent the way the server does', () => {
        expect(settlementPreview('Won', 3.33, 1.91)).toEqual({ payout: 6.36, profitLoss: 3.03 });
        expect(roundMoney(2.675)).toBe(2.68);
    });
});

describe('kick-off times', () => {
    it('round-trip between the input and UTC', () => {
        const iso = localToUtcIso('2026-10-20T21:00')!;

        expect(utcIsoToLocal(iso)).toBe('2026-10-20T21:00');
        expect(localToUtcIso('')).toBeNull();
        expect(utcIsoToLocal(null)).toBe('');
    });
});
