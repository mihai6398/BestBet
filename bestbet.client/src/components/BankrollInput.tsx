import { useT } from '../lib/i18n';

interface Props {
    value: number;
    onChange: (next: number) => void;
}

export function BankrollInput({ value, onChange }: Props) {
    const t = useT();

    return (
        <section className="card card-compact">
            <label className="field field-narrow">
                <span>{t.bankroll.label}</span>
                <input
                    type="number"
                    step="1"
                    min="0"
                    value={Number.isFinite(value) ? value : 0}
                    onChange={(e) => onChange(Number(e.target.value))}
                />
            </label>
            <p className="note">{t.bankroll.note}</p>
        </section>
    );
}
