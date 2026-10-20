import type { LiveMatchState, Bet } from '../types/domain';
import type { LiveFormState } from './forms';

/**
 * Following an open bet through the match.
 *
 * The state typed in for each bet lives with that bet — a second bet in another match must not
 * inherit the first one's minute — and every reading is kept, because the useful question while a
 * bet runs is not only "what is the chance now" but "which way has it been moving".
 */

/** What the model said at one moment. Enough to draw the trend and nothing the server needs back. */
export interface TrackReading {
    minute: number;
    goalsHome: number;
    goalsAway: number;
    /** P(the bet wins outright), before any returned stake is set aside. */
    win: number;
    /** P(stake returned): non-zero only on a whole goal line. */
    push: number;
}

export interface TrackForm {
    minute: string;
    goalsHome: string;
    goalsAway: string;
    xgHome: string;
    xgAway: string;
    history: TrackReading[];
}

/** More than a match's worth of readings, so the trend is never cut short while a game is on. */
export const MAX_READINGS = 60;

/** The state the bet was struck in, which is the best guess at where the match has got to. */
export function initialTrackForm(position: Bet): TrackForm {
    const text = (value: number | null) => (value === null ? '' : String(value));

    return {
        minute: text(position.minutePlayed),
        goalsHome: text(position.goalsHome),
        goalsAway: text(position.goalsAway),
        xgHome: text(position.cumulativeXgHome),
        xgAway: text(position.cumulativeXgAway),
        history: [],
    };
}

const num = (text: string) => (text.trim() === '' ? Number.NaN : Number(text.replace(',', '.')));

/**
 * The match state the form describes, or null while it is not a complete one. Null rather than a
 * state with blanks turned into zeros: a missing xG read as 0 would say the match has produced
 * nothing, and the model would answer that with a very confident Under.
 */
export function trackLiveState(form: TrackForm): LiveMatchState | null {
    const minute = num(form.minute);
    const xgHome = num(form.xgHome);
    const xgAway = num(form.xgAway);
    const goalsHome = form.goalsHome.trim() === '' ? 0 : num(form.goalsHome);
    const goalsAway = form.goalsAway.trim() === '' ? 0 : num(form.goalsAway);

    const valid =
        Number.isFinite(minute) && minute >= 1 && minute <= 130 &&
        Number.isFinite(xgHome) && xgHome >= 0 &&
        Number.isFinite(xgAway) && xgAway >= 0 &&
        Number.isInteger(goalsHome) && goalsHome >= 0 &&
        Number.isInteger(goalsAway) && goalsAway >= 0;

    return valid
        ? {
              minutePlayed: Math.round(minute),
              cumulativeXgHome: xgHome,
              cumulativeXgAway: xgAway,
              currentGoalsHome: goalsHome,
              currentGoalsAway: goalsAway,
              isPreMatch: false,
          }
        : null;
}

/** A stable key for a state, so an effect re-runs when the numbers change and not when they merely re-render. */
export const stateKey = (state: LiveMatchState | null) =>
    state === null
        ? ''
        : [
              state.minutePlayed,
              state.cumulativeXgHome,
              state.cumulativeXgAway,
              state.currentGoalsHome,
              state.currentGoalsAway,
          ].join('|');

/** The match-state form at the top of the page, carried onto one bet. Null when it is not filled in. */
export function trackFromLiveForm(live: LiveFormState): Omit<TrackForm, 'history'> | null {
    if (!live.isLive) return null;

    const candidate: TrackForm = {
        minute: live.minutePlayed,
        goalsHome: live.currentGoalsHome,
        goalsAway: live.currentGoalsAway,
        xgHome: live.cumulativeXgHome,
        xgAway: live.cumulativeXgAway,
        history: [],
    };
    if (trackLiveState(candidate) === null) return null;

    const { minute, goalsHome, goalsAway, xgHome, xgAway } = candidate;
    return { minute, goalsHome, goalsAway, xgHome, xgAway };
}

/**
 * Adds a reading, unless it says exactly what the newest one already said. Re-reading an unchanged
 * match must not stretch the trend into a flat line that looks like stability.
 */
export function withReading(history: TrackReading[], reading: TrackReading): TrackReading[] {
    const last = history[0];
    const same =
        last !== undefined &&
        last.minute === reading.minute &&
        last.goalsHome === reading.goalsHome &&
        last.goalsAway === reading.goalsAway &&
        Math.abs(last.win - reading.win) < 0.0005 &&
        Math.abs(last.push - reading.push) < 0.0005;

    // Newest first: that is the order it is read in, and the cheap one to trim.
    return same ? history : [reading, ...history].slice(0, MAX_READINGS);
}

/** Chance of losing: whatever is neither a win nor a returned stake. */
export const loseProbability = (win: number, push: number) => Math.max(0, 1 - win - push);

/** The win chances oldest to newest, as 0..1, for a trend line. */
export const trend = (history: TrackReading[]): number[] => [...history].reverse().map((r) => r.win);
