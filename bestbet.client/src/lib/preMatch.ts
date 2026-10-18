/**
 * Small pure helpers for the pre-match panel, kept out of the component so they can be tested on
 * their own and so the component file only exports a component.
 */

/** Same thresholds as the server's pre-match verdict: an edge of 3 points is actionable, 1 is marginal. */
export const WORTH_EDGE_PP = 3;
export const MARGINAL_EDGE_PP = 1;

export type ScorerCall = 'worth' | 'marginal' | 'not-worth';

export interface ScorerAssessment {
    call: ScorerCall;
    /** Estimated chance minus the price's own implied chance, in percentage points. */
    edgePp: number;
    /** Expected profit per unit staked at that price. */
    expectedValue: number;
}

/**
 * Whether backing a scorer at a bookmaker's price is worth it.
 *
 * There is no other side of the market to remove the margin with, so the edge is measured against
 * the price's raw implied chance. That makes it a stricter test than the main analysis, not a
 * looser one: the bookmaker's margin counts against the bet.
 */
export function assessScorer(probability: number, decimalOdds: number): ScorerAssessment | null {
    if (!Number.isFinite(decimalOdds) || decimalOdds <= 1 || !(probability >= 0)) return null;

    const edgePp = (probability - 1 / decimalOdds) * 100;
    const call: ScorerCall =
        edgePp >= WORTH_EDGE_PP ? 'worth' : edgePp >= MARGINAL_EDGE_PP ? 'marginal' : 'not-worth';

    return { call, edgePp, expectedValue: probability * decimalOdds - 1 };
}

/** "Arsenal - Chelsea", "Arsenal vs. Chelsea" and "Arsenal – Chelsea" all name two teams. */
export function splitMatchLabel(label: string): { home: string; away: string } | null {
    const parts = label.split(/\s+(?:-|–|—|vs\.?|v)\s+/i).map((part) => part.trim());
    return parts.length === 2 && parts[0] !== '' && parts[1] !== ''
        ? { home: parts[0], away: parts[1] }
        : null;
}
