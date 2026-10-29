import { describe, expect, it } from 'vitest';
import {
    buildAnalyzeRequest,
    buildSaveBetRequest,
    canonicalSelection,
    defaultLiveForm,
    defaultOddsForm,
    defaultPaceForm,
} from './forms';
import type { OddsFormState, ReferenceState } from './forms';
import type { AnalyzeResponse } from '../types/domain';

const totals = (): OddsFormState => ({
    ...defaultOddsForm('TotalGoals'),
    matchLabel: 'Sunderland - Arsenal',
    selection: 'Over',
    line: '2.5',
    decimalOdds: '2.30',
    siblingOdds: ['1.60'],
});

const live = () => ({
    ...defaultLiveForm(),
    minutePlayed: '67',
    cumulativeXgHome: '1.35',
    cumulativeXgAway: '0.90',
});

const polymarket = (): ReferenceState => ({
    source: 'Polymarket',
    label: 'Yes',
    question: 'Will three total goals be scored in regulation time?',
    decimalOdds: 1 / 0.6,
    siblingOdds: [1 / 0.4],
});

describe('naming the chosen side', () => {
    it('writes the form the server reads back', () => {
        // The log stores this string and re-pricing an open bet parses it again. A display label —
        // "Under / Sub 2.5" — cannot be read back, and a running bet could not be priced again.
        expect(canonicalSelection(totals())).toBe('Over 2.5');
    });

    it('leaves the line off a market that has none', () => {
        const nextGoal = { ...defaultOddsForm('NextGoal'), selection: 'NextGoalAway' as const };
        expect(canonicalSelection(nextGoal)).toBe('NextGoalAway');
    });

    it('leaves the line off when the field is empty', () => {
        expect(canonicalSelection({ ...totals(), line: '' })).toBe('Over');
    });

    it('is what a saved position carries when no model ran', () => {
        const result = {
            edge: { fairMarketProbability: 0.5, ownProbability: 0.6, edgePercentagePoints: 10, expectedValuePerUnit: 0.1 },
            stake: { suggestedStakeFraction: 0.03, suggestedStakeAmount: 3, capReason: 'floor-3pct' },
            verdict: 'Bet',
            estimate: { method: 'manual-input' },
            model: null,
        } as unknown as AnalyzeResponse;

        const saved = buildSaveBetRequest(totals(), { ...live(), isLive: false }, 100, result);

        expect(saved.selection).toBe('Over 2.5');
    });
});

describe('sending a reference line', () => {
    it('is absent unless one was chosen', () => {
        const request = buildAnalyzeRequest(totals(), live(), 100, defaultPaceForm());

        expect(request.referenceMarket).toBeUndefined();
        expect(request.referenceSource).toBeUndefined();
    });

    it('carries the reference prices and leaves the bookmaker price alone', () => {
        // The point of the whole feature: the edge is measured against the reference, the stake is
        // sized at the price you can actually get. Swapping them would size against a price nobody
        // will pay, so the request has to keep both.
        const request = buildAnalyzeRequest(totals(), live(), 100, defaultPaceForm(), polymarket());

        expect(request.market.decimalOdds).toBe(2.3);
        expect(request.market.siblingOdds).toEqual([1.6]);
        expect(request.referenceSource).toBe('Polymarket');
        expect(request.referenceMarket?.decimalOdds).toBeCloseTo(1 / 0.6, 10);
        expect(request.referenceMarket?.siblingOdds).toEqual([1 / 0.4]);
    });

    it('labels the reference with the outcome the exchange used', () => {
        const request = buildAnalyzeRequest(totals(), live(), 100, defaultPaceForm(), polymarket());

        expect(request.referenceMarket?.label).toBe('Yes');
        expect(request.referenceMarket?.kind).toBe('TotalGoals');
    });
});

describe('sending the pre-match model', () => {
    const rates = { homeExpectedGoals: 2.1, awayExpectedGoals: 1.0 };

    it('replaces the typed probability when the match has not started', () => {
        const prematch = { ...live(), isLive: false, manualOwnProbability: '55' };
        const request = buildAnalyzeRequest(totals(), prematch, 100, defaultPaceForm(), null, rates);

        expect(request.preMatch).toEqual(rates);
        expect(request.manualOwnProbability).toBeUndefined();
        expect(request.liveState).toBeNull();
    });

    it('goes along with a live match, as what its pace is pulled towards', () => {
        const request = buildAnalyzeRequest(totals(), live(), 100, defaultPaceForm(), null, rates);

        expect(request.preMatch).toEqual(rates);
        expect(request.liveState).not.toBeNull();
        expect(request.manualOwnProbability).toBeUndefined();
    });

    it('leaves the typed probability alone when no model is in use', () => {
        const prematch = { ...live(), isLive: false, manualOwnProbability: '55' };
        const request = buildAnalyzeRequest(totals(), prematch, 100, defaultPaceForm());

        expect(request.manualOwnProbability).toBeCloseTo(0.55, 10);
        expect(request.preMatch).toBeUndefined();
    });
});

describe('telling the timing plan how old the reading is', () => {
    it('sends the age of a live reading, rounded', () => {
        const request = buildAnalyzeRequest(totals(), live(), 100, defaultPaceForm(), null, null, 42.6);

        expect(request.feedAgeSeconds).toBe(43);
    });

    it('sends nothing for a reading that was typed in', () => {
        expect(buildAnalyzeRequest(totals(), live(), 100).feedAgeSeconds).toBeUndefined();
    });

    it('sends nothing before kick-off, where there is no reading to age', () => {
        const prematch = { ...live(), isLive: false, manualOwnProbability: '55' };
        const request = buildAnalyzeRequest(totals(), prematch, 100, defaultPaceForm(), null, null, 42);

        expect(request.feedAgeSeconds).toBeUndefined();
    });
});

describe('switching the corrections off', () => {
    it('turns off the tempo and the spread along with the rest, so off means the spec', () => {
        const request = buildAnalyzeRequest(totals(), live(), 100, { ...defaultPaceForm(), corrected: false });

        expect(request.pace).toEqual({
            shrink: false,
            scoreState: false,
            stoppage: false,
            redCards: false,
            timeProfile: false,
            rateUncertainty: false,
        });
    });
});
