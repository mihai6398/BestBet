/**
 * Watching a match without asking the provider's server for anything.
 *
 * The obvious design — have the backend poll Sofascore every half minute — does not work and cannot
 * be made to work honestly. Sofascore answers a programmatic request with 403, every request, down
 * to robots.txt: Cloudflare rejects anything that is not a real browser. Getting past that means
 * impersonating one, which is against their terms and breaks the first time they tighten it.
 *
 * So the reading comes from the browser that is already allowed: the user's own, on the tab they
 * already have the match open in. A bookmarklet started on that tab reads the page as text every
 * few seconds and posts it to this app. Nothing is requested from Sofascore that the user was not
 * already loading, and nothing pretends to be a browser it is not.
 *
 * The text is parsed here by the same parser a manual paste goes through, so there is one
 * implementation to keep working rather than two that can disagree.
 */

/** How often the bookmarklet pushes. Slow enough to be unremarkable, fast enough to matter. */
export const PUSH_INTERVAL_SECONDS = 20;

/** How often this app asks its own API for the latest reading. Localhost, so this is nearly free. */
export const POLL_INTERVAL_SECONDS = 8;

/** Past this, the reading is old enough that it should not be driving anything. */
export const STALE_AFTER_SECONDS = 150;

/**
 * Builds the bookmarklet.
 *
 * It targets this app's own origin rather than a hard-coded port, which is what makes the same
 * bookmarklet work whether the app is on the Vite dev server or in the container.
 *
 * Pressing it a second time stops the loop — a bookmark has no other way to be turned off, and a
 * timer left running on a tab the user forgot about would keep pushing a finished match.
 */
export function bookmarkletFor(
    origin: string,
    token: string,
    intervalSeconds: number = PUSH_INTERVAL_SECONDS,
): string {
    const source = `(function(){
var u=${JSON.stringify(`${origin}/api/live-feed`)},t=${JSON.stringify(token)},ms=${intervalSeconds * 1000},k='__bestbetMonitor';
if(window[k]){clearInterval(window[k]);window[k]=0;alert('BestBet: monitorizarea s-a oprit.');return;}
var send=function(){try{fetch(u,{method:'POST',mode:'cors',credentials:'omit',headers:{'Content-Type':'text/plain'},body:JSON.stringify({token:t,text:(document.body.innerText||'').slice(0,180000),source:location.href})}).catch(function(){});}catch(e){}};
send();window[k]=setInterval(send,ms);
alert('BestBet: monitorizarea a pornit. Lasa fila deschisa. Apasa din nou semnul ca sa opresti.');
})();`
        .split('\n')
        .map((line) => line.trim())
        .join('');

    // Encoded, because a javascript: URL in a bookmark goes through URL parsing on the way in and
    // an unencoded quote or space is where that silently loses half the script.
    return `javascript:${encodeURIComponent(source)}`;
}

/**
 * Whether a saved position belongs to the match currently being watched.
 *
 * This decides which open bets a new reading is allowed to re-price, and getting it wrong would
 * price one match's bet against another match's clock — so it errs towards saying no. Two labels
 * have to share two real words, unless one of them only has one word to give.
 */
export function sameMatch(a: string | null | undefined, b: string | null | undefined): boolean {
    const left = significantWords(a);
    const right = significantWords(b);

    if (left.length === 0 || right.length === 0) return false;

    const shared = left.filter((word) => right.includes(word)).length;
    const smallest = Math.min(left.length, right.length);

    return shared >= Math.min(2, smallest);
}

/** Club names, with the noise a header carries around them dropped. */
function significantWords(label: string | null | undefined): string[] {
    const words = (label ?? '')
        .toLowerCase()
        .normalize('NFD')
        .replace(/[̀-ͯ]/g, '')
        .split(/[^a-z0-9]+/)
        .filter((word) => word.length >= 3 && !NOISE.has(word));

    return [...new Set(words)];
}

/** Words that appear in half the club names in Europe and tell two matches apart in none of them. */
const NOISE = new Set([
    'fc',
    'cf',
    'afc',
    'cfr',
    'sport',
    'sporting',
    'club',
    'city',
    'town',
    'united',
    'real',
    'athletic',
    'atletico',
    'deportivo',
    'the',
    'vs',
]);
