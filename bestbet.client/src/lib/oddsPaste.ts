import type { MarketKind, MarketSelection } from '../types/domain';

/**
 * Reads a bookmaker's odds block out of pasted text.
 *
 * Deliberately NOT tied to one site's markup. A CSS selector against 7777.md would work until they
 * rename a class, and would do nothing for anyone else's page. What every bookmaker does have in
 * common is the wording of the outcomes — Sub/Peste, 1/X/2, Da/Nu — sitting next to a decimal price,
 * and that survives both a copy and a redesign.
 *
 * Every market found is offered back for confirmation rather than applied. The parser's job is to
 * stop the user retyping four numbers; deciding which side of a market a stake goes on stays theirs.
 */

export interface OddsOutcome {
    /** What the bookmaker called it, e.g. "Sub 6.5". */
    label: string;
    selection: MarketSelection;
    odds: number;
}

export interface ParsedMarket {
    kind: MarketKind;
    /** Goal line, for the markets that have one. */
    line: number | null;
    outcomes: OddsOutcome[];
}

/**
 * Something found but not usable, reported as data rather than as a sentence.
 *
 * The parser does not know which language the interface is in, and a half-read market is exactly the
 * moment the user most needs to be told why in a language they read. So the reason travels as a code
 * plus its numbers, and the wording is chosen where the wording belongs.
 */
export type ParseIssue =
    | {
          kind: 'incomplete';
          market: MarketKind;
          line: number | null;
          found: number;
          required: number;
      }
    | { kind: 'headingWithoutPrices'; market: MarketKind };

export interface ParsedOdds {
    markets: ParsedMarket[];
    issues: ParseIssue[];
}

/**
 * How many outcomes each market needs before it can be de-vigged. Anything short of a complete book
 * is useless: the margin cannot be removed from a market you only have half of.
 */
const OUTCOMES_REQUIRED: Record<MarketKind, number> = {
    TotalGoals: 2,
    BothTeamsToScore: 2,
    Handicap: 2,
    Moneyline1X2: 3,
    NextGoal: 3,
};

/** Decimal prices live here. Below 1.01 is not a price; above this nobody is analysing it. */
const MIN_ODDS = 1.01;
const MAX_ODDS = 1000;

/** A goal line is a small half- or whole number, which is how it is told apart from a price. */
const MAX_GOAL_LINE = 15;

const DECIMAL = String.raw`\d{1,4}(?:[.,]\d{1,3})?`;

export function parseOddsPaste(raw: string): ParsedOdds {
    const lines = (raw ?? '')
        .replace(/\t/g, '\n')
        .split(/\r?\n/)
        .map((line) => line.trim())
        .filter((line) => line !== '');

    const text = lines.join('\n');
    const issues: ParseIssue[] = [];

    const markets = [
        ...readTotalGoals(text),
        ...readBothTeamsToScore(text, issues),
        ...readMoneyline(text, lines, issues),
        ...readNextGoal(text, issues),
    ];

    const complete = markets.filter((market) => {
        const required = OUTCOMES_REQUIRED[market.kind];
        if (market.outcomes.length >= required) return true;

        issues.push({
            kind: 'incomplete',
            market: market.kind,
            line: market.line,
            found: market.outcomes.length,
            required,
        });
        return false;
    });

    return { markets: complete, issues };
}

// --- Total goals ------------------------------------------------------------

/**
 * "Sub 6.5 1.91" / "Peste 6.5 1.91", in either order and either language, plus the shape where the
 * line sits in a header of its own above the two prices.
 */
function readTotalGoals(text: string): ParsedMarket[] {
    const sideWord = String.raw`(sub|under|peste|over)`;
    const byLine = new Map<number, ParsedMarket>();

    // Shape A: the line travels with the side — "Peste 6.5 1.91".
    const withLine = new RegExp(
        String.raw`\b${sideWord}\s*(${DECIMAL})\s*(?:goluri|goals)?\s*[\s:|]\s*(${DECIMAL})\b`,
        'gi',
    );

    for (const match of text.matchAll(withLine)) {
        const line = toNumber(match[2]);
        const odds = toNumber(match[3]);
        if (line === null || odds === null || !isGoalLine(line) || !isPrice(odds)) continue;

        add(byLine, line, sideOf(match[1]), `${capitalise(match[1])} ${format(line)}`, odds);
    }

    // Shape B: the line is a heading, the sides follow — "Total goluri 6.5" / "Sub 1.91" / "Peste 1.91".
    // The line may sit on the heading's own line or on the next one, which is how a div grid copies.
    const heading = new RegExp(
        String.raw`\b(?:total|goluri|goals|sub\s*/\s*peste|under\s*/\s*over|over\s*/\s*under)\b` +
            String.raw`[^\n\d]*\n?[^\n\d]*(${DECIMAL})\s*(?:\n|$)`,
        'gi',
    );

    for (const match of text.matchAll(heading)) {
        const line = toNumber(match[1]);
        if (line === null || !isGoalLine(line)) continue;

        // Only the lines immediately below the heading belong to it.
        const after = text.slice(match.index! + match[0].length).split('\n').slice(0, 6).join('\n');
        const bare = new RegExp(String.raw`\b${sideWord}\b\s*[\s:|]?\s*(${DECIMAL})`, 'gi');

        for (const side of after.matchAll(bare)) {
            const odds = toNumber(side[2]);
            if (odds === null || !isPrice(odds)) continue;
            add(byLine, line, sideOf(side[1]), `${capitalise(side[1])} ${format(line)}`, odds);
        }
    }

    return [...byLine.values()];
}

function add(
    byLine: Map<number, ParsedMarket>,
    line: number,
    selection: MarketSelection,
    label: string,
    odds: number,
): void {
    const market = byLine.get(line) ?? { kind: 'TotalGoals' as const, line, outcomes: [] };

    // First price wins: a page often repeats a market, and the first copy is the one being looked at.
    if (!market.outcomes.some((outcome) => outcome.selection === selection)) {
        market.outcomes.push({ label, selection, odds });
    }

    byLine.set(line, market);
}

const sideOf = (word: string): MarketSelection =>
    /^(sub|under)$/i.test(word) ? 'Under' : 'Over';

// --- Both teams to score ----------------------------------------------------

/**
 * "Da" and "Nu" are far too common to match on their own, so this one needs its market heading. A
 * stray "Nu" in a sentence must never be read as a price on a market nobody asked about.
 */
function readBothTeamsToScore(text: string, issues: ParseIssue[]): ParsedMarket[] {
    const heading = /\b(ambele\s*echipe[^\n]*|both\s*teams[^\n]*|gg\s*\/\s*ng|btts)\b/i.exec(text);
    if (!heading) return [];

    const after = text.slice(heading.index + heading[0].length).split('\n').slice(0, 8).join('\n');
    const pattern = new RegExp(String.raw`\b(da|yes|gg|nu|no|ng)\b\s*[\s:|]?\s*(${DECIMAL})`, 'gi');

    const outcomes: OddsOutcome[] = [];
    for (const match of after.matchAll(pattern)) {
        const odds = toNumber(match[2]);
        if (odds === null || !isPrice(odds)) continue;

        const selection: MarketSelection = /^(da|yes|gg)$/i.test(match[1]) ? 'Yes' : 'No';
        if (!outcomes.some((outcome) => outcome.selection === selection)) {
            outcomes.push({ label: capitalise(match[1]), selection, odds });
        }
    }

    if (outcomes.length === 0) {
        issues.push({ kind: 'headingWithoutPrices', market: 'BothTeamsToScore' });
        return [];
    }

    return [{ kind: 'BothTeamsToScore', line: null, outcomes }];
}

// --- Match result -----------------------------------------------------------

/**
 * Three prices under a 1 / X / 2 header, in either of the two shapes a grid copies as: the labels on
 * one row and the prices on the next, or each label beside its own price.
 */
function readMoneyline(text: string, lines: string[], issues: ParseIssue[]): ParsedMarket[] {
    const labelled = new RegExp(String.raw`(?:^|\n)\s*([1X2])\s*[\s:|]\s*(${DECIMAL})\s*(?=\n|$)`, 'gi');

    const outcomes: OddsOutcome[] = [];
    for (const match of text.matchAll(labelled)) {
        const odds = toNumber(match[2]);
        if (odds === null || !isPrice(odds)) continue;
        push(outcomes, match[1].toUpperCase(), odds);
    }

    if (outcomes.length < 3) {
        // "1  X  2" on one row, prices on the next.
        const headerIndex = lines.findIndex((line) => /^1\s+x\s+2$/i.test(line.replace(/\|/g, ' ').trim()));
        if (headerIndex >= 0 && headerIndex + 1 < lines.length) {
            const prices = (lines[headerIndex + 1].match(new RegExp(DECIMAL, 'g')) ?? [])
                .map(toNumber)
                .filter((value): value is number => value !== null && isPrice(value));

            if (prices.length >= 3) {
                outcomes.length = 0;
                ['1', 'X', '2'].forEach((label, index) => push(outcomes, label, prices[index]));
            }
        }
    }

    if (outcomes.length === 0) return [];
    if (outcomes.length < 3) {
        issues.push({
            kind: 'incomplete',
            market: 'Moneyline1X2',
            line: null,
            found: outcomes.length,
            required: OUTCOMES_REQUIRED.Moneyline1X2,
        });
        return [];
    }

    return [{ kind: 'Moneyline1X2', line: null, outcomes }];
}

function push(outcomes: OddsOutcome[], label: string, odds: number): void {
    const selection: MarketSelection = label === '1' ? 'Home' : label === 'X' ? 'Draw' : 'Away';
    if (!outcomes.some((outcome) => outcome.selection === selection)) {
        outcomes.push({ label, selection, odds });
    }
}

// --- Next goal --------------------------------------------------------------

function readNextGoal(text: string, issues: ParseIssue[]): ParsedMarket[] {
    const heading = /\b((?:primul|urm[aă]torul)\s*gol[^\n]*|next\s*goal[^\n]*)/i.exec(text);
    if (!heading) return [];

    const after = text.slice(heading.index + heading[0].length).split('\n').slice(0, 10).join('\n');
    const pattern = new RegExp(
        // "Niciun gol" carries its noun, so the alternatives have to allow it before the price.
        String.raw`\b(1|2|0|gazde|home|oaspe[tț]i|away|niciun(?:\s*gol)?|none|f[aă]r[aă]\s*gol)\b` +
            String.raw`\s*[\s:|]?\s*(${DECIMAL})`,
        'gi',
    );

    const outcomes: OddsOutcome[] = [];
    for (const match of after.matchAll(pattern)) {
        const odds = toNumber(match[2]);
        if (odds === null || !isPrice(odds)) continue;

        const token = match[1].toLowerCase();
        const selection: MarketSelection = /^(1|gazde|home)$/.test(token)
            ? 'NextGoalHome'
            : /^(2|oaspe[tț]i|away)$/.test(token)
              ? 'NextGoalAway'
              : 'NextGoalNone';

        if (!outcomes.some((outcome) => outcome.selection === selection)) {
            outcomes.push({ label: capitalise(match[1]), selection, odds });
        }
    }

    if (outcomes.length === 0) {
        issues.push({ kind: 'headingWithoutPrices', market: 'NextGoal' });
        return [];
    }

    return [{ kind: 'NextGoal', line: null, outcomes }];
}

// --- Helpers ----------------------------------------------------------------

/**
 * A goal line is a small number, usually on the half. Requiring that is what keeps "Peste 2.5" from
 * reading its own line as the price when the real price is missing.
 */
function isGoalLine(value: number): boolean {
    return value >= 0 && value <= MAX_GOAL_LINE && (value * 2) % 1 === 0;
}

const isPrice = (value: number) => value >= MIN_ODDS && value <= MAX_ODDS;

function toNumber(raw: string | undefined): number | null {
    if (raw === undefined) return null;
    const value = Number(raw.replace(',', '.'));
    return Number.isFinite(value) ? value : null;
}

const format = (value: number) => String(value);

const capitalise = (word: string) => word.charAt(0).toUpperCase() + word.slice(1).toLowerCase();

/**
 * The bookmaker's margin on a market, as a multiplier: 1.06 means the implied probabilities add up
 * to 106%. Worth showing next to every market the reader picks up, because it is the one number that
 * says whether the prices really are two sides of the same bet. A pair that comes out near 1.00, or
 * far above a normal book, is almost always two prices that were never meant to be paired — a
 * full-match price next to a second-half one, or a digit the reader lost.
 */
export const overroundOf = (market: ParsedMarket): number =>
    market.outcomes.reduce((total, outcome) => total + 1 / outcome.odds, 0);

/** Outside this band, a set of prices is not a book — it is a misread. */
export const isPlausibleOverround = (overround: number): boolean =>
    overround >= 1.005 && overround <= 1.4;

/** The other prices in the market, which is what the de-vig needs. */
export const siblingsOf = (market: ParsedMarket, backing: MarketSelection): number[] =>
    market.outcomes.filter((outcome) => outcome.selection !== backing).map((outcome) => outcome.odds);
