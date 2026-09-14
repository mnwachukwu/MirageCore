namespace Mirage.Modules.Survey;

/// <summary>
/// The names this game uses, in one place.
///
/// <para><b>Every one of these is persisted, sent, or both</b> — an attribute key reaches a save file and
/// a socket, a family id is a folder on disk and a section in the editor. Renaming one is a data change
/// rather than a refactor, which is why they are constants here instead of literals at the call sites.</para>
/// </summary>
public static class Survey
{
    // ── What a surveyor carries ───────────────────────────────────────────────

    /// <summary>How much walking is left in them today.</summary>
    public const string Stamina = "stamina";

    /// <summary>How much there is when rested. Read beside <see cref="Stamina"/> by the overhead bar and
    /// by the heads-up meter, which is why both are declared Viewport — a bar over somebody else's head
    /// is useless if only they can see the numbers behind it.</summary>
    public const string StaminaMax = "staminaMax";

    /// <summary>How many distinct species they have cataloged.</summary>
    public const string Specimens = "specimens";

    /// <summary>What that count has earned them. A word rather than a number, so the display field that
    /// shows it needs no table to read it against.</summary>
    public const string Rank = "rank";

    // ── What the world holds ──────────────────────────────────────────────────

    /// <summary>The family of things there are to find. Its records are authored in the editor like any
    /// other, and Core has never heard of it.</summary>
    public const string Species = "species";

    /// <summary>Where a species grows. A closed set, so the editor offers a picker rather than a
    /// free-text box an author can misspell.</summary>
    public const string Habitats = "habitat";

    /// <summary>The one place a surveyor can carry something.</summary>
    public const string Satchel = "satchel";

    /// <summary>The field book: the screen a surveyor reads their own record in.</summary>
    public const string FieldBook = "survey.fieldbook";

    /// <summary>The display surface that fills it. Its own, so the sidebar and the book can show
    /// different amounts of the same thing.</summary>
    public const string BookSurface = "survey.book";

    // ── Tuning ────────────────────────────────────────────────────────────────

    /// <summary>What a rested surveyor starts the day with.</summary>
    public const int FullStamina = 40;

    /// <summary>One tile walked, one point spent.</summary>
    public const int StepCost = 1;

    /// <summary>One point back every this many ticks. Slower than walking spends it, so a survey has a
    /// shape: range out, find things, come back.</summary>
    public const int RecoveryEveryTicks = 20;

    /// <summary>One step in this many turns up something worth writing down.</summary>
    public const int FindOneStepIn = 12;

    /// <summary>How long a surveyor's body stays in the world after their connection drops. Long enough
    /// that a dropped line is not a lost afternoon, short enough that nobody is left standing in a
    /// meadow overnight.</summary>
    public const int LingerSeconds = 30;

    /// <summary>How long a find is held for whoever turned it up. Long enough to bend down, short enough
    /// that a specimen nobody wants is not fenced off all afternoon.</summary>
    public const int ClaimSeconds = 20;

    /// <summary>The count at which a surveyor starts seeing what a beginner walks past — the same rung
    /// <see cref="RankFor"/> calls a Naturalist, read from there rather than restated as a number.</summary>
    public const int PracticedEye = 20;

    /// <summary>And how much more often their finds turn something up, as a percent of the chance the
    /// world authored.</summary>
    public const int PracticedEyeBonusPercent = 25;

    /// <summary>The rank a count has earned. Deliberately a lookup a GAME owns: Core has no notion of
    /// progression, and this is the whole of what this one means by it.</summary>
    public static string RankFor(long specimens) => specimens switch
    {
        >= 40 => "Botanist",
        >= 20 => "Naturalist",
        >= 8 => "Field Hand",
        >= 1 => "Apprentice",
        _ => "Unenrolled",
    };
}
