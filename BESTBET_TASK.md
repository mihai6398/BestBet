# BestBet — Application Logic & Build Task

**Purpose of this document:** a complete, self-contained specification of what BestBet does and how it should be built. Feed this directly to a coding CLI agent (Claude Code, etc.) as the task description. It exists so the *decision logic* (the hard, valuable part) is written down once, correctly, instead of re-derived by hand for every match.

---

## 0. What this app actually is

BestBet is a **personal decision-support calculator**, not a betting bot. It takes:
- odds currently offered by a bookmaker or Polymarket,
- (for live matches) minute-by-minute match stats, primarily cumulative xG for both teams,
- a bankroll figure,

and returns:
- the fair (de-vigged) probability implied by those odds,
- the user's own probability estimate for the same outcome (computed from a live goal-rate model, or entered manually pre-match),
- the edge, in percentage points, between the two,
- a Kelly-capped stake recommendation,
- a plain verdict: `BET`, `MARGINAL`, or `SKIP`.

It mirrors, in software, the exact manual process used all evening: read xG + minute from a stats provider (Sofascore-style), read the current price from a bookmaker screen, compute fair value, compute edge, decide.

## 1. Non-goals (explicit — do not build these yet)

- **No automated bet placement.** The app never submits a bet anywhere. It only recommends; the user clicks the button on their own bookmaker's site.
- **No scraping of bookmaker sites.** Odds are entered manually via a form in v1. Bookmaker terms of service generally prohibit automated scraping/betting; this app stays on the safe side of that line.
- **No live scraping in v1.** Live stats (xG, minute, shots) are entered manually via a form in v1, exactly like pasting numbers from a screenshot. Automated collection (Sofascore-style) is explicitly **Phase 2** (see §9) — build the calculator first, prove it's useful, then automate the data entry.
- **Not financial/betting advice.** Every result screen must show a persistent, non-dismissable-by-default disclaimer: "Educational decision-support only. Not financial or betting advice."

## 2. Architecture

```
BestBet/
├── BestBet.Server/              # ASP.NET Core (.NET 8) Web API
│   ├── Domain/                  # POCOs, no framework dependencies
│   │   ├── MarketOdds.cs
│   │   ├── LiveMatchState.cs
│   │   ├── ProbabilityEstimate.cs
│   │   ├── EdgeResult.cs
│   │   └── StakeRecommendation.cs
│   ├── Services/
│   │   ├── OddsMath.cs           # implied prob, de-vig, EV
│   │   ├── PoissonGoalModel.cs   # live xG-rate extrapolation
│   │   ├── KellyStaking.cs       # fractional Kelly, capped
│   │   └── VerdictEngine.cs      # combines the above into BET/MARGINAL/SKIP
│   ├── Controllers/
│   │   └── AnalysisController.cs # POST /api/analyze
│   ├── Data/
│   │   ├── BestBetDbContext.cs   # EF Core, SQLite for v1
│   │   └── Entities/PositionLog.cs
│   └── Program.cs
└── bestbet.client/               # React + TypeScript + Vite
    ├── src/
    │   ├── components/
    │   │   ├── OddsInputForm.tsx
    │   │   ├── LiveStatsInputForm.tsx
    │   │   ├── BankrollInput.tsx
    │   │   ├── EdgeResultCard.tsx
    │   │   ├── VerdictBadge.tsx
    │   │   └── PositionsLogTable.tsx
    │   ├── api/analysisClient.ts
    │   ├── types/domain.ts        # mirrors the C# domain records
    │   └── App.tsx
    └── package.json
```

Single user, no auth, local SQLite for v1. `PositionLog` exists specifically to enable CLV tracking later (rule 5 of the underlying strategy: measure by closing-line-value, not by feelings).

## 3. Domain models (C#)

```csharp
namespace BestBet.Domain;

public enum MarketKind { Moneyline1X2, TotalGoals, BothTeamsToScore, NextGoal, Handicap }

public record MarketOdds(
    MarketKind Kind,
    string Label,            // e.g. "Sub 4", "Sevilla FC", "Da"
    double DecimalOdds,      // e.g. 1.57
    double[] SiblingOdds     // the other side(s) of the SAME market, for de-vig. e.g. [Peste4Odds] or [DrawOdds, AwayOdds]
);

public record LiveMatchState(
    int MinutePlayed,
    double CumulativeXgHome,
    double CumulativeXgAway,
    int CurrentGoalsHome,
    int CurrentGoalsAway,
    bool IsPreMatch          // true => LiveMatchState fields other than this are ignored
);

public record ProbabilityEstimate(
    double OwnProbability,   // 0..1, the model's estimate for the specific outcome being priced
    string Method,           // "poisson-live-xg" | "manual-input" | "external-consensus"
    string? Notes            // free text: "based on 61 min of xG at this rate", caveats, etc.
);

public record EdgeResult(
    double FairMarketProbability,   // de-vigged, from MarketOdds.SiblingOdds
    double OwnProbability,
    double EdgePercentagePoints,    // (OwnProbability - FairMarketProbability) * 100
    double ExpectedValuePerUnit     // OwnProbability * DecimalOdds - 1
);

public record StakeRecommendation(
    double SuggestedStakeFraction,  // 0.03..0.05 of bankroll, 0 if verdict is SKIP
    double SuggestedStakeAmount,    // SuggestedStakeFraction * bankroll
    string CapReason                // "quarter-kelly", "floor-3pct", "cap-5pct", "skip-below-threshold"
);

public enum Verdict { Bet, Marginal, Skip }

public record AnalysisResult(
    EdgeResult Edge,
    StakeRecommendation Stake,
    Verdict Verdict,
    string VerdictReason
);
```

## 4. Core algorithms — exact rules, not vibes

These formalize what was done by hand all evening. Implement in `Services/`, unit-test every one of them with the worked examples below.

### 4.1 `OddsMath.ImpliedProbability(decimalOdds)`
```
return 1.0 / decimalOdds;
```

### 4.2 `OddsMath.DeVig(oddsForOutcome, allSiblingOddsInSameMarket)`
Two-way and three-way markets both reduce to: sum the implied probability of every outcome in the market (including the one being priced), then normalize.
```
overround = sum(ImpliedProbability(o) for o in [oddsForOutcome, ...allSiblingOdds])
fairProbability = ImpliedProbability(oddsForOutcome) / overround
```
**Worked example (must pass as a unit test):** odds 1.57 / 1.66 (BTTS-style two-way market from tonight's Sevilla-Valencia analysis) →
`overround = 1/1.57 + 1/1.66 = 0.6369 + 0.6024 = 1.2393` →
`fair = 0.6369 / 1.2393 = 0.5139` → **51.4%**. (Cross-check against any hand-calculated example from this evening's conversation before trusting the implementation.)

### 4.3 `PoissonGoalModel.ExtrapolateRemainingGoals(state: LiveMatchState)`
This is the single most important algorithm in the app — it's the one used all night to turn "0.79 xG in 67 minutes" into "probability of 3+ total goals by full time."

```
elapsedMinutes = state.MinutePlayed
remainingMinutes = 90 - elapsedMinutes   // add ~5-6 for stoppage time if minute > 85

xgRateHome = state.CumulativeXgHome / elapsedMinutes
xgRateAway = state.CumulativeXgAway / elapsedMinutes

expectedRemainingHome = xgRateHome * remainingMinutes
expectedRemainingAway = xgRateAway * remainingMinutes

// Poisson probability mass function, home and away treated as independent
P(exactly k remaining goals, home) = PoissonPmf(k, expectedRemainingHome)
P(exactly k remaining goals, away) = PoissonPmf(k, expectedRemainingAway)

// Convolve to get the distribution of TOTAL remaining goals, then add to current score
// to get the distribution of the final total, for total-goals markets.
```

**Known limitation to document in code comments, not hide:** this assumes a constant scoring rate for the rest of the match. It systematically *underestimates* the chance of a sudden goal-scoring burst after a long quiet stretch (observed repeatedly tonight — Venezia-Fiorentina sat scoreless for 31 minutes despite huge xG, then this model would have been too pessimistic about a late goal). Flag this in the UI as a caveat string on every live result, not just in a comment: *"Assumes current pace holds. Real matches can suddenly open up — treat this as a floor, not a ceiling, on scoring probability."*

### 4.4 `KellyStaking.RecommendStake(ownProbability, decimalOdds, bankroll)`
```
b = decimalOdds - 1
fullKelly = (b * ownProbability - (1 - ownProbability)) / b
quarterKelly = max(0, fullKelly * 0.25)
stakeFraction = clamp(quarterKelly, min: 0.03, max: 0.05)
if fullKelly <= 0: stakeFraction = 0   // no positive edge, no stake at all
return stakeFraction * bankroll
```

### 4.5 `VerdictEngine.Decide(edge: EdgeResult, isLive: bool)`
```
threshold = isLive ? 5.0 : 3.0     // percentage points, per the underlying strategy's rule 1
if edge.EdgePercentagePoints >= threshold: return Verdict.Bet
if edge.EdgePercentagePoints >= threshold - 2.0: return Verdict.Marginal
return Verdict.Skip
```

## 5. API contract

`POST /api/analyze`

Request:
```json
{
  "market": { "kind": "TotalGoals", "label": "Sub 4", "decimalOdds": 1.57, "siblingOdds": [1.66] },
  "liveState": { "minutePlayed": 67, "cumulativeXgHome": 0.79, "cumulativeXgAway": 0.01, "currentGoalsHome": 1, "currentGoalsAway": 0, "isPreMatch": false },
  "bankroll": 263.0
}
```

Response:
```json
{
  "edge": { "fairMarketProbability": 0.514, "ownProbability": 0.60, "edgePercentagePoints": 8.6, "expectedValuePerUnit": -0.058 },
  "stake": { "suggestedStakeFraction": 0.0, "suggestedStakeAmount": 0.0, "capReason": "skip-below-threshold" },
  "verdict": "Marginal",
  "verdictReason": "Edge of 8.6pp is above the live floor but below the confident-bet threshold; also note the constant-pace caveat."
}
```

## 6. UI requirements (React)

- **`OddsInputForm`** — market kind dropdown, label, decimal odds, sibling odds (dynamic count based on kind: 1 for two-way, 2 for three-way).
- **`LiveStatsInputForm`** — shown only if "this match is live" toggle is on: minute, xG home, xG away, current score. Hidden entirely for pre-match analysis (in which case `ownProbability` must be entered directly, since there's no live data to derive it from).
- **`BankrollInput`** — single number field, persisted in local storage (browser-side only, never sent anywhere except this API call).
- **`EdgeResultCard`** — shows fair %, own %, edge in pp, EV%, suggested stake in currency, and the verdict badge.
- **`VerdictBadge`** — color-coded: green "BET", amber "MARGINAL", grey "SKIP".
- **`PositionsLogTable`** — every analysis the user chooses to "save" gets a row: timestamp, market, entry odds, entry probability estimate, outcome (filled in later by the user: won/lost/void), and — critically — a field for the **closing price** the user manually updates before kickoff/match end, so CLV can be computed retroactively. This table is what turns "I think this works" into "I verified this beats the close on 55%+ of trades over 30+ trades," per the strategy's rule 5.
- Persistent disclaimer footer on every screen (see §1).

## 7. v1 scope — build this first, nothing else

1. The four `Services/` classes above, each with unit tests using tonight's worked examples as fixtures.
2. `AnalysisController` wired to those services.
3. The input form → result card flow in React, calling the API.
4. `PositionsLogTable` with manual save/update (no automated closing-price fetch yet).

Do **not** start on live data scraping, odds API integration, or authentication until this loop works end-to-end and has been used on a handful of real matches.

## 8. Phase 2 — explicitly deferred, listed here so it isn't forgotten

- **Automated live-stats ingestion**, replacing `LiveStatsInputForm`'s manual entry with a background poller. Candidate building block: the open-source Sofascore client `EasySoccerData` (Python, PyPI) or `sofascore_scraper` — run as a small local microservice the .NET backend calls over HTTP, rather than porting the scraping logic into C#.
- **Reference-odds ingestion** (e.g. odds-api.net or a Pinnacle feed) so `siblingOdds` for the de-vig calculation, and a "sharp line" comparison, are fetched automatically instead of typed in.
- **Automated CLV capture**: when a market's event ends, automatically fetch and store the closing price against every saved `PositionLog` row.
- Player-level live data collection (mentioned as "interesting for later") — scope this only after the above three are working; it adds significant complexity (per-player xG, substitution effects) for a feature that doesn't yet map to any market this app prices.

## 9. Acceptance test for "v1 is done"

Given the exact inputs from tonight's Sunderland-Arsenal analysis at minute 45 (xG 0.13 home / 0.86 away, market "Primul gol: 2" at odds 1.60, bankroll 263), the app must reproduce — within rounding — the edge and verdict that were computed by hand in this conversation. If it doesn't, the Poisson model or the de-vig math has a bug; fix that before adding any feature from Phase 2.
