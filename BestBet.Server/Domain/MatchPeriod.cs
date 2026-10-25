namespace BestBet.Domain;

/// <summary>
/// Which part of the match the clock is in.
/// </summary>
/// <remarks>
/// The minute alone cannot say. A clock reading "45+2" is turned into minute 47, which is also what
/// the second half shows two minutes after the restart — and the two have very different amounts of
/// football left: a few seconds of first-half stoppage plus a whole half, against forty-odd minutes.
/// A provider knows which it is; a typed minute usually does not, so the period is optional and the
/// minute is read on its own when it is missing.
/// </remarks>
public enum MatchPeriod
{
    FirstHalf,
    HalfTime,
    SecondHalf,
}
