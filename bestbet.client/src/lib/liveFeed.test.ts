import { describe, expect, it } from 'vitest';
import { bookmarkletFor, sameMatch } from './liveFeed';

describe('which open bets a reading may re-price', () => {
    it('matches a label against itself', () => {
        expect(sameMatch('FC Arouca - CF Estrela Amadora', 'FC Arouca - CF Estrela Amadora')).toBe(true);
    });

    it('matches a shortened label against the full one', () => {
        expect(sameMatch('Arouca - Estrela Amadora', 'FC Arouca - CF Estrela Amadora')).toBe(true);
    });

    it('ignores diacritics and punctuation', () => {
        expect(sameMatch('Atlético Madrid – Betis', 'Atletico Madrid - Betis')).toBe(true);
    });

    it('refuses two different matches', () => {
        // The one that matters. Priced against another match's clock, a saved bet gets a confident
        // number about a game it has nothing to do with.
        expect(sameMatch('Arsenal - Chelsea', 'FC Arouca - CF Estrela Amadora')).toBe(false);
    });

    it('refuses two matches that merely share one club', () => {
        expect(sameMatch('Arsenal - Chelsea', 'Arsenal - Tottenham')).toBe(false);
    });

    it('does not match on the filler half of a club name', () => {
        // "FC ... City" would otherwise pair up half the fixtures in Europe.
        expect(sameMatch('FC Porto - Sporting CP', 'FC Basel - Sporting Lisboa')).toBe(false);
    });

    it('accepts a single distinctive word when that is all one side has', () => {
        expect(sameMatch('Arouca', 'FC Arouca - CF Estrela Amadora')).toBe(true);
    });

    it('says no when there is nothing to compare', () => {
        expect(sameMatch('', 'Arsenal - Chelsea')).toBe(false);
        expect(sameMatch(null, null)).toBe(false);
        expect(sameMatch('FC - CF', 'FC Arouca - CF Estrela Amadora')).toBe(false);
    });
});

describe('the bookmarklet', () => {
    const made = bookmarkletFor('http://localhost:8080', 'abc123', 20);
    const source = decodeURIComponent(made.replace(/^javascript:/, ''));

    it('is a javascript URL, encoded so a bookmark cannot lose half of it', () => {
        expect(made.startsWith('javascript:')).toBe(true);
        expect(made).not.toContain(' ');
    });

    it('posts to this app rather than to a hard-coded port', () => {
        // The same bookmarklet has to work on the dev server and in the container.
        expect(source).toContain('"http://localhost:8080/api/live-feed"');
    });

    it('carries the token, which is the only thing guarding the endpoint', () => {
        expect(source).toContain('"abc123"');
    });

    it('sends the page as text, capped', () => {
        expect(source).toContain('document.body.innerText');
        expect(source).toContain('slice(0,180000)');
    });

    it('uses a content type that does not need a preflight', () => {
        // The pushing page is one we do not control and cannot debug from, so the request is kept
        // to the shape a browser sends without asking first.
        expect(source).toContain("'Content-Type':'text/plain'");
    });

    it('pushes on the interval it was given, after an immediate first reading', () => {
        expect(source).toContain('ms=20000');
        expect(source).toContain('setInterval(send,ms)');
        expect(source).toContain('send();window');
    });

    it('turns itself off when pressed again', () => {
        // A bookmark has no other off switch, and a timer left on a forgotten tab would keep
        // pushing a match that finished an hour ago.
        expect(source).toContain('clearInterval');
    });

    it('never sends credentials with it', () => {
        expect(source).toContain("credentials:'omit'");
    });
});
