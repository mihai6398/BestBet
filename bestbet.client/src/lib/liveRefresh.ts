import type { LiveRepriceResult, LiveUnavailable } from '../types/domain';

/**
 * When each open bet is re-priced while the monitor is on.
 *
 * The monitor reads one match — the one its link points at — and re-prices the bets on it at every
 * reading. Every other open bet used to sit still until somebody pressed a button. Now each one is
 * asked about once a minute: the server finds its match by the bet's team names and prices it, and
 * a bet the monitor has just priced is simply not due yet, so nothing is asked twice.
 *
 * A match that cannot be priced is asked about less often, or not again, depending on whether
 * waiting can change the answer: a match that has not started will start, a league without xG
 * will not grow one.
 */

/** How often a bet is re-priced while the monitor is on. */
export const REFRESH_SECONDS = 60;

/**
 * How often the schedule is looked at. Short, so a due bet waits seconds rather than most of another
 * minute: a bet is re-priced between 60 and 65 seconds after the last time. Looking costs nothing —
 * only a due bet makes a request.
 */
export const CHECK_SECONDS = 5;

/** Not found yet, or not started yet: worth asking again, but not every minute. */
export const RETRY_LATER_SECONDS = 5 * 60;

/** The provider did not answer. Again soon, but not at once. */
export const RETRY_AFTER_ERROR_SECONDS = 2 * 60;

/** Where a bet's numbers came from, so the user can see the right match was found. */
export interface RefreshSource {
    provider: string;
    matchLabel: string | null;
    url: string | null;
}

export interface BetRefresh {
    /** When it was last re-priced, by the monitor or by the minute-by-minute refresh. ms since epoch. */
    lastAt: number | null;
    /** When it is due again. Null: not again until the monitor is restarted. */
    nextAt: number | null;
    source: RefreshSource | null;
    unavailable: LiveUnavailable | null;
    error: string | null;
}

/** Due when it has never been asked about, or its time has come. */
export const isDue = (refresh: BetRefresh | undefined, now: number) =>
    refresh === undefined || (refresh.nextAt !== null && now >= refresh.nextAt);

/** A bet was just priced — by whichever route — so it is good for another minute. */
export function afterPriced(now: number, source: RefreshSource | null): BetRefresh {
    return { lastAt: now, nextAt: now + REFRESH_SECONDS * 1000, source, unavailable: null, error: null };
}

/** The server answered, but could not price the bet. */
export function afterUnavailable(
    previous: BetRefresh | undefined,
    now: number,
    reason: LiveUnavailable,
    source: RefreshSource | null,
): BetRefresh {
    // A match that is over, or a league with no xG, will be the same in five minutes.
    const retry = reason === 'not-found' || reason === 'not-started' ? now + RETRY_LATER_SECONDS * 1000 : null;

    return { lastAt: previous?.lastAt ?? null, nextAt: retry, source, unavailable: reason, error: null };
}

/** The request failed; the last good numbers stay, marked as such. */
export function afterError(previous: BetRefresh | undefined, now: number, message: string): BetRefresh {
    return {
        lastAt: previous?.lastAt ?? null,
        nextAt: now + RETRY_AFTER_ERROR_SECONDS * 1000,
        source: previous?.source ?? null,
        unavailable: null,
        error: message,
    };
}

/** One server answer, folded into the schedule. */
export function afterResult(previous: BetRefresh | undefined, now: number, result: LiveRepriceResult): BetRefresh {
    const source = result.reading
        ? { provider: result.reading.provider, matchLabel: result.reading.matchLabel, url: result.reading.url }
        : null;

    return result.repriced
        ? afterPriced(now, source)
        : afterUnavailable(previous, now, result.unavailable ?? 'not-found', source);
}

/** The page a pushed reading came from, cut down to the part worth showing. */
export function hostOf(url: string): string {
    try {
        return new URL(url).hostname.replace(/^www\./, '');
    } catch {
        return url.slice(0, 40);
    }
}

/** Whole seconds from one moment to another, never negative. For "updated 12 s ago" and "next in 48 s". */
export const secondsBetween = (from: number, to: number) => Math.max(0, Math.round((to - from) / 1000));
