import type { ImportPositionRequest, MarketKind, MarketSelection } from '../types/domain';
import { marketKindConfig } from './forms';
import { normaliseOcrText } from './ocrText';

/**
 * Reading a bet that is already placed off the bookmaker's own slip.
 *
 * The slip is the one place a running bet's price, stake and score sit together, and it is also the
 * only place — the bookmaker's app does not let anything be copied out of it. This reads what it can
 * recognise and leaves the rest blank rather than guessing: a guessed side of a market, or a stake
 * taken from the wrong line, would track a bet nobody placed. Everything lands in a form the user
 * checks before anything is saved.
 *
 * What a slip cannot give is xG, so the probability of winning is not here. That comes from the
 * match state typed in afterwards, which is what the tracker is for.
 */

export type SlipIssue =
    | 'no-odds'
    | 'no-stake'
    | 'no-market'
    | 'no-match'
    | 'no-minute'
    | 'payout-mismatch';

export interface BetSlip {
    stake: number | null;
    odds: number | null;
    /** What the slip says a win returns, kept only to cross-check the other two. */
    payout: number | null;
    kind: MarketKind | null;
    selection: MarketSelection | null;
    line: number | null;
    /** The side as the bookmaker wrote it, e.g. "Sub 2". */
    marketLabel: string;
    matchLabel: string;
    goalsHome: number | null;
    goalsAway: number | null;
    /** Whole minute, from the live clock: 61:43 is read as the 62nd. */
    minute: number | null;
    issues: SlipIssue[];
}

/** A total-goals line: "Sub 2   2.75", "Peste 2.5 1.85". */
const TOTAL_LINE = /^(sub|peste|under|over)\s+(\d+(?:[.,]\d+)?)\s+(\d+[.,]\d{1,3})$/i;

/** A team and its current score: "Atletico Madrid 1". The score may have been read as a letter. */
const TEAM_LINE = /^(.*\p{L}.*?)\s+(\d{1,2}|[OoIl])$/u;

/** Lines that end in a number but are not a team. */
const NOT_A_TEAM = /suma|profit|pariat|posibil|total|simplu|nerezolvat|cota|miza|castig|câștig/i;

const LIVE_LINE = /repriz|pauz|prelungir|live|min\b/i;

export function parseBetSlip(raw: string): BetSlip {
    const lines = normaliseOcrText(raw)
        .split(/\r?\n/)
        .map((line) => line.replace(/\s+/g, ' ').trim())
        .filter((line) => line !== '');

    const slip: BetSlip = {
        stake: null,
        odds: null,
        payout: null,
        kind: null,
        selection: null,
        line: null,
        marketLabel: '',
        matchLabel: '',
        goalsHome: null,
        goalsAway: null,
        minute: null,
        issues: [],
    };

    // --- The side of the market and its price --------------------------------

    let selectionAt = -1;
    lines.forEach((text, index) => {
        if (selectionAt !== -1) return;
        const found = TOTAL_LINE.exec(text);
        if (!found) return;

        selectionAt = index;
        const word = found[1].toLowerCase();
        slip.kind = 'TotalGoals';
        slip.selection = word === 'sub' || word === 'under' ? 'Under' : 'Over';
        slip.line = toNumber(found[2]);
        slip.odds = toNumber(found[3]);
        slip.marketLabel = `${found[1][0].toUpperCase()}${found[1].slice(1).toLowerCase()} ${found[2].replace(',', '.')}`;
    });

    // --- Stake, and what a win returns ---------------------------------------

    slip.stake = amountAfter(lines, /suma\s+pariat\w*/i) ?? headerStake(lines);
    slip.payout = amountAfter(lines, /profit\s+posibil|c[aâ]știg\s+posibil|castig\s+posibil/i);

    // --- The match and its score ---------------------------------------------

    const teams: { name: string; goals: number }[] = [];
    for (let i = selectionAt + 1; i < lines.length && teams.length < 2; i++) {
        const text = lines[i];
        if (NOT_A_TEAM.test(text)) {
            if (teams.length > 0) break;
            continue;
        }

        const found = TEAM_LINE.exec(text);
        if (!found) {
            if (teams.length > 0) break;
            continue;
        }
        teams.push({ name: found[1].trim(), goals: scoreOf(found[2]) });
    }

    if (teams.length === 2) {
        slip.matchLabel = `${teams[0].name} - ${teams[1].name}`;
        slip.goalsHome = teams[0].goals;
        slip.goalsAway = teams[1].goals;
    }

    // --- The match clock -----------------------------------------------------

    // Only on the line that talks about the match period: the bet's own timestamp ("13 oct., 23:12")
    // has the same shape and is not the clock.
    const liveLine = lines.find((text) => LIVE_LINE.test(text));
    if (liveLine) {
        slip.minute = minuteOf(liveLine);
    }

    return { ...slip, issues: issuesFor(slip) };
}

function issuesFor(slip: BetSlip): SlipIssue[] {
    const issues: SlipIssue[] = [];

    if (slip.kind === null) issues.push('no-market');
    if (slip.odds === null) issues.push('no-odds');
    if (slip.stake === null) issues.push('no-stake');
    if (slip.matchLabel === '') issues.push('no-match');
    if (slip.minute === null) issues.push('no-minute');

    // "Profit posibil" on some slips is the net profit and on others the whole return, so either is
    // accepted. Neither matching means the price or the stake was misread, which is worth a warning
    // because both drive the numbers the tracker will show.
    if (slip.stake !== null && slip.odds !== null && slip.payout !== null) {
        const total = slip.stake * slip.odds;
        const net = slip.stake * (slip.odds - 1);
        const close = (a: number, b: number) => Math.abs(a - b) <= Math.max(0.02, b * 0.01);
        if (!close(slip.payout, total) && !close(slip.payout, net)) {
            issues.push('payout-mismatch');
        }
    }

    return issues;
}

// --- Readers ----------------------------------------------------------------

function toNumber(raw: string): number | null {
    const value = Number(raw.replace(',', '.'));
    return Number.isFinite(value) ? value : null;
}

/** A score digit the engine may have read as a letter: O for 0, I or l for 1. */
function scoreOf(token: string): number {
    if (/^[Oo]$/.test(token)) return 0;
    if (/^[Il]$/.test(token)) return 1;
    return Number(token);
}

/** The first amount after a label, wherever on the page it sits: "Suma pariată: 2.00 MDL". */
function amountAfter(lines: string[], label: RegExp): number | null {
    for (const text of lines) {
        const at = label.exec(text);
        if (!at) continue;

        const found = /(\d+(?:[.,]\d{1,2})?)/.exec(text.slice(at.index + at[0].length));
        if (found) return toNumber(found[1]);
    }
    return null;
}

/** "2.00 MDL Simplu …" opens every slip; the fallback when the stake line was not read. */
function headerStake(lines: string[]): number | null {
    for (const text of lines.slice(0, 3)) {
        const found = /^(\d+[.,]\d{2})\s*[A-Za-z]{3}\b/.exec(text);
        if (found) return toNumber(found[1]);
    }
    return null;
}

/**
 * "61:43" is 61 minutes 43 seconds in, which is the 62nd minute; "45+2'" is the 47th; "Pauză" is 45.
 * Both ":" and "." are accepted as the separator because the cleaning step turns the former into the
 * latter between digits.
 */
function minuteOf(text: string): number | null {
    const clock = /(\d{1,3})\s*[:.]\s*(\d{2})\b/.exec(text);
    if (clock) {
        const minutes = Number(clock[1]);
        const seconds = Number(clock[2]);
        if (seconds < 60 && minutes <= 130) return Math.max(1, Math.round(minutes + seconds / 60));
    }

    const added = /(\d{1,3})\s*\+\s*(\d{1,2})/.exec(text);
    if (added) return Number(added[1]) + Number(added[2]);

    const plain = /(\d{1,3})\s*['’′]/.exec(text);
    if (plain) return Number(plain[1]);

    if (/pauz/i.test(text)) return 45;
    return null;
}

// --- The review form --------------------------------------------------------

/** Everything a slip becomes before it is saved. Strings, because that is what an <input> holds. */
export interface SlipFormState {
    matchLabel: string;
    kind: MarketKind;
    selection: MarketSelection;
    line: string;
    label: string;
    odds: string;
    stake: string;
    minute: string;
    goalsHome: string;
    goalsAway: string;
    xgHome: string;
    xgAway: string;
}

export function slipToForm(slip: BetSlip): SlipFormState {
    const kind: MarketKind = slip.kind ?? 'TotalGoals';
    const config = marketKindConfig(kind);

    return {
        matchLabel: slip.matchLabel,
        kind,
        selection: slip.selection ?? config.selections[0].value,
        line: slip.line === null ? (config.needsLine ? '2.5' : '') : String(slip.line),
        label: slip.marketLabel,
        odds: slip.odds === null ? '' : String(slip.odds),
        stake: slip.stake === null ? '' : String(slip.stake),
        minute: slip.minute === null ? '' : String(slip.minute),
        goalsHome: slip.goalsHome === null ? '' : String(slip.goalsHome),
        goalsAway: slip.goalsAway === null ? '' : String(slip.goalsAway),
        xgHome: '',
        xgAway: '',
    };
}

export type SlipFormProblem = 'odds' | 'stake' | 'line' | 'minute' | 'score' | 'xg';

const nonNegative = (text: string) => text.trim() !== '' && Number(text.replace(',', '.')) >= 0;
const asNumber = (text: string) => Number(text.replace(',', '.'));

/** What stands between this form and a bet worth tracking, or null when it is ready. */
export function slipFormProblem(form: SlipFormState): SlipFormProblem | null {
    if (!(asNumber(form.odds) > 1)) return 'odds';
    if (!nonNegative(form.stake)) return 'stake';

    const config = marketKindConfig(form.kind);
    if (config.needsLine && form.line.trim() === '') return 'line';
    if (config.needsLine && !Number.isFinite(asNumber(form.line))) return 'line';

    const hasMinute = form.minute.trim() !== '';
    if (hasMinute && !(asNumber(form.minute) >= 1 && asNumber(form.minute) <= 130)) return 'minute';

    const hasXg = form.xgHome.trim() !== '' || form.xgAway.trim() !== '';
    if (hasXg && !(nonNegative(form.xgHome) && nonNegative(form.xgAway))) return 'xg';
    // A model run needs the clock as well as the xG: half a match state is not one.
    if (hasXg && !hasMinute) return 'minute';

    const hasScore = form.goalsHome.trim() !== '' || form.goalsAway.trim() !== '';
    if (hasScore && !(nonNegative(form.goalsHome) && nonNegative(form.goalsAway))) return 'score';

    return null;
}

/** The canonical side the server reads back when it re-prices: "Under 2", "NextGoalAway". */
export function slipSelection(form: SlipFormState): string {
    const config = marketKindConfig(form.kind);
    return config.needsLine && form.line.trim() !== ''
        ? `${form.selection} ${asNumber(form.line)}`
        : form.selection;
}

export function buildImportRequest(form: SlipFormState): ImportPositionRequest {
    const optional = (text: string) => (text.trim() === '' ? undefined : asNumber(text));
    const hasMinute = form.minute.trim() !== '';
    const hasXg = form.xgHome.trim() !== '' && form.xgAway.trim() !== '';

    return {
        matchLabel: form.matchLabel.trim(),
        marketKind: form.kind,
        marketLabel: form.label.trim() || slipSelection(form),
        selection: slipSelection(form),
        entryDecimalOdds: asNumber(form.odds),
        stakeAmount: asNumber(form.stake),
        minutePlayed: hasMinute ? Math.round(asNumber(form.minute)) : undefined,
        goalsHome: optional(form.goalsHome),
        goalsAway: optional(form.goalsAway),
        cumulativeXgHome: hasXg ? asNumber(form.xgHome) : undefined,
        cumulativeXgAway: hasXg ? asNumber(form.xgAway) : undefined,
    };
}
