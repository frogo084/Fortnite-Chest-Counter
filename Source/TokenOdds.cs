namespace LootTracker;

internal sealed record OddsEstimate(double Chance, double MissChance, double Chests, double Matches, double MatchesForHalf, bool Guaranteed)
{
    public string Ratio => Guaranteed ? "100%" : Chance <= 0 ? "No openings" : Chance > .9999 ? "> 99.99%" : "1 in " + (1 / Chance).ToString(Chance > .1 ? "0.##" : "N0");
    public string Percent => !Guaranteed && Chance > .999999 ? "> 99.9999%" : (Chance * 100).ToString("0.0000") + "%";
}

internal static class TokenOdds
{
    static double LogMiss(double n, double p) => n <= 0 ? 0 : n * Math.Log(1 - Math.Clamp(p, 0, 1));

    internal static OddsEstimate Calculate(double chests, double boosted, double denominator, double boost, int party, double matches = 0, double perMatch = 0, double boostedFraction = 0)
    {
        if (!double.IsFinite(chests) || !double.IsFinite(boosted) || !double.IsFinite(denominator) || !double.IsFinite(boost)
            || chests < 0 || boosted < 0 || boosted > chests || denominator < 1 || boost < 1 || party is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(chests), "Check the chest counts, rate and party size.");
        var p = 1 / denominator;
        var log = LogMiss((chests - boosted) * party, p) + LogMiss(boosted * party, Math.Min(1, p * boost));
        var miss = Math.Exp(log);
        var perMatchLog = LogMiss(perMatch * (1 - boostedFraction) * party, p) + LogMiss(perMatch * boostedFraction * party, Math.Min(1, p * boost));
        var half = perMatchLog == 0 ? double.PositiveInfinity : double.IsNegativeInfinity(perMatchLog) ? 1 : Math.Ceiling(Math.Log(.5) / perMatchLog);
        return new(Math.Clamp(1 - miss, 0, 1), miss, chests * party, matches, half, double.IsNegativeInfinity(log));
    }

    internal static OddsEstimate Forecast(double hours, double days, double perMatch, double minutes, double boostedHours, double denominator, double boost, int party)
    {
        var totalHours = hours * days;
        if (hours < 0 || hours > 24 || days < 0 || days > 3660 || perMatch < 0 || perMatch > 1000
            || minutes <= 0 || minutes > 240 || boostedHours < 0 || boostedHours > totalHours)
            throw new ArgumentOutOfRangeException(nameof(hours), "Treasure Hours must fit inside your total planned playtime.");
        var matches = Math.Floor(totalHours * 60 / minutes);
        var chests = Math.Floor(matches * perMatch);
        var boosted = Math.Min(chests, Math.Floor(boostedHours * 60 / minutes * perMatch));
        return Calculate(chests, boosted, denominator, boost, party, matches, perMatch, totalHours == 0 ? 0 : boostedHours / totalHours);
    }

    internal static void SelfCheck()
    {
        var forecast = Forecast(3, 24, 25, 20, 0, 137500, 1.5, 1);
        if (forecast.Matches != 216 || forecast.Chests != 5400 || forecast.Ratio != "1 in 26") throw new InvalidOperationException("Forecast example failed.");
        var past = Calculate(2000, 0, 137500, 1.5, 1);
        if (past.Ratio != "1 in 69" || Math.Abs(past.MissChance * 100 - 98.556) > .001) throw new InvalidOperationException("Past odds example failed.");
        if (Calculate(0, 0, 1, 2, 1).Chance != 0 || !Calculate(1, 0, 1, 2, 1).Guaranteed) throw new InvalidOperationException("Probability edge failed.");
        if (Calculate(100, 50, 137500, 2, 1).Chance <= Calculate(100, 0, 137500, 2, 1).Chance) throw new InvalidOperationException("Boost scenario failed.");
        if (Calculate(100, 0, 137500, 1.5, 4).Chance <= Calculate(100, 0, 137500, 1.5, 1).Chance) throw new InvalidOperationException("Party scenario failed.");
        var large = Calculate(3012000, 0, 137500, 1.5, 1);
        if (large.Guaranteed || large.Percent == "100.0000%" || string.IsNullOrWhiteSpace(large.Ratio)) throw new InvalidOperationException("High odds display failed.");
        bool rejected = false;
        try { Calculate(20, 21, 137500, 2, 1); } catch (ArgumentOutOfRangeException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("Invalid boosted count was accepted.");
    }
}
