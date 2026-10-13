import { describe, expect, it } from 'vitest';
import { buildImportRequest, parseBetSlip, slipFormProblem, slipToForm } from './betSlip';

/** The slip as the bookmaker's app shows it, read left to right and top to bottom. */
const SLIP = `2.00 MDL Simplu 13 oct., 23:12 Nerezolvat
Sub 2 2.75
Total
Atletico Madrid 1
Manchester United 0
LIVE A doua repriză 61:43
Suma pariată: 2.00 MDL
Profit posibil: 5.50 MDL`;

describe('reading a bet slip', () => {
    it('reads the side, the price and the stake', () => {
        const slip = parseBetSlip(SLIP);

        expect(slip.kind).toBe('TotalGoals');
        expect(slip.selection).toBe('Under');
        expect(slip.line).toBe(2);
        expect(slip.odds).toBe(2.75);
        expect(slip.stake).toBe(2);
        expect(slip.marketLabel).toBe('Sub 2');
    });

    it('reads the teams and the score off the two lines that end in a number', () => {
        const slip = parseBetSlip(SLIP);

        expect(slip.matchLabel).toBe('Atletico Madrid - Manchester United');
        expect(slip.goalsHome).toBe(1);
        expect(slip.goalsAway).toBe(0);
    });

    it('reads 61:43 as the 62nd minute and does not mistake the bet timestamp for the clock', () => {
        // "23:12" is when the bet was placed. Reading it as a clock would put the match at 23 minutes.
        expect(parseBetSlip(SLIP).minute).toBe(62);
    });

    it('accepts a profit that is the whole return, since 2.00 x 2.75 = 5.50', () => {
        expect(parseBetSlip(SLIP).issues).toEqual([]);
    });

    it('accepts a profit that is the net gain too', () => {
        const net = SLIP.replace('5.50', '3.50');
        expect(parseBetSlip(net).issues).not.toContain('payout-mismatch');
    });

    it('warns when the return does not follow from the price and the stake', () => {
        // One digit misread is exactly how a wrong price gets tracked, so it is flagged, not fixed.
        const misread = SLIP.replace('2.75', '2.15');
        expect(parseBetSlip(misread).issues).toContain('payout-mismatch');
    });

    it('survives the digits an OCR engine swaps for letters', () => {
        const noisy = SLIP.replace('Manchester United 0', 'Manchester United O').replace('2.75', '2.7S');
        const slip = parseBetSlip(noisy);

        expect(slip.goalsAway).toBe(0);
        expect(slip.odds).toBe(2.75);
    });

    it('reads Peste as Over and keeps a decimal line', () => {
        const slip = parseBetSlip(SLIP.replace('Sub 2 2.75', 'Peste 2.5 1.85').replace('5.50', '3.70'));

        expect(slip.selection).toBe('Over');
        expect(slip.line).toBe(2.5);
        expect(slip.odds).toBe(1.85);
    });

    it('reads added time and the half-time break', () => {
        expect(parseBetSlip(SLIP.replace('61:43', "45+2'")).minute).toBe(47);
        expect(parseBetSlip(SLIP.replace('A doua repriză 61:43', 'Pauză')).minute).toBe(45);
    });

    it('says what it could not read instead of guessing', () => {
        const slip = parseBetSlip('Ambele echipe marchează Da 1.80\nSuma pariată: 5.00');

        expect(slip.kind).toBeNull();
        expect(slip.odds).toBeNull();
        expect(slip.issues).toEqual(expect.arrayContaining(['no-market', 'no-odds', 'no-match', 'no-minute']));
        expect(slip.stake).toBe(5);
    });

    it('takes the stake from the header when the stake line was not read', () => {
        const slip = parseBetSlip(SLIP.replace('Suma pariată: 2.00 MDL', ''));
        expect(slip.stake).toBe(2);
    });
});

describe('the review form', () => {
    it('turns a read slip into the request the server wants', () => {
        const form = slipToForm(parseBetSlip(SLIP));
        const request = buildImportRequest(form);

        expect(request).toMatchObject({
            matchLabel: 'Atletico Madrid - Manchester United',
            marketKind: 'TotalGoals',
            marketLabel: 'Sub 2',
            // The canonical form the server reads back when it re-prices an open bet.
            selection: 'Under 2',
            entryDecimalOdds: 2.75,
            stakeAmount: 2,
            minutePlayed: 62,
            goalsHome: 1,
            goalsAway: 0,
        });
        // No xG on a slip, so no model is asked to price the entry.
        expect(request.cumulativeXgHome).toBeUndefined();
    });

    it('sends the xG only when both sides were filled in', () => {
        const form = { ...slipToForm(parseBetSlip(SLIP)), xgHome: '1.1', xgAway: '0.5' };
        expect(buildImportRequest(form)).toMatchObject({ cumulativeXgHome: 1.1, cumulativeXgAway: 0.5 });
    });

    it('is ready when the price and the stake are there', () => {
        expect(slipFormProblem(slipToForm(parseBetSlip(SLIP)))).toBeNull();
    });

    it('refuses a price at or below evens, a missing stake and half a match state', () => {
        const form = slipToForm(parseBetSlip(SLIP));

        expect(slipFormProblem({ ...form, odds: '1' })).toBe('odds');
        expect(slipFormProblem({ ...form, stake: '' })).toBe('stake');
        expect(slipFormProblem({ ...form, xgHome: '1.1', xgAway: '' })).toBe('xg');
        expect(slipFormProblem({ ...form, minute: '', xgHome: '1.1', xgAway: '0.5' })).toBe('minute');
    });
});
