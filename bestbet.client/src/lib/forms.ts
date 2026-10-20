import { MARKET_KINDS } from '../types/domain';
import type {
    AnalyzeRequest,
    AnalyzeResponse,
    MarketKind,
    MarketSelection,
    PaceSettings,
    PreMatchRates,
    SaveBetRequest,
} from '../types/domain';

/**
 * Form state and its helpers live here rather than beside the components that render them: a module
 * that exports both a component and a plain function breaks React Fast Refresh for the whole file.
 *
 * Every field is a string because that is what an <input> holds. Parsing happens once, at the point
 * the request is built, so a half-typed "1." never turns into a silent NaN in the middle of a form.
 */

export interface OddsFormState {
    matchLabel: string;
    kind: MarketKind;
    selection: MarketSelection;
    label: string;
    line: string;
    decimalOdds: string;
    siblingOdds: string[];
}

export interface LiveFormState {
    isLive: boolean;
    minutePlayed: string;
    cumulativeXgHome: string;
    cumulativeXgAway: string;
    currentGoalsHome: string;
    currentGoalsAway: string;
    /** Only used when isLive is false — there is no live data to derive a probability from. */
    manualOwnProbability: string;
}

/**
 * The match-dynamics controls. Correction is on by default: the flat extrapolation is only right for
 * a match running at an unremarkable tempo, and it is most wrong exactly when it looks most like a bet.
 */
export interface PaceFormState {
    corrected: boolean;
    /** Blank means no recent window was read off the provider's period split. */
    recentWindowMinutes: string;
    recentXgHome: string;
    recentXgAway: string;
}

export const defaultPaceForm = (): PaceFormState => ({
    corrected: true,
    recentWindowMinutes: '',
    recentXgHome: '',
    recentXgAway: '',
});

export const marketKindConfig = (kind: MarketKind) =>
    MARKET_KINDS.find((m) => m.kind === kind) ?? MARKET_KINDS[0];

/** A fresh form for a market kind, with the selection list and sibling count that kind expects. */
export function defaultOddsForm(kind: MarketKind = 'TotalGoals'): OddsFormState {
    const config = marketKindConfig(kind);
    return {
        matchLabel: '',
        kind,
        selection: config.selections[0].value,
        label: '',
        line: config.needsLine ? '2.5' : '',
        decimalOdds: '',
        siblingOdds: Array.from({ length: config.siblingCount }, () => ''),
    };
}

export const defaultLiveForm = (): LiveFormState => ({
    isLive: true,
    minutePlayed: '',
    cumulativeXgHome: '',
    cumulativeXgAway: '',
    currentGoalsHome: '0',
    currentGoalsAway: '0',
    manualOwnProbability: '',
});

/**
 * A reference line picked off another book or a prediction market.
 *
 * Kept as plain numbers rather than as a form of strings: nothing here was typed, it was chosen from
 * a list, so there is no half-written "1." state to protect against.
 */
export interface ReferenceState {
    /** What to call it on screen, e.g. "Polymarket". */
    source: string;
    /** The outcome as the reference names it, e.g. "Yes" — shown so a mismatch is spottable. */
    label: string;
    /** The market's own question, for the same reason. */
    question: string;
    decimalOdds: number;
    siblingOdds: number[];
}

/**
 * The canonical name for the chosen side, e.g. "Over 2.5".
 *
 * This is the form the server writes into the log and reads back when re-pricing an open position,
 * so the client has to write it the same way. A display label would be unreadable to the parser and
 * a bet already running could not be priced again.
 */
export function canonicalSelection(odds: OddsFormState): string {
    const config = marketKindConfig(odds.kind);
    return config.needsLine && odds.line !== '' ? `${odds.selection} ${odds.line}` : odds.selection;
}

/** Human wording for the chosen side, used as a fallback label when the user left that field empty. */
export function describeSelection(odds: OddsFormState) {
    const config = marketKindConfig(odds.kind);
    const side = config.selections.find((s) => s.value === odds.selection)?.label ?? odds.selection;
    return config.needsLine && odds.line ? `${side} ${odds.line}` : side;
}

/**
 * Turns the two forms into the API request. Kept out of the component so the string-to-number
 * parsing — the place a typo becomes a NaN — has one home and can be exercised on its own.
 */
export function buildAnalyzeRequest(
    odds: OddsFormState,
    live: LiveFormState,
    bankroll: number,
    pace: PaceFormState = defaultPaceForm(),
    reference: ReferenceState | null = null,
    preMatch: PreMatchRates | null = null,
): AnalyzeRequest {
    const config = marketKindConfig(odds.kind);

    return {
        market: {
            kind: odds.kind,
            label: odds.label || describeSelection(odds),
            decimalOdds: Number(odds.decimalOdds),
            siblingOdds: odds.siblingOdds.map(Number).filter((n) => Number.isFinite(n) && n > 0),
        },
        liveState: live.isLive
            ? {
                  minutePlayed: Number(live.minutePlayed),
                  cumulativeXgHome: Number(live.cumulativeXgHome),
                  cumulativeXgAway: Number(live.cumulativeXgAway),
                  currentGoalsHome: Number(live.currentGoalsHome),
                  currentGoalsAway: Number(live.currentGoalsAway),
                  isPreMatch: false,
              }
            : null,
        bankroll,
        // Always explicit: the server can parse a label, but it should never have to guess which side
        // of the market a stake is being sized against.
        selection: odds.selection,
        line: config.needsLine && odds.line !== '' ? Number(odds.line) : undefined,
        // Pre-match, the model's rates replace the typed probability when there are any: the server
        // prices the chosen market from them, so the number is not left to a guess. Live, the same
        // rates are what the pace is pulled towards instead of a league-average match, so they go
        // along either way.
        manualOwnProbability:
            live.isLive || preMatch ? undefined : Number(live.manualOwnProbability) / 100,
        preMatch: preMatch ?? undefined,
        pace: live.isLive ? buildPaceSettings(pace) : undefined,
        referenceMarket: reference
            ? {
                  kind: odds.kind,
                  label: reference.label,
                  decimalOdds: reference.decimalOdds,
                  siblingOdds: reference.siblingOdds,
              }
            : undefined,
        referenceSource: reference?.source,
    };
}

/**
 * A recent window is only sent when BOTH sides of it are filled in. Half a window is not data, and
 * letting the missing side default to zero would invent a collapse in scoring nobody reported.
 */
function buildPaceSettings(pace: PaceFormState): PaceSettings {
    if (!pace.corrected) {
        return { shrink: false, scoreState: false };
    }

    const window = Number(pace.recentWindowMinutes);
    const home = Number(pace.recentXgHome);
    const away = Number(pace.recentXgAway);

    const hasWindow =
        pace.recentWindowMinutes !== '' &&
        pace.recentXgHome !== '' &&
        pace.recentXgAway !== '' &&
        Number.isFinite(window) &&
        window > 0 &&
        Number.isFinite(home) &&
        Number.isFinite(away);

    return hasWindow
        ? { shrink: true, scoreState: true, recentWindowMinutes: window, recentXgHome: home, recentXgAway: away }
        : { shrink: true, scoreState: true };
}

/**
 * The row that goes into the positions log.
 *
 * Shared by the single analysis and by every row of a batch, so a position saved from the table is
 * indistinguishable from one saved from the form — same entry price, same siblings, same match state.
 * A log whose rows mean slightly different things depending on which button made them cannot answer
 * the only question it exists to answer.
 */
export function buildSaveBetRequest(
    odds: OddsFormState,
    live: LiveFormState,
    bankroll: number,
    result: AnalyzeResponse,
): SaveBetRequest {
    const isLive = live.isLive;

    return {
        matchLabel: odds.matchLabel,
        marketKind: odds.kind,
        marketLabel: odds.label || describeSelection(odds),
        // The canonical form, always: the server reads this back when an open position is re-priced,
        // and a display label such as "Under / Sub 2.5" cannot be read back as a side of a market.
        selection: result.model?.resolvedSelection ?? canonicalSelection(odds),
        entryDecimalOdds: Number(odds.decimalOdds),
        entrySiblingOdds: odds.siblingOdds.map(Number).filter(Number.isFinite),
        entryFairProbability: result.edge.fairMarketProbability,
        entryOwnProbability: result.edge.ownProbability,
        entryEdgePercentagePoints: result.edge.edgePercentagePoints,
        estimateMethod: result.estimate.method,
        bankrollAtEntry: bankroll,
        stakeAmount: result.stake.suggestedStakeAmount,
        stakeFraction: result.stake.suggestedStakeFraction,
        verdict: result.verdict,
        wasLive: isLive,
        minutePlayed: isLive ? Number(live.minutePlayed) : null,
        cumulativeXgHome: isLive ? Number(live.cumulativeXgHome) : null,
        cumulativeXgAway: isLive ? Number(live.cumulativeXgAway) : null,
        goalsHome: isLive ? Number(live.currentGoalsHome) : null,
        goalsAway: isLive ? Number(live.currentGoalsAway) : null,
        notes: null,
    };
}
