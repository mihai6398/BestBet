import { describe, expect, it } from 'vitest';
import { batchBlockedBy, candidatesFrom, oddsFormFor, sortByEdge } from './batch';
import type { BatchRow } from './batch';
import { defaultLiveForm, defaultOddsForm } from './forms';
import { parseOddsPaste } from './oddsPaste';
import type { AnalyzeResponse } from '../types/domain';

const ladder = parseOddsPaste(
    ['Peste 1.5 1.27 Sub 1.5 3.75', 'Peste 2.5 2.30 Sub 2.5 1.60'].join('\n'),
).markets;

describe('turning a capture into things to price', () => {
    it('offers both sides of every line, not just the one that caught the eye', () => {
        const candidates = candidatesFrom(ladder);

        expect(candidates).toHaveLength(4);
        expect(candidates.map((candidate) => `${candidate.line} ${candidate.selection}`)).toEqual([
            '1.5 Over',
            '1.5 Under',
            '2.5 Over',
            '2.5 Under',
        ]);
    });

    it('gives each candidate the other prices in ITS line, and no others', () => {
        // The failure worth guarding: siblings pulled from the whole capture would de-vig Over 1.5
        // against Under 2.5, which is not a market and not a margin.
        const candidates = candidatesFrom(ladder);

        expect(candidates[0].siblingOdds).toEqual([3.75]);
        expect(candidates[1].siblingOdds).toEqual([1.27]);
        expect(candidates[2].siblingOdds).toEqual([1.6]);
    });

    it('keeps an id that survives re-reading the same capture', () => {
        const again = candidatesFrom(
            parseOddsPaste('Peste 1.5 1.27 Sub 1.5 3.75').markets,
        );

        expect(again[0].id).toBe(candidatesFrom(ladder)[0].id);
    });

    it('fills the form the way a hand-typed market would', () => {
        const candidate = candidatesFrom(ladder)[1];
        const form = oddsFormFor({ ...defaultOddsForm(), matchLabel: 'Sunderland - Arsenal' }, candidate);

        expect(form).toMatchObject({
            matchLabel: 'Sunderland - Arsenal',
            kind: 'TotalGoals',
            selection: 'Under',
            line: '1.5',
            decimalOdds: '3.75',
            siblingOdds: ['1.27'],
        });
    });
});

describe('ordering the results', () => {
    const row = (edge: number | null): BatchRow => ({
        candidate: candidatesFrom(ladder)[0],
        result:
            edge === null
                ? null
                : ({ edge: { edgePercentagePoints: edge } } as unknown as AnalyzeResponse),
        error: edge === null ? 'nope' : null,
    });

    it('puts the biggest edge first and the failures last', () => {
        const sorted = sortByEdge([row(1.2), row(null), row(8.4), row(-3)]);

        expect(sorted.map((one) => one.result?.edge.edgePercentagePoints ?? 'failed')).toEqual([
            8.4,
            1.2,
            -3,
            'failed',
        ]);
    });
});

describe('when a batch cannot run at all', () => {
    it('says so before kick-off, where there is no model to run', () => {
        expect(batchBlockedBy({ ...defaultLiveForm(), isLive: false })).toBe('not-live');
    });

    it('says so while the live fields are still empty', () => {
        expect(batchBlockedBy(defaultLiveForm())).toBe('incomplete-live');
    });

    it('refuses minute zero rather than dividing by it', () => {
        const live = {
            ...defaultLiveForm(),
            minutePlayed: '0',
            cumulativeXgHome: '0.8',
            cumulativeXgAway: '0.3',
        };

        expect(batchBlockedBy(live)).toBe('incomplete-live');
    });

    it('is happy once the match state is there', () => {
        const live = {
            ...defaultLiveForm(),
            minutePlayed: '67',
            cumulativeXgHome: '0.79',
            cumulativeXgAway: '0.01',
        };

        expect(batchBlockedBy(live)).toBeNull();
    });
});
