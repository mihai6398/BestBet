using System.Globalization;
using BestBet.Contracts;
using BestBet.Data;
using BestBet.Data.Entities;
using BestBet.Domain;
using Microsoft.EntityFrameworkCore;

namespace BestBet.Services;

/// <summary>
/// Reads and writes the positions log, and computes the CLV scoreboard over it.
/// </summary>
public class PositionService(BestBetDbContext db)
{
    /// <summary>Below this many positions the CLV rate is noise, and the readout says so.</summary>
    public const int MinimumMeaningfulSample = 30;

    /// <summary>The rate the underlying strategy treats as evidence that the process works.</summary>
    public const double TargetBeatCloseRate = 0.55;

    public async Task<PositionDto> SaveAsync(SavePositionRequest request, CancellationToken cancellationToken = default)
    {
        var entity = new PositionLog
        {
            CreatedUtc = DateTime.UtcNow,
            MatchLabel = request.MatchLabel ?? string.Empty,
            MarketKind = request.MarketKind,
            MarketLabel = request.MarketLabel ?? string.Empty,
            Selection = request.Selection ?? string.Empty,
            EntryDecimalOdds = request.EntryDecimalOdds,
            EntrySiblingOddsCsv = ClvMath.FormatOddsCsv(request.EntrySiblingOdds),
            EntryFairProbability = request.EntryFairProbability,
            EntryOwnProbability = request.EntryOwnProbability,
            EntryEdgePercentagePoints = request.EntryEdgePercentagePoints,
            EstimateMethod = request.EstimateMethod ?? string.Empty,
            BankrollAtEntry = request.BankrollAtEntry,
            StakeAmount = request.StakeAmount,
            StakeFraction = request.StakeFraction,
            Verdict = request.Verdict,
            WasLive = request.WasLive,
            MinutePlayed = request.MinutePlayed,
            CumulativeXgHome = request.CumulativeXgHome,
            CumulativeXgAway = request.CumulativeXgAway,
            GoalsHome = request.GoalsHome,
            GoalsAway = request.GoalsAway,
            Notes = request.Notes,
        };

        db.Positions.Add(entity);
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
    public async Task<PositionDto> ImportAsync(
        ImportPositionRequest request,
        CancellationToken cancellationToken = default)
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

        return await SaveAsync(new SavePositionRequest(
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
            notes), cancellationToken);
    }

    private static (double Probability, string Method) PriceAtEntry(
        MarketKind kind,
        MarketSelectionSpec selection,
        LiveMatchState state)
    {
        var (estimate, _) = AnalysisService.PriceLive(kind, selection, state, pace: null);
        return (estimate.OwnProbability, "slip-import-model");
    }

    public async Task<IReadOnlyList<PositionDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Positions
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedUtc)
            .ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    public async Task<PositionDto?> UpdateAsync(
        int id,
        UpdatePositionRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await db.Positions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        ApplyEntryEdits(entity, request);

        if (request.ClosingDecimalOdds is { } closing)
        {
            entity.ClosingDecimalOdds = closing > 1.0 ? closing : null;
        }

        if (request.ClosingSiblingOdds is { } siblings)
        {
            entity.ClosingSiblingOddsCsv = ClvMath.FormatOddsCsv(siblings);
        }

        if (request.Outcome is { } outcome)
        {
            entity.Outcome = outcome;
        }

        if (request.Notes is not null)
        {
            entity.Notes = request.Notes;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    /// <summary>
    /// Corrects what was actually taken: the price, the other prices in the market, the stake.
    /// </summary>
    /// <remarks>
    /// The fair probability and the edge are recomputed rather than kept, because both are functions
    /// of the entry price. Leaving them as they were would produce a row describing a bet at one
    /// price with the edge of a bet at another — and that row is the input to the CLV scoreboard,
    /// which exists precisely to be trusted about what happened.
    /// </remarks>
    private static void ApplyEntryEdits(PositionLog entity, UpdatePositionRequest request)
    {
        var entryChanged = false;

        if (request.EntrySiblingOdds is { } siblings)
        {
            if (siblings.Any(o => o <= 1.0))
            {
                throw new AnalysisInputException("Every price in the market must be greater than 1.0.");
            }

            entity.EntrySiblingOddsCsv = ClvMath.FormatOddsCsv(siblings);
            entryChanged = true;
        }

        if (request.EntryDecimalOdds is { } entry)
        {
            if (entry <= 1.0)
            {
                throw new AnalysisInputException(
                    $"The price taken must be greater than 1.0, got {entry}.");
            }

            entity.EntryDecimalOdds = entry;
            entryChanged = true;
        }

        if (entryChanged)
        {
            var entrySiblings = ClvMath.ParseOddsCsv(entity.EntrySiblingOddsCsv);
            if (entrySiblings.Length > 0)
            {
                entity.EntryFairProbability = OddsMath.DeVig(entity.EntryDecimalOdds, entrySiblings);
                entity.EntryEdgePercentagePoints =
                    (entity.EntryOwnProbability - entity.EntryFairProbability) * 100.0;
            }
        }

        if (request.StakeAmount is { } stake)
        {
            if (stake < 0.0 || double.IsNaN(stake) || double.IsInfinity(stake))
            {
                throw new AnalysisInputException($"The stake must be a non-negative number, got {stake}.");
            }

            entity.StakeAmount = stake;
            entity.StakeFraction = entity.BankrollAtEntry > 0.0 ? stake / entity.BankrollAtEntry : 0.0;
        }
    }

    /// <summary>
    /// Re-runs the live model for a position that is still open, against the match as it is now.
    /// </summary>
    /// <remarks>
    /// Read-only by design — see <see cref="RepricedPosition"/>. The one number that cannot be
    /// recomputed is the price: that was taken and is fixed, which is why the EV here is measured
    /// against the entry odds rather than against anything on a screen now.
    /// </remarks>
    public async Task<RepricedPosition?> RepriceAsync(
        int id,
        RepricePositionRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await db.Positions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
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

        // Measured over the whole stake, so a returned stake counts as the zero it is. The estimate's
        // own probability is conditional on no push — the right number to hold against a de-vigged
        // price, and the wrong one here: on "Under 2" it would credit the bet with a win in every
        // case where the stake merely comes back, and overstate the EV by 1 / (1 - P(push)).
        var expectedValuePerUnit =
            model.RawWinProbability * entity.EntryDecimalOdds + model.PushProbability - 1.0;

        return new RepricedPosition(
            entity.Id,
            entity.MatchLabel,
            entity.Selection,
            entity.EntryDecimalOdds,
            entity.StakeAmount,
            entity.EntryOwnProbability,
            estimate.OwnProbability,
            (estimate.OwnProbability - entity.EntryOwnProbability) * 100.0,
            expectedValuePerUnit,
            expectedValuePerUnit * entity.StakeAmount,
            estimate.OwnProbability > 0.0 ? 1.0 / estimate.OwnProbability : null,
            estimate,
            model);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await db.Positions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        db.Positions.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PositionsSummary> SummariseAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Positions.AsNoTracking().ToListAsync(cancellationToken);
        return Summarise(rows.Select(ToDto).ToList());
    }

    /// <summary>Pure so it can be unit-tested without a database.</summary>
    public static PositionsSummary Summarise(IReadOnlyList<PositionDto> positions)
    {
        var withClose = positions.Where(p => p.ClvPercent is not null).ToList();
        var beatClose = withClose.Count(p => p.BeatTheClose == true);
        var settled = positions.Where(p => p.Outcome != PositionOutcome.Pending).ToList();

        double? beatCloseRate = withClose.Count > 0 ? (double)beatClose / withClose.Count : null;
        double? averageClv = withClose.Count > 0 ? withClose.Average(p => p.ClvPercent!.Value) : null;

        var totalStaked = settled.Sum(p => p.StakeAmount);
        var totalPl = settled.Sum(p => p.ProfitLoss ?? 0.0);

        return new PositionsSummary(
            positions.Count,
            withClose.Count,
            beatClose,
            beatCloseRate,
            averageClv,
            settled.Count,
            totalStaked,
            totalPl,
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
            return $"Beating the close on {rateText} of {sampleSize} priced positions. That is too small a " +
                   $"sample to mean anything — come back at {MinimumMeaningfulSample}+.";
        }

        var targetText = (TargetBeatCloseRate * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";

        return beatCloseRate >= TargetBeatCloseRate
            ? $"Beating the close on {rateText} of {sampleSize} positions, at or above the {targetText} " +
              "target. The process is finding real prices."
            : $"Beating the close on {rateText} of {sampleSize} positions, below the {targetText} target. " +
              "Over this many bets that points at the estimates, not at luck.";
    }

    private static PositionDto ToDto(PositionLog entity)
    {
        var entrySiblings = ClvMath.ParseOddsCsv(entity.EntrySiblingOddsCsv);
        var closingSiblings = ClvMath.ParseOddsCsv(entity.ClosingSiblingOddsCsv);

        return new PositionDto(
            entity.Id,
            entity.CreatedUtc,
            entity.MatchLabel,
            entity.MarketKind,
            entity.MarketLabel,
            entity.Selection,
            entity.EntryDecimalOdds,
            entrySiblings,
            entity.EntryFairProbability,
            entity.EntryOwnProbability,
            entity.EntryEdgePercentagePoints,
            entity.EstimateMethod,
            entity.BankrollAtEntry,
            entity.StakeAmount,
            entity.StakeFraction,
            entity.Verdict,
            entity.WasLive,
            entity.MinutePlayed,
            entity.CumulativeXgHome,
            entity.CumulativeXgAway,
            entity.GoalsHome,
            entity.GoalsAway,
            entity.ClosingDecimalOdds,
            closingSiblings,
            entity.Outcome,
            entity.Notes,
            ClvMath.ClvPercent(entity.EntryDecimalOdds, entity.ClosingDecimalOdds),
            ClvMath.BeatTheClose(entity.EntryDecimalOdds, entity.ClosingDecimalOdds),
            ClvMath.ClvProbabilityPoints(entity.EntryFairProbability, entity.ClosingDecimalOdds, closingSiblings),
            ProfitLoss(entity));
    }

    /// <summary>Realised P/L in currency. Null while the position is unsettled.</summary>
    private static double? ProfitLoss(PositionLog entity) => entity.Outcome switch
    {
        PositionOutcome.Won => entity.StakeAmount * (entity.EntryDecimalOdds - 1.0),
        PositionOutcome.Lost => -entity.StakeAmount,
        PositionOutcome.Void => 0.0,
        _ => null,
    };
}
