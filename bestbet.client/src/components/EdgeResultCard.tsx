import type { AnalyzeResponse } from '../types/domain';
import { money, percent, percentagePoints, signedPercent } from '../lib/format';
import { capReasonLabel, methodLabel, useLocale, useT } from '../lib/i18n';
import { VerdictBadge } from './VerdictBadge';

/** A per-minute rate is unreadable; the same rate over a full match is the number people know. */
const perNinety = (perMinute: number) => (perMinute * 90).toFixed(2);

interface Props {
    result: AnalyzeResponse;
    onSave: () => void;
    saving: boolean;
    saved: boolean;
}

export function EdgeResultCard({ result, onSave, saving, saved }: Props) {
    const t = useT();
    const locale = useLocale();
    const { edge, stake, model, reference } = result;

    // The one genuinely confusing output: a positive edge against the de-vigged fair line while the
    // price on offer still loses money. Worth calling out rather than leaving the reader to spot it.
    const edgeBeatsFairButNotPrice = edge.edgePercentagePoints > 0 && edge.expectedValuePerUnit <= 0;

    return (
        <section className="card card-result">
            <div className="card-head">
                <h2>{t.result.title}</h2>
                <VerdictBadge verdict={result.verdict} />
            </div>

            <dl className="metrics">
                <div className="metric">
                    <dt>{t.result.fair}</dt>
                    <dd>{percent(edge.fairMarketProbability)}</dd>
                </div>
                <div className="metric">
                    <dt>{t.result.yourEstimate}</dt>
                    <dd>{percent(edge.ownProbability)}</dd>
                </div>
                <div className={`metric ${edge.edgePercentagePoints >= 0 ? 'metric-good' : 'metric-bad'}`}>
                    <dt>{t.result.edge}</dt>
                    <dd>{percentagePoints(edge.edgePercentagePoints)}</dd>
                </div>
                <div className={`metric ${edge.expectedValuePerUnit >= 0 ? 'metric-good' : 'metric-bad'}`}>
                    <dt>{t.result.ev}</dt>
                    <dd>{signedPercent(edge.expectedValuePerUnit)}</dd>
                </div>
                <div className="metric metric-stake">
                    <dt>{t.result.stake}</dt>
                    <dd>
                        {money(stake.suggestedStakeAmount, locale)}
                        <span className="metric-sub">
                            {percent(stake.suggestedStakeFraction)} — {capReasonLabel(t, stake.capReason)}
                        </span>
                    </dd>
                </div>
            </dl>

            {reference && (
                <div className="reference-panel">
                    <div className="pace-head">
                        <span className="pace-badge reference-badge">{t.result.referenceTitle}</span>
                        <span className="hint">{reference.source}</span>
                    </div>
                    <dl className="metrics metrics-compact">
                        <div className="metric">
                            <dt>{t.result.referenceFair(reference.source)}</dt>
                            <dd>{percent(reference.fairProbability)}</dd>
                        </div>
                        <div className="metric">
                            <dt>{t.result.bookFair}</dt>
                            <dd>{percent(reference.bookFairProbability)}</dd>
                        </div>
                        <div
                            className={`metric ${
                                reference.disagreementPoints >= 0 ? 'metric-good' : 'metric-bad'
                            }`}
                        >
                            <dt>{t.result.disagreement}</dt>
                            <dd>{percentagePoints(reference.disagreementPoints)}</dd>
                        </div>
                    </dl>
                    <p className="hint">{t.result.referenceNote(reference.source)}</p>
                    <p className="hint">
                        {t.result.referenceOverround(percent(reference.overround - 1))}
                    </p>
                </div>
            )}

            <p className="verdict-reason">{result.verdictReason}</p>

            {model?.pace.warning && <p className="warning">{model.pace.warning}</p>}

            {model?.pace.isAdjusted && (
                <div className="pace-panel">
                    <div className="pace-head">
                        <span className={`pace-badge ${model.pace.paceMultiplier < 1 ? 'pace-down' : 'pace-up'}`}>
                            {model.pace.paceMultiplier < 1 ? t.result.paceDown : t.result.paceUp}{' '}
                            {percent(model.pace.paceMultiplier, 0)}
                        </span>
                        <span className="hint">
                            {t.result.paceHint(
                                perNinety(model.pace.rawRateTotal),
                                perNinety(model.pace.adjustedRateTotal),
                            )}
                        </span>
                    </div>
                    <ul>
                        {model.pace.steps.map((step, index) => (
                            <li key={index}>{step}</li>
                        ))}
                    </ul>
                </div>
            )}

            {edgeBeatsFairButNotPrice && <p className="warning">{t.result.mismatch}</p>}

            <details className="working">
                <summary>{t.result.working}</summary>
                <p>{result.estimate.notes}</p>
                <ul>
                    <li>
                        {t.result.method}: {methodLabel(t, result.estimate.method)}
                    </li>
                    <li>{t.result.overround(percent(result.overround - 1))}</li>
                    {model && (
                        <>
                            <li>
                                {t.result.resolvedSelection}: {model.resolvedSelection}
                            </li>
                            <li>
                                {t.result.minutes}: {model.remainingMinutes}
                            </li>
                            <li>
                                {t.result.xgRate(
                                    model.xgRateHomePerMinute.toFixed(4),
                                    model.xgRateAwayPerMinute.toFixed(4),
                                )}
                            </li>
                            <li>
                                {t.result.expectedRemaining(
                                    model.expectedRemainingHome.toFixed(3),
                                    model.expectedRemainingAway.toFixed(3),
                                )}
                            </li>
                            <li>
                                {t.result.flatProjection(
                                    model.pace.projectedFullMatchXgHome.toFixed(2),
                                    model.pace.projectedFullMatchXgAway.toFixed(2),
                                )}
                            </li>
                            {model.pushProbability > 0 && (
                                <li>
                                    {t.result.push(
                                        percent(model.pushProbability, 2),
                                        percent(model.rawWinProbability),
                                    )}
                                </li>
                            )}
                        </>
                    )}
                </ul>
            </details>

            <div className="card-actions">
                <button type="button" onClick={onSave} disabled={saving || saved}>
                    {saved ? t.result.saved : saving ? t.result.saving : t.result.save}
                </button>
                <span className="hint">{t.result.saveHint}</span>
            </div>
        </section>
    );
}
