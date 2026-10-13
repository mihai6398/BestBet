import { describe, expect, it } from 'vitest';
import { QUICK_IMPORT_EXAMPLE, QuickImportError, parseQuickImport, toQuickImportJson } from './quickImport';
import { defaultLiveForm, defaultOddsForm } from './forms';

const LIVE_TOTAL = {
    match: 'Barcelona - Racing Club',
    minute: 36,
    xgHome: 2.38,
    xgAway: 0.3,
    goalsHome: 3,
    goalsAway: 1,
    market: 'TotalGoals',
    selection: 'Over',
    line: 6.5,
    odds: 1.91,
    siblingOdds: [1.91],
    bankroll: 263,
};

const json = (body: unknown) => JSON.stringify(body);

/** The message, for asserting that a rejection actually says what is wrong. */
const failureOf = (body: unknown) => {
    try {
        parseQuickImport(typeof body === 'string' ? body : json(body));
    } catch (e) {
        expect(e).toBeInstanceOf(QuickImportError);
        return (e as Error).message;
    }
    throw new Error('expected the import to be rejected, but it succeeded');
};

describe('a complete live import', () => {
    it('fills both forms from one object', () => {
        const { odds, live, bankroll } = parseQuickImport(json(LIVE_TOTAL));

        expect(odds.matchLabel).toBe('Barcelona - Racing Club');
        expect(odds.kind).toBe('TotalGoals');
        expect(odds.selection).toBe('Over');
        expect(odds.line).toBe('6.5');
        expect(odds.decimalOdds).toBe('1.91');
        expect(odds.siblingOdds).toEqual(['1.91']);

        expect(live.isLive).toBe(true);
        expect(live.minutePlayed).toBe('36');
        expect(live.cumulativeXgHome).toBe('2.38');
        expect(live.cumulativeXgAway).toBe('0.3');
        expect(live.currentGoalsHome).toBe('3');
        expect(live.currentGoalsAway).toBe('1');

        expect(bankroll).toBe(263);
    });

    it('accepts the example shipped in the panel unchanged', () => {
        // The placeholder text is the first thing anyone will paste, so it has to actually work.
        expect(() => parseQuickImport(QUICK_IMPORT_EXAMPLE)).not.toThrow();
    });

    it('leaves the bankroll alone when the object does not mention it', () => {
        const { bankroll } = parseQuickImport(json({ ...LIVE_TOTAL, bankroll: undefined }));
        expect(bankroll).toBeNull();
    });
});

describe('forgiving input', () => {
    it('matches field names case-insensitively and ignores separators', () => {
        const { live } = parseQuickImport(
            json({ ...LIVE_TOTAL, xgHome: undefined, xgAway: undefined, XG_Home: 2.38, 'xg away': 0.3 }),
        );

        expect(live.cumulativeXgHome).toBe('2.38');
        expect(live.cumulativeXgAway).toBe('0.3');
    });

    it('accepts numbers typed as strings, comma decimals included', () => {
        // A Romanian keyboard produces "2,38", and pasting from a stats page can quote everything.
        const { odds, live } = parseQuickImport(
            json({ ...LIVE_TOTAL, xgHome: '2,38', odds: '1,91', siblingOdds: ['1,91'], minute: '36' }),
        );

        expect(live.cumulativeXgHome).toBe('2.38');
        expect(odds.decimalOdds).toBe('1.91');
        expect(odds.siblingOdds).toEqual(['1.91']);
        expect(live.minutePlayed).toBe('36');
    });

    it('accepts a bare number as the sibling of a two-way market', () => {
        const { odds } = parseQuickImport(json({ ...LIVE_TOTAL, siblingOdds: 1.91 }));
        expect(odds.siblingOdds).toEqual(['1.91']);
    });

    it('defaults an unstated score to nil-nil', () => {
        const { live } = parseQuickImport(
            json({ ...LIVE_TOTAL, goalsHome: undefined, goalsAway: undefined }),
        );

        expect(live.currentGoalsHome).toBe('0');
        expect(live.currentGoalsAway).toBe('0');
    });

    it('treats a minute on the clock as meaning the match is live', () => {
        const { live } = parseQuickImport(json({ ...LIVE_TOTAL, live: undefined }));
        expect(live.isLive).toBe(true);
    });
});

describe('market and selection aliases', () => {
    it.each([
        ['TotalGoals', 'TotalGoals'],
        ['total', 'TotalGoals'],
        ['goluri', 'TotalGoals'],
        ['NextGoal', 'NextGoal'],
        ['btts', 'BothTeamsToScore'],
        ['1x2', 'Moneyline1X2'],
        ['handicap', 'Handicap'],
    ])('reads market %s as %s', (given, expected) => {
        const body: Record<string, unknown> = {
            ...LIVE_TOTAL,
            market: given,
            selection: expected === 'BothTeamsToScore' ? 'yes' : expected === 'TotalGoals' ? 'over' : 'home',
            siblingOdds: expected === 'NextGoal' || expected === 'Moneyline1X2' ? [3.2, 4.5] : [1.91],
        };
        expect(parseQuickImport(json(body)).odds.kind).toBe(expected);
    });

    it.each([
        ['Sub', 'Under'],
        ['sub', 'Under'],
        ['under', 'Under'],
        ['Peste', 'Over'],
        ['over', 'Over'],
    ])('reads total-goals selection %s as %s', (given, expected) => {
        expect(parseQuickImport(json({ ...LIVE_TOTAL, selection: given })).odds.selection).toBe(expected);
    });

    it.each([
        ['home', 'NextGoalHome'],
        ['1', 'NextGoalHome'],
        ['away', 'NextGoalAway'],
        ['2', 'NextGoalAway'],
        ['none', 'NextGoalNone'],
        ['niciun', 'NextGoalNone'],
    ])('reads next-goal selection %s as %s', (given, expected) => {
        const body = { ...LIVE_TOTAL, market: 'NextGoal', selection: given, siblingOdds: [3.2, 4.5] };
        expect(parseQuickImport(json(body)).odds.selection).toBe(expected);
    });

    it('accepts a numeric selection, since JSON has no reason to quote a 2', () => {
        const body = { ...LIVE_TOTAL, market: 'NextGoal', selection: 2, siblingOdds: [3.2, 4.5] };
        expect(parseQuickImport(json(body)).odds.selection).toBe('NextGoalAway');
    });

    it('refuses a selection that belongs to a different market', () => {
        // "Over" in a both-teams-to-score market is a typo, not an instruction. Guessing here would
        // size a stake against a market nobody asked about.
        const message = failureOf({ ...LIVE_TOTAL, market: 'btts', selection: 'over' });
        expect(message).toContain('does not belong to the BothTeamsToScore market');
        expect(message).toContain('Yes');
    });
});

describe('pre-match imports', () => {
    const PRE_MATCH = {
        match: 'Sevilla - Valencia',
        live: false,
        market: 'BothTeamsToScore',
        selection: 'Da',
        odds: 1.8,
        siblingOdds: [1.95],
        ownProbability: 62,
    };

    it('takes the probability by hand and runs no model', () => {
        const { odds, live } = parseQuickImport(json(PRE_MATCH));

        expect(live.isLive).toBe(false);
        expect(live.manualOwnProbability).toBe('62');
        expect(odds.selection).toBe('Yes');
    });

    it('reads a fraction and a percentage as the same thing', () => {
        expect(parseQuickImport(json({ ...PRE_MATCH, ownProbability: 0.62 })).live.manualOwnProbability)
            .toBe('62');
    });

    it('insists on a probability, because there is no live data to derive one from', () => {
        const message = failureOf({ ...PRE_MATCH, ownProbability: undefined });
        expect(message).toContain('ownProbability');
    });

    it('rejects a probability outside the possible range', () => {
        expect(failureOf({ ...PRE_MATCH, ownProbability: 140 })).toContain('between 0 and 100');
    });
});

describe('rejections name the field they are unhappy about', () => {
    it('rejects text that is not JSON at all', () => {
        expect(failureOf('Barcelona 3-1, xG 2.38')).toContain('not valid JSON');
    });

    it('rejects an empty box', () => {
        expect(failureOf('   ')).toContain('Nothing to import');
    });

    it('rejects a JSON array', () => {
        expect(failureOf('[1, 2, 3]')).toContain('Expected a JSON object');
    });

    it('names a missing market', () => {
        expect(failureOf({ ...LIVE_TOTAL, market: undefined })).toContain('"market" is required');
    });

    it('names an unknown market', () => {
        expect(failureOf({ ...LIVE_TOTAL, market: 'corners' })).toContain('not a market this app prices');
    });

    it('names a missing line on a market that needs one', () => {
        expect(failureOf({ ...LIVE_TOTAL, line: undefined })).toContain('"line" is required');
    });

    it('rejects odds at or below evens return, and says why', () => {
        const message = failureOf({ ...LIVE_TOTAL, odds: 0.91 });
        expect(message).toContain('greater than 1.0');
        expect(message).toContain('decimal odds');
    });

    it('insists on the other prices in the market', () => {
        expect(failureOf({ ...LIVE_TOTAL, siblingOdds: undefined })).toContain('siblingOdds');
    });

    it('rejects the wrong number of sibling prices for the market', () => {
        // A three-way market with one sibling would de-vig against an incomplete book and quietly
        // overstate the fair probability.
        const body = { ...LIVE_TOTAL, market: 'NextGoal', selection: 'away', siblingOdds: [3.2] };
        expect(failureOf(body)).toContain('exactly 2 prices');
    });

    it('rejects a live import with no minute', () => {
        expect(failureOf({ ...LIVE_TOTAL, minute: undefined, live: true })).toContain('"minute" is required');
    });

    it('rejects a minute off the clock', () => {
        expect(failureOf({ ...LIVE_TOTAL, minute: 0 })).toContain('at least 1');
        expect(failureOf({ ...LIVE_TOTAL, minute: 200 })).toContain('at most 120');
    });

    it('rejects negative xG', () => {
        expect(failureOf({ ...LIVE_TOTAL, xgHome: -1 })).toContain('cannot be negative');
    });

    it('rejects a fractional score', () => {
        expect(failureOf({ ...LIVE_TOTAL, goalsHome: 1.5 })).toContain('whole number');
    });
});

describe('export', () => {
    it('round-trips a live setup', () => {
        const original = parseQuickImport(json(LIVE_TOTAL));
        const exported = toQuickImportJson(original.odds, original.live, 263);
        const reimported = parseQuickImport(exported);

        expect(reimported.odds).toEqual(original.odds);
        expect(reimported.live).toEqual(original.live);
        expect(reimported.bankroll).toBe(263);
    });

    it('round-trips a pre-match setup', () => {
        const original = parseQuickImport(
            json({ live: false, market: 'btts', selection: 'da', odds: 1.8, siblingOdds: [1.95], ownProbability: 62 }),
        );
        const reimported = parseQuickImport(toQuickImportJson(original.odds, original.live, 100));

        expect(reimported.live.isLive).toBe(false);
        expect(reimported.live.manualOwnProbability).toBe('62');
        expect(reimported.odds.selection).toBe('Yes');
    });

    it('produces something a person can read and edit', () => {
        const exported = toQuickImportJson(defaultOddsForm('TotalGoals'), defaultLiveForm(), 100);
        expect(exported).toContain('\n  "market"');
    });
});
