import { describe, expect, it } from 'vitest';
import { assessScorer, splitMatchLabel } from './preMatch';

describe('assessScorer', () => {
    it('calls a clear gap between the chance and the price worth it', () => {
        // 40% chance against a 3.00 price (33.3% implied): +6.7 points.
        const result = assessScorer(0.4, 3.0)!;
        expect(result.call).toBe('worth');
        expect(result.edgePp).toBeCloseTo(6.67, 1);
        expect(result.expectedValue).toBeCloseTo(0.2, 6);
    });

    it('calls a small gap marginal and no gap not worth it', () => {
        expect(assessScorer(0.36, 3.0)!.call).toBe('marginal');
        expect(assessScorer(0.3, 3.0)!.call).toBe('not-worth');
    });

    it('measures against the raw implied chance, so a short price needs more', () => {
        // Fair price for 50% is 2.00; a book offering 1.90 is against the bet.
        expect(assessScorer(0.5, 1.9)!.call).toBe('not-worth');
    });

    it('declines to judge a price that is not a price', () => {
        expect(assessScorer(0.4, Number.NaN)).toBeNull();
        expect(assessScorer(0.4, 1.0)).toBeNull();
    });
});

describe('splitMatchLabel', () => {
    it.each([
        ['Arsenal - Chelsea', 'Arsenal', 'Chelsea'],
        ['Arsenal vs. Chelsea', 'Arsenal', 'Chelsea'],
        ['Atletico Madrid – Manchester United', 'Atletico Madrid', 'Manchester United'],
    ])('reads %s', (label, home, away) => {
        expect(splitMatchLabel(label)).toEqual({ home, away });
    });

    it('does not split a hyphenated name', () => {
        expect(splitMatchLabel('Stoke-on-Trent')).toBeNull();
    });
});
