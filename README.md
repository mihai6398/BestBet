# BestBet

A personal decision-support calculator for football betting markets. It takes a price you can get, the
other prices in the same market, and (for a live match) the xG on the clock, and tells you what the
market really thinks, what the model thinks, and whether the gap between them is worth a stake.

**It never places a bet.** It recommends; you decide and click the button yourself on your own
bookmaker's site. Nothing is scraped. Educational decision-support only — not financial or betting
advice.

The interface is in Romanian by default, with an English switch in the header.

The full specification of the decision logic lives in [BESTBET_TASK.md](BESTBET_TASK.md).

## Getting the prices in

A bookmaker's app will not always let you select its text, and retyping eight prices off a second
screen while a match is running is where a digit gets lost. Four routes in, in order of how little
they ask of the source:

1. **A screenshot.** Paste an image with Ctrl+V (or drop a file) and the prices are read out of it
   in the browser — the engine, its WebAssembly core and the training data are all served by this
   app, so the capture is never uploaded and the import works offline. The transcript is shown and
   editable before anything is parsed, and every market found is printed with its **overround** next
   to it: a pair that does not come out between about 3% and 15% is almost certainly two prices that
   were never two sides of one bet.
2. **Pasted odds** from a page that does allow copying.
3. **A pasted page** from Sofascore, Flashscore or FotMob. Not the stats panel — the whole page,
   Ctrl+A and Ctrl+C, because selecting the panel exactly is the step nobody takes at half time. The
   minute, the score, both teams' names and both xG figures come out of it. The clock is found by
   its position next to the scoreline rather than by being the first clock-shaped thing on the page,
   which is what keeps a kickoff time from being read as the 23rd minute.
4. **One JSON object** that fills everything at once.

### Pricing the whole ladder

One capture of a totals ladder is eight bets. **Analizează toate selecțiile** runs every outcome
through the same analysis a single one gets — same de-vig, same model, same verdict thresholds — and
puts them in a table sorted by edge, best first. The losing lines stay in the table on purpose: most
of the time the book has the whole ladder about right, and that is the result, not a failure.

It needs the live match state, because that is what turns prices into probabilities. Pre-match, the
probability has to come from you, one selection at a time — which is not a batch, it is eight guesses
in a table — so the button says so and stays disabled.

## Running it

Full instructions, in Romanian, are in [GHID_RULARE.md](GHID_RULARE.md) — including Docker, local
development, backups and troubleshooting. The short version:

```bash
docker compose up -d --build
```

Then open <http://localhost:8080>. One container serves both the API and the UI; the positions log
lives in a Docker volume and survives rebuilds.

For development, open `BestBet.sln` and press F5 — Visual Studio starts the API and the Vite dev
server together, with hot reload, at <https://localhost:5173>.

## Running the tests

```bash
dotnet test
```

```bash
npm --prefix bestbet.client test
```

287 server tests, including the worked examples from the specification pinned as an acceptance suite in
`BestBet.Server.Tests/Acceptance/SpecWorkedExamplesTests.cs`. Those are the definition of "the maths
is still right" — if one of them drifts, fix that before adding anything.

## How a number gets to the screen

1. **De-vig** (`OddsMath`) — the price on the screen includes the bookmaker's margin. Summing the
   implied probability of every outcome in the market and normalising strips it out. 1.57 / 1.66 is a
   1.24 overround, and the 1.57 side is really 51.4%, not 63.7%.
2. **Estimate** (`PaceModel` → `PoissonGoalModel` → `MarketProbabilityResolver`) — live, each team's
   accumulated xG becomes a rate per minute. That rate is corrected before it is projected, then run
   forward over the minutes left as two independent Poisson processes, and the resulting distribution
   is projected onto whichever market is being priced. Pre-match, you supply the number yourself.
3. **Compare** — the edge is the gap in percentage points between your estimate and the de-vigged
   line. The EV is measured against the price actually offered, which is a different question; they
   disagree whenever the margin is wider than the edge, and the result card says so when they do.
4. **Size** (`KellyStaking`) — quarter Kelly, clamped into 3–5% of bankroll, zero if the price does
   not cover the estimate.
5. **Decide** (`VerdictEngine`) — 5pp clears a live market, 3pp a pre-match one, with a 2pp marginal
   band below that.

### Match dynamics

A rate read off the clock is a small-sample estimate, and running it flat to full time is wrong in
both directions. Barcelona 3-1 up at minute 36 on 2.38 xG projects 5.95 over ninety and prices Over
6.5 at 76% against a market at 50%; a match that has been quiet for half an hour gets priced as if it
will stay quiet. `PaceModel` corrects for three things, all on by default and all switchable:

- **Shrinkage** towards a league-average match, weighted by how much has actually been played. This
  does most of the work — on the match above it alone takes Over 6.5 from 76% to 50%.
- **Score state**: the side protecting a lead attacks less, the side chasing attacks more, and both
  effects grow as the clock runs down.
- **Recency**, when you supply xG for a recent window (a provider's per-half split). This is what
  catches a game that has just opened up, or one that has died.

Every correction that fires is reported in the result rather than applied silently, and the caveat
names the direction it went — the spec's fixed "treat this as a floor" wording reads backwards after
a hot start, which is exactly when it matters most.

## Pre-match: teams and players

Before kick-off there is no live xG, so a pre-match analysis used to need a probability typed by
hand. The pre-match panel works one out. Each side's expected goals come from its season so far —
goals scored and conceded, home and away, against the competition's average, shrunk towards that
average while the sample is small (`PreMatchModel`). Player statistics refine it: each player's
scoring rate is measured from goals *and* shots, shrunk towards what his position usually produces,
and weighted by how much of a match he plays. Mark a player absent and his share of the goals leaves
with him, less the part a replacement keeps. The same numbers give every likely scorer's chance of
scoring and a fair price to hold a bookmaker's against.

The expected goals feed the ordinary analysis (`preMatch` on `POST /api/analyze`), so any 1X2,
totals, both-teams-to-score or handicap price is priced from them and goes through the same edge test
and Kelly sizing as everything else.

It needs a free [API-Football](https://dashboard.api-football.com) key (`ApiFootball:Key`, or the
`ApiFootball__Key` environment variable): nothing else serves player statistics to a server. Requests
carry the competition, season and team ids only, the key never reaches the browser, and a match
costs three requests, cached for hours. Without a key the rest of the app is unchanged.

It does not know the starting eleven, tactics, motivation or late news, and treats the two scores as
independent — a better prior than a typed number, not a price.

## The reference line

A bookmaker does two jobs with one set of prices: it tells you what the market thinks, and it tells
you what you will be paid. It is only good at the second. A book 5–15% wide de-vigs to a fair line
that is only that accurate, which on a side market is not very.

Polymarket trades probability directly — 63 cents is 63%, and a market's prices add up to about
100%, so there is nothing to remove. Supplying one as a **reference line** measures the edge against
it while EV and the Kelly stake stay at the price you can actually get. Mixing those two up — sizing
a stake against a price nobody will give you — is the one way this could cost money, so the server
keeps them apart and the result card shows both lines and their disagreement.

`GET /api/reference/polymarket?q=` makes an outbound request with nothing but the search term in it, and
the rest of the app works unchanged when it fails. The only other is the pre-match model's, above.

## The positions log

Saving an analysis records the entry price. The entry price and the stake stay **editable** — a row
means what was actually bet, not what the analysis suggested, and a live price moves between reading
it and clicking it. Editing the price re-derives the fair line and the edge from it, and leaves the
model's own probability alone: getting a worse price says nothing about what the match was doing.

### Watching a match that is still being played

Sofascore answers a programmatic request with 403 — every request, down to robots.txt — because
Cloudflare rejects anything that is not a real browser. A server-side poller would therefore have to
impersonate one, which is against their terms and breaks the first time they tighten it. So the
reading is **pushed, not fetched**: a bookmarklet started on the tab the user already has the match
open in reads the page as text every 20 seconds and posts it to `POST /api/live-feed`.

The text is parsed by the same parser a manual paste goes through — one implementation to keep
working rather than two that can disagree about what a redesign means. Each reading fills the live
form and re-prices the open positions **on that match only**: the team-name check is what stops a
saved bet being priced against another match's clock.

The endpoint is guarded by a token generated per run and readable only from the app's own origin; a
page that was not handed it gets a 401. Readings live in memory and are gone when the app stops.

`POST /api/positions/{id}/reprice` re-runs the live model on an open position against the match as
it is now, and returns the probability, the EV at the price already taken, and the fair price at
this moment — which is what a cash-out offer has to be compared against. It writes nothing: the
entry snapshot is the evidence that the decision was reasonable when it was made.

Later you fill in the **closing price** by hand, and the log computes closing-line value. That is the point of the table: win rate over thirty bets is noise,
but consistently beating the close is the signature of a real edge. The scoreboard says nothing
confident until there are 30+ priced positions, and says so explicitly.

## Not built yet

Automated closing-price capture is still Phase 2 — see §8 of the specification. Build nothing from that list until this loop has been used on
real matches.
