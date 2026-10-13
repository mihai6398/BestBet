namespace BestBet.Domain;

/// <summary>
/// Which side of a market the priced outcome sits on. The bookmaker's <see cref="MarketOdds.Label"/>
/// is free text in whatever language the site uses ("Sub 4", "Da", "Sevilla FC"), so the label alone
/// is not a reliable key. The UI sends this explicitly; the parser is only a fallback.
/// </summary>
public enum MarketSelection
{
    /// <summary>1X2: home win.</summary>
    Home,
    /// <summary>1X2: draw.</summary>
    Draw,
    /// <summary>1X2: away win.</summary>
    Away,
    /// <summary>Total goals strictly above <see cref="MarketSelectionSpec.Line"/>.</summary>
    Over,
    /// <summary>Total goals strictly below <see cref="MarketSelectionSpec.Line"/>.</summary>
    Under,
    /// <summary>Both teams to score: yes.</summary>
    Yes,
    /// <summary>Both teams to score: no.</summary>
    No,
    /// <summary>Next (or first) goal scored by the home team.</summary>
    NextGoalHome,
    /// <summary>Next (or first) goal scored by the away team.</summary>
    NextGoalAway,
    /// <summary>No further goal before full time.</summary>
    NextGoalNone,
    /// <summary>Handicap applied to the home team, e.g. Home -1.5.</summary>
    HandicapHome,
    /// <summary>Handicap applied to the away team, e.g. Away +1.5.</summary>
    HandicapAway
}

/// <param name="Side">Which outcome of the market is being priced.</param>
/// <param name="Line">Goal line for <see cref="MarketKind.TotalGoals"/> (e.g. 2.5, 4) and
/// <see cref="MarketKind.Handicap"/> (e.g. -1.5, signed from the perspective of the selected team).
/// Null for markets that have no line.</param>
public record MarketSelectionSpec(MarketSelection Side, double? Line);
