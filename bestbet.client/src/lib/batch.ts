import { analysisClient, ApiError } from '../api/analysisClient';
import { buildAnalyzeRequest } from './forms';
import type { LiveFormState, OddsFormState, PaceFormState } from './forms';
import { siblingsOf } from './oddsPaste';
import type { ParsedMarket } from './oddsPaste';
import type { AnalyzeResponse, MarketKind, MarketSelection } from '../types/domain';

/**
 * Pricing every outcome a capture contained, in one go.
 *
 * One screenshot of a totals ladder is eight bets, not one, and which of them is mispriced is
 * exactly the question that cannot be answered by looking. Typing each line into the form in turn is
 * slow enough that in practice only the line the user already fancied gets checked — which is the
 * wrong one to check, because the reason to look at a ladder at all is that the model and the book
 * disagree about where the goals are, not about one line.
 *
 * Nothing here decides anything. Every row is the same analysis the single form runs, with the same
 * thresholds and the same verdict; the only thing this adds is that all of them get run.
 */

export interface BatchCandidate {
    /** Stable across re-parses of the same transcript, so a tick survives an edit elsewhere. */
    id: string;
    kind: MarketKind;
    line: number | null;
    selection: MarketSelection;
    /** The bookmaker's own wording, kept because it is what the user will recognise. */
    label: string;
    odds: number;
    siblingOdds: number[];
}

export interface BatchRow {
    candidate: BatchCandidate;
    result: AnalyzeResponse | null;
    error: string | null;
}

export const candidateId = (
    kind: MarketKind,
    line: number | null,
    selection: MarketSelection,
): string => `${kind}:${line ?? '-'}:${selection}`;

/** Every side of every complete market, each already carrying the siblings its de-vig needs. */
export function candidatesFrom(markets: ParsedMarket[]): BatchCandidate[] {
    return markets.flatMap((market) =>
        market.outcomes.map((outcome) => ({
            id: candidateId(market.kind, market.line, outcome.selection),
            kind: market.kind,
            line: market.line,
            selection: outcome.selection,
            label: outcome.label,
            odds: outcome.odds,
            siblingOdds: siblingsOf(market, outcome.selection),
        })),
    );
}

/**
 * The form state this candidate would fill in.
 *
 * Going through the form rather than building a request directly is deliberate: it keeps one place
 * where strings become numbers, and it means the "open this row" button has something real to put
 * on screen rather than a result with no inputs behind it.
 */
export function oddsFormFor(base: OddsFormState, candidate: BatchCandidate): OddsFormState {
    return {
        ...base,
        kind: candidate.kind,
        selection: candidate.selection,
        label: candidate.label,
        line: candidate.line === null ? '' : String(candidate.line),
        decimalOdds: String(candidate.odds),
        siblingOdds: candidate.siblingOdds.map(String),
    };
}

/** How many analyses are in flight at once. The work is trivial; the politeness is the point. */
const CONCURRENCY = 5;

export interface BatchInputs {
    base: OddsFormState;
    live: LiveFormState;
    pace: PaceFormState;
    bankroll: number;
}

/**
 * Runs every candidate and keeps the failures.
 *
 * A row that the server rejected is reported as a row, not thrown: one impossible price in a
 * ladder — a misread digit, a suspended market — must not take the other seven results away.
 */
export async function runBatch(
    candidates: BatchCandidate[],
    inputs: BatchInputs,
    onDone?: (completed: number) => void,
): Promise<BatchRow[]> {
    const rows: BatchRow[] = [];
    let completed = 0;

    for (let start = 0; start < candidates.length; start += CONCURRENCY) {
        const slice = candidates.slice(start, start + CONCURRENCY);

        const settled = await Promise.all(
            slice.map(async (candidate): Promise<BatchRow> => {
                try {
                    const request = buildAnalyzeRequest(
                        oddsFormFor(inputs.base, candidate),
                        inputs.live,
                        inputs.bankroll,
                        inputs.pace,
                    );
                    return { candidate, result: await analysisClient.analyze(request), error: null };
                } catch (error) {
                    return {
                        candidate,
                        result: null,
                        error:
                            error instanceof ApiError || error instanceof Error
                                ? error.message
                                : String(error),
                    };
                } finally {
                    completed += 1;
                    onDone?.(completed);
                }
            }),
        );

        rows.push(...settled);
    }

    return sortByEdge(rows);
}

/**
 * Best edge first, failures last. Sorting by edge rather than by the verdict is on purpose: the
 * verdict is a threshold applied to this number, and seeing the near-misses next to the ones that
 * cleared it is most of what makes a ladder worth reading.
 */
export function sortByEdge(rows: BatchRow[]): BatchRow[] {
    return [...rows].sort((a, b) => {
        if (!a.result && !b.result) return 0;
        if (!a.result) return 1;
        if (!b.result) return -1;
        return b.result.edge.edgePercentagePoints - a.result.edge.edgePercentagePoints;
    });
}

/**
 * Whether a batch can run at all.
 *
 * Pre-match there is no model: the probability would have to be typed in by hand, once per
 * selection, which is not a batch — it is eight guesses wearing a table.
 */
export function batchBlockedBy(live: LiveFormState): 'not-live' | 'incomplete-live' | null {
    if (!live.isLive) return 'not-live';

    const numbers = [live.minutePlayed, live.cumulativeXgHome, live.cumulativeXgAway];
    const usable =
        numbers.every((value) => value.trim() !== '' && Number.isFinite(Number(value))) &&
        Number(live.minutePlayed) > 0;

    return usable ? null : 'incomplete-live';
}
