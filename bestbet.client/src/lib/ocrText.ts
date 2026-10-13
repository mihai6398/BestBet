/**
 * Turning what an OCR engine saw into something the odds parser can read.
 *
 * Two separate jobs live here, and neither of them belongs in the recogniser:
 *
 * 1. Layout. A bookmaker's odds block is a grid, and a grid read naively comes out column by column
 *    — "Peste 1.5", "Peste 2.5", "1.27", "2.30" — which pairs every price with the wrong outcome.
 *    The engine does hand back a box for every word, so the rows can simply be rebuilt from the
 *    geometry instead of trusted from the reading order.
 *
 * 2. Character confusion. An engine trained on prose reads a lone zero as an O and a one as an l,
 *    and that turns 1.27 into l.27. The fixes here are deliberately narrow: a character is only
 *    reinterpreted inside a token that is already clearly a number, and a word is only corrected
 *    towards a word this domain actually uses.
 *
 * Everything is still shown to the user afterwards, editable, before a single stake is sized. This
 * reduces how much typing a misread costs; it is not allowed to become a reason to trust one.
 */

export interface OcrWord {
    text: string;
    x0: number;
    y0: number;
    x1: number;
    y1: number;
}

/**
 * Rebuilds the visual rows of the capture.
 *
 * Words are grouped by how far their vertical centre sits from the row being built, measured against
 * the typical word height in that capture rather than a fixed pixel count — the same screenshot at
 * twice the resolution has to group identically.
 */
export function layoutLines(words: OcrWord[]): string[] {
    const usable = words.filter((word) => word.text.trim() !== '');
    if (usable.length === 0) return [];

    const heights = usable.map((word) => word.y1 - word.y0).sort((a, b) => a - b);
    const medianHeight = heights[Math.floor(heights.length / 2)] || 1;
    const tolerance = medianHeight * 0.6;

    const byHeight = [...usable].sort((a, b) => centreY(a) - centreY(b));

    const rows: { centre: number; words: OcrWord[] }[] = [];
    for (const word of byHeight) {
        const row = rows[rows.length - 1];
        if (row && Math.abs(centreY(word) - row.centre) <= tolerance) {
            row.words.push(word);
            // A running mean, so a row that drifts slightly down the page stays one row.
            row.centre = row.words.reduce((sum, w) => sum + centreY(w), 0) / row.words.length;
        } else {
            rows.push({ centre: centreY(word), words: [word] });
        }
    }

    return rows.map((row) =>
        [...row.words]
            .sort((a, b) => a.x0 - b.x0)
            .map((word) => word.text.trim())
            .join(' ')
            .replace(/\s+/g, ' ')
            .trim(),
    );
}

const centreY = (word: OcrWord) => (word.y0 + word.y1) / 2;

/** The words this domain uses, and the only ones a misread token is ever pulled towards. */
const VOCABULARY = [
    'peste',
    'sub',
    'over',
    'under',
    'total',
    'goluri',
    'goals',
    'meci',
    'ambele',
    'echipe',
    'marcheaza',
    'primul',
    'urmatorul',
    'gol',
    'niciun',
    'rezultat',
    'final',
    'repriza',
    'minute',
    'handicap',
    'gazde',
    'oaspeti',
    'score',
    'teams',
    'both',
    'next',
    'result',
    'match',
];

/**
 * Cleans an OCR transcript up to the point where the odds parser has a fair chance at it.
 *
 * Applied to the raw transcript AND to whatever the user types over it, so a hand correction of
 * "l.27" is fixed the same way the machine's own output is.
 */
export function normaliseOcrText(raw: string): string {
    return (raw ?? '')
        .normalize('NFKC')
        // Spaces and dashes a screen capture is full of, none of which the parser knows.
        .replace(/[  -​  　]/g, ' ')
        .replace(/[‐-―−]/g, '-')
        .split(/\r?\n/)
        .map((line) => line.split(/\s+/).map(fixToken).join(' ').trim())
        .join('\n');
}

/** Characters an engine swaps for digits, and the digit each one was really trying to be. */
const AS_DIGIT: Record<string, string> = {
    O: '0',
    o: '0',
    D: '0',
    Q: '0',
    I: '1',
    l: '1',
    '|': '1',
    '!': '1',
    i: '1',
    S: '5',
    s: '5',
    B: '8',
    b: '8',
    Z: '2',
    z: '2',
    g: '9',
};

const LOOKS_NUMERIC = /^[0-9OoDQIli|!SsBbZzg.,:;']+$/;
const HAS_DIGIT = /[0-9]/;

function fixToken(token: string): string {
    if (token === '') return token;

    // The lock a bookmaker draws over a suspended price comes back as punctuation soup. Dropping it
    // is right: a suspended market has no price, and inventing one would be the worst failure here.
    const stripped = token.replace(/^[^\w.,]+|[^\w.,%]+$/g, '');
    if (stripped === '') return '';

    if (LOOKS_NUMERIC.test(stripped) && HAS_DIGIT.test(stripped)) {
        return fixNumber(stripped);
    }

    return correctWord(stripped);
}

/**
 * A token that is already mostly digits: every lookalike becomes the digit it was meant to be, and
 * the decimal separator becomes the one JavaScript parses.
 */
function fixNumber(token: string): string {
    const digits = [...token].map((char) => AS_DIGIT[char] ?? char).join('');

    return (
        digits
            // ";" and ":" between digits are a misread comma far more often than anything else.
            .replace(/(?<=\d)[;:](?=\d)/g, '.')
            .replace(/(?<=\d),(?=\d)/g, '.')
            // "1 . 27" arrives spaced on low-resolution captures; the split already removed the gaps,
            // but a trailing separator with nothing after it is just noise.
            .replace(/[.,]+$/, '')
            .replace(/^[.,]+/, '')
    );
}

/**
 * Pulls a word towards the vocabulary, but only one character's worth. Two characters wrong is no
 * longer a reading of that word, it is a guess about what the user meant — and a guess that renames
 * "Sub" to "Peste" would flip a stake onto the opposite side of the market.
 */
function correctWord(token: string): string {
    const plain = token
        .toLowerCase()
        .normalize('NFD')
        .replace(/[̀-ͯ]/g, '');

    if (plain.length < 3) return token;
    if (VOCABULARY.includes(plain)) return token;

    // The confusion runs both ways: a word can come back with a digit in it ("5ub"), which is the
    // same mistake as a number coming back with a letter in it, read from the other end.
    const forms = HAS_DIGIT.test(plain)
        ? [plain, [...plain].map((char) => AS_LETTER[char] ?? char).join('')]
        : [plain];

    for (const form of forms) {
        if (VOCABULARY.includes(form)) return form;
    }

    for (const form of forms) {
        const candidates = VOCABULARY.filter(
            (word) => Math.abs(word.length - form.length) <= 1 && distanceWithin1(word, form),
        );

        // Exactly one candidate, or the correction is ambiguous and the token is left as it was.
        if (candidates.length === 1) return candidates[0];
    }

    return token;
}

/** The same confusion as AS_DIGIT, read from the other end. Only the unmistakable ones. */
const AS_LETTER: Record<string, string> = {
    '0': 'o',
    '1': 'i',
    '5': 's',
    '8': 'b',
};

/** Levenshtein distance capped at 1 — the only distance worth computing here, and the cheapest. */
function distanceWithin1(a: string, b: string): boolean {
    if (a === b) return true;
    if (Math.abs(a.length - b.length) > 1) return false;

    const [shorter, longer] = a.length <= b.length ? [a, b] : [b, a];

    let i = 0;
    let j = 0;
    let edits = 0;

    while (i < shorter.length && j < longer.length) {
        if (shorter[i] === longer[j]) {
            i += 1;
            j += 1;
            continue;
        }

        edits += 1;
        if (edits > 1) return false;

        if (shorter.length === longer.length) {
            i += 1;
        }
        j += 1;
    }

    return edits + (longer.length - j) + (shorter.length - i) <= 1;
}
