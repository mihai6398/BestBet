import { useState } from 'react';
import { analysisClient, ApiError } from '../api/analysisClient';
import { dollars, odds as formatOdds, percent } from '../lib/format';
import type { ReferenceState } from '../lib/forms';
import { useLocale, useT } from '../lib/i18n';
import type { PredictionMarket, PredictionOutcome } from '../types/domain';

interface Props {
    /** Seeds the search box, because the match is usually already typed into the form. */
    matchLabel: string;
    value: ReferenceState | null;
    onChange: (next: ReferenceState | null) => void;
}

/**
 * A second opinion on the fair price, taken off a prediction market.
 *
 * The reason this is worth a panel of its own: a bookmaker's de-vigged line and the price you can
 * get are the same numbers doing two different jobs, and a book that is 5–15% wide is bad at the
 * first while being the only option for the second. Polymarket trades probabilities directly, so
 * its prices add up to about 100% and there is no margin to remove — which makes its line a better
 * yardstick than the one being bet into, and a worse place to put a stake, because you cannot.
 *
 * Choosing which outcome corresponds to the bet stays the user's: nothing here can tell whether a
 * bookmaker's "Peste 2.5" and a Polymarket question mean the same market, and the failure mode of
 * guessing wrong is an edge that points the wrong way.
 */
export function ReferenceLinePanel({ matchLabel, value, onChange }: Props) {
    const t = useT();
    const locale = useLocale();

    const [open, setOpen] = useState(false);
    const [query, setQuery] = useState('');
    const [results, setResults] = useState<PredictionMarket[] | null>(null);
    const [searching, setSearching] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const search = async () => {
        const term = (query || matchLabel).trim();
        if (term.length < 3) return;

        setSearching(true);
        setError(null);
        try {
            setResults(await analysisClient.searchPolymarket(term));
        } catch (failure) {
            setResults(null);
            setError(
                failure instanceof ApiError || failure instanceof Error
                    ? `${t.reference.failed} ${failure.message}`
                    : t.reference.failed,
            );
        } finally {
            setSearching(false);
        }
    };

    const use = (market: PredictionMarket, outcome: PredictionOutcome) => {
        onChange({
            source: 'Polymarket',
            label: outcome.label,
            question: market.question,
            decimalOdds: outcome.decimalOdds,
            siblingOdds: market.outcomes
                .filter((other) => other.label !== outcome.label)
                .map((other) => other.decimalOdds),
        });
        setResults(null);
    };

    if (!open) {
        return (
            <section className="card card-compact">
                <button
                    type="button"
                    className="link-button link-button-strong"
                    onClick={() => setOpen(true)}
                >
                    {t.reference.open}
                </button>
                <p className="note note-tight">
                    {value ? t.reference.active(value.source) : t.reference.openNote}
                </p>
            </section>
        );
    }

    return (
        <section className="card">
            <div className="card-head">
                <h2>{t.reference.title}</h2>
                <button type="button" className="link-button" onClick={() => setOpen(false)}>
                    {t.reference.hide}
                </button>
            </div>

            <p className="note">{t.reference.note}</p>

            {value && (
                <div className="reference-active">
                    <div>
                        <strong>{t.reference.active(value.source)}</strong>
                        <div className="cell-sub">{value.question}</div>
                        <div className="cell-sub">
                            {value.label} · {percent(1 / value.decimalOdds)} ·{' '}
                            {formatOdds(value.decimalOdds)}
                        </div>
                    </div>
                    <button type="button" className="link-button" onClick={() => onChange(null)}>
                        {t.reference.remove}
                    </button>
                </div>
            )}

            <div className="field-row">
                <label className="field">
                    <span>{t.reference.searchLabel}</span>
                    <input
                        type="text"
                        placeholder={matchLabel || t.reference.searchPlaceholder}
                        value={query}
                        onChange={(event) => setQuery(event.target.value)}
                        onKeyDown={(event) => {
                            if (event.key === 'Enter') {
                                event.preventDefault();
                                void search();
                            }
                        }}
                    />
                </label>
                <button type="button" className="search-button" onClick={() => void search()} disabled={searching}>
                    {searching ? t.reference.searching : t.reference.search}
                </button>
            </div>

            <p className="hint">{t.reference.privacy}</p>

            {error && (
                <>
                    <p className="error error-tight">{error}</p>
                    <p className="hint">{t.reference.offlineNote}</p>
                </>
            )}

            {results !== null && results.length === 0 && <p className="note">{t.reference.noResults}</p>}

            {results !== null && results.length > 0 && (
                <div className="parse-preview">
                    <h3>{t.reference.pick}</h3>
                    <p className="warning">{t.reference.mismatch}</p>

                    {results.map((market) => (
                        <div className="market-found" key={market.id}>
                            <div className="market-found-title">
                                {market.question}
                                <span className="muted">
                                    {' · '}
                                    {market.eventTitle}
                                    {market.volume !== null && ` · ${t.reference.volume(dollars(market.volume, locale))}`}
                                </span>
                                {market.url && (
                                    <>
                                        {' · '}
                                        <a
                                            className="link-button"
                                            href={market.url}
                                            target="_blank"
                                            rel="noreferrer noopener"
                                        >
                                            {t.reference.openMarket}
                                        </a>
                                    </>
                                )}
                            </div>

                            <div className="market-found-outcomes">
                                {market.outcomes.map((outcome) => (
                                    <button
                                        type="button"
                                        key={outcome.label}
                                        className="outcome-button"
                                        onClick={() => use(market, outcome)}
                                    >
                                        <span className="outcome-label">{outcome.label}</span>
                                        <span className="outcome-odds">{percent(outcome.probability)}</span>
                                        <span className="cell-sub">{formatOdds(outcome.decimalOdds)}</span>
                                    </button>
                                ))}
                            </div>
                        </div>
                    ))}
                </div>
            )}
        </section>
    );
}
