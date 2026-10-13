import { useT } from '../lib/i18n';
import type { Verdict } from '../types/domain';

const CLASS_NAME: Record<Verdict, string> = {
    Bet: 'verdict verdict-bet',
    Marginal: 'verdict verdict-marginal',
    Skip: 'verdict verdict-skip',
};

export function VerdictBadge({ verdict }: { verdict: Verdict }) {
    const t = useT();
    // The colour is decoration; the word carries the meaning, so it is never the only signal.
    return <span className={CLASS_NAME[verdict]}>{t.verdict[verdict]}</span>;
}
