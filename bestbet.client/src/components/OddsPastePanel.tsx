import { useMemo, useState } from 'react';
import type { ClipboardEvent } from 'react';
import { marketKindConfig } from '../lib/forms';
import type { OddsFormState } from '../lib/forms';
import { readPastedHtml } from '../lib/htmlPaste';
import { parseOddsPaste, siblingsOf } from '../lib/oddsPaste';
import { describeIssue, marketTitle, useT } from '../lib/i18n';
import type { ParsedMarket } from '../lib/oddsPaste';
import { odds as formatOdds } from '../lib/format';
import type { MarketSelection } from '../types/domain';

interface Props {
    value: OddsFormState;
    onApply: (next: OddsFormState) => void;
}

/**
 * Import a market by pasting a bookmaker's odds block.
 *
 * This is the half the stats import cannot cover: the prices come off a bookmaker screen, not a stats
 * provider. The parser is written against the WORDING of the outcomes rather than any one site's
 * markup — Sub/Peste, 1/X/2, Da/Nu next to a decimal price — so it survives a redesign and works on
 * whichever book the user happens to have open.
 *
 * Which side the stake goes on stays the user's decision: the panel lists what it found and waits.
 */
export function OddsPastePanel({ value, onApply }: Props) {
    const t = useT();
    const [open, setOpen] = useState(false);
    const [text, setText] = useState('');

    const parsed = useMemo(() => (text.trim() === '' ? null : parseOddsPaste(text)), [text]);

    /**
     * A copy from a web page carries both a flattened `text/plain` and the original `text/html`. The
     * markup still says which price belonged to which row, so it is worth preferring — a grid pasted
     * as plain text often arrives as one run of numbers with no way to pair them up.
     */
    const handlePaste = (event: ClipboardEvent<HTMLTextAreaElement>) => {
        const fromHtml = readPastedHtml(event.clipboardData);
        if (!fromHtml) return;

        event.preventDefault();
        setText(fromHtml);
    };

    const use = (market: ParsedMarket, selection: MarketSelection) => {
        const backing = market.outcomes.find((outcome) => outcome.selection === selection);
        if (!backing) return;

        const config = marketKindConfig(market.kind);

        onApply({
            ...value,
            kind: market.kind,
            selection,
            label: backing.label,
            line: config.needsLine && market.line !== null ? String(market.line) : '',
            decimalOdds: String(backing.odds),
            siblingOdds: siblingsOf(market, selection).map(String),
        });
        setText('');
    };

    if (!open) {
        return (
            <section className="card card-compact">
                <button type="button" className="link-button link-button-strong" onClick={() => setOpen(true)}>
                    {t.oddsPaste.open}
                </button>
                <p className="note note-tight">{t.oddsPaste.openNote}</p>
            </section>
        );
    }

    return (
        <section className="card">
            <div className="card-head">
                <h2>{t.oddsPaste.title}</h2>
                <button type="button" className="link-button" onClick={() => setOpen(false)}>
                    {t.oddsPaste.hide}
                </button>
            </div>

            <p className="note">{t.oddsPaste.note}</p>

            <textarea
                className="json-input"
                rows={7}
                spellCheck={false}
                placeholder={'Goluri în meci\n6.5\nPeste  1.91\nSub    1.91'}
                value={text}
                onPaste={handlePaste}
                onChange={(e) => setText(e.target.value)}
            />

            {parsed && parsed.markets.length === 0 && (
                <p className="error error-tight">{t.oddsPaste.noMarket}</p>
            )}

            {parsed && parsed.markets.length > 0 && (
                <div className="parse-preview">
                    <h3>{t.oddsPaste.pickSide}</h3>
                    {parsed.markets.map((market, index) => (
                        <div className="market-found" key={index}>
                            <div className="market-found-title">
                                {marketTitle(t, market.kind, market.line)}
                            </div>
                            <div className="market-found-outcomes">
                                {market.outcomes.map((outcome) => (
                                    <button
                                        type="button"
                                        key={outcome.selection}
                                        className="outcome-button"
                                        onClick={() => use(market, outcome.selection)}
                                    >
                                        <span className="outcome-label">{outcome.label}</span>
                                        <span className="outcome-odds">{formatOdds(outcome.odds)}</span>
                                    </button>
                                ))}
                            </div>
                        </div>
                    ))}
                </div>
            )}

            {parsed && parsed.issues.length > 0 && (
                <ul className="paste-notes">
                    {parsed.issues.map((issue, index) => (
                        <li key={index}>{describeIssue(t, issue)}</li>
                    ))}
                </ul>
            )}

            <div className="card-actions card-actions-tight">
                <button type="button" onClick={() => setText('')} disabled={text === ''}>
                    {t.oddsPaste.clear}
                </button>
                <span className="hint">{t.oddsPaste.hint}</span>
            </div>
        </section>
    );
}
