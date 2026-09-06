namespace Mirage.Shared.Records;

/// <summary>A perpetual record of one finished season's final leaderboard ("archived in
/// perpetuity") — written to <c>seasons/season{N}.json</c> at season end. Bragging rights that outlive the
/// per-season score reset.</summary>
public sealed class SeasonArchive
{
    /// <summary>The season number this archive is for (1-based).</summary>
    public int Season { get; set; }
    /// <summary>Server-local date the season ended, ISO <c>yyyy-MM-dd</c>.</summary>
    public string EndDate { get; set; } = "";
    /// <summary>Final standings, ordered best-first.</summary>
    public List<SeasonStanding> Standings { get; set; } = new();
}

/// <summary>One guild's final standing in an archived season.</summary>
public sealed class SeasonStanding
{
    /// <summary>1-based placing among the SCORING guilds; 0 = did not score.</summary>
    public int Placing { get; set; }
    public string Guild { get; set; } = "";
    public long Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
}
