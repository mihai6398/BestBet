import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import './App.css';
import { analysisClient, ApiError } from './api/analysisClient';
import { BankrollInput } from './components/BankrollInput';
import { BatchResultsTable } from './components/BatchResultsTable';
import { EdgeResultCard } from './components/EdgeResultCard';
import { LiveMonitorPanel } from './components/LiveMonitorPanel';
import { LiveStatsInputForm } from './components/LiveStatsInputForm';
import { OddsInputForm } from './components/OddsInputForm';
import { PositionsLogTable } from './components/PositionsLogTable';
import { PreMatchPanel } from './components/PreMatchPanel';
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
    buildSaveBetRequest,
    defaultLiveForm,
    defaultOddsForm,
    defaultPaceForm,
} from './lib/forms';
import type { LiveFormState, OddsFormState, PaceFormState, ReferenceState } from './lib/forms';
import { LANGUAGES, useLanguage } from './lib/i18n';
import { POLL_INTERVAL_SECONDS, sameMatch } from './lib/liveFeed';
import { readingToParsedStats } from './lib/providerFeed';
import { parseStatsPaste } from './lib/statsPaste';
import type { QuickImportResult } from './lib/quickImport';
import type {
    AnalyzeResponse,
    LiveFeedState,
    LiveMatchState,
    Bet,
    BetStatus,
    BetStats,
    PreMatchRates,
    ProviderReading,
    RepricedBet,
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

    // Expected goals from the pre-match model, once the user has chosen to price the market with
    // them. Null means the probability is typed by hand, as it was before the model existed. Once
    // the match is under way the same rates are what the live pace is pulled towards.
    const [preMatch, setPreMatch] = useState<PreMatchRates | null>(null);
    // The match those rates were worked out for, so an open bet on some other match is never
    // re-priced against them.
    const [preMatchFor, setPreMatchFor] = useState('');

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

    const [positions, setPositions] = useState<Bet[]>([]);
    const [summary, setSummary] = useState<BetStats | null>(null);

    // Re-priced open positions, keyed by row. Computed on demand and never persisted, so this lives
    // in the component rather than on the row the server sends back.
    const [repriced, setRepriced] = useState<Record<number, RepricedBet>>({});
    const [repricing, setRepricing] = useState<number | null>(null);

    // Readings pushed in from the match page the user has open. See lib/liveFeed.ts for why they
    // are pushed rather than fetched.
    const [watching, setWatching] = useLocalStorage('bestbet.watching', false);
    const [feed, setFeed] = useState<LiveFeedState | null>(null);
    const [feedError, setFeedError] = useState<string | null>(null);

    // The other route in: a match link the server fetches itself, so no tab has to stay open. See
    // lib/providerFeed.ts. Persisted because monitoring survives a reload and it would be a poor
    // joke to ask for the link again every time.
    const [providerMatch, setProviderMatch] = useLocalStorage('bestbet.providerMatch', '');
    const [providerReading, setProviderReading] = useState<ProviderReading | null>(null);
    const [providerResolving, setProviderResolving] = useState(false);

    // When the last fetched reading landed here, so its age keeps growing while the provider is not
    // answering. The server says how old the reading already was when it left (it may have come out
    // of a short cache), and this adds the time since — which measures staleness without either
    // side having to trust the other's clock.
    const [providerFetchedAt, setProviderFetchedAt] = useState<number | null>(null);
    const [tick, setTick] = useState(() => Date.now());

    // Which reading has already been written into the form, so a poll returning the same one is a
    // no-op rather than a fight with whatever the user is typing.
    const appliedReading = useRef<string | null>(null);
    // Which reading has been applied to which rows. Keyed on both, because the log arrives from its
    // own request: on a reload with monitoring already on, the first reading lands before the rows
    // do, and a guard that only remembered the reading would never re-price any of them.
    const repricedReading = useRef<string | null>(null);
    // The minute the result on screen was computed at, so it can say when it has been overtaken.
    const [resultMinute, setResultMinute] = useState<string | null>(null);

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
                    analysisClient.listBets(),
                    analysisClient.betStats(),
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
        setResultMinute(live.isLive ? live.minutePlayed : null);
        try {
            setResult(
                await analysisClient.analyze(
                    buildAnalyzeRequest(odds, live, bankroll, pace, reference, preMatch),
                ),
            );
        } catch (e) {
            setResult(null);
            setError(e instanceof ApiError || e instanceof Error ? e.message : t.app.genericError);
        } finally {
            setAnalysing(false);
        }
    };

    const saveBet = async () => {
        if (!result) return;
        setSaving(true);
        try {
            const saved = await analysisClient.saveBet(
                buildSaveBetRequest(odds, live, bankroll, result),
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
            await analysisClient.saveBet(
                buildSaveBetRequest(
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

    // --- Watching a match that is still being played ------------------------

    const feedText = feed?.text ?? null;
    const pushParsed = useMemo(() => (feedText ? parseStatsPaste(feedText) : null), [feedText]);

    /**
     * Which of the two routes is in force.
     *
     * The link wins whenever one is set. Not a close call: a link is the user naming the match they
     * want watched, while a push is whatever page a bookmarklet was last pressed on — possibly a
     * different match, possibly a tab that has been asleep for an hour. Letting both through would
     * leave the form flipping between two minutes with nothing on screen to say why.
     *
     * Setting a link therefore also means "stop listening to the bookmarklet", and clearing it hands
     * the channel back. That is what makes a league without xG at the provider workable: the user
     * presses Stop, and the push they were already sending takes over.
     */
    const via: 'provider' | 'push' | null =
        providerMatch !== '' && providerReading ? 'provider' : feed?.receivedUtc ? 'push' : null;

    const feedParsed = useMemo(
        () => (via === 'provider' && providerReading ? readingToParsedStats(providerReading) : pushParsed),
        [via, providerReading, pushParsed],
    );

    /**
     * How old the reading in force is.
     *
     * The push route is timed by the server, which is the only clock that saw the reading arrive.
     * The fetched route is timed from when it landed here plus the age the server reported, so the
     * number keeps climbing through a provider that has stopped answering rather than freezing at
     * whatever the last successful poll said.
     */
    const readingAge: number | null =
        via === 'provider' && providerReading && providerFetchedAt !== null
            ? providerReading.ageSeconds + (tick - providerFetchedAt) / 1000
            : via === 'push'
              ? feed?.ageSeconds ?? null
              : null;

    /** Identifies the reading in force, so an unchanged one is not applied twice. */
    const readingKey =
        via === 'provider' && providerReading
            ? `provider:${providerReading.matchId}:${providerReading.readUtc}`
            : via === 'push' && feed?.receivedUtc
              ? `push:${feed.receivedUtc}`
              : null;

    const tracked = useMemo(
        () =>
            positions.filter(
                (position) =>
                    position.status === 'Open' &&
                    sameMatch(position.matchLabel, feedParsed?.matchLabel),
            ),
        [positions, feedParsed?.matchLabel],
    );


    // The token has to be in hand before the bookmarklet can be built, so one read happens whether
    // or not monitoring is on. A backend that is not up yet simply leaves the panel waiting.
    useEffect(() => {
        let cancelled = false;

        void (async () => {
            try {
                const first = await analysisClient.liveFeed();
                if (!cancelled) setFeed(first);
            } catch {
                // The monitor is optional; nothing else on the page depends on it.
            }
        })();

        return () => {
            cancelled = true;
        };
    }, []);

    useEffect(() => {
        if (!watching) return undefined;

        let cancelled = false;

        const read = async () => {
            try {
                const next = await analysisClient.liveFeed();
                if (cancelled) return;
                setFeed(next);
                setFeedError(null);
            } catch (e) {
                if (!cancelled) setFeedError(e instanceof Error ? e.message : t.monitor.failed);
            }
        };

        void read();
        const timer = window.setInterval(() => void read(), POLL_INTERVAL_SECONDS * 1000);

        return () => {
            cancelled = true;
            window.clearInterval(timer);
        };
    }, [watching, t]);

    /**
     * Asks our own API to fetch the match behind the pasted link, on the same interval the push
     * route is polled at. One interval for both: the two routes are one channel to everything
     * downstream, and two cadences would be two sets of timings to reason about for no gain. The
     * provider is not asked this often either way — the server holds a reading for a few seconds in
     * front of it, so a poll that is too eager costs nothing outside this machine.
     *
     * The tick is advanced whether the poll worked or not. That is what makes the staleness warning
     * fire when a provider goes quiet, instead of the age freezing at the last good answer and the
     * numbers going on looking current.
     */
    useEffect(() => {
        // Nothing is cleared on the way out. A reading left behind cannot be mistaken for a live one
        // — `via` only looks at it while a link is set — and the handler that changes the link
        // clears it there, at the event that caused it, rather than from here.
        if (!watching || providerMatch === '') return undefined;

        let cancelled = false;

        const read = async () => {
            setProviderResolving(true);
            try {
                const next = await analysisClient.providerReading(providerMatch);
                if (cancelled) return;
                setProviderReading(next);
                setProviderFetchedAt(Date.now());
                setFeedError(null);
            } catch (e) {
                if (!cancelled) setFeedError(e instanceof Error ? e.message : t.monitor.failed);
            } finally {
                if (!cancelled) {
                    setProviderResolving(false);
                    setTick(Date.now());
                }
            }
        };

        void read();
        const timer = window.setInterval(() => void read(), POLL_INTERVAL_SECONDS * 1000);

        return () => {
            cancelled = true;
            window.clearInterval(timer);
        };
    }, [watching, providerMatch, t]);

    /** The reading as the model wants it, or null while it is missing something the model needs. */
    const feedLiveState = useMemo<LiveMatchState | null>(() => {
        if (!feedParsed || feedParsed.missing.length > 0) return null;

        return {
            minutePlayed: feedParsed.minute!,
            cumulativeXgHome: feedParsed.xgHome!,
            cumulativeXgAway: feedParsed.xgAway!,
            currentGoalsHome: feedParsed.goalsHome ?? 0,
            currentGoalsAway: feedParsed.goalsAway ?? 0,
            isPreMatch: false,
        };
    }, [feedParsed]);

    /** Writes a new reading into the form, once. */
    useEffect(() => {
        if (!watching || !readingKey || !feedLiveState) return;
        if (appliedReading.current === readingKey) return;

        appliedReading.current = readingKey;

        // Synchronising with an external system — a reading that arrived from another browser tab —
        // is what effects are for, and there is no event here to hang it on instead.
        // eslint-disable-next-line react/set-state-in-effect
        setLive((current) => ({
            ...current,
            isLive: true,
            minutePlayed: String(feedLiveState.minutePlayed),
            cumulativeXgHome: String(feedLiveState.cumulativeXgHome),
            cumulativeXgAway: String(feedLiveState.cumulativeXgAway),
            currentGoalsHome: String(feedLiveState.currentGoalsHome),
            currentGoalsAway: String(feedLiveState.currentGoalsAway),
        }));

        const label = feedParsed?.matchLabel;
        if (label) {
            // eslint-disable-next-line react/set-state-in-effect
            setOdds((current) => (current.matchLabel === '' ? { ...current, matchLabel: label } : current));
        }
    }, [watching, readingKey, feedLiveState, feedParsed?.matchLabel, setLive, setOdds]);

    /**
     * Re-prices the open bets on the match the reading came from.
     *
     * Keyed on the reading AND the rows, because they arrive from two different requests and in
     * either order. The match check is the part that matters most: priced against another match's
     * clock, a saved bet would get a confident number about a game it has nothing to do with.
     */
    useEffect(() => {
        if (!watching || !readingKey || !feedLiveState || tracked.length === 0) return;

        // The prior is part of the key: applying or dropping the pre-match numbers mid-match has
        // to re-price the open bets, not wait for the next reading to happen along.
        const prior = preMatch ? `${preMatch.homeExpectedGoals}/${preMatch.awayExpectedGoals}@${preMatchFor}` : '';
        const reading = `${readingKey}|${tracked.map((position) => position.id).join(',')}|${prior}`;
        if (repricedReading.current === reading) return;
        repricedReading.current = reading;

        void Promise.all(
            tracked.map(async (position) => {
                try {
                    const now = await analysisClient.reprice(position.id, {
                        liveState: feedLiveState,
                        preMatch: preMatch && sameMatch(position.matchLabel, preMatchFor) ? preMatch : undefined,
                    });
                    setRepriced((current) => ({ ...current, [position.id]: now }));
                } catch (e) {
                    // One row that cannot be re-priced must not stop the others — but it must not
                    // vanish either. A monitor that fails quietly leaves the previous minute's
                    // numbers on screen looking current, which is worse than showing nothing.
                    setFeedError(e instanceof Error ? e.message : t.reprice.failed);
                }
            }),
        );
    }, [watching, readingKey, feedLiveState, tracked, preMatch, preMatchFor, t]);

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

            const matchLabel = positions.find((position) => position.id === id)?.matchLabel;
            const now = await analysisClient.reprice(id, {
                liveState,
                preMatch: preMatch && sameMatch(matchLabel, preMatchFor) ? preMatch : undefined,
            });
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
            await analysisClient.updateBet(id, { closingDecimalOdds });
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
            await analysisClient.updateBet(id, { entryDecimalOdds });
            refreshLog();
        } catch (e) {
            setError(e instanceof Error ? e.message : t.app.updateError);
        }
    };

    const updateStake = async (id: number, stakeAmount: number) => {
        try {
            await analysisClient.updateBet(id, { stakeAmount });
            refreshLog();
        } catch (e) {
            setError(e instanceof Error ? e.message : t.app.updateError);
        }
    };

    const updateOutcome = async (id: number, outcome: BetStatus) => {
        try {
            await analysisClient.updateBet(id, { status: outcome });
            refreshLog();
        } catch (e) {
            setError(e instanceof Error ? e.message : t.app.outcomeError);
        }
    };

    const deleteBet = async (id: number) => {
        try {
            await analysisClient.deleteBet(id);
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
                    <LiveMonitorPanel
                        feed={feed}
                        parsed={feedParsed}
                        ageSeconds={readingAge}
                        via={via}
                        watching={watching}
                        onWatchingChange={setWatching}
                        match={providerMatch}
                        onMatchChange={(next) => {
                            setProviderMatch(next);
                            // Switching routes mid-match means the next reading is a different one
                            // by definition, so the guards have to forget what they last applied or
                            // the first reading on the new route would be skipped as a repeat.
                            appliedReading.current = null;
                            repricedReading.current = null;
                            setProviderReading(null);
                            setProviderFetchedAt(null);
                            setFeedError(null);
                        }}
                        reading={providerReading}
                        resolving={providerResolving}
                        tracked={tracked.map((position) => position.marketLabel || position.selection)}
                        error={feedError}
                        onClear={() => {
                            void analysisClient.clearLiveFeed().catch(() => {});
                            appliedReading.current = null;
                            repricedReading.current = null;
                            setProviderReading(null);
                            setProviderFetchedAt(null);
                            setFeed((current) =>
                                current
                                    ? { ...current, text: null, receivedUtc: null, ageSeconds: null, source: null }
                                    : current,
                            );
                        }}
                    />
                    <StatsPastePanel live={live} onApply={applyStatsPaste} />
                    <PreMatchPanel
                        matchLabel={odds.matchLabel}
                        active={preMatch}
                        onApply={(rates, teams) => {
                            setPreMatch(rates);
                            setPreMatchFor(rates ? odds.matchLabel || teams : '');
                            // Before kick-off the rates price the match outright, so the live switch
                            // goes off. Once it is under way they become what the live pace is pulled
                            // towards instead, and the clock already on the form stays where it is.
                            if (rates) {
                                setLive((current) =>
                                    current.isLive && current.minutePlayed !== ''
                                        ? current
                                        : { ...current, isLive: false },
                                );
                            }
                            setResult(null);
                        }}
                    />
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
                            onSave={saveBet}
                            saving={saving}
                            saved={savedId !== null}
                            overtakenAtMinute={
                                watching && resultMinute !== null && resultMinute !== live.minutePlayed
                                    ? resultMinute
                                    : null
                            }
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

            <PositionsLogTable
                positions={positions}
                summary={summary}
                onUpdateClosingOdds={updateClosingOdds}
                onUpdateEntryOdds={updateEntryOdds}
                onUpdateStake={updateStake}
                onUpdateOutcome={updateOutcome}
                onDelete={deleteBet}
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
