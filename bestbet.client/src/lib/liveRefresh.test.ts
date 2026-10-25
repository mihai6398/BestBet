import { describe, expect, it } from 'vitest';
import {
    afterError,
    afterPriced,
    afterResult,
    afterUnavailable,
    isDue,
    REFRESH_SECONDS,
    RETRY_AFTER_ERROR_SECONDS,
    RETRY_LATER_SECONDS,
    secondsBetween,
} from './liveRefresh';
import type { LiveRepriceResult, ProviderReading, RepricedBet } from '../types/domain';

const now = 1_000_000;
const source = { provider: 'FotMob', matchLabel: 'Como - Manchester United', url: 'https://www.fotmob.com/match/6106410' };

describe('re-pricing every open bet once a minute', () => {
    it('asks about a bet it has never asked about', () => {
        expect(isDue(undefined, now)).toBe(true);
    });

    it('leaves a bet alone for a minute after it was priced, by either route', () => {
        // A bet the monitor has just re-priced off its own reading is not asked about again: one
        // minute's numbers, one request.
        const priced = afterPriced(now, source);

        expect(isDue(priced, now + (REFRESH_SECONDS - 1) * 1000)).toBe(false);
        expect(isDue(priced, now + REFRESH_SECONDS * 1000)).toBe(true);
    });

    it('asks less often about a match that has not started or was not found', () => {
        for (const reason of ['not-started', 'not-found'] as const) {
            const later = afterUnavailable(undefined, now, reason, null);

            expect(isDue(later, now + REFRESH_SECONDS * 1000)).toBe(false);
            expect(isDue(later, now + RETRY_LATER_SECONDS * 1000)).toBe(true);
        }
    });

    it('stops asking about a match that is over or has no xG — waiting will not change either', () => {
        for (const reason of ['finished', 'no-xg'] as const) {
            expect(isDue(afterUnavailable(undefined, now, reason, source), now + 24 * 3600 * 1000)).toBe(false);
        }
    });

    it('tries again a little later when the provider does not answer, and keeps the last good time', () => {
        const failed = afterError(afterPriced(now, source), now + 60_000, 'FotMob answered 503');

        expect(failed.lastAt).toBe(now);
        expect(failed.source).toEqual(source);
        expect(failed.error).toBe('FotMob answered 503');
        expect(isDue(failed, now + 60_000 + RETRY_AFTER_ERROR_SECONDS * 1000)).toBe(true);
    });

    it('folds a server answer into the schedule, with the match it found', () => {
        const reading = { provider: 'FotMob', matchLabel: 'Como - Manchester United', url: source.url } as ProviderReading;

        const priced = afterResult(undefined, now, {
            reading,
            liveState: null,
            repriced: {} as RepricedBet,
            unavailable: null,
        } as LiveRepriceResult);
        const notStarted = afterResult(undefined, now, {
            reading,
            liveState: null,
            repriced: null,
            unavailable: 'not-started',
        });

        expect(priced).toMatchObject({ lastAt: now, unavailable: null, source });
        expect(notStarted).toMatchObject({ lastAt: null, unavailable: 'not-started', source });
    });

    it('counts whole seconds and never backwards', () => {
        expect(secondsBetween(now, now + 12_400)).toBe(12);
        expect(secondsBetween(now, now - 5_000)).toBe(0);
    });
});
