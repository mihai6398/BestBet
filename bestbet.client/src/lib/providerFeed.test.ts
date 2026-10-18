import { describe, expect, it } from 'vitest';
import { missingXg, parseMatchRef, readingToParsedStats } from './providerFeed';
import type { ProviderReading } from '../types/domain';

/**
 * Reading what the user pasted.
 *
 * These are the same inputs MatchReferenceTests.cs runs against the server's parser. The two have to
 * agree about what is acceptable — this one decides whether the button is live, that one decides
 * whether anything is fetched — and a drift between them is a field that accepts a link the server
 * then refuses.
 */
describe('what a pasted link or id points at', () => {
    it('takes a bare number as a FotMob id', () => {
        expect(parseMatchRef('5881191')).toEqual({ provider: 'fotmob', id: '5881191', teams: null });
    });

    it('takes a long live-only id too', () => {
        expect(parseMatchRef('1000022162')?.id).toBe('1000022162');
    });

    it('reads the id out of a short FotMob link', () => {
        expect(parseMatchRef('https://www.fotmob.com/match/5881191')?.id).toBe('5881191');
    });

    it('reads the id out of a short FotMob link that carries the slug after it', () => {
        expect(
            parseMatchRef('https://www.fotmob.com/match/5881191/eintracht-frankfurt-vs-1-fc-koln')?.id,
        ).toBe('5881191');
    });

    it('reads the id out of the fragment of a long FotMob link', () => {
        expect(
            parseMatchRef('https://www.fotmob.com/matches/eintracht-frankfurt-vs-1-fc-koln/2mn1g7#5881191')
                ?.id,
        ).toBe('5881191');
    });

    it('reads the id out of a fragment carrying more than the id', () => {
        expect(parseMatchRef('https://www.fotmob.com/matches/frankfurt-vs-koln/2mn1g7#5881191:tab:stats')?.id)
            .toBe('5881191');
    });

    it('ignores a tracking query glued onto a share link', () => {
        expect(parseMatchRef('https://www.fotmob.com/match/5881191?utm_source=share')?.id).toBe('5881191');
    });

    it('accepts a link copied without its scheme', () => {
        expect(parseMatchRef('www.fotmob.com/match/5881191')?.id).toBe('5881191');
    });

    it('keeps the team names from a Sofascore link and drops their id', () => {
        // Their id means nothing to anyone else. Handing that number to FotMob would read a real
        // clock off a real match that is not the one being bet on, and nothing downstream could tell.
        expect(parseMatchRef('https://www.sofascore.com/football/match/arouca-porto/IdbsNcb#id:12345678'))
            .toEqual({ provider: 'sofascore', id: null, teams: 'arouca porto' });
    });

    it('keeps a multi-word slug whole', () => {
        // There is no honest split point: the slug joins the two names with the same hyphen it uses
        // inside them. The name matching works on a bag of words, so it does not need one.
        expect(parseMatchRef('https://www.sofascore.com/football/match/estrela-amadora-arouca/pQbsCpb')
            ?.teams).toBe('estrela amadora arouca');
    });

    it('still reads an older Sofascore link without the sport in it', () => {
        expect(parseMatchRef('https://www.sofascore.com/arouca-porto/IdbsNcb')?.provider).toBe('sofascore');
    });

    it('refuses what is not a match link rather than guessing', () => {
        expect(parseMatchRef(null)).toBeNull();
        expect(parseMatchRef('')).toBeNull();
        expect(parseMatchRef('   ')).toBeNull();
        expect(parseMatchRef('Arouca - Porto')).toBeNull();
        expect(parseMatchRef('https://www.flashscore.ro/meci/abc123/')).toBeNull();
        expect(parseMatchRef('https://www.fotmob.com/leagues/54/overview/bundesliga')).toBeNull();
    });

    it('refuses a number too short to be a match id', () => {
        // Far likelier to be a minute or a score typed into the wrong box than a fixture.
        expect(parseMatchRef('63')).toBeNull();
        expect(parseMatchRef('1234')).toBeNull();
    });
});

describe('a fetched reading in the shape a pasted one has', () => {
    const reading = (over: Partial<ProviderReading> = {}): ProviderReading => ({
        provider: 'FotMob',
        matchId: '5881191',
        url: 'https://www.fotmob.com/match/5881191',
        matchLabel: 'Eintracht Frankfurt - 1. FC Köln',
        minute: 63,
        goalsHome: 1,
        goalsAway: 0,
        xgHome: 1.34,
        xgAway: 0.65,
        extras: [{ id: 'shotsOnTarget', label: 'Shots on target', home: '7', away: '3' }],
        missing: [],
        ongoing: true,
        finished: false,
        readUtc: '2026-10-17T21:18:00Z',
        ageSeconds: 0,
        ...over,
    });

    it('carries the fields the model needs straight across', () => {
        const parsed = readingToParsedStats(reading());

        expect(parsed.minute).toBe(63);
        expect(parsed.xgHome).toBe(1.34);
        expect(parsed.goalsAway).toBe(0);
        expect(parsed.matchLabel).toBe('Eintracht Frankfurt - 1. FC Köln');
        expect(parsed.missing).toEqual([]);
        expect(parsed.extras[0].id).toBe('shotsOnTarget');
    });

    it('reports a league without xG as missing it rather than as a zero', () => {
        // The whole reason the bookmarklet stays. A zero here would be a real stake sized on a
        // number nobody measured.
        const parsed = readingToParsedStats(reading({ xgHome: null, xgAway: null, missing: ['xgHome', 'xgAway'] }));

        expect(parsed.xgHome).toBeNull();
        expect(parsed.missing).toEqual(['xgHome', 'xgAway']);
    });

    it('tells a missing xG apart from any other kind of failure', () => {
        // It is the one gap that is nobody's fault and has a specific answer, so the panel has to
        // say "this league has no xG here" rather than "the match could not be read".
        expect(missingXg(reading())).toBe(false);
        expect(missingXg(reading({ missing: ['xgHome', 'xgAway'] }))).toBe(true);
        expect(missingXg(reading({ missing: ['minute', 'xgHome', 'xgAway'] }))).toBe(true);
        expect(missingXg(reading({ missing: ['minute'] }))).toBe(false);
    });
});
