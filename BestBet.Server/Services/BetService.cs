using System.Globalization;
using BestBet.Contracts;
using BestBet.Data;
using BestBet.Data.Entities;
using BestBet.Domain;
using Microsoft.EntityFrameworkCore;

namespace BestBet.Services;

/// <summary>
/// Reads and writes one user's bets, settles them, and computes the scoreboard over them.
/// </summary>
/// <remarks>
/// Every query starts from <see cref="Mine"/>, which is filtered on the signed-in user. Nothing reads
/// <c>db.Bets</c> directly, so a bet that belongs to someone else is indistinguishable from one that
/// does not exist: the controller answers 404 for both, and nothing leaks whether the id is in use.
/// </remarks>
public class BetService(BestBetDbContext db, ICurrentUser user)
{
    /// <summary>Below this many bets the CLV rate is noise, and the readout says so.</summary>
    public const int MinimumMeaningfulSample = 30;

    /// <summary>The rate the underlying strategy treats as evidence that the process works.</summary>
    public const double TargetBeatCloseRate = 0.55;

    private IQueryable<Bet> Mine => db.Bets.Where(b => b.UserId == user.Id);

    public async Task<BetDto> SaveAsync(SaveBetRequest request, CancellationToken cancellationToken = default)
    {
        RequirePrice(request.EntryDecimalOdds, "The price taken");
        RequirePrices(request.EntrySiblingOdds ?? [], "Every other price in the market");
        RequireStake(request.StakeAmount);
        RequireProbability(request.EntryOwnProbability, "The estimated probability");
        RequireProbability(request.EntryFairProbability, "The fair probability");
        if (!(request.BankrollAtEntry >= 0.0) || !double.IsFinite(request.BankrollAtEntry))
        {
            throw new AnalysisInputException($"The bankroll must be a non-negative number, got {request.BankrollAtEntry}.");
        }

        var entity = new Bet
        {
            UserId = user.Id,
            CreatedUtc = DateTime.UtcNow,
            Sport = Text(request.Sport, 64, "The sport"),
            Competition = Text(request.Competition, 128, "The competition"),
            MatchLabel = Text(request.MatchLabel, 200, "The event") ?? string.Empty,
            EventStartUtc = Utc(request.EventStartUtc),
            MarketKind = request.MarketKind,
            MarketLabel = Text(request.MarketLabel, 200, "The market") ?? string.Empty,
            Selection = Text(request.Selection, 64, "The selection") ?? string.Empty,
            Bookmaker = Text(request.Bookmaker, 64, "The bookmaker"),
            EntryDecimalOdds = Price(request.EntryDecimalOdds),
            EntrySiblingOddsCsv = ClvMath.FormatOddsCsv(request.EntrySiblingOdds ?? []),
            EntryFairProbability = request.EntryFairProbability,
            EntryOwnProbability = request.EntryOwnProbability,
            EntryEdgePercentagePoints = request.EntryEdgePercentagePoints,
            EstimateMethod = Text(request.EstimateMethod, 64, "The estimate method") ?? string.Empty,
            BankrollAtEntry = Money(request.BankrollAtEntry),
            StakeAmount = Money(request.StakeAmount),
            StakeFraction = request.StakeFraction,
            Verdict = request.Verdict,
            WasLive = request.WasLive,
            MinutePlayed = request.MinutePlayed,
            CumulativeXgHome = request.CumulativeXgHome,
            CumulativeXgAway = request.CumulativeXgAway,
            GoalsHome = request.GoalsHome,
            GoalsAway = request.GoalsAway,
            Notes = Text(request.Notes, 2000, "The notes"),
        };

        db.Bets.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    /// <summary>
    /// Records a bet that was placed elsewhere, so it can be tracked and settled like any other row.
    /// </summary>
    /// <remarks>
    /// The entry snapshot is only as good as what the slip carried. With the minute, the score and
    /// both xG figures the model prices the bet as it stood when it was taken; without them the model
    /// has nothing to run on, and the row says so by taking the price's own implied probability as
    /// its estimate — an edge of exactly zero, not an edge somebody invented. The "fair" probability
    /// is the implied one too, because a slip shows one side of the market and there is no margin to
    /// remove, so any edge measured against it still contains the bookmaker's margin.
    /// </remarks>
    public async Task<BetDto> ImportAsync(ImportBetRequest request, CancellationToken cancellationToken = default)
    {
        if (!(request.EntryDecimalOdds > 1.0) || double.IsInfinity(request.EntryDecimalOdds))
        {
            throw new AnalysisInputException(
                $"The price on the slip must be greater than 1.0, got {request.EntryDecimalOdds}.");
        }

        if (request.StakeAmount < 0.0 || double.IsNaN(request.StakeAmount) || double.IsInfinity(request.StakeAmount))
        {
            throw new AnalysisInputException($"The stake must be a non-negative number, got {request.StakeAmount}.");
        }

        // Parsed even when nothing is priced: a row whose side cannot be read back could be saved but
        // never tracked, which is the whole reason to import it.
        var selection = StoredSelection.Parse(request.MarketKind, request.Selection);

        var implied = 1.0 / request.EntryDecimalOdds;
        var own = implied;
        var method = "slip-import";

        var priced = request.MinutePlayed is not null
                     && request.CumulativeXgHome is not null
                     && request.CumulativeXgAway is not null;

        if (priced)
        {
            var state = new LiveMatchState(
                request.MinutePlayed!.Value,
                request.CumulativeXgHome!.Value,
                request.CumulativeXgAway!.Value,
                request.GoalsHome ?? 0,
                request.GoalsAway ?? 0,
                IsPreMatch: false);

            (own, method) = PriceAtEntry(request.MarketKind, selection, state);
        }

        var notes = request.Notes ?? (priced
            ? "Imported from a bet slip. The fair price at entry is the price's own implied probability, " +
              "so the edge shown still contains the bookmaker's margin."
            : "Imported from a bet slip. No match state was given, so there is no estimate at entry.");

        return await SaveAsync(new SaveBetRequest(
            request.MatchLabel,
            request.MarketKind,
            request.MarketLabel,
            request.Selection,
            request.EntryDecimalOdds,
            [],
            implied,
            own,
            (own - implied) * 100.0,
            method,
            BankrollAtEntry: 0.0,
            request.StakeAmount,
            StakeFraction: 0.0,
            Verdict.Marginal,
            WasLive: request.MinutePlayed is not null,
            request.MinutePlayed,
            request.CumulativeXgHome,
            request.CumulativeXgAway,
            request.GoalsHome,
            request.GoalsAway,
            notes,
            Bookmaker: request.Bookmaker), cancellationToken);
    }

    private static (double Probability, string Method) PriceAtEntry(
        MarketKind kind,
        MarketSelectionSpec selection,
        LiveMatchState state)
    {
        var (estimate, _) = AnalysisService.PriceLive(kind, selection, state, pace: null);
        return (estimate.OwnProbability, "slip-import-model");
    }

    /// <summary>Every bet of the signed-in user, newest first.</summary>
    public async Task<IReadOnlyList<BetDto>> ListAsync(CancellationToken cancellationToken = default)
        => (await QueryAsync(new BetListQuery(), cancellationToken)).Items;

    /// <summary>The signed-in user's bets, filtered by status and optionally one page at a time.</summary>
    public async Task<BetPage> QueryAsync(BetListQuery query, CancellationToken cancellationToken = default)
    {
        var rows = Mine;

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim();
            if (status.Equals("open", StringComparison.OrdinalIgnoreCase))
            {
                rows = rows.Where(b => b.Status == BetStatus.Open);
            }
            else if (status.Equals("closed", StringComparison.OrdinalIgnoreCase)
                     || status.Equals("settled", StringComparison.OrdinalIgnoreCase))
            {
                rows = rows.Where(b => b.Status != BetStatus.Open);
            }
            else if (Enum.TryParse<BetStatus>(status, ignoreCase: true, out var exact))
            {
                rows = rows.Where(b => b.Status == exact);
            }
            else
            {
                throw new AnalysisInputException(
                    $"Unknown status \"{status}\". Use open, closed, or one of: {string.Join(", ", Enum.GetNames<BetStatus>())}.");
            }
        }

        var total = await rows.CountAsync(cancellationToken);
        IQueryable<Bet> ordered = rows.OrderByDescending(b => b.CreatedUtc).ThenByDescending(b => b.Id);

        if (query.Page is { } page)
        {
            if (page < 1)
            {
                throw new AnalysisInputException("The page number starts at 1.");
            }

            var size = Math.Clamp(query.PageSize ?? 50, 1, BetListQuery.MaxPageSize);
            ordered = ordered.Skip((page - 1) * size).Take(size);
        }

        var items = await ordered.AsNoTracking().ToListAsync(cancellationToken);
        return new BetPage(items.Select(ToDto).ToList(), total);
    }

    public async Task<BetDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Mine.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<BetDto?> UpdateAsync(
        int id,
        UpdateBetRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await Mine.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        ApplyEntryEdits(entity, request);

        if (request.MatchLabel is not null)
        {
            entity.MatchLabel = Text(request.MatchLabel, 200, "The event") ?? string.Empty;
        }

        if (request.Sport is not null)
        {
            entity.Sport = Blank(Text(request.Sport, 64, "The sport"));
        }

        if (request.Competition is not null)
        {
            entity.Competition = Blank(Text(request.Competition, 128, "The competition"));
        }

        if (request.Bookmaker is not null)
        {
            entity.Bookmaker = Blank(Text(request.Bookmaker, 64, "The bookmaker"));
        }

        if (request.EventStartUtc is not null)
        {
            entity.EventStartUtc = Utc(request.EventStartUtc);
        }

        if (request.ClosingDecimalOdds is { } closing)
        {
            entity.ClosingDecimalOdds = closing > 1.0 && double.IsFinite(closing) ? Price(closing) : null;
        }

        if (request.ClosingSiblingOdds is { } siblings)
        {
            entity.ClosingSiblingOddsCsv = ClvMath.FormatOddsCsv(siblings);
        }

        if (request.Status is { } status)
        {
            ApplySettlement(entity, status, request.Payout);
        }

        if (request.Notes is not null)
        {
            entity.Notes = Text(request.Notes, 2000, "The notes");
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    /// <summary>Records how a bet ended and what it paid back.</summary>
    public async Task<BetDto?> SettleAsync(
        int id,
        SettleBetRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await Mine.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (request.Status == BetStatus.Open)
        {
            throw new AnalysisInputException("Settling needs a result. To reopen a bet, update its status instead.");
        }

        if (request.ClosingDecimalOdds is { } closing)
        {
            RequirePrice(closing, "The closing price");
            entity.ClosingDecimalOdds = Price(closing);
        }

        ApplySettlement(entity, request.Status, request.Payout);

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    /// <summary>
    /// Sets the status and works the payout and profit out from it, or clears them on a reopen.
    /// </summary>
    private static void ApplySettlement(Bet entity, BetStatus status, double? cashOutPayout)
    {
        if (status == BetStatus.Open)
        {
            entity.Status = BetStatus.Open;
            entity.Payout = null;
            entity.ProfitLoss = null;
            entity.SettledUtc = null;
            return;
        }

        if (cashOutPayout is { } amount && (!(amount >= 0.0) || !double.IsFinite(amount)))
        {
            throw new AnalysisInputException($"The payout must be a non-negative number, got {amount}.");
        }

        var (payout, profitLoss) = Settlement.For(
            status,
            entity.StakeAmount,
            entity.EntryDecimalOdds,
            cashOutPayout is null ? null : Money(cashOutPayout.Value));

        entity.Status = status;
        entity.Payout = payout;
        entity.ProfitLoss = profitLoss;
        entity.SettledUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Corrects what was actually taken: the price, the other prices in the market, the stake.
    /// </summary>
    /// <remarks>
    /// The fair probability and the edge are recomputed rather than kept, because both are functions
    /// of the entry price. Leaving them as they were would produce a row describing a bet at one
    /// price with the edge of a bet at another — and that row is the input to the CLV scoreboard,
    /// which exists precisely to be trusted about what happened. A settled bet has its payout worked
    /// out again for the same reason: it is a function of the price and the stake.
    /// </remarks>
    private static void ApplyEntryEdits(Bet entity, UpdateBetRequest request)
    {
        var entryChanged = false;
        var moneyChanged = false;

        if (request.EntrySiblingOdds is { } siblings)
        {
            if (siblings.Any(o => !(o > 1.0)))
            {
                throw new AnalysisInputException("Every price in the market must be greater than 1.0.");
            }

            entity.EntrySiblingOddsCsv = ClvMath.FormatOddsCsv(siblings);
            entryChanged = true;
        }

        if (request.EntryDecimalOdds is { } entry)
        {
            RequirePrice(entry, "The price taken");
            entity.EntryDecimalOdds = Price(entry);
            entryChanged = true;
            moneyChanged = true;
        }

        if (entryChanged)
        {
            var entrySiblings = ClvMath.ParseOddsCsv(entity.EntrySiblingOddsCsv);
            if (entrySiblings.Length > 0)
            {
                entity.EntryFairProbability = OddsMath.DeVig((double)entity.EntryDecimalOdds, entrySiblings);
                entity.EntryEdgePercentagePoints =
                    (entity.EntryOwnProbability - entity.EntryFairProbability) * 100.0;
            }
        }

        if (request.StakeAmount is { } stake)
        {
            RequireStake(stake);
            entity.StakeAmount = Money(stake);
            entity.StakeFraction = entity.BankrollAtEntry > 0m
                ? (double)(entity.StakeAmount / entity.BankrollAtEntry)
                : 0.0;
            moneyChanged = true;
        }

        // A status in the same request settles from the new numbers anyway; this is for a correction
        // to a bet that was already settled.
        if (moneyChanged && request.Status is null && entity.Status != BetStatus.Open)
        {
            ApplySettlement(
                entity,
                entity.Status,
                entity.Status == BetStatus.CashedOut ? (double?)entity.Payout : null);
        }
    }

    /// <summary>
    /// Re-runs the live model for a bet that is still open, against the match as it is now.
    /// </summary>
    /// <remarks>
    /// Read-only by design — see <see cref="RepricedBet"/>. The one number that cannot be recomputed
    /// is the price: that was taken and is fixed, which is why the EV here is measured against the
    /// entry odds rather than against anything on a screen now.
    /// </remarks>
    public async Task<RepricedBet?> RepriceAsync(
        int id,
        RepriceBetRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await Mine.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (request?.LiveState is null)
        {
            throw new AnalysisInputException(
                "Re-pricing needs the match state as it is now: the minute and the xG accrued so far.");
        }

        var selection = StoredSelection.Parse(entity.MarketKind, entity.Selection);
        var (estimate, model) = AnalysisService.PriceLive(
            entity.MarketKind,
            selection,
            request.LiveState,
            request.Pace,
            request.PreMatch);

        var odds = (double)entity.EntryDecimalOdds;
        var stake = (double)entity.StakeAmount;

        // Measured over the whole stake, so a returned stake counts as the zero it is. The estimate's
        // own probability is conditional on no push — the right number to hold against a de-vigged
        // price, and the wrong one here: on "Under 2" it would credit the bet with a win in every
        // case where the stake merely comes back, and overstate the EV by 1 / (1 - P(push)).
        var expectedValuePerUnit = model.RawWinProbability * odds + model.PushProbability - 1.0;

        return new RepricedBet(
            entity.Id,
            entity.MatchLabel,
            entity.Selection,
            odds,
            stake,
            entity.EntryOwnProbability,
            estimate.OwnProbability,
            (estimate.OwnProbability - entity.EntryOwnProbability) * 100.0,
            expectedValuePerUnit,
            expectedValuePerUnit * stake,
            estimate.OwnProbability > 0.0 ? 1.0 / estimate.OwnProbability : null,
            estimate,
            model);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Mine.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        db.Bets.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>The scoreboard over every bet of the signed-in user.</summary>
    public async Task<BetStats> StatsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Mine.AsNoTracking().ToListAsync(cancellationToken);

        var movements = await db.BankrollTransactions
            .Where(t => t.UserId == user.Id)
            .Select(t => new { t.Kind, t.Amount })
            .ToListAsync(cancellationToken);

        var capital = movements.Sum(t => t.Kind == BankrollTransactionKind.Deposit ? t.Amount : -t.Amount);

        return Summarise(rows.Select(ToDto).ToList(), (double)capital);
    }

    /// <summary>Pure so it can be unit-tested without a database.</summary>
    /// <param name="capital">Deposits less withdrawals, for the ROI. Zero or less leaves the ROI empty.</param>
    public static BetStats Summarise(IReadOnlyList<BetDto> bets, double capital = 0.0)
    {
        var withClose = bets.Where(b => b.ClvPercent is not null).ToList();
        var beatClose = withClose.Count(b => b.BeatTheClose == true);
        var open = bets.Where(b => b.Status == BetStatus.Open).ToList();
        var settled = bets.Where(b => b.Status != BetStatus.Open).ToList();

        double? beatCloseRate = withClose.Count > 0 ? (double)beatClose / withClose.Count : null;
        double? averageClv = withClose.Count > 0 ? withClose.Average(b => b.ClvPercent!.Value) : null;

        var totalStaked = Math.Round(settled.Sum(b => b.StakeAmount), 2);
        var totalPl = Math.Round(settled.Sum(b => b.ProfitLoss ?? 0.0), 2);

        return new BetStats(
            bets.Count,
            open.Count,
            Math.Round(open.Sum(b => b.StakeAmount), 2),
            settled.Count,
            totalStaked,
            totalPl,
            totalStaked > 0.0 ? totalPl / totalStaked : null,
            capital > 0.0 ? totalPl / capital : null,
            withClose.Count,
            beatClose,
            beatCloseRate,
            averageClv,
            BuildReadout(withClose.Count, beatCloseRate));
    }

    private static string BuildReadout(int sampleSize, double? beatCloseRate)
    {
        if (sampleSize == 0)
        {
            return "No closing prices recorded yet. Until they are, there is nothing here to learn from — " +
                   "fill in the closing price on each row once its market settles.";
        }

        var rateText = (beatCloseRate!.Value * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";

        if (sampleSize < MinimumMeaningfulSample)
        {
            return $"Beating the close on {rateText} of {sampleSize} priced bets. That is too small a " +
                   $"sample to mean anything — come back at {MinimumMeaningfulSample}+.";
        }

        var targetText = (TargetBeatCloseRate * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";

        return beatCloseRate >= TargetBeatCloseRate
            ? $"Beating the close on {rateText} of {sampleSize} bets, at or above the {targetText} " +
              "target. The process is finding real prices."
            : $"Beating the close on {rateText} of {sampleSize} bets, below the {targetText} target. " +
              "Over this many bets that points at the estimates, not at luck.";
    }

    private static BetDto ToDto(Bet entity)
    {
        var entrySiblings = ClvMath.ParseOddsCsv(entity.EntrySiblingOddsCsv);
        var closingSiblings = ClvMath.ParseOddsCsv(entity.ClosingSiblingOddsCsv);
        var entryOdds = (double)entity.EntryDecimalOdds;
        var closingOdds = (double?)entity.ClosingDecimalOdds;

        return new BetDto(
            entity.Id,
            entity.CreatedUtc,
            entity.MatchLabel,
            entity.MarketKind,
            entity.MarketLabel,
            entity.Selection,
            entryOdds,
            entrySiblings,
            entity.EntryFairProbability,
            entity.EntryOwnProbability,
            entity.EntryEdgePercentagePoints,
            entity.EstimateMethod,
            (double)entity.BankrollAtEntry,
            (double)entity.StakeAmount,
            entity.StakeFraction,
            entity.Verdict,
            entity.WasLive,
            entity.MinutePlayed,
            entity.CumulativeXgHome,
            entity.CumulativeXgAway,
            entity.GoalsHome,
            entity.GoalsAway,
            closingOdds,
            closingSiblings,
            entity.Status,
            entity.Notes,
            ClvMath.ClvPercent(entryOdds, closingOdds),
            ClvMath.BeatTheClose(entryOdds, closingOdds),
            ClvMath.ClvProbabilityPoints(entity.EntryFairProbability, closingOdds, closingSiblings),
            (double?)entity.ProfitLoss,
            (double?)entity.Payout,
            entity.SettledUtc,
            entity.Sport,
            entity.Competition,
            entity.Bookmaker,
            entity.EventStartUtc);
    }

    // --- Input rules ----------------------------------------------------------

    /// <summary>Money to the cent, rounded half away from zero like a bookmaker's slip.</summary>
    internal static decimal Money(double value) => Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero);

    /// <summary>A decimal price to four places.</summary>
    internal static decimal Price(double value) => Math.Round((decimal)value, 4, MidpointRounding.AwayFromZero);

    private static void RequirePrice(double value, string what)
    {
        if (!(value > 1.0) || !double.IsFinite(value))
        {
            throw new AnalysisInputException($"{what} must be greater than 1.0, got {value}.");
        }
    }

    private static void RequirePrices(IEnumerable<double> values, string what)
    {
        if (values.Any(v => !(v > 1.0) || !double.IsFinite(v)))
        {
            throw new AnalysisInputException($"{what} must also be greater than 1.0.");
        }
    }

    private static void RequireStake(double value)
    {
        if (!(value >= 0.0) || !double.IsFinite(value))
        {
            throw new AnalysisInputException($"The stake must be a non-negative number, got {value}.");
        }
    }

    private static void RequireProbability(double value, string what)
    {
        if (!(value >= 0.0 && value <= 1.0))
        {
            throw new AnalysisInputException($"{what} must be between 0 and 1, got {value}.");
        }
    }

    /// <summary>Trims, and refuses text longer than its column rather than letting the database cut it.</summary>
    private static string? Text(string? value, int max, string what)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max
            ? trimmed
            : throw new AnalysisInputException($"{what} is limited to {max} characters, got {trimmed.Length}.");
    }

    /// <summary>An emptied optional field is stored as missing, not as an empty string.</summary>
    private static string? Blank(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Timestamps are stored as UTC; Npgsql refuses to write any other kind into timestamptz.</summary>
    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        var unspecified => DateTime.SpecifyKind(unspecified.Value, DateTimeKind.Utc),
    };
}
