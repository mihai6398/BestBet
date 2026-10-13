import { useCallback, useEffect, useState } from 'react';
import './App.css';
import { analysisClient, ApiError } from './api/analysisClient';
import { BankrollInput } from './components/BankrollInput';
import { BatchResultsTable } from './components/BatchResultsTable';
import { EdgeResultCard } from './components/EdgeResultCard';
import { LiveStatsInputForm } from './components/LiveStatsInputForm';
import { BetSlipImportPanel } from './components/BetSlipImportPanel';
import { OddsInputForm } from './components/OddsInputForm';
import { OpenBetsTracker } from './components/OpenBetsTracker';
import { PositionsLogTable } from './components/PositionsLogTable';
import { ReferenceLinePanel } from './components/ReferenceLinePanel';
import { QuickImportPanel } from './components/QuickImportPanel';
import { OddsPastePanel } from './components/OddsPastePanel';
import { ScreenshotImportPanel } from './components/ScreenshotImportPanel';
import { StatsPastePanel } from './components/StatsPastePanel';
import { useLocalStorage } from './hooks/useLocalStorage';
import { batchBlockedBy, oddsFormFor, runBatch } from './lib/batch';
import type { BatchCandidate, BatchRow } from './lib/batch';
import {
    buildAnalyzeRequest,
    buildSavePositionRequest,
    defaultLiveForm,
    defaultOddsForm,
    defaultPaceForm,
} from './lib/forms';
import type { LiveFormState, OddsFormState, PaceFormState, ReferenceState } from './lib/forms';
import { LANGUAGES, useLanguage } from './lib/i18n';
import type { QuickImportResult } from './lib/quickImport';
import type {
    AnalyzeResponse,
    LiveMatchState,
    Position,
    PositionOutcome,
    PositionsSummary,
    RepricedPosition,
} from './types/domain';

function App() {
    const { lang, setLang, t } = useLanguage();

    // Bankroll persists across reloads; nothing else does, because nothing else should be reused.
    const [bankroll, setBankroll] = useLocalStorage('bestbet.bankroll', 100);
    const [odds, setOdds] = useState<OddsFormState>(() => defaultOddsForm());
    const [live, setLive] = useState<LiveFormState>(defaultLiveForm);
    const [pace, setPace] = useState<PaceFormState>(defaultPaceForm);

    // A sharper line to measure the edge against. Null means the bookmaker's own de-vig is used,
    // which is how this app worked before and still works when Polymarket has nothing to say.
    const [reference, setReference] = useState<ReferenceState | null>(null);

    const [result, setResult] = useState<AnalyzeResponse | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [analysing, setAnalysing] = useState(false);
    const [saving, setSaving] = useState(false);
    const [savedId, setSavedId] = useState<number | null>(null);

    // One capture's worth of outcomes, all priced against the same match state.
    const [batch, setBatch] = useState<BatchRow[]>([]);
    const [batchRunning, setBatchRunning] = useState(false);
    const [batchCompleted, setBatchCompleted] = useState(0);
    const [batchTotal, setBatchTotal] = useState(0);
    const [batchSaved, setBatchSaved] = useState<ReadonlySet<string>>(new Set());
    const [batchSaving, setBatchSaving] = useState<string | null>(null);

    const [positions, setPositions] = useState<Position[]>([]);
    const [summary, setSummary] = useState<PositionsSummary | null>(null);

    // Re-priced open positions, keyed by row. Computed on demand and never persisted, so this lives
    // in the component rather than on the row the server sends back.
    const [repriced, setRepriced] = useState<Record<number, RepricedPosition>>({});
    const [repricing, setRepricing] = useState<number | null>(null);

    // The log is fetched by one effect keyed on a counter, and every mutation just bumps the counter.
    // That keeps a single place responsible for talking to the API and for ignoring a response that
    // lands after the component has gone away.
    const [logVersion, setLogVersion] = useState(0);
    const refreshLog = useCallback(() => setLogVersion((version) => version + 1), []);

    useEffect(() => {
        let cancelled = false;

        void (async () => {
            try {
                const [rows, totals] = await Promise.all([
                    analysisClient.listPositions(),
                    analysisClient.positionsSummary(),
                ]);
                if (cancelled) return;
                setPositions(rows);
                setSummary(totals);
            } catch {
                // The log is secondary; a backend that is not up yet should not blank the form.
            }
        })();

        return () => {
            cancelled = true;
        };
    }, [logVersion]);

    // A pasted match replaces the whole form, so any result on screen belongs to the previous one.
    const applyQuickImport = (imported: QuickImportResult) => {
        setOdds(imported.odds);
        setLive(imported.live);
        if (imported.bankroll !== null) {
            setBankroll(imported.bankroll);
        }
        setResult(null);
        setError(null);
        setSavedId(null);
    };

    // Pasted stats only replace the match state. The market and its prices are the user's own reading
    // off a bookmaker and have nothing to do with the stats panel.
    const applyStatsPaste = (patch: { live: LiveFormState; matchLabel: string | null }) => {
        setLive(patch.live);
        if (patch.matchLabel !== null) {
            setOdds((current) => ({ ...current, matchLabel: patch.matchLabel! }));
        }
        setResult(null);
        setError(null);
        setSavedId(null);
    };

    // Pasted odds only replace the market. The match state came off a stats provider and has nothing
    // to do with which price a bookmaker happens to be showing.
    const applyOddsPaste = (next: OddsFormState) => {
        setOdds(next);
        setResult(null);
        setError(null);
        setSavedId(null);
    };

    const analyse = async () => {
        setAnalysing(true);
        setError(null);
        setSavedId(null);
        try {
            setResult(
                await analysisClient.analyze(
                    buildAnalyzeRequest(odds, live, bankroll, pace, reference),
                ),
            );
        } catch (e) {
            setResult(null);
            setError(e instanceof ApiError || e instanceof Error ? e.message : t.app.genericError);
        } finally {
            setAnalysing(false);
        }
    };

    const savePosition = async () => {
        if (!result) return;
        setSaving(true);
        try {
            const saved = await analysisClient.savePosition(
                buildSavePositionRequest(odds, live, bankroll, result),
            );
            setSavedId(saved.id);
            refreshLog();
        } catch (e) {
            setError(e instanceof Error ? e.message : t.app.saveError);
        } finally {
            setSaving(false);
        }
    };

    // --- One capture, every selection ---------------------------------------

    const analyseAll = async (candidates: BatchCandidate[]) => {
        if (candidates.length === 0) return;

        setError(null);
        setResult(null);
        setSavedId(null);
        setBatch([]);
        setBatchSaved(new Set());
        setBatchTotal(candidates.length);
        setBatchCompleted(0);
        setBatchRunning(true);

        try {
            setBatch(
                await runBatch(
                    candidates,
                    { base: odds, live, pace, bankroll },
                    setBatchCompleted,
                ),
            );
        } catch (e) {
            setError(e instanceof Error ? e.message : t.app.genericError);
        } finally {
            setBatchRunning(false);
        }
    };

    /** Moves one row into the form, so its full working is on screen next to the inputs behind it. */
    const openBatchRow = (row: BatchRow) => {
        setOdds((current) => oddsFormFor(current, row.candidate));
        setResult(row.result);
        setSavedId(null);
        setError(null);
    };

    const saveBatchRow = async (row: BatchRow) => {
        if (!row.result) return;
        setBatchSaving(row.candidate.id);
        try {
            await analysisClient.savePosition(
                buildSavePositionRequest(
                    oddsFormFor(odds, row.candidate),
                    live,
                    bankroll,
                    row.result,
                ),
            );
            setBatchSaved((current) => new Set(current).add(row.candidate.id));
            refreshLog();
        } catch (e) {
            setError(e instanceof Error ? e.message : t.app.saveError);
        } finally {
            setBatchSaving(null);
        }
    };

    const blocked = batchBlockedBy(live);
    const blockedReason =
        blocked === 'not-live' ? t.shot.needLive : blocked === 'incomplete-live' ? t.shot.needMinute : null;

    // --- What an open position is worth now ---------------------------------

    /**
     * Re-pricing reuses the live state already on screen rather than asking for it again: the user
     * has just refreshed the stats to look at the next bet, and the open one is in the same match.
     */
    const reprice = async (id: number) => {
        if (blocked !== null) return;

        setRepricing(id);
        setError(null);
        try {
            const liveState: LiveMatchState = {
                minutePlayed: Number(live.minutePlayed),
                cumulativeXgHome: Number(live.cumulativeXgHome),
                cumulativeXgAway: Number(live.cumulativeXgAway),
                currentGoalsHome: Number(live.currentGoalsHome),
                currentGoalsAway: Number(live.currentGoalsAway),
                isPreMatch: false,
            };

            const now = await analysisClient.reprice(id, { liveState });
            setRepriced((current) => ({ ...current, [id]: now }));
        } catch (e) {
            setError(e instanceof Error ? e.message : t.reprice.failed);
        } finally {
            setRepricing(null);
        }
    };

    const clearReprice = (id: number) =>
        setRepriced((current) => {
            const next = { ...current };
            delete next[id];
            return next;
        });

    // --- Positions log ------------------------------------------------------

    const updateClosingOdds = async (id: number, closingDecimalOdds: number | null) => {
        try {
            await analysisClient.updatePosition(id, { closingDecimalOdds });
            refreshLog();
        } catch (e) {
            setError(e instanceof Error ? e.message : t.app.updateError);
        }
    };

    /**
     * The price actually got, which is not always the price the analysis was run on — a live number
     * moves between reading it and clicking it. The server re-derives the fair line and the edge.
     */
    const updateEntryOdds = async (id: number, entryDecimalOdds: number) => {
        try {
            await analysisClient.updatePosition(id, { entryDecimalOdds });
            refreshLog();
        } catch (e) {
            setError(e instanceof Error ? e.message : t.app.updateError);
        }
    };

    const updateStake = async (id: number, stakeAmount: number) => {
        try {
            await analysisClient.updatePosition(id, { stakeAmount });
            refreshLog();
        } catch (e) {
            setError(e instanceof Error ? e.message : t.app.updateError);
        }
    };

    const updateOutcome = async (id: number, outcome: PositionOutcome) => {
        try {
            await analysisClient.updatePosition(id, { outcome });
            refreshLog();
        } catch (e) {
            setError(e instanceof Error ? e.message : t.app.outcomeError);
        }
    };

    const deletePosition = async (id: number) => {
        try {
            await analysisClient.deletePosition(id);
            refreshLog();
        } catch (e) {
            setError(e instanceof Error ? e.message : t.app.deleteError);
        }
    };

    return (
        <div className="app">
            <header className="app-head">
                <div className="app-head-row">
                    <h1>{t.app.title}</h1>
                    <div className="lang-switch" role="group" aria-label={t.app.language}>
                        {LANGUAGES.map((code) => (
                            <button
                                key={code}
                                type="button"
                                className={code === lang ? 'lang-active' : undefined}
                                aria-pressed={code === lang}
                                onClick={() => setLang(code)}
                            >
                                {t.langName[code]}
                            </button>
                        ))}
                    </div>
                </div>
                <p className="tagline">{t.app.tagline}</p>
            </header>

            <main className="layout">
                <div className="column">
                    <BetSlipImportPanel onSaved={refreshLog} />
                    <ScreenshotImportPanel
                        onAnalyseAll={analyseAll}
                        onPickOne={(candidate) => {
                            setOdds((current) => oddsFormFor(current, candidate));
                            setResult(null);
                            setError(null);
                            setSavedId(null);
                        }}
                        blockedReason={blockedReason}
                        busy={batchRunning}
                    />
                    <StatsPastePanel live={live} onApply={applyStatsPaste} />
                    <ReferenceLinePanel
                        matchLabel={odds.matchLabel}
                        value={reference}
                        onChange={(next) => {
                            setReference(next);
                            setResult(null);
                        }}
                    />
                    <OddsPastePanel value={odds} onApply={applyOddsPaste} />
                    <QuickImportPanel
                        odds={odds}
                        live={live}
                        bankroll={bankroll}
                        onImport={applyQuickImport}
                    />
                    <BankrollInput value={bankroll} onChange={setBankroll} />
                    <OddsInputForm value={odds} onChange={setOdds} />
                    <LiveStatsInputForm
                        value={live}
                        onChange={setLive}
                        pace={pace}
                        onPaceChange={setPace}
                    />

                    <button type="button" className="primary" onClick={analyse} disabled={analysing}>
                        {analysing ? t.app.analysing : t.app.analyse}
                    </button>

                    {error && <p className="error">{error}</p>}
                </div>

                <div className="column">
                    {(batch.length > 0 || batchRunning) && (
                        <BatchResultsTable
                            rows={batch}
                            running={batchRunning}
                            completed={batchCompleted}
                            total={batchTotal}
                            matchLabel={odds.matchLabel}
                            live={live}
                            onOpen={openBatchRow}
                            onSave={saveBatchRow}
                            savedIds={batchSaved}
                            savingId={batchSaving}
                            onClear={() => setBatch([])}
                        />
                    )}

                    {result ? (
                        <EdgeResultCard
                            result={result}
                            onSave={savePosition}
                            saving={saving}
                            saved={savedId !== null}
                        />
                    ) : (
                        batch.length === 0 &&
                        !batchRunning && (
                            <section className="card card-empty">
                                <p className="note">{t.app.emptyResult}</p>
                            </section>
                        )
                    )}
                </div>
            </main>

            <OpenBetsTracker positions={positions} live={live} onSettle={updateOutcome} />

            <PositionsLogTable
                positions={positions}
                summary={summary}
                onUpdateClosingOdds={updateClosingOdds}
                onUpdateEntryOdds={updateEntryOdds}
                onUpdateStake={updateStake}
                onUpdateOutcome={updateOutcome}
                onDelete={deletePosition}
                onReprice={reprice}
                repriceBlocked={blocked === null ? null : t.reprice.needsLive}
                repricing={repricing}
                repriced={repriced}
                onClearReprice={clearReprice}
            />

            <footer className="disclaimer">{t.app.disclaimer}</footer>
        </div>
    );
}

export default App;
