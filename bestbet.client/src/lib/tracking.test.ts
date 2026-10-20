import { describe, expect, it } from 'vitest';
import { defaultLiveForm } from './forms';
import {
    MAX_READINGS,
    initialTrackForm,
    loseProbability,
    stateKey,
    trackFromLiveForm,
    trackLiveState,
    trend,
    withReading,
} from './tracking';
import type { TrackForm, TrackReading } from './tracking';
import type { Bet } from '../types/domain';

const form = (patch: Partial<TrackForm> = {}): TrackForm => ({
    minute: '62',
    goalsHome: '1',
    goalsAway: '0',
    xgHome: '1.1',
    xgAway: '0.5',
    history: [],
    ...patch,
});

const reading = (patch: Partial<TrackReading> = {}): TrackReading => ({
    minute: 62,
    goalsHome: 1,
    goalsAway: 0,
    win: 0.6,
    push: 0.2,
    ...patch,
});

describe('the state typed in for one bet', () => {
    it('becomes a live match state when it is complete', () => {
        expect(trackLiveState(form())).toEqual({
            minutePlayed: 62,
            cumulativeXgHome: 1.1,
            cumulativeXgAway: 0.5,
            currentGoalsHome: 1,
            currentGoalsAway: 0,
            isPreMatch: false,
        });
    });

    it('accepts a comma decimal, as a Romanian keyboard types it', () => {
        expect(trackLiveState(form({ xgHome: '1,25' }))?.cumulativeXgHome).toBe(1.25);
    });

    it('is not a state while the xG is missing — a blank is not a zero', () => {
        // Read as 0, "no xG yet" would tell the model the match has produced nothing.
        expect(trackLiveState(form({ xgHome: '' }))).toBeNull();
        expect(trackLiveState(form({ xgAway: '' }))).toBeNull();
    });

    it('is not a state without a sensible minute or with an impossible score', () => {
        expect(trackLiveState(form({ minute: '' }))).toBeNull();
        expect(trackLiveState(form({ minute: '0' }))).toBeNull();
        expect(trackLiveState(form({ minute: '200' }))).toBeNull();
        expect(trackLiveState(form({ goalsHome: '1.5' }))).toBeNull();
        expect(trackLiveState(form({ goalsAway: '-1' }))).toBeNull();
    });

    it('reads an empty score as nil-nil', () => {
        expect(trackLiveState(form({ goalsHome: '', goalsAway: '' }))?.currentGoalsHome).toBe(0);
    });

    it('keys equal states equally, so an unchanged form does not trigger another reading', () => {
        expect(stateKey(trackLiveState(form()))).toBe(stateKey(trackLiveState(form())));
        expect(stateKey(trackLiveState(form()))).not.toBe(stateKey(trackLiveState(form({ minute: '63' }))));
        expect(stateKey(null)).toBe('');
    });
});

describe('starting a bet off from what is known', () => {
    const position = {
        minutePlayed: 62,
        goalsHome: 1,
        goalsAway: 0,
        cumulativeXgHome: null,
        cumulativeXgAway: null,
    } as Bet;

    it('uses the minute and score the bet was struck at, and leaves the unknown xG blank', () => {
        const start = initialTrackForm(position);

        expect(start).toMatchObject({ minute: '62', goalsHome: '1', goalsAway: '0', xgHome: '', xgAway: '' });
        expect(trackLiveState(start)).toBeNull();
    });
});

describe('taking the state from the main form', () => {
    it('copies a complete live form', () => {
        const live = {
            ...defaultLiveForm(),
            minutePlayed: '70',
            cumulativeXgHome: '1.4',
            cumulativeXgAway: '0.9',
            currentGoalsHome: '1',
            currentGoalsAway: '1',
        };

        expect(trackFromLiveForm(live)).toEqual({
            minute: '70',
            goalsHome: '1',
            goalsAway: '1',
            xgHome: '1.4',
            xgAway: '0.9',
        });
    });

    it('copies nothing from a form that is not live or not filled in', () => {
        expect(trackFromLiveForm(defaultLiveForm())).toBeNull();
        expect(trackFromLiveForm({ ...defaultLiveForm(), isLive: false, minutePlayed: '70' })).toBeNull();
    });
});

describe('the history of readings', () => {
    it('puts the newest first', () => {
        const history = withReading([reading({ minute: 62 })], reading({ minute: 70, win: 0.7 }));
        expect(history.map((r) => r.minute)).toEqual([70, 62]);
    });

    it('ignores a reading that repeats the newest one', () => {
        // Re-reading an unchanged match would stretch the trend into a flat line that looks like stability.
        const once = withReading([], reading());
        expect(withReading(once, reading())).toHaveLength(1);
    });

    it('keeps a reading at the same minute when the score has changed', () => {
        const once = withReading([], reading());
        expect(withReading(once, reading({ goalsAway: 1, win: 0.3 }))).toHaveLength(2);
    });

    it('is capped', () => {
        let history: TrackReading[] = [];
        for (let i = 0; i < MAX_READINGS + 10; i++) {
            history = withReading(history, reading({ minute: i + 1 }));
        }
        expect(history).toHaveLength(MAX_READINGS);
        expect(history[0].minute).toBe(MAX_READINGS + 10);
    });

    it('draws the trend oldest to newest', () => {
        const history = [reading({ win: 0.8 }), reading({ minute: 55, win: 0.5 })];
        expect(trend(history)).toEqual([0.5, 0.8]);
    });
});

describe('the three outcomes of a bet that can push', () => {
    it('adds the chance of losing so the three make a whole', () => {
        expect(loseProbability(0.55, 0.15)).toBeCloseTo(0.3, 10);
        expect(loseProbability(0.6, 0)).toBeCloseTo(0.4, 10);
    });

    it('never goes below zero on rounding', () => {
        expect(loseProbability(0.7, 0.3000000001)).toBe(0);
    });
});
