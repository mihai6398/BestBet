/**
 * Reads the match-stats block copied off a live-score site.
 *
 * Why parse pasted text rather than call an API: the stats providers do not offer one, so a "fetch"
 * button would mean scraping an undocumented internal endpoint — against their terms, and broken the
 * next time they rename a field. Text parsing costs the user one Ctrl+C and works on Sofascore,
 * Flashscore, FotMob and anything else that renders the same numbers, in Romanian or English.
 *
 * Nothing here is applied silently. The parser returns what it found and what it could not find, and
 * the UI shows that back before anything reaches the form — a misread xG would size a real stake.
 */

export interface StatRow {
    /** Stable key for the stat, so the interface can name it in the reader's own language. */
    id: StatId;
    /** English name, kept as the fallback for a stat the dictionary has not been taught yet. */
    label: string;
    home: string;
    away: string;
}

export type StatId =
    | 'xg'
    | 'shotsOnTarget'
    | 'bigChances'
    | 'shots'
    | 'possession'
    | 'corners'
    | 'saves'
    | 'fouls'
    | 'yellowCards';

/** The fields an analysis cannot run without, named as keys rather than as sentences. */
export type MissingField = 'minute' | 'xgHome' | 'xgAway';

export interface ParsedStats {
    matchLabel: string | null;
    minute: number | null;
    goalsHome: number | null;
    goalsAway: number | null;
    xgHome: number | null;
    xgAway: number | null;
    /** Everything else recognised, shown so the user can confirm the right block was copied. */
    extras: StatRow[];
    /** The fields the analysis needs and the paste did not supply. */
    missing: MissingField[];
}

const REQUIRED: ReadonlyArray<MissingField> = ['minute', 'xgHome', 'xgAway'];

interface StatDefinition {
    id: StatId;
    display: string;
    pattern: RegExp;
}

/**
 * Ordered most specific first: "Șuturi pe poartă" has to be claimed before the bare "Șuturi" pattern
 * gets a chance at it.
 */
const STATS: ReadonlyArray<StatDefinition> = [
    { id: 'xg', display: 'Expected goals (xG)', pattern: /goluri\s*estimate|expected\s*goals|^xg$|\(xg\)/i },
    { id: 'shotsOnTarget', display: 'Shots on target', pattern: /[sș]uturi\s*(pe\s*poart[aă]|pe\s*spa[tț]iu)|shots?\s*on\s*(target|goal)/i },
    { id: 'bigChances', display: 'Big chances', pattern: /[sș]anse\s*mari|big\s*chances/i },
    { id: 'shots', display: 'Total shots', pattern: /^[sș]uturi$|total\s*shots|^shots$/i },
    { id: 'possession', display: 'Possession', pattern: /posesia|ball\s*possession|^possession$/i },
    { id: 'corners', display: 'Corners', pattern: /^cornere$|corner\s*kicks|^corners$/i },
    { id: 'saves', display: 'Goalkeeper saves', pattern: /interven[tț]ii\s*de\s*portar|goalkeeper\s*saves|^saves$/i },
    { id: 'fouls', display: 'Fouls', pattern: /^faulturi$|^fouls$/i },
    { id: 'yellowCards', display: 'Yellow cards', pattern: /cartona[sș]e\s*galbene|yellow\s*cards/i },
];

const NUMBER = /-?\d+(?:[.,]\d+)?/g;

export function parseStatsPaste(raw: string): ParsedStats {
    const lines = (raw ?? '')
        // A tab-separated row is one row; splitting on tabs first makes it look like the line-per-value
        // shape everything else already handles.
        .replace(/\t/g, '\n')
        .split(/\r?\n/)
        .map((line) => line.trim())
        .filter((line) => line !== '');

    const found = new Map<StatId, StatRow>();

    // Walking in document order and remembering how far the last stat consumed is what tells the two
    // ambiguous layouts apart. Both put numbers either side of a label; the difference is whether the
    // number above belongs to this label or is the previous stat's away value, already spoken for.
    let consumedUpTo = -1;

    for (let i = 0; i < lines.length; i++) {
        const definition = STATS.find((stat) => stat.pattern.test(lines[i]));
        if (!definition || found.has(definition.id)) continue;

        const match = readPair(lines, i, definition.pattern, consumedUpTo);
        if (match) {
            found.set(definition.id, {
                id: definition.id,
                label: definition.display,
                home: match.home,
                away: match.away,
            });
            consumedUpTo = match.lastLineUsed;
        }
    }

    const xg = found.get('xg');
    const score = readScore(lines);

    const result: ParsedStats = {
        matchLabel: readTeams(lines, score?.index ?? null),
        minute: readMinute(lines, score?.index ?? null),
        goalsHome: score?.home ?? null,
        goalsAway: score?.away ?? null,
        xgHome: xg ? toNumber(xg.home) : null,
        xgAway: xg ? toNumber(xg.away) : null,
        extras: [...found.values()].filter((row) => row.id !== 'xg'),
        missing: [],
    };

    result.missing = REQUIRED.filter((field) => result[field] === null);
    return result;
}

/**
 * Finds the two numbers belonging to a stat label, in whichever of the three shapes a copy produces:
 * all on one line, the label sandwiched between its values, or the label followed by both values.
 */
function readPair(
    lines: string[],
    index: number,
    label: RegExp,
    consumedUpTo: number,
): { home: string; away: string; lastLineUsed: number } | null {
    const line = lines[index];

    // "68%  Posesia mingii  32%" — both values share the label's line.
    const onLine = numbersIn(line.replace(label, ' '));
    if (onLine.length >= 2) {
        return { home: onLine[0], away: onLine[onLine.length - 1], lastLineUsed: index };
    }

    const above = index > 0 ? soleNumber(lines[index - 1]) : null;
    const below = index + 1 < lines.length ? soleNumber(lines[index + 1]) : null;

    // "2.38" / "Goluri estimate (xG)" / "0.30" — the label sits between its values. The line above
    // only counts if no earlier stat has already claimed it.
    if (above !== null && below !== null && index - 1 > consumedUpTo) {
        return { home: above, away: below, lastLineUsed: index + 1 };
    }

    // "Goluri estimate (xG)" / "2.38  0.30" — label first, both values on the next line.
    if (index + 1 < lines.length) {
        const next = numbersIn(lines[index + 1]);
        if (next.length >= 2) {
            return { home: next[0], away: next[next.length - 1], lastLineUsed: index + 1 };
        }
    }

    // "Goluri estimate (xG)" / "2.38" / "0.30" — label first, then one value per line.
    if (below !== null && index + 2 < lines.length) {
        const third = soleNumber(lines[index + 2]);
        if (third !== null) {
            return { home: below, away: third, lastLineUsed: index + 2 };
        }
    }

    return null;
}

/**
 * The clock.
 *
 * Which number on the page is the clock is the whole problem once a whole page gets pasted rather
 * than just the stats panel. A kickoff time, a goal in the timeline and a fixture list all look
 * like minutes, and reading the wrong one is worse than reading none: every projection downstream
 * would be computed against a match that is somewhere else entirely.
 *
 * So the score anchors it. Every live-score site puts the running clock within a line or two of the
 * scoreline, and nothing else on the page sits there — which settles the question without having to
 * recognise the rest of the page at all. Only with no score does it fall back to taking the first
 * clock-shaped thing in the text.
 */
function readMinute(lines: string[], scoreIndex: number | null): number | null {
    const at = (index: number) =>
        index >= 0 && index < lines.length
            ? minuteOn(lines[index], index > 0 ? lines[index - 1] : '')
            : null;

    if (scoreIndex !== null) {
        // Outwards from the score, nearest line first.
        for (const offset of [1, -1, 2, -2, 3, -3]) {
            const minute = at(scoreIndex + offset);
            if (minute !== null) return minute;
        }
    }

    for (let i = 0; i < lines.length; i++) {
        const minute = at(i);
        if (minute !== null) return minute;
    }

    return null;
}

/** Accepts "35:57", "36'", "45+2", "HT" and "FT". `previous` is only read to rule a line out. */
function minuteOn(line: string, previous: string): number | null {
    // "35:57" — a running clock, so the 36th minute is the one in progress.
    const clock = line.match(/\b(\d{1,3}):([0-5]\d)\b/);
    if (clock && !isKickoffTime(line, previous)) {
        const minutes = Number(clock[1]);
        const seconds = Number(clock[2]);
        return clamp(seconds > 0 ? minutes + 1 : minutes);
    }

    // "45+2", with or without the prime.
    const stoppage = line.match(/\b(\d{1,3})\s*\+\s*(\d{1,2})\s*'?/);
    if (stoppage) {
        return clamp(Number(stoppage[1]) + Number(stoppage[2]));
    }

    // "36'"
    const prime = line.match(/\b(\d{1,3})\s*'/);
    if (prime) {
        return clamp(Number(prime[1]));
    }

    // Exact matches only. A live page carries the word "Final" as a tab label beside "Live", and
    // "Rezultat final" is the name of a market — neither one means the match is over.
    const word = line.trim();
    if (/^(ht|half[- ]?time|pauz[aă])$/i.test(word)) return 45;
    if (/^(ft|full[- ]?time|final)$/i.test(word)) return 90;

    return null;
}

/**
 * "11.10.2026" / "22:30" is when the match starts, not where it is. Read as a clock it would claim
 * the 23rd minute, and every xG rate on the page would then be projected over the wrong hour.
 */
function isKickoffTime(line: string, previous: string): boolean {
    const date = /\d{1,2}[./-]\d{1,2}[./-]\d{2,4}/;
    return (
        date.test(line) ||
        date.test(previous) ||
        /^(dat[aă]|date|or[aă]|time|kick\s*-?\s*off|start)\b/i.test(previous.trim())
    );
}

/** The score, and where it was found — the position is what anchors the clock and the team names. */
interface Score {
    home: number;
    away: number;
    index: number;
}

/** "3 - 1", "3-1" and "3 : 1" all count; a clock like "35:57" must not. */
function readScore(lines: string[]): Score | null {
    for (let i = 0; i < lines.length; i++) {
        const dash = lines[i].match(/(?<!\d)(\d{1,2})\s*[-–]\s*(\d{1,2})(?!\d)/);
        if (dash) {
            return { home: Number(dash[1]), away: Number(dash[2]), index: i };
        }

        // A colon score has to be told apart from a clock: "3 : 1" is a score, "35:57" is not.
        const colon = lines[i].match(/(?<![\d:])(\d{1,2})\s+:\s+(\d{1,2})(?![\d:])/);
        if (colon) {
            return { home: Number(colon[1]), away: Number(colon[2]), index: i };
        }
    }

    return null;
}

/**
 * Team names, when the paste includes the header.
 *
 * Two shapes, and nothing else is attempted — guessing which of a few hundred stray lines on a full
 * page are club names would produce confident nonsense, and this is the one field where confident
 * nonsense is cheap to spot and tempting to ship.
 *
 * The second shape is what a whole Sofascore page gives: the header is stacked vertically, with the
 * full club name and its abbreviation on either side of the score and the clock in between. So the
 * run of name-shaped lines next to the score is walked outwards, and the longest of each run is
 * kept — between "Arouca" and "FC Arouca", the full name is the one worth showing.
 */
function readTeams(lines: string[], scoreIndex: number | null): string | null {
    // "Barcelona 3 - 1 Racing Club" — one line, which is what a header copies as on most sites.
    for (const line of lines) {
        const match = line.match(/^(.+?)\s+\d{1,2}\s*[-–]\s*\d{1,2}\s+(.+?)$/);
        if (match) {
            const home = match[1].trim();
            const away = match[2].trim();
            if (home && away && !/\d/.test(home) && !/\d/.test(away)) {
                return `${home} - ${away}`;
            }
        }
    }

    if (scoreIndex === null) return null;

    const home = nameBeside(lines, scoreIndex - 1, -1);
    const away = nameBeside(lines, scoreIndex + 1, +1);
    return home && away ? `${home} - ${away}` : null;
}

/** How far from the score a club name is still allowed to be, in lines. */
const HEADER_REACH = 4;

/** Walks away from the score over the lines that could be a club name and keeps the longest. */
function nameBeside(lines: string[], start: number, step: number): string | null {
    let longest: string | null = null;

    for (let i = start, seen = 0; i >= 0 && i < lines.length && seen < HEADER_REACH; i += step, seen++) {
        const line = lines[i];

        // The clock sits between the score and the away team, so it is stepped over, not stopped at.
        if (/^\d{1,3}:[0-5]\d$/.test(line) || /^\d{1,3}\s*\+?\s*\d*\s*'$/.test(line)) continue;
        if (!isTeamName(line)) break;

        if (longest === null || line.length > longest.length) longest = line;
    }

    return longest;
}

/** Words that sit next to a scoreline on a live page and are not anybody's name. */
const NOT_A_TEAM = /^(logo|live|[iî]n\s*direct|final|scor|score|vs\.?|v|ht|ft|aet|pen)$/i;

function isTeamName(line: string): boolean {
    return (
        line.length >= 2 &&
        line.length <= 40 &&
        /\p{L}/u.test(line) &&
        !/\d/.test(line) &&
        !NOT_A_TEAM.test(line) &&
        // A stats panel pasted without its header has rows either side of a number too. Naming the
        // match "Cornere - Faulturi" would then follow a saved position around for good.
        !STATS.some((stat) => stat.pattern.test(line))
    );
}

function numbersIn(text: string): string[] {
    return text.match(NUMBER) ?? [];
}

/** The value of a line that is nothing but a number, so a stray label cannot be read as one. */
function soleNumber(line: string): string | null {
    const stripped = line.replace(/%/g, '').trim();
    return /^-?\d+(?:[.,]\d+)?$/.test(stripped) ? stripped : null;
}

function toNumber(raw: string): number | null {
    const value = Number(raw.replace(/%/g, '').replace(',', '.').trim());
    return Number.isFinite(value) ? value : null;
}

const clamp = (minute: number) => (minute >= 1 && minute <= 130 ? minute : null);
