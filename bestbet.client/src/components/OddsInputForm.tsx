import { MARKET_KINDS } from '../types/domain';
import type { MarketKind, MarketSelection } from '../types/domain';
import { marketKindConfig, defaultOddsForm } from '../lib/forms';
import type { OddsFormState } from '../lib/forms';
import { useT } from '../lib/i18n';

interface Props {
    value: OddsFormState;
    onChange: (next: OddsFormState) => void;
}

export function OddsInputForm({ value, onChange }: Props) {
    const t = useT();
    const config = marketKindConfig(value.kind);

    const changeKind = (kind: MarketKind) => {
        // The selection list and the number of sibling prices both depend on the kind, so switching
        // kind has to rebuild them rather than leave a stale selection pointing at the wrong market.
        const next = defaultOddsForm(kind);
        onChange({ ...next, matchLabel: value.matchLabel, decimalOdds: value.decimalOdds });
    };

    const changeSibling = (index: number, odds: string) => {
        const siblingOdds = [...value.siblingOdds];
        siblingOdds[index] = odds;
        onChange({ ...value, siblingOdds });
    };

    return (
        <section className="card">
            <h2>{t.market.title}</h2>

            <label className="field">
                <span>{t.market.match}</span>
                <input
                    type="text"
                    placeholder={t.market.matchPlaceholder}
                    value={value.matchLabel}
                    onChange={(e) => onChange({ ...value, matchLabel: e.target.value })}
                />
            </label>

            <div className="field-row">
                <label className="field">
                    <span>{t.market.kind}</span>
                    <select value={value.kind} onChange={(e) => changeKind(e.target.value as MarketKind)}>
                        {MARKET_KINDS.map((m) => (
                            <option key={m.kind} value={m.kind}>
                                {t.marketKind[m.kind]}
                            </option>
                        ))}
                    </select>
                </label>

                <label className="field">
                    <span>{t.market.side}</span>
                    <select
                        value={value.selection}
                        onChange={(e) => onChange({ ...value, selection: e.target.value as MarketSelection })}
                    >
                        {config.selections.map((s) => (
                            <option key={s.value} value={s.value}>
                                {t.selection[s.value]}
                            </option>
                        ))}
                    </select>
                </label>

                {config.needsLine && (
                    <label className="field field-narrow">
                        <span>{t.market.line}</span>
                        <input
                            type="number"
                            step="0.25"
                            placeholder="2.5"
                            value={value.line}
                            onChange={(e) => onChange({ ...value, line: e.target.value })}
                        />
                    </label>
                )}
            </div>

            <div className="field-row">
                <label className="field">
                    <span>
                        {t.market.bookLabel} <em className="muted">{t.market.optional}</em>
                    </span>
                    <input
                        type="text"
                        placeholder={t.market.bookLabelPlaceholder}
                        value={value.label}
                        onChange={(e) => onChange({ ...value, label: e.target.value })}
                    />
                </label>

                <label className="field field-narrow">
                    <span>{t.market.decimalOdds}</span>
                    <input
                        type="number"
                        step="0.01"
                        min="1.01"
                        placeholder="1.57"
                        value={value.decimalOdds}
                        onChange={(e) => onChange({ ...value, decimalOdds: e.target.value })}
                    />
                </label>
            </div>

            <fieldset className="subgroup">
                <legend>
                    {t.market.otherPrices}
                    <span className="hint"> — {t.marketKindHint[value.kind]}</span>
                </legend>
                <p className="note">{t.market.note}</p>
                <div className="field-row">
                    {value.siblingOdds.map((odds, index) => (
                        <label className="field field-narrow" key={index}>
                            <span>{t.market.price(index + 2)}</span>
                            <input
                                type="number"
                                step="0.01"
                                min="1.01"
                                placeholder="1.66"
                                value={odds}
                                onChange={(e) => changeSibling(index, e.target.value)}
                            />
                        </label>
                    ))}
                </div>
            </fieldset>
        </section>
    );
}
