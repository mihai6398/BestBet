import { describe, expect, it } from 'vitest';
import { parseStatsPaste } from './statsPaste';
import { SOFASCORE_WHOLE_PAGE } from './sofascorePage.fixture';

/**
 * The fixtures are the shapes a real copy produces. Live-score sites lay the stats panel out as
 * value / label / value, and selecting it gives one line per cell — but a table copy gives tabs, and
 * a mobile copy gives everything on one line, so all three have to work.
 */

const SOFASCORE_RO = `
Barcelona 3 - 1 Racing Club
35:57
Analiza meciului
68%
Posesia mingii
32%
2.38
Goluri estimate (xG)
0.30
5
Șanse mari
1
13
Șuturi
1
0
Intervenții de portar
1
5
Cornere
0
3
Faulturi
4
235
Pase
115
0
Cartonașe galbene
1
`;

describe('the Romanian Sofascore layout', () => {
    const parsed = parseStatsPaste(SOFASCORE_RO);

    it('reads the xG that the whole analysis hangs on', () => {
        expect(parsed.xgHome).toBe(2.38);
        expect(parsed.xgAway).toBe(0.3);
    });

    it('reads the clock as the minute in progress', () => {
        // 35:57 means the 36th minute is being played, not the 35th.
        expect(parsed.minute).toBe(36);
    });

    it('reads the score', () => {
        expect(parsed.goalsHome).toBe(3);
        expect(parsed.goalsAway).toBe(1);
    });

    it('reads the team names out of the header', () => {
        expect(parsed.matchLabel).toBe('Barcelona - Racing Club');
    });

    it('reports nothing missing', () => {
        expect(parsed.missing).toEqual([]);
    });

    it('keeps the other stats so the user can see the right block was copied', () => {
        const ids = parsed.extras.map((row) => row.id);
        expect(ids).toContain('possession');
        expect(ids).toContain('shots');
        expect(ids).toContain('bigChances');

        const shots = parsed.extras.find((row) => row.id === 'shots');
        expect(shots).toEqual({ id: 'shots', label: 'Total shots', home: '13', away: '1' });
    });

    it('does not mistake a stat for the xG row', () => {
        expect(parsed.extras.some((row) => row.label === 'Expected goals (xG)')).toBe(false);
    });
});

describe('other layouts of the same numbers', () => {
    it('reads values and label on one line', () => {
        const parsed = parseStatsPaste(`
            Barcelona 3 - 1 Racing Club
            36'
            68%   Posesia mingii   32%
            2.38  Goluri estimate (xG)  0.30
        `);

        expect(parsed.xgHome).toBe(2.38);
        expect(parsed.xgAway).toBe(0.3);
        expect(parsed.minute).toBe(36);
    });

    it('reads a tab-separated table copy', () => {
        const parsed = parseStatsPaste('Posesia mingii\t68%\t32%\nGoluri estimate (xG)\t2.38\t0.30\n36\'');

        expect(parsed.xgHome).toBe(2.38);
        expect(parsed.xgAway).toBe(0.3);
    });

    it('reads the label-first, both-values-next shape', () => {
        const parsed = parseStatsPaste("Goluri estimate (xG)\n2.38  0.30\n36'");

        expect(parsed.xgHome).toBe(2.38);
        expect(parsed.xgAway).toBe(0.3);
    });

    it('reads the English wording', () => {
        const parsed = parseStatsPaste(`
            Arsenal 0 - 0 Chelsea
            67'
            Ball possession
            55%
            45%
            Expected goals (xG)
            1.10
            0.40
            Total shots
            12
            6
        `);

        expect(parsed.xgHome).toBe(1.1);
        expect(parsed.xgAway).toBe(0.4);
        expect(parsed.minute).toBe(67);
        expect(parsed.matchLabel).toBe('Arsenal - Chelsea');
    });

    it('survives diacritics being stripped by the clipboard', () => {
        const parsed = parseStatsPaste("Suturi\n13\n1\nSanse mari\n5\n1\nGoluri estimate (xG)\n2.38\n0.30\n36'");

        expect(parsed.xgHome).toBe(2.38);
        expect(parsed.extras.map((r) => r.label)).toContain('Big chances');
    });

    it('accepts comma decimals', () => {
        const parsed = parseStatsPaste("Goluri estimate (xG)\n2,38\n0,30\n36'");
        expect(parsed.xgHome).toBe(2.38);
        expect(parsed.xgAway).toBe(0.3);
    });
});

describe('reading the clock', () => {
    it.each([
        ["36'", 36],
        ['35:57', 36],
        ['35:00', 35],
        ["45+2'", 47],
        ['90+4', 94],
        ['HT', 45],
        ['Half-time', 45],
        ['FT', 90],
    ])('reads %s as minute %i', (text, expected) => {
        expect(parseStatsPaste(`Goluri estimate (xG)\n1.0\n1.0\n${text}`).minute).toBe(expected);
    });

    it('reports a missing minute rather than inventing one', () => {
        const parsed = parseStatsPaste('Goluri estimate (xG)\n2.38\n0.30');

        expect(parsed.minute).toBeNull();
        expect(parsed.missing).toContain('minute');
    });
});

describe('reading the score', () => {
    it.each([
        ['Barcelona 3 - 1 Racing', 3, 1],
        ['3-1', 3, 1],
        ['3 : 1', 3, 1],
    ])('reads %s', (text, home, away) => {
        const parsed = parseStatsPaste(`${text}\n36'\nGoluri estimate (xG)\n1.0\n1.0`);
        expect(parsed.goalsHome).toBe(home);
        expect(parsed.goalsAway).toBe(away);
    });

    it('does not read a running clock as a score', () => {
        // "35:57" is the single most likely thing to be mistaken for 35-57.
        const parsed = parseStatsPaste("35:57\nGoluri estimate (xG)\n2.38\n0.30");

        expect(parsed.goalsHome).toBeNull();
        expect(parsed.minute).toBe(36);
    });

    it('leaves the score null when the paste has no header', () => {
        const parsed = parseStatsPaste("36'\nGoluri estimate (xG)\n2.38\n0.30");

        expect(parsed.goalsHome).toBeNull();
        // The score is not required: 0-0 is a sane default and the form shows it for correction.
        expect(parsed.missing).toEqual([]);
    });
});

describe('what it refuses to guess', () => {
    it('returns everything missing for text that is not a stats block', () => {
        const parsed = parseStatsPaste('Barcelona are playing well tonight');

        expect(parsed.missing).toEqual(['minute', 'xgHome', 'xgAway']);
        expect(parsed.extras).toEqual([]);
    });

    it('handles an empty paste without throwing', () => {
        expect(parseStatsPaste('').missing).toHaveLength(3);
        expect(parseStatsPaste('   \n  \n').xgHome).toBeNull();
    });

    it('names the half of the xG row it could not find', () => {
        // A truncated copy that caught the label and one value is worse than none, because the
        // missing side would silently become zero.
        const parsed = parseStatsPaste("36'\nGoluri estimate (xG)\n2.38");

        expect(parsed.xgHome).toBeNull();
        expect(parsed.missing).toContain('xgHome');
    });

    it('does not claim team names from a line full of numbers', () => {
        expect(parseStatsPaste("13 3 - 1 5\n36'").matchLabel).toBeNull();
    });

    it('tells shots on target apart from total shots', () => {
        const parsed = parseStatsPaste(
            "Șuturi pe poartă\n5\n1\nȘuturi\n13\n1\nGoluri estimate (xG)\n2.38\n0.30\n36'",
        );

        const onTarget = parsed.extras.find((r) => r.id === 'shotsOnTarget');
        const total = parsed.extras.find((r) => r.id === 'shots');

        expect(onTarget).toEqual({
            id: 'shotsOnTarget',
            label: 'Shots on target',
            home: '5',
            away: '1',
        });
        expect(total).toEqual({ id: 'shots', label: 'Total shots', home: '13', away: '1' });
    });
});

describe('a whole Sofascore page, pasted as it comes', () => {
    // Selecting just the stats panel is the step nobody takes at half time, so the whole page has to
    // work. Everything the analysis needs is in there; the job is not picking up its neighbours.
    const parsed = parseStatsPaste(SOFASCORE_WHOLE_PAGE);

    it('reads the match xG and not a single shot from the shot map', () => {
        expect(parsed.xgHome).toBe(0.4);
        expect(parsed.xgAway).toBe(1.38);
    });

    it('reads the running clock and not the kickoff time', () => {
        // 54:53 is the match. 22:30 is when it started, and reading that would put the match in its
        // 23rd minute — every rate projected from here would be against the wrong hour.
        expect(parsed.minute).toBe(55);
    });

    it('reads the score from the header, not from the half-time strip', () => {
        expect(parsed.goalsHome).toBe(1);
        expect(parsed.goalsAway).toBe(0);
    });

    it('reads the team names out of the header Sofascore stacks vertically', () => {
        expect(parsed.matchLabel).toBe('FC Arouca - CF Estrela Amadora');
    });

    it('needs nothing typed in by hand afterwards', () => {
        expect(parsed.missing).toEqual([]);
    });

    it('still shows the other stats, so the right page is recognisable at a glance', () => {
        const ids = parsed.extras.map((row) => row.id);
        expect(ids).toContain('possession');
        expect(ids).toContain('shots');
    });
});

describe('the things on a page that look like a clock and are not', () => {
    const header = (clock: string) => `Arsenal\nThe Arsenal\n2 - 1\n${clock}\nChelsea\nChelsea FC`;
    const stats = 'Goluri estimate (xG)\n1.10\n0.40';

    it('ignores a kickoff time sitting under its date', () => {
        const paste = `${header("67'")}\n11.10.2026\n22:30\n${stats}`;
        expect(parseStatsPaste(paste).minute).toBe(67);
    });

    it('ignores a kickoff time even when it comes first', () => {
        // Order is no defence on a page the user scrolled before copying, so the date next to it is
        // what rules it out rather than the clock simply being found earlier.
        const paste = `Dată și oră\n22:30\n${header("67'")}\n${stats}`;
        expect(parseStatsPaste(paste).minute).toBe(67);
    });

    it('does not take the "Final" tab label for full time', () => {
        const paste = `FinalÎn direct\n${header('54:53')}\n${stats}`;
        expect(parseStatsPaste(paste).minute).toBe(55);
    });

    it('still reads a bare FT or HT when that is all there is', () => {
        expect(parseStatsPaste(`${stats}\nFT`).minute).toBe(90);
        expect(parseStatsPaste(`${stats}\nHT`).minute).toBe(45);
    });

    it('takes the clock beside the score over a goal minute further down', () => {
        const paste = `${header('54:53')}\nIvan Barbero 5'\n50'\n${stats}`;
        expect(parseStatsPaste(paste).minute).toBe(55);
    });
});

describe('team names from a stacked header', () => {
    it('keeps the full name rather than the abbreviation beside it', () => {
        const paste = 'FC Arouca\nArouca\n1 - 0\n54:53\nCF Estrela Amadora\nEstrela Amadora';
        expect(parseStatsPaste(paste).matchLabel).toBe('FC Arouca - CF Estrela Amadora');
    });

    it('works with nothing but the names either side of the score', () => {
        expect(parseStatsPaste('Arsenal\n2 - 1\nChelsea').matchLabel).toBe('Arsenal - Chelsea');
    });

    it('says nothing when one side of the header is missing', () => {
        // Half a header is a guess, and a guess here puts another match's name on a saved position.
        expect(parseStatsPaste('2 - 1\nChelsea').matchLabel).toBeNull();
    });

    it('does not read a stat row as a header', () => {
        // A stats panel pasted without its header has rows either side of a number too, and naming
        // the match "Cornere - Faulturi" would follow a saved position around for good.
        expect(parseStatsPaste('Cornere\n4 - 3\nFaulturi').matchLabel).toBeNull();
    });
});
