// Mirrors the C# records in BestBet.Server/Domain and BestBet.Server/Contracts.
// Enums cross the wire as their names (the server registers JsonStringEnumConverter), so these are
// string unions rather than numeric enums.

export type MarketKind =
    | 'Moneyline1X2'
    | 'TotalGoals'
    | 'BothTeamsToScore'
    | 'NextGoal'
    | 'Handicap';

export type MarketSelection =
    | 'Home'
    | 'Draw'
    | 'Away'
    | 'Over'
    | 'Under'
    | 'Yes'
    | 'No'
    | 'NextGoalHome'
    | 'NextGoalAway'
    | 'NextGoalNone'
    | 'HandicapHome'
    | 'HandicapAway';

export type Verdict = 'Bet' | 'Marginal' | 'Skip';

export type PositionOutcome = 'Pending' | 'Won' | 'Lost' | 'Void';

export interface MarketOdds {
    kind: MarketKind;
    label: string;
    decimalOdds: number;
    /** The other side(s) of the SAME market, so the vig can be removed. */
    siblingOdds: number[];
}

export interface LiveMatchState {
    minutePlayed: number;
    cumulativeXgHome: number;
    cumulativeXgAway: number;
    currentGoalsHome: number;
    currentGoalsAway: number;
    /** true => every other field is ignored and a manual probability is required. */
    isPreMatch: boolean;
}

export interface ProbabilityEstimate {
    ownProbability: number;
    method: 'poisson-live-xg' | 'poisson-live-xg-paced' | 'manual-input' | 'external-consensus';
    notes: string | null;
}

export interface EdgeResult {
    fairMarketProbability: number;
    ownProbability: number;
    edgePercentagePoints: number;
    /** Measured at the price offered, so it can be negative while the edge is positive. */
    expectedValuePerUnit: number;
}

export interface StakeRecommendation {
    suggestedStakeFraction: number;
    suggestedStakeAmount: number;
    capReason: string;
}

/** How the live xG rate is projected forward. Omit for the corrected model. */
export interface PaceSettings {
    shrink: boolean;
    scoreState: boolean;
    recentWindowMinutes?: number | null;
    recentXgHome?: number | null;
    recentXgAway?: number | null;
    recencyWeight?: number;
}

/** What the pace model did to the raw rate, and why. */
export interface PaceAdjustment {
    rawRateHome: number;
    rawRateAway: number;
    adjustedRateHome: number;
    adjustedRateAway: number;
    /** What the RAW rate implies over ninety minutes — the sanity check. */
    projectedFullMatchXgHome: number;
    projectedFullMatchXgAway: number;
    isAdjusted: boolean;
    steps: string[];
    warning: string | null;
    rawRateTotal: number;
    adjustedRateTotal: number;
    /** Below 1 the pace was corrected down, above 1 up. */
    paceMultiplier: number;
}

export interface ModelDiagnostics {
    remainingMinutes: number;
    xgRateHomePerMinute: number;
    xgRateAwayPerMinute: number;
    expectedRemainingHome: number;
    expectedRemainingAway: number;
    rawWinProbability: number;
    pushProbability: number;
    resolvedSelection: string;
    pace: PaceAdjustment;
}

/**
 * A second opinion on the fair price, from a book that is not the one being bet into.
 *
 * The bookmaker's own de-vigged line and the price you can get are the same number doing two jobs,
 * and a wide book is bad at the first while being the only option for the second. A prediction
 * market prices at a margin near zero, so its de-vig is closer to the truth — and you cannot bet
 * your stake into it. Keeping them apart lets the sharper line decide whether there is an edge
 * while the stake is still sized against what will actually be paid.
 */
export interface ReferenceLine {
    source: string;
    /** De-vigged from the reference. This is what the edge is measured against. */
    fairProbability: number;
    /** The reference's own margin. Near 1.00 for a prediction market. */
    overround: number;
    /** What the bookmaker's own prices de-vig to, kept for comparison. */
    bookFairProbability: number;
    /** Reference minus bookmaker, in percentage points. */
    disagreementPoints: number;
}

export interface AnalyzeRequest {
    market: MarketOdds;
    liveState: LiveMatchState | null;
    bankroll: number;
    selection?: MarketSelection;
    line?: number;
    manualOwnProbability?: number;
    pace?: PaceSettings;
    /** The SAME outcome priced somewhere else. Nothing checks that; the panel says so. */
    referenceMarket?: MarketOdds;
    referenceSource?: string;
    /** Expected goals from the pre-match model. Prices a match that has not started. */
    preMatch?: PreMatchRates;
}

/** One outcome on a prediction market, priced both as a probability and as decimal odds. */
export interface PredictionOutcome {
    label: string;
    /** 0..1, the traded price — 63 cents is the market's 63%. */
    probability: number;
    decimalOdds: number;
}

export interface PredictionMarket {
    id: string;
    question: string;
    eventTitle: string;
    url: string | null;
    /** Traded volume in dollars. A thin market is a worse reference than no reference. */
    volume: number | null;
    liquidity: number | null;
    endDate: string | null;
    outcomes: PredictionOutcome[];
    /** Polymarket's own tag: "moneyline", "totals", "both_teams_to_score", "spreads". */
    marketType: string | null;
    /** The goal line for totals and spreads (2.5 in "O/U 2.5"). */
    line: number | null;
}

/** What an already-placed bet is worth now. Computed on demand; never written to the row. */
export interface RepricedPosition {
    id: number;
    matchLabel: string;
    selection: string;
    entryDecimalOdds: number;
    stakeAmount: number;
    entryOwnProbability: number;
    ownProbability: number;
    probabilityPoints: number;
    /** At the price already taken — the only price that can still apply. */
    expectedValuePerUnit: number;
    expectedValueAmount: number;
    /** 1 / probability: what a fair price would be now, to judge a cash-out against. */
    /** Null once the score has decided the bet against you: there is no price for what cannot happen. */
    fairOddsNow: number | null;
    estimate: ProbabilityEstimate;
    model: ModelDiagnostics;
}

/**
 * The last reading pushed in from a match page the user has open.
 *
 * It arrives as raw page text and is parsed here, by the same parser a manual paste goes through —
 * one implementation rather than two that can disagree about what a provider's redesign means.
 */
export interface LiveFeedState {
    /** The push secret. Readable only from this app's own origin, which is what guards the endpoint. */
    token: string;
    text: string | null;
    receivedUtc: string | null;
    /** How old the reading is. The number that says whether to trust it. */
    ageSeconds: number | null;
    source: string | null;
}

export interface RepriceRequest {
    liveState: LiveMatchState;
    pace?: PaceSettings;
}

export interface AnalyzeResponse {
    edge: EdgeResult;
    stake: StakeRecommendation;
    verdict: Verdict;
    verdictReason: string;
    estimate: ProbabilityEstimate;
    overround: number;
    model: ModelDiagnostics | null;
    disclaimer: string;
    reference: ReferenceLine | null;
}

export interface SavePositionRequest {
    matchLabel: string;
    marketKind: MarketKind;
    marketLabel: string;
    selection: string;
    entryDecimalOdds: number;
    entrySiblingOdds: number[];
    entryFairProbability: number;
    entryOwnProbability: number;
    entryEdgePercentagePoints: number;
    estimateMethod: string;
    bankrollAtEntry: number;
    stakeAmount: number;
    stakeFraction: number;
    verdict: Verdict;
    wasLive: boolean;
    minutePlayed: number | null;
    cumulativeXgHome: number | null;
    cumulativeXgAway: number | null;
    goalsHome: number | null;
    goalsAway: number | null;
    notes: string | null;
}

export interface UpdatePositionRequest {
    closingDecimalOdds?: number | null;
    closingSiblingOdds?: number[] | null;
    outcome?: PositionOutcome | null;
    notes?: string | null;
    /** What was actually taken. Editing it re-derives the fair line and the edge server-side. */
    entryDecimalOdds?: number;
    entrySiblingOdds?: number[];
    stakeAmount?: number;
}

/**
 * A bet already placed at a bookmaker, read off its slip. Everything after the stake is optional:
 * a slip has the score and the clock but no xG, and without xG there is nothing to price the entry
 * with — the row is then only a record that the bet exists, to be tracked from here on.
 */
export interface ImportPositionRequest {
    matchLabel: string;
    marketKind: MarketKind;
    marketLabel: string;
    /** Canonical form, e.g. "Under 2" — the server reads it back to re-price. */
    selection: string;
    entryDecimalOdds: number;
    stakeAmount: number;
    minutePlayed?: number;
    goalsHome?: number;
    goalsAway?: number;
    cumulativeXgHome?: number;
    cumulativeXgAway?: number;
    notes?: string;
}

export interface Position {
    id: number;
    createdUtc: string;
    matchLabel: string;
    marketKind: MarketKind;
    marketLabel: string;
    selection: string;
    entryDecimalOdds: number;
    entrySiblingOdds: number[];
    entryFairProbability: number;
    entryOwnProbability: number;
    entryEdgePercentagePoints: number;
    estimateMethod: string;
    bankrollAtEntry: number;
    stakeAmount: number;
    stakeFraction: number;
    verdict: Verdict;
    wasLive: boolean;
    minutePlayed: number | null;
    cumulativeXgHome: number | null;
    cumulativeXgAway: number | null;
    goalsHome: number | null;
    goalsAway: number | null;
    closingDecimalOdds: number | null;
    closingSiblingOdds: number[];
    outcome: PositionOutcome;
    notes: string | null;
    clvPercent: number | null;
    beatTheClose: boolean | null;
    clvProbabilityPoints: number | null;
    profitLoss: number | null;
}

export interface PositionsSummary {
    totalPositions: number;
    positionsWithClosingPrice: number;
    positionsBeatingClose: number;
    beatCloseRate: number | null;
    averageClvPercent: number | null;
    settledPositions: number;
    totalStaked: number;
    totalProfitLoss: number;
    readout: string;
}

/** Which selections each market kind offers, and whether that kind needs a goal line. */
export const MARKET_KINDS: ReadonlyArray<{
    kind: MarketKind;
    label: string;
    selections: ReadonlyArray<{ value: MarketSelection; label: string }>;
    needsLine: boolean;
    siblingCount: number;
    siblingHint: string;
}> = [
    {
        kind: 'TotalGoals',
        label: 'Total goals (Sub / Peste)',
        selections: [
            { value: 'Under', label: 'Under / Sub' },
            { value: 'Over', label: 'Over / Peste' },
        ],
        needsLine: true,
        siblingCount: 1,
        siblingHint: 'The other side of the same line',
    },
    {
        kind: 'NextGoal',
        label: 'Next goal / Primul gol',
        selections: [
            { value: 'NextGoalHome', label: 'Home (1)' },
            { value: 'NextGoalAway', label: 'Away (2)' },
            { value: 'NextGoalNone', label: 'No further goal (0)' },
        ],
        needsLine: false,
        siblingCount: 2,
        siblingHint: 'The other two prices in this market',
    },
    {
        kind: 'BothTeamsToScore',
        label: 'Both teams to score (GG/NG)',
        selections: [
            { value: 'Yes', label: 'Yes / Da' },
            { value: 'No', label: 'No / Nu' },
        ],
        needsLine: false,
        siblingCount: 1,
        siblingHint: 'The other side of the same market',
    },
    {
        kind: 'Moneyline1X2',
        label: 'Match result (1X2)',
        selections: [
            { value: 'Home', label: 'Home (1)' },
            { value: 'Draw', label: 'Draw (X)' },
            { value: 'Away', label: 'Away (2)' },
        ],
        needsLine: false,
        siblingCount: 2,
        siblingHint: 'The other two prices in this market',
    },
    {
        kind: 'Handicap',
        label: 'Handicap',
        selections: [
            { value: 'HandicapHome', label: 'Home' },
            { value: 'HandicapAway', label: 'Away' },
        ],
        needsLine: true,
        siblingCount: 1,
        siblingHint: 'The other side of the same handicap',
    },
];

export const DISCLAIMER = 'Educational decision-support only. Not financial or betting advice.';

// --- Pre-match model: team form and the players expected to play --------------------------------

export interface PreMatchRates {
    homeExpectedGoals: number;
    awayExpectedGoals: number;
}

export interface LeagueOption {
    id: number;
    name: string;
}

export interface PreMatchStatus {
    /** Whether an API key has been set on the server. */
    configured: boolean;
    leagues: LeagueOption[];
    currentSeason: number;
}

export interface PreMatchRequest {
    leagueId: number;
    homeTeam: string;
    awayTeam: string;
    season?: number;
    homeAbsent?: number[];
    awayAbsent?: number[];
}

export interface TeamForm {
    homePlayed: number;
    awayPlayed: number;
    played: number;
    goalsForHome: number;
    goalsAgainstHome: number;
    goalsForAway: number;
    goalsAgainstAway: number;
}

export interface PlayerScoringChance {
    id: number;
    name: string;
    position: string | null;
    appearances: number;
    minutes: number;
    goals: number;
    shots: number;
    goalsPer90: number;
    expectedMatchFraction: number;
    /** P(at least one goal), 0..1. */
    scoringProbability: number;
    /** 1 / scoringProbability: the price at which backing him breaks even. */
    fairOdds: number;
    absent: boolean;
}

export interface PreMatchTeam {
    id: number;
    name: string;
    form: TeamForm;
    /** After absences — the number the probabilities use. */
    expectedGoals: number;
    baseExpectedGoals: number;
    /** Share (0..1) of the squad's expected scoring that is marked absent. */
    missingShare: number;
    players: PlayerScoringChance[];
}

export interface PreMatchResponse {
    leagueId: number;
    leagueName: string;
    season: number;
    league: { homeGoals: number; awayGoals: number };
    home: PreMatchTeam;
    away: PreMatchTeam;
    rates: PreMatchRates;
    probabilities: {
        homeWin: number;
        draw: number;
        awayWin: number;
        bothTeamsToScore: number;
        totals: { line: number; over: number }[];
    };
    warnings: string[];
    disclaimer: string;
}
