import { describe, expect, it } from 'vitest';
import {
    addReading,
    breakEven,
    clockLabel,
    eventsIn,
    MAX_WIN_READINGS,
    nearestIndex,
    periodOf,
    position,
    pruneHistories,
    readingFrom,
    scaleFor,
} from './winChart';
import type { WinReading } from './winChart';
import type { RepricedBet } from '../types/domain';

const at = (minute: number, over: Partial<WinReading> = {}): WinReading => ({
    minute,
    period: periodOf(minute, null),
    goalsHome: 0,
    goalsAway: 0,
    redCardsHome: 0,
    redCardsAway: 0,
    addedTimeFirstHalf: null,
    addedTimeSecondHalf: null,
    win: 0.5,
    push: 0,
    remainingMinutes: 90 - minute,
    at: '2026-10-21T19:00:00.000Z',
    ...over,
});

describe('one reading per minute of the match', () => {
    it('replaces the last reading when the same minute is read again', () => {
        // A feed polled every few seconds reprices one minute many times; only the last word on a
        // minute belongs on a chart drawn in minutes.
        const history = addReading([at(60, { win: 0.4 })], at(60, { win: 0.45 }));

        expect(history).toHaveLength(1);
        expect(history[0].win).toBe(0.45);
    });

    it('appends a later minute', () => {
        expect(addReading([at(60)], at(61)).map((r) => r.minute)).toEqual([60, 61]);
    });

    it('drops a reading that goes back in time rather than drawing the line backwards', () => {
        expect(addReading([at(60), at(61)], at(58)).map((r) => r.minute)).toEqual([60, 61]);
    });

    it('tells first-half stoppage from the second half', () => {
        // Both are minute 47. The second half's 47th minute comes after 45+2, not before it.
        const stoppage = at(47, { period: 'FirstHalf', addedTimeFirstHalf: 3 });
        const restart = at(47, { period: 'SecondHalf', addedTimeFirstHalf: 3 });

        expect(addReading([stoppage], restart)).toHaveLength(2);
    });

    it('keeps a bounded history', () => {
        let history: WinReading[] = [];
        for (let minute = 1; minute <= MAX_WIN_READINGS + 20; minute++) {
            history = addReading(history, at(minute, { period: 'SecondHalf' }));
        }

        expect(history).toHaveLength(MAX_WIN_READINGS);
        expect(history[history.length - 1].minute).toBe(MAX_WIN_READINGS + 20);
    });
});

describe('the minute axis', () => {
    it('is the full ninety before any stoppage is known', () => {
        expect(scaleFor([at(30)]).end).toBe(90);
    });

    it('opens a band for each half as wide as its board', () => {
        const scale = scaleFor([at(60, { period: 'SecondHalf', addedTimeFirstHalf: 2, addedTimeSecondHalf: 5 })]);

        expect(scale.firstStoppage).toBe(2);
        expect(scale.secondStoppage).toBe(5);
        expect(scale.end).toBe(97);
    });

    it('widens a band when the referee plays past the board', () => {
        const scale = scaleFor([
            at(91, { period: 'SecondHalf', addedTimeSecondHalf: 3 }),
            at(95, { period: 'SecondHalf', addedTimeSecondHalf: 3 }),
        ]);

        expect(scale.secondStoppage).toBe(5);
    });

    it('puts 45+2 inside the first-half band and the second half after it', () => {
        const scale = scaleFor([at(47, { period: 'FirstHalf', addedTimeFirstHalf: 3 })]);

        expect(position({ minute: 47, period: 'FirstHalf' }, scale)).toBe(47);
        expect(position({ minute: 45, period: 'HalfTime' }, scale)).toBe(48);
        expect(position({ minute: 47, period: 'SecondHalf' }, scale)).toBe(50);
        expect(position({ minute: 90, period: 'SecondHalf' }, scale)).toBe(93);
    });

    it('says the minute the way a match clock does', () => {
        expect(clockLabel({ minute: 47, period: 'FirstHalf' }, 'HT')).toBe("45+2'");
        expect(clockLabel({ minute: 47, period: 'SecondHalf' }, 'HT')).toBe("47'");
        expect(clockLabel({ minute: 94, period: 'SecondHalf' }, 'HT')).toBe("90+4'");
        expect(clockLabel({ minute: 45, period: 'HalfTime' }, 'Pauză')).toBe('Pauză');
    });
});

describe('what happened between readings', () => {
    it('marks a goal where the score first changed, with the score after it', () => {
        const history = [at(60), at(61, { goalsAway: 1 })];
        const events = eventsIn(history, scaleFor(history));

        expect(events).toEqual([{ kind: 'goal', side: 'away', x: 61, score: '0-1' }]);
    });

    it('marks two goals between readings as two goals', () => {
        const history = [at(60), at(64, { goalsHome: 2 })];

        expect(eventsIn(history, scaleFor(history))).toHaveLength(2);
    });

    it('marks a red card', () => {
        const history = [at(60), at(61, { redCardsHome: 1 })];

        expect(eventsIn(history, scaleFor(history))).toEqual([{ kind: 'red', side: 'home', x: 61, score: '0-0' }]);
    });
});

describe('reading the chart', () => {
    it('finds the reading nearest the pointer', () => {
        const history = [at(10), at(20), at(30)];

        expect(nearestIndex(history, scaleFor(history), 24)).toBe(1);
        expect(nearestIndex([], scaleFor([]), 24)).toBeNull();
    });

    it('draws the break-even line at one over the price taken', () => {
        expect(breakEven(2.0)).toBe(0.5);
        expect(breakEven(1.25)).toBe(0.8);
    });

    it('builds a reading from a re-price and the state it answered', () => {
        const repriced = {
            model: { rawWinProbability: 0.62, pushProbability: 0.05, remainingMinutes: 31.5 },
        } as RepricedBet;
        const reading = readingFrom(
            {
                minutePlayed: 47,
                cumulativeXgHome: 0.9,
                cumulativeXgAway: 0.4,
                currentGoalsHome: 1,
                currentGoalsAway: 0,
                isPreMatch: false,
                period: 'FirstHalf',
                addedTimeFirstHalf: 3,
                redCardsAway: 1,
            },
            repriced,
            new Date('2026-10-21T19:00:00Z'),
        );

        expect(reading).toMatchObject({
            minute: 47,
            period: 'FirstHalf',
            goalsHome: 1,
            redCardsAway: 1,
            addedTimeFirstHalf: 3,
            addedTimeSecondHalf: null,
            win: 0.62,
            push: 0.05,
            remainingMinutes: 31.5,
        });
    });

    it('keeps the history of open bets only', () => {
        const kept = pruneHistories({ '1': [at(10)], '2': [at(20)] }, [2, 3]);

        expect(Object.keys(kept)).toEqual(['2']);
    });
});
