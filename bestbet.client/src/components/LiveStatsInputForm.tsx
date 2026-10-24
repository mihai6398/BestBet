import type { LiveFormState, PaceFormState } from '../lib/forms';
import { useT } from '../lib/i18n';

interface Props {
    value: LiveFormState;
    onChange: (next: LiveFormState) => void;
    pace: PaceFormState;
    onPaceChange: (next: PaceFormState) => void;
}

export function LiveStatsInputForm({ value, onChange, pace, onPaceChange }: Props) {
    const t = useT();
    const set = (patch: Partial<LiveFormState>) => onChange({ ...value, ...patch });
    const setPace = (patch: Partial<PaceFormState>) => onPaceChange({ ...pace, ...patch });

    return (
        <section className="card">
            <div className="card-head">
                <h2>{t.live.title}</h2>
                <label className="toggle">
                    <input
                        type="checkbox"
                        checked={value.isLive}
                        onChange={(e) => set({ isLive: e.target.checked })}
                    />
                    <span>{t.live.isLive}</span>
                </label>
            </div>

            {value.isLive ? (
                <>
                    <p className="note">{t.live.note}</p>

                    <div className="field-row">
                        <label className="field field-narrow">
                            <span>{t.live.minute}</span>
                            <input
                                type="number"
                                min="1"
                                max="120"
                                placeholder="67"
                                value={value.minutePlayed}
                                onChange={(e) => set({ minutePlayed: e.target.value })}
                            />
                        </label>
                        <label className="field field-narrow">
                            <span>{t.live.xgHome}</span>
                            <input
                                type="number"
                                step="0.01"
                                min="0"
                                placeholder="0.79"
                                value={value.cumulativeXgHome}
                                onChange={(e) => set({ cumulativeXgHome: e.target.value })}
                            />
                        </label>
                        <label className="field field-narrow">
                            <span>{t.live.xgAway}</span>
                            <input
                                type="number"
                                step="0.01"
                                min="0"
                                placeholder="0.01"
                                value={value.cumulativeXgAway}
                                onChange={(e) => set({ cumulativeXgAway: e.target.value })}
                            />
                        </label>
                        <label className="field field-narrow">
                            <span>{t.live.goalsHome}</span>
                            <input
                                type="number"
                                min="0"
                                value={value.currentGoalsHome}
                                onChange={(e) => set({ currentGoalsHome: e.target.value })}
                            />
                        </label>
                        <label className="field field-narrow">
                            <span>{t.live.goalsAway}</span>
                            <input
                                type="number"
                                min="0"
                                value={value.currentGoalsAway}
                                onChange={(e) => set({ currentGoalsAway: e.target.value })}
                            />
                        </label>
                    </div>

                    <fieldset className="subgroup">
                        <legend>
                            {t.live.stoppage}
                            <span className="hint">{t.live.stoppageHint}</span>
                        </legend>

                        <p className="note">{t.live.stoppageNote}</p>

                        <div className="field-row">
                            <label className="field field-narrow">
                                <span>{t.live.period}</span>
                                <select
                                    value={value.period ?? ''}
                                    onChange={(e) => set({ period: e.target.value as LiveFormState['period'] })}
                                >
                                    <option value="">{t.live.periodAuto}</option>
                                    <option value="FirstHalf">{t.live.periodFirst}</option>
                                    <option value="HalfTime">{t.live.periodHalfTime}</option>
                                    <option value="SecondHalf">{t.live.periodSecond}</option>
                                </select>
                            </label>
                            <label className="field field-narrow">
                                <span>{t.live.addedFirst}</span>
                                <input
                                    type="number"
                                    min="0"
                                    max="30"
                                    placeholder="—"
                                    value={value.addedTimeFirstHalf ?? ''}
                                    onChange={(e) => set({ addedTimeFirstHalf: e.target.value })}
                                />
                            </label>
                            <label className="field field-narrow">
                                <span>{t.live.addedSecond}</span>
                                <input
                                    type="number"
                                    min="0"
                                    max="30"
                                    placeholder="—"
                                    value={value.addedTimeSecondHalf ?? ''}
                                    onChange={(e) => set({ addedTimeSecondHalf: e.target.value })}
                                />
                            </label>
                        </div>
                        <div className="field-row">
                            <label className="field field-narrow">
                                <span>{t.live.redHome}</span>
                                <input
                                    type="number"
                                    min="0"
                                    max="4"
                                    placeholder="0"
                                    value={value.redCardsHome ?? ''}
                                    onChange={(e) => set({ redCardsHome: e.target.value })}
                                />
                            </label>
                            <label className="field field-narrow">
                                <span>{t.live.redAway}</span>
                                <input
                                    type="number"
                                    min="0"
                                    max="4"
                                    placeholder="0"
                                    value={value.redCardsAway ?? ''}
                                    onChange={(e) => set({ redCardsAway: e.target.value })}
                                />
                            </label>
                            <label className="field field-narrow">
                                <span>{t.live.redMinute}</span>
                                <input
                                    type="number"
                                    min="0"
                                    max="120"
                                    placeholder="—"
                                    value={value.lastRedCardMinute ?? ''}
                                    onChange={(e) => set({ lastRedCardMinute: e.target.value })}
                                />
                            </label>
                        </div>
                    </fieldset>

                    <fieldset className="subgroup">
                        <legend>
                            {t.live.dynamics}
                            <span className="hint">{t.live.dynamicsHint}</span>
                        </legend>

                        <label className="toggle toggle-block">
                            <input
                                type="checkbox"
                                checked={pace.corrected}
                                onChange={(e) => setPace({ corrected: e.target.checked })}
                            />
                            <span>{t.live.correct}</span>
                        </label>

                        <p className="note">{pace.corrected ? t.live.correctedNote : t.live.rawNote}</p>

                        {pace.corrected && (
                            <>
                                <p className="note">{t.live.windowNote}</p>
                                <div className="field-row">
                                    <label className="field field-narrow">
                                        <span>{t.live.windowMinutes}</span>
                                        <input
                                            type="number"
                                            min="1"
                                            max="90"
                                            placeholder="15"
                                            value={pace.recentWindowMinutes}
                                            onChange={(e) => setPace({ recentWindowMinutes: e.target.value })}
                                        />
                                    </label>
                                    <label className="field field-narrow">
                                        <span>{t.live.recentXgHome}</span>
                                        <input
                                            type="number"
                                            step="0.01"
                                            min="0"
                                            placeholder="0.40"
                                            value={pace.recentXgHome}
                                            onChange={(e) => setPace({ recentXgHome: e.target.value })}
                                        />
                                    </label>
                                    <label className="field field-narrow">
                                        <span>{t.live.recentXgAway}</span>
                                        <input
                                            type="number"
                                            step="0.01"
                                            min="0"
                                            placeholder="0.10"
                                            value={pace.recentXgAway}
                                            onChange={(e) => setPace({ recentXgAway: e.target.value })}
                                        />
                                    </label>
                                </div>
                            </>
                        )}
                    </fieldset>
                </>
            ) : (
                <>
                    <p className="note">{t.live.preMatchNote}</p>

                    <label className="field field-narrow">
                        <span>{t.live.yourProbability}</span>
                        <input
                            type="number"
                            step="0.1"
                            min="0"
                            max="100"
                            placeholder="62"
                            value={value.manualOwnProbability}
                            onChange={(e) => set({ manualOwnProbability: e.target.value })}
                        />
                    </label>
                </>
            )}
        </section>
    );
}
