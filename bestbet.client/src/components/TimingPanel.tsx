import type { TimingAdvice, TimingOption } from '../types/domain';
import { money, odds as formatOdds, percent } from '../lib/format';
import { useLocale, useT } from '../lib/i18n';

interface Props {
    timing: TimingAdvice;
    /** To turn the plan's expected profit, a share of the bankroll, into money. */
    bankroll: number;
}

const BADGE: Record<TimingAdvice['action'], string> = {
    BetNow: 'timing-now',
    Wait: 'timing-wait',
    Skip: 'timing-skip',
};

/**
 * The timing plan under a live result: bet now, come back later, or leave it.
 *
 * The scenarios shown are the ones for the moment the plan points at — or, when it points at none,
 * the middle one — because "what happens if I wait" is the question the reader came with, and one
 * moment answered in full reads better than four answered in passing.
 */
export function TimingPanel({ timing, bankroll }: Props) {
    const t = useT();
    const locale = useLocale();

    const later = timing.options.slice(1);
    const focus =
        later.find((option) => option.minutesFromNow === timing.waitMinutes) ??
        later.find((option) => option.minutesFromNow === 10) ??
        later[0] ??
        null;
    // Only marked when the plan found something worth doing: on a skip, the "best" of four
    // worthless moments is not a recommendation.
    const best =
        timing.action === 'Skip'
            ? null
            : timing.options.reduce<TimingOption | null>(
                  (top, option) => (top === null || option.expectedValue > top.expectedValue ? option : top),
                  null,
              );

    const profit = (share: number) =>
        bankroll > 0 ? money(share * bankroll, locale) : percent(share, 2);

    const summary =
        timing.action === 'BetNow'
            ? t.timing.betNow
            : timing.action === 'Skip'
              ? t.timing.skip
              : timing.waitMinutes !== null
                ? t.timing.waitFor(
                      timing.waitMinutes,
                      timing.options.find((o) => o.minutesFromNow === timing.waitMinutes)?.atMinute ?? 0,
                  )
                : t.timing.waitWarnings;

    return (
        <div className="timing-panel">
            <div className="pace-head">
                <span className={`pace-badge ${BADGE[timing.action]}`}>{t.timing.action[timing.action]}</span>
                <strong>{t.timing.title}</strong>
            </div>

            <p className="timing-summary">{summary}</p>

            {timing.reasons.length > 0 && (
                <ul className="timing-reasons">
                    {timing.reasons.map((reason) => (
                        <li key={reason.code}>
                            {t.timing.reasons[reason.code]?.(reason.value ?? 0, reason.reference ?? 0) ?? reason.code}
                        </li>
                    ))}
                </ul>
            )}

            <p className="hint">{t.timing.credibility(percent(timing.credibility, 0))}</p>
            {timing.marketRemainingGoals !== null && (
                <p className="hint">
                    {t.timing.goals(
                        timing.modelRemainingGoals.toFixed(2),
                        timing.marketRemainingGoals.toFixed(2),
                    )}
                </p>
            )}
            {timing.leaguePrior && <p className="hint">{t.timing.leaguePrior}</p>}

            <div className="table-scroll">
                <table className="timing-table">
                    <thead>
                        <tr>
                            <th>{t.timing.moment}</th>
                            <th className="numeric">{t.timing.expectedProfit}</th>
                            <th className="numeric">{t.timing.stillWorth}</th>
                            <th className="numeric">{t.timing.likelyOdds}</th>
                            <th className="numeric">{t.timing.stake}</th>
                        </tr>
                    </thead>
                    <tbody>
                        {timing.options.map((option) => (
                            <tr
                                key={option.minutesFromNow}
                                className={option === best && option.expectedValue > 0 ? 'timing-best' : undefined}
                            >
                                <td
                                    className="timing-moment"
                                    title={option === best && option.expectedValue > 0 ? t.timing.best : undefined}
                                >
                                    {option.minutesFromNow === 0
                                        ? t.timing.now(option.atMinute)
                                        : t.timing.later(option.minutesFromNow, option.atMinute)}
                                    {option === best && option.expectedValue > 0 && ' ✓'}
                                </td>
                                <td className="numeric">{profit(option.expectedValue)}</td>
                                <td className="numeric">{percent(option.betProbability, 0)}</td>
                                <td className="numeric">
                                    {option.expectedOdds !== null ? formatOdds(option.expectedOdds) : '—'}
                                </td>
                                <td className="numeric">
                                    {option.stakeFraction > 0 ? percent(option.stakeFraction, 1) : '—'}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
            <p className="hint">{t.timing.columns}</p>

            {focus && (
                <div className="timing-scenarios">
                    <p className="timing-scenarios-title">{t.timing.scenariosTitle(focus.minutesFromNow)}</p>
                    <ul>
                        {focus.scenarios.map((scenario) => (
                            <li key={scenario.code}>
                                <strong>{t.timing.scenario[scenario.code]}</strong>{' '}
                                {t.timing.scenarioLine(
                                    percent(scenario.probability, 0),
                                    percent(scenario.modelProbability, 0),
                                    percent(scenario.marketProbability, 0),
                                )}
                                {' · '}
                                {scenario.odds === null
                                    ? t.timing.scenarioClosed
                                    : `${t.timing.scenarioOdds(formatOdds(scenario.odds))} → ${
                                          scenario.betShare >= 0.5 ? t.timing.scenarioBet : t.timing.scenarioNoBet
                                      }`}
                            </li>
                        ))}
                    </ul>
                </div>
            )}

            <details className="working">
                <summary>{t.timing.how}</summary>
                <p>{t.timing.howText}</p>
            </details>
        </div>
    );
}
