/**
 * Watching a match by pasting its link, rather than by leaving a tab open.
 *
 * This is the route the bookmarklet in `liveFeed.ts` had to exist instead of. Sofascore answers a
 * programmatic request with 403 and always will, so for them the reading can only come from a
 * browser that is already allowed. FotMob answers a plain request, with no key and nothing
 * pretending to be anything — so for FotMob the server can simply ask, and the user pastes a link
 * once instead of dragging a bookmarklet and babysitting a tab.
 *
 * A Sofascore link is still accepted here. Not by asking Sofascore anything: their URL gives up the
 * team names in its slug, and that is enough to find the same match at FotMob by name.
 *
 * The bookmarklet is not retired and must not be. FotMob computes xG for the big leagues and not
 * for the small ones, and xG is the field this app's model is built on — so a match it cannot
 * supply comes back saying so, and the only route left for that match is the one that reads the
 * page the user is looking at.
 */

import type { MissingField, ParsedStats, StatId, StatRow } from './statsPaste';
import type { ProviderReading } from '../types/domain';

/** What a pasted link or id points at, as far as the browser can tell before asking the server. */
export interface MatchRef {
    provider: 'fotmob' | 'sofascore';
    /** The FotMob match id, when the input already carried one. */
    id: string | null;
    /** Team names out of a Sofascore slug, for a match that has to be found by name. */
    teams: string | null;
}

/**
 * Reads a match id, a FotMob link or a Sofascore link.
 *
 * The server parses the input again and is the one whose answer counts — this exists so the field
 * can say "that is not a match link" while the user is still looking at what they pasted, instead
 * of after a round trip. The two have to agree about what is acceptable, so this is a deliberate
 * mirror of `MatchReference.Parse` in `BestBet.Server/Services/MatchReference.cs` and the tests on
 * both sides cover the same inputs.
 */
export function parseMatchRef(input: string | null | undefined): MatchRef | null {
    const trimmed = (input ?? '').trim();
    if (trimmed === '') return null;

    // A bare id. Up to twelve digits because FotMob's live-only ids run to ten (1000022162), at
    // least five because an ordinary fixture is seven (5881191) and anything shorter is far likelier
    // to be a minute or a score pasted into the wrong box.
    if (/^\d{5,12}$/.test(trimmed)) {
        return { provider: 'fotmob', id: trimmed, teams: null };
    }

    let url: URL;
    try {
        // A link copied without its scheme is still a link, and users copy them that way.
        url = new URL(trimmed.includes('://') ? trimmed : `https://${trimmed}`);
    } catch {
        return null;
    }

    const host = url.hostname.replace(/^www\./i, '').toLowerCase();

    if (host === 'fotmob.com') return readFotMob(url);
    if (host === 'sofascore.com') return readSofascore(url);

    return null;
}

/** The id is either the last path segment or the fragment, depending on which URL style was copied. */
function readFotMob(url: URL): MatchRef | null {
    // "#5881191", and sometimes "#5881191:tab:stats" — the fragment carries more than the id.
    const fragment = url.hash.replace(/^#/, '').match(/^(\d{5,12})(?!\d)/);
    if (fragment) {
        return { provider: 'fotmob', id: fragment[1], teams: null };
    }

    // "/match/5881191" or "/match/5881191/eintracht-frankfurt-vs-1-fc-koln".
    const segment = segments(url).find((part) => /^\d{5,12}$/.test(part));
    return segment ? { provider: 'fotmob', id: segment, teams: null } : null;
}

/**
 * A Sofascore link, read for the one thing in it that means anything elsewhere: the team names.
 *
 * Their own id is in the URL too and is deliberately ignored — it only means something to an API
 * that will not answer us, and handing that number to FotMob would read a real clock off a real
 * match that is not the one being bet on.
 */
function readSofascore(url: URL): MatchRef | null {
    const parts = segments(url);
    const after = parts.findIndex((part) => part.toLowerCase() === 'match');

    const slug =
        after >= 0 && isTeamSlug(parts[after + 1] ?? '')
            ? parts[after + 1]
            : parts.find(isTeamSlug);

    return slug ? { provider: 'sofascore', id: null, teams: slug.replace(/-/g, ' ') } : null;
}

/** Whether a segment looks like "arouca-porto" rather than a sport, an id or a URL hash. */
function isTeamSlug(segment: string): boolean {
    return segment.includes('-') && segment.length >= 5 && !/\d/.test(segment);
}

function segments(url: URL): string[] {
    return url.pathname.split('/').filter((part) => part !== '');
}

/**
 * A fetched reading, in the shape a pasted one already has.
 *
 * The whole point of the server mapping FotMob's response itself is that the two routes become
 * indistinguishable from here on: the form, the re-pricing and the staleness warning all work on
 * `ParsedStats` and never learn which way a reading arrived. So this is a rename and nothing else —
 * if it ever needs to compute something, that belongs on the server beside the mapping it came from.
 */
export function readingToParsedStats(reading: ProviderReading): ParsedStats {
    return {
        matchLabel: reading.matchLabel,
        minute: reading.minute,
        goalsHome: reading.goalsHome,
        goalsAway: reading.goalsAway,
        xgHome: reading.xgHome,
        xgAway: reading.xgAway,
        extras: reading.extras.map((row): StatRow => ({
            id: row.id as StatId,
            label: row.label,
            home: row.home,
            away: row.away,
        })),
        missing: reading.missing as MissingField[],
    };
}

/**
 * Whether the match was found but cannot be analysed here.
 *
 * Worth telling apart from any other failure, because it is the one that is nobody's fault and has
 * a specific answer: the provider does not compute xG for this league, so this match needs the
 * bookmarklet or a manual paste. Said wrongly it reads as a bug; said as "no data" it reads as
 * something to retry.
 */
export function missingXg(reading: ProviderReading): boolean {
    return reading.missing.includes('xgHome') || reading.missing.includes('xgAway');
}
