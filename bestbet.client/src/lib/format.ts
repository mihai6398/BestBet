/**
 * Shared number formatting, so the same quantity never appears two different ways on one screen.
 *
 * Odds and percentages keep a dot, in every language: that is how a price is written on a betting
 * slip in Bucharest as much as in London, and "cota 2,30" would read as a thousands separator to
 * half the people looking at it. Money and dates are the opposite — those follow the language the
 * interface is in, which is why they take a locale rather than trusting the browser's own. Left to
 * the browser, a machine set to Russian prints Romanian dates in Russian.
 */

export const percent = (fraction: number, digits = 1) => `${(fraction * 100).toFixed(digits)}%`;

export const percentagePoints = (pp: number, digits = 1) =>
    `${pp >= 0 ? '+' : ''}${pp.toFixed(digits)}pp`;

export const signedPercent = (fraction: number, digits = 1) =>
    `${fraction >= 0 ? '+' : ''}${(fraction * 100).toFixed(digits)}%`;

export const odds = (value: number) => value.toFixed(2);

export const money = (value: number, locale?: string) =>
    value.toLocaleString(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** A profit or loss: a plus on a gain, so a result reads at a glance without a colour. */
export const signedMoney = (value: number, locale?: string) => `${value > 0 ? '+' : ''}${money(value, locale)}`;

/** Whole dollars, for a traded volume where the cents are noise and the magnitude is the point. */
export const dollars = (value: number, locale?: string) =>
    `$${Math.round(value).toLocaleString(locale, { maximumFractionDigits: 0 })}`;

export const dateTime = (iso: string, locale?: string) => {
    // The server stores UTC without a zone suffix in some serialisers; make the intent explicit.
    const withZone = iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`;
    return new Date(withZone).toLocaleString(locale, {
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
    });
};
