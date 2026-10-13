import { describe, expect, it } from 'vitest';
import { layoutLines, normaliseOcrText } from './ocrText';
import type { OcrWord } from './ocrText';
import { overroundOf, parseOddsPaste } from './oddsPaste';

/** A word box, written the way the capture reads: text, where it starts, which row it sits on. */
const word = (text: string, x: number, row: number, width = text.length * 11): OcrWord => ({
    text,
    x0: x,
    x1: x + width,
    y0: row * 48,
    y1: row * 48 + 26,
});

describe('rebuilding rows from word boxes', () => {
    it('puts two columns of the same row back on one line', () => {
        // The failure this exists to prevent: read column by column, the 1.27 belonging to "Peste
        // 1.5" lands next to "Peste 2.5", and a stake gets sized against a price nobody offered.
        const words = [
            word('Peste', 40, 0),
            word('1.5', 120, 0),
            word('1.27', 230, 0),
            word('Sub', 320, 0),
            word('1.5', 390, 0),
            word('3.75', 500, 0),
            word('Peste', 40, 1),
            word('2.5', 120, 1),
            word('2.30', 230, 1),
        ];

        expect(layoutLines(words)).toEqual(['Peste 1.5 1.27 Sub 1.5 3.75', 'Peste 2.5 2.30']);
    });

    it('does not care what order the engine handed the words over in', () => {
        const rows = [
            word('1.27', 230, 0),
            word('Peste', 40, 1),
            word('Peste', 40, 0),
            word('2.30', 230, 1),
            word('2.5', 120, 1),
            word('1.5', 120, 0),
        ];

        expect(layoutLines(rows)).toEqual(['Peste 1.5 1.27', 'Peste 2.5 2.30']);
    });

    it('tolerates a row whose words do not line up to the pixel', () => {
        const wobbly = [
            { ...word('Sub', 40, 0), y0: 2, y1: 28 },
            { ...word('2.5', 120, 0), y0: 6, y1: 32 },
            { ...word('1.60', 230, 0), y0: 0, y1: 26 },
        ];

        expect(layoutLines(wobbly)).toEqual(['Sub 2.5 1.60']);
    });

    it('drops the empty boxes an engine emits around icons', () => {
        const withIcon = [word('Sub', 40, 0), word('   ', 150, 0), word('1.60', 230, 0)];
        expect(layoutLines(withIcon)).toEqual(['Sub 1.60']);
    });

    it('has nothing to say about an empty page', () => {
        expect(layoutLines([])).toEqual([]);
    });
});

describe('cleaning up what the engine read', () => {
    it('turns letters back into the digits they were', () => {
        expect(normaliseOcrText('Peste 1.5 l.27')).toBe('Peste 1.5 1.27');
        expect(normaliseOcrText('Sub 2.5 l6O')).toBe('Sub 2.5 160');
    });

    it('accepts a comma decimal, and a colon read as one', () => {
        expect(normaliseOcrText('Peste 2,5 1,95')).toBe('Peste 2.5 1.95');
        expect(normaliseOcrText('Peste 2.5 1:95')).toBe('Peste 2.5 1.95');
    });

    it('pulls a misread word back to the one word it can be', () => {
        expect(normaliseOcrText('Pesle 1.5 1.27')).toBe('peste 1.5 1.27');
        expect(normaliseOcrText('5ub 1.5 3.75')).toBe('sub 1.5 3.75');
    });

    it('leaves a word that is not nearly one of ours alone', () => {
        // A correction two characters wide is not a reading of the word, it is a guess about the
        // user's intent — and the guess that renames a team to "Sub" would flip a stake.
        expect(normaliseOcrText('Barcelona Racing')).toBe('Barcelona Racing');
        expect(normaliseOcrText('FCSB 1.5')).toBe('FCSB 1.5');
    });

    it('throws away the lock a suspended market is drawn with', () => {
        // Keeping it would be bad; inventing a price for it would be worse.
        expect(normaliseOcrText('Sub 5.5 \u{1f512}')).toBe('Sub 5.5');
    });

    it('keeps the rows it was given', () => {
        expect(normaliseOcrText('Peste 1.5 1.27\nSub 1.5 3.75')).toBe('Peste 1.5 1.27\nSub 1.5 3.75');
    });
});

describe('the whole way through, on the capture this was built for', () => {
    // "Total Goluri", two columns, five goal lines, the last one suspended behind a padlock.
    const capture: OcrWord[] = [
        word('Total', 200, 0),
        word('Goluri', 270, 0),
        word('90', 100, 1),
        word('de', 130, 1),
        word('minute', 165, 1),
        word('A', 330, 1),
        word('2-a', 355, 1),
        word('Repriza', 400, 1),
        word('Peste', 40, 2),
        word('1.5', 120, 2),
        word('1.27', 230, 2),
        word('Sub', 320, 2),
        word('1.5', 390, 2),
        word('3.75', 500, 2),
        word('Peste', 40, 3),
        word('2.5', 120, 3),
        word('2.30', 230, 3),
        word('Sub', 320, 3),
        word('2.5', 390, 3),
        word('1.60', 500, 3),
        word('Peste', 40, 4),
        word('3.5', 120, 4),
        word('5.50', 230, 4),
        word('Sub', 320, 4),
        word('3.5', 390, 4),
        word('1.14', 500, 4),
        word('Peste', 40, 5),
        word('4.5', 120, 5),
        word('13.50', 230, 5),
        word('Sub', 320, 5),
        word('4.5', 390, 5),
        word('1.02', 500, 5),
        word('Peste', 40, 6),
        word('5.5', 120, 6),
        word('40.00', 230, 6),
        word('Sub', 320, 6),
        word('5.5', 390, 6),
        word('\u{1f512}', 500, 6),
    ];

    const parsed = parseOddsPaste(normaliseOcrText(layoutLines(capture).join('\n')));

    it('reads every line that has both of its prices', () => {
        expect(parsed.markets.map((market) => market.line)).toEqual([1.5, 2.5, 3.5, 4.5]);
    });

    it('pairs each price with the side it was printed next to', () => {
        const priceOf = (line: number, selection: string) =>
            parsed.markets
                .find((market) => market.line === line)!
                .outcomes.find((outcome) => outcome.selection === selection)!.odds;

        expect(priceOf(1.5, 'Over')).toBe(1.27);
        expect(priceOf(1.5, 'Under')).toBe(3.75);
        expect(priceOf(4.5, 'Over')).toBe(13.5);
        expect(priceOf(4.5, 'Under')).toBe(1.02);
    });

    it('comes out with a margin a real book would have, which is the check that the pairing is right', () => {
        // Four lines of one market are priced off one model, so their margins land within a point of
        // each other. A pairing that crossed two different markets would not do that.
        for (const market of parsed.markets) {
            expect(overroundOf(market)).toBeGreaterThan(1.04);
            expect(overroundOf(market)).toBeLessThan(1.07);
        }
    });

    it('refuses the suspended line instead of guessing a price for it', () => {
        expect(parsed.markets.some((market) => market.line === 5.5)).toBe(false);
        expect(parsed.issues).toContainEqual({
            kind: 'incomplete',
            market: 'TotalGoals',
            line: 5.5,
            found: 1,
            required: 2,
        });
    });
});
