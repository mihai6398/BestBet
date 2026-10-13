import { describe, expect, it } from 'vitest';
import { parseOddsPaste, siblingsOf } from './oddsPaste';
import { htmlToLines } from './htmlPaste';

const priceOf = (text: string, selection: string) => {
    const market = parseOddsPaste(text).markets[0];
    return market?.outcomes.find((o) => o.selection === selection)?.odds ?? null;
};

describe('total goals', () => {
    it('reads the line and both prices when the line travels with the side', () => {
        const { markets } = parseOddsPaste('Sub 6.5   1.91\nPeste 6.5   1.91');

        expect(markets).toHaveLength(1);
        expect(markets[0].kind).toBe('TotalGoals');
        expect(markets[0].line).toBe(6.5);
        expect(markets[0].outcomes.map((o) => [o.selection, o.odds])).toEqual([
            ['Under', 1.91],
            ['Over', 1.91],
        ]);
    });

    it('reads the shape where the line is a heading above the sides', () => {
        // The layout on the screenshot: market name and line, then the two prices under it.
        const { markets } = parseOddsPaste('Goluri în meci\n6.5\nPeste 1.91\nSub 1.91');

        expect(markets).toHaveLength(1);
        expect(markets[0].line).toBe(6.5);
        expect(markets[0].outcomes).toHaveLength(2);
    });

    it('reads the English wording', () => {
        expect(priceOf('Under 2.5  1.85\nOver 2.5  1.95', 'Under')).toBe(1.85);
        expect(priceOf('Under 2.5  1.85\nOver 2.5  1.95', 'Over')).toBe(1.95);
    });

    it('accepts comma decimals', () => {
        expect(priceOf('Sub 2,5  1,85\nPeste 2,5  1,95', 'Under')).toBe(1.85);
    });

    it('keeps two different lines as two separate markets', () => {
        // Over 2.5 and Over 3.5 are different bets; merging their prices would de-vig nonsense.
        const { markets } = parseOddsPaste(
            'Sub 2.5 1.85\nPeste 2.5 1.95\nSub 3.5 1.40\nPeste 3.5 2.90',
        );

        expect(markets).toHaveLength(2);
        expect(markets.map((m) => m.line).sort()).toEqual([2.5, 3.5]);
    });

    it('does not read the goal line as the price', () => {
        // "Peste 2.5" alone is half a market. Offering it would de-vig against one side.
        const { markets } = parseOddsPaste('Peste 2.5');
        expect(markets).toHaveLength(0);
    });

    it('drops a market with only one side, and says why', () => {
        const { markets, issues } = parseOddsPaste('Sub 6.5 1.91');

        expect(markets).toHaveLength(0);
        expect(issues).toEqual([
            { kind: 'incomplete', market: 'TotalGoals', line: 6.5, found: 1, required: 2 },
        ]);
    });

    it('takes the first price when a page repeats the same market', () => {
        const { markets } = parseOddsPaste('Sub 6.5 1.91\nPeste 6.5 1.91\nSub 6.5 1.80\nPeste 6.5 2.05');

        expect(markets).toHaveLength(1);
        expect(markets[0].outcomes.find((o) => o.selection === 'Under')!.odds).toBe(1.91);
    });
});

describe('match result', () => {
    it('reads three prices each beside its own label', () => {
        const { markets } = parseOddsPaste('Rezultat final\n1 | 1.45\nX | 4.50\n2 | 6.00');

        expect(markets[0].kind).toBe('Moneyline1X2');
        expect(markets[0].outcomes.map((o) => o.odds)).toEqual([1.45, 4.5, 6.0]);
    });

    it('reads a grid with the labels on one row and the prices on the next', () => {
        const { markets } = parseOddsPaste('1  X  2\n1.45  4.50  6.00');

        expect(markets[0].outcomes.map((o) => [o.selection, o.odds])).toEqual([
            ['Home', 1.45],
            ['Draw', 4.5],
            ['Away', 6.0],
        ]);
    });

    it('refuses a partial 1X2 rather than de-vigging against an incomplete book', () => {
        const { markets, issues } = parseOddsPaste('1 | 1.45\nX | 4.50');

        expect(markets).toHaveLength(0);
        expect(issues).toEqual([
            { kind: 'incomplete', market: 'Moneyline1X2', line: null, found: 2, required: 3 },
        ]);
    });
});

describe('both teams to score', () => {
    it('reads Da and Nu under their heading', () => {
        const { markets } = parseOddsPaste('Ambele echipe marchează\nDa 1.80\nNu 1.95');

        expect(markets[0].kind).toBe('BothTeamsToScore');
        expect(markets[0].outcomes.map((o) => [o.selection, o.odds])).toEqual([
            ['Yes', 1.8],
            ['No', 1.95],
        ]);
    });

    it('needs the heading, because "da" and "nu" are ordinary words', () => {
        // Without this guard, any sentence with "nu" next to a number becomes a market.
        const { markets } = parseOddsPaste('Nu 1.95 este o cotă bună\nDa 1.80 poate');
        expect(markets).toHaveLength(0);
    });

    it('reads the English heading too', () => {
        const { markets } = parseOddsPaste('Both teams to score\nYes 1.80\nNo 1.95');
        expect(markets[0].outcomes).toHaveLength(2);
    });
});

describe('next goal', () => {
    it('reads the three outcomes under their heading', () => {
        const { markets } = parseOddsPaste('Primul gol\n1 | 3.20\n2 | 1.60\nNiciun gol | 4.50');

        expect(markets.some((m) => m.kind === 'NextGoal')).toBe(true);
        const next = markets.find((m) => m.kind === 'NextGoal')!;
        expect(next.outcomes.map((o) => [o.selection, o.odds])).toEqual([
            ['NextGoalHome', 3.2],
            ['NextGoalAway', 1.6],
            ['NextGoalNone', 4.5],
        ]);
    });
});

describe('what it will not do', () => {
    it('returns nothing for text with no odds in it', () => {
        expect(parseOddsPaste('Barcelona joacă foarte bine în seara asta').markets).toEqual([]);
    });

    it('handles an empty paste', () => {
        expect(parseOddsPaste('').markets).toEqual([]);
        expect(parseOddsPaste('   \n \n').markets).toEqual([]);
    });

    it('ignores numbers that cannot be a decimal price', () => {
        // 0.91 pays less than the stake back; a "price" like that is a misread.
        expect(parseOddsPaste('Sub 6.5 0.91\nPeste 6.5 0.95').markets).toHaveLength(0);
    });
});

describe('siblings', () => {
    it('are every price in the market except the one being backed', () => {
        const market = parseOddsPaste('1 | 1.45\nX | 4.50\n2 | 6.00').markets[0];

        expect(siblingsOf(market, 'Home')).toEqual([4.5, 6.0]);
        expect(siblingsOf(market, 'Away')).toEqual([1.45, 4.5]);
    });
});

describe('pasted HTML', () => {
    it('rebuilds the rows a flattened copy would have lost', () => {
        const html = `
            <table><tr><td>Sub 6.5</td><td>1.91</td></tr>
                   <tr><td>Peste 6.5</td><td>1.91</td></tr></table>`;

        const { markets } = parseOddsPaste(htmlToLines(html));

        expect(markets).toHaveLength(1);
        expect(markets[0].outcomes).toHaveLength(2);
    });

    it('reads a div grid, which is what most bookmakers actually ship', () => {
        const html = `
            <div class="market"><div class="name">Goluri în meci</div><div class="line">6.5</div>
              <div class="row"><span>Peste</span><span>1.91</span></div>
              <div class="row"><span>Sub</span><span>1.91</span></div>
            </div>`;

        const { markets } = parseOddsPaste(htmlToLines(html));

        expect(markets[0].line).toBe(6.5);
        expect(markets[0].outcomes).toHaveLength(2);
    });

    it('keeps inline siblings apart instead of gluing them together', () => {
        expect(htmlToLines('<div><span>Sub</span><span>1.91</span></div>')).toBe('Sub 1.91');
    });

    it('drops script and style content rather than parsing it as odds', () => {
        const html = '<div>Sub 6.5 1.91</div><script>var x = "Peste 6.5 9.99";</script><style>.a{}</style>';
        expect(htmlToLines(html)).toBe('Sub 6.5 1.91');
    });

    it('never executes what it parses', () => {
        // DOMParser builds an inert document: no script runs, no resource loads. The test asserts the
        // only thing that matters downstream — markup goes in, text comes out.
        const html = '<img src=x onerror="globalThis.__pwned = true"><div>Sub 6.5 1.91</div>';
        const text = htmlToLines(html);

        expect((globalThis as Record<string, unknown>).__pwned).toBeUndefined();
        expect(text).toBe('Sub 6.5 1.91');
    });

    it('survives markup that is not really markup', () => {
        expect(htmlToLines('')).toBe('');
        expect(htmlToLines('<<<>>> not html')).toContain('not html');
    });
});
