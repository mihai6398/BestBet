import type { LiveMatchState, MatchPeriod, RepricedBet } from '../types/domain';

/**
 * The win chance of an open bet, minute by minute, for the chart that follows it through a match.
 *
 * Everything here is arithmetic on a list of readings, kept out of the component so it can be tested
 * without drawing anything. The component turns these numbers into pixels and nothing else.
 */

/** What the model said about one bet at one moment of the match. */
export interface WinReading {
    minute: number;
    /** Resolved when the reading is taken, so a typed minute 47 is not re-guessed later. */
    period: MatchPeriod;
    goalsHome: number;
    goalsAway: number;
    redCardsHome: number;
    redCardsAway: number;
    addedTimeFirstHalf: number | null;
    addedTimeSecondHalf: number | null;
    /** P(the bet wins outright). */
    win: number;
    /** P(stake returned) — only ever non-zero on a whole goal line. */
    push: number;
    /** Minutes of play the model ran the rest of the match over, stoppage included. */
    remainingMinutes: number;
    /** When it was taken, ISO. */
    at: string;
}

/**
 * More than a match's worth, at one reading per minute of play with room for a long stoppage. A
 * bet's history is dropped once the bet is settled, so this is a ceiling, not something to reach.
 */
export const MAX_WIN_READINGS = 140;

/** The period a state is in: as the provider said, or read off the minute as the server would. */
export function periodOf(minute: number, period: MatchPeriod | null | undefined): MatchPeriod {
    return period ?? (minute <= 45 ? 'FirstHalf' : 'SecondHalf');
}

/** One reading, out of a re-price and the state it was asked about. */
export function readingFrom(state: LiveMatchState, repriced: RepricedBet, at = new Date()): WinReading {
    return {
        minute: state.minutePlayed,
        period: periodOf(state.minutePlayed, state.period),
        goalsHome: state.currentGoalsHome,
        goalsAway: state.currentGoalsAway,
        redCardsHome: state.redCardsHome ?? 0,
        redCardsAway: state.redCardsAway ?? 0,
        addedTimeFirstHalf: state.addedTimeFirstHalf ?? null,
        addedTimeSecondHalf: state.addedTimeSecondHalf ?? null,
        win: repriced.model.rawWinProbability,
        push: repriced.model.pushProbability,
        remainingMinutes: repriced.model.remainingMinutes,
        at: at.toISOString(),
    };
}

const sameMoment = (a: WinReading, b: WinReading) => a.period === b.period && a.minute === b.minute;

/**
 * Adds a reading, oldest first, keeping one per minute of the match.
 *
 * A feed polled every few seconds reprices the same minute several times — the xG ticks up, a shot
 * lands — and only the last word on a minute belongs on a chart drawn in minutes. So a reading for
 * the minute already at the end replaces it, and an earlier minute than the last one is a reading
 * that arrived out of order, or a different match's clock: dropped rather than drawn backwards.
 */
export function addReading(history: readonly WinReading[], next: WinReading): WinReading[] {
    const last = history[history.length - 1];

    if (last !== undefined) {
        if (sameMoment(last, next)) return [...history.slice(0, -1), next];

        const scale = scaleFor([...history, next]);
        if (position(next, scale) < position(last, scale)) return [...history];
    }

    return [...history, next].slice(-MAX_WIN_READINGS);
}

/**
 * How the match's minutes are laid out along the chart.
 *
 * The two halves sit end to end with each half's stoppage drawn as its own band, so "45+3" lands in
 * the first-half band rather than on top of the 48th minute of the second half. A band is as wide
 * as the board, or as far as the readings have gone past 45 or 90 when that is further: a referee
 * who plays on past the board still played those minutes.
 */
export interface Scale {
    /** Width of the first-half stoppage band, in minutes. 0 until there is one. */
    firstStoppage: number;
    secondStoppage: number;
    /** Where the chart ends, in chart minutes: at least the full ninety, plus both bands. */
    end: number;
    boardFirst: number | null;
    boardSecond: number | null;
}

export function scaleFor(history: readonly WinReading[]): Scale {
    let boardFirst: number | null = null;
    let boardSecond: number | null = null;
    let overFirst = 0;
    let overSecond = 0;

    for (const reading of history) {
        boardFirst = reading.addedTimeFirstHalf ?? boardFirst;
        boardSecond = reading.addedTimeSecondHalf ?? boardSecond;
        if (reading.period === 'FirstHalf') overFirst = Math.max(overFirst, reading.minute - 45);
        if (reading.period === 'SecondHalf') overSecond = Math.max(overSecond, reading.minute - 90);
    }

    const firstStoppage = Math.max(boardFirst ?? 0, overFirst);
    const secondStoppage = Math.max(boardSecond ?? 0, overSecond);

    return {
        firstStoppage,
        secondStoppage,
        end: 90 + firstStoppage + secondStoppage,
        boardFirst,
        boardSecond,
    };
}

/** Where on the chart's minute axis a moment of the match falls. */
export function position(moment: { minute: number; period: MatchPeriod }, scale: Scale): number {
    switch (moment.period) {
        case 'FirstHalf':
            return Math.min(Math.max(moment.minute, 0), 45 + scale.firstStoppage);
        case 'HalfTime':
            return 45 + scale.firstStoppage;
        default:
            return Math.max(moment.minute, 45) + scale.firstStoppage;
    }
}

/** The minute as a match clock says it: 63', 45+2', 90+4', or half time. */
export function clockLabel(moment: { minute: number; period: MatchPeriod }, halfTime: string): string {
    if (moment.period === 'HalfTime') return halfTime;
    if (moment.period === 'FirstHalf' && moment.minute > 45) return `45+${moment.minute - 45}'`;
    if (moment.period === 'SecondHalf' && moment.minute > 90) return `90+${moment.minute - 90}'`;
    return `${moment.minute}'`;
}

/** Something that happened between two readings and belongs on the chart as a marker. */
export interface MatchEvent {
    kind: 'goal' | 'red';
    side: 'home' | 'away';
    /** Chart minute of the first reading that showed it. */
    x: number;
    /** The score after it, for a goal. */
    score: string;
}

/**
 * Goals and red cards, found as the readings change.
 *
 * Placed at the first reading that showed them rather than at the minute they happened: the chart
 * only knows the match through its readings, and a marker at the moment the line jumped is the one
 * that explains the jump.
 */
export function eventsIn(history: readonly WinReading[], scale: Scale): MatchEvent[] {
    const events: MatchEvent[] = [];

    for (let i = 1; i < history.length; i++) {
        const before = history[i - 1];
        const now = history[i];
        const x = position(now, scale);
        const score = `${now.goalsHome}-${now.goalsAway}`;

        for (let g = before.goalsHome; g < now.goalsHome; g++) events.push({ kind: 'goal', side: 'home', x, score });
        for (let g = before.goalsAway; g < now.goalsAway; g++) events.push({ kind: 'goal', side: 'away', x, score });
        if (now.redCardsHome > before.redCardsHome) events.push({ kind: 'red', side: 'home', x, score });
        if (now.redCardsAway > before.redCardsAway) events.push({ kind: 'red', side: 'away', x, score });
    }

    return events;
}

/** The reading nearest a chart minute, for the crosshair. Null on an empty history. */
export function nearestIndex(history: readonly WinReading[], scale: Scale, x: number): number | null {
    let best: number | null = null;
    let distance = Number.POSITIVE_INFINITY;

    history.forEach((reading, index) => {
        const d = Math.abs(position(reading, scale) - x);
        if (d < distance) {
            best = index;
            distance = d;
        }
    });

    return best;
}

/**
 * The win chance a bet has to keep to be worth what was paid for it: 1 / the price taken. Above it
 * the bet is worth more than its stake on average, below it less — the line a cash-out offer is
 * judged against.
 */
export const breakEven = (entryOdds: number) => (entryOdds > 1 ? 1 / entryOdds : 1);

/** Only the bets still open keep a history; everything else is dropped. */
export function pruneHistories(
    histories: Readonly<Record<string, WinReading[]>>,
    openIds: readonly number[],
): Record<string, WinReading[]> {
    const keep = new Set(openIds.map(String));
    return Object.fromEntries(Object.entries(histories).filter(([id]) => keep.has(id)));
}
