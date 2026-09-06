using Mirage.Shared.Records;

namespace Mirage.Shared;

/// <summary>
/// The one place that answers "may this class use this thing?".
///
/// <para>Items, spells and quests each restrict themselves to a set of classes, held as a list of
/// 1-based class ids on the record. **An empty or absent list means every class** — that is the common
/// case, so it is the one that costs nothing to author and nothing to store.</para>
///
/// <para>A list rather than a bit mask, deliberately. <see cref="Constants.MaxClasses"/> is a knob, not
/// a fact: a mask would cap the world at whatever its integer holds (15 in a <c>short</c>, 31 in an
/// <c>int</c>, 63 in a <c>long</c>) and would fail quietly the first time that ceiling was raised past
/// it. A list has no ceiling, and it reads as itself in a data file — <c>[1, 2, 3]</c> rather than a
/// <c>7</c> the reader has to decode bit by bit, which is the packed-slot habit this codebase spent an
/// afternoon getting rid of. The cost is a scan of a handful of entries on equip, learn and quest
/// acceptance, none of which is a hot path.</para>
/// </summary>
public static class ClassGate
{
    /// <summary>Whether <paramref name="playerClass"/> may use something gated by
    /// <paramref name="allowedClasses"/>. Null and empty both mean unrestricted, so a caller never has
    /// to care which of the two a record happens to hold.</summary>
    public static bool Allows(IReadOnlyList<short>? allowedClasses, int playerClass) =>
        allowedClasses is not { Count: > 0 } || allowedClasses.Contains((short)playerClass);

    /// <summary>Whether a gate is set at all — i.e. whether it is worth showing a requirement line.</summary>
    public static bool IsRestricted(IReadOnlyList<short>? allowedClasses) =>
        allowedClasses is { Count: > 0 };

    /// <summary>Canonical stored form: drop ids outside the class table, drop duplicates, sort ascending,
    /// and collapse an empty result to null so an unrestricted row carries no key at all.
    /// <para>Called from every save path, exactly as the record <c>Normalize</c> methods are — two rows
    /// allowing the same classes in a different order should not read as different rows in a diff.</para></summary>
    public static List<short>? Normalize(IReadOnlyList<short>? allowedClasses)
    {
        if (allowedClasses is not { Count: > 0 }) return null;

        List<short>? kept = null;
        foreach (short id in allowedClasses)
        {
            if (!SlotValidation.IsValidClassNum(id)) continue;
            kept ??= new List<short>(allowedClasses.Count);
            if (!kept.Contains(id)) kept.Add(id);
        }
        kept?.Sort();
        return kept;
    }

    /// <summary>The gate as a readable list of class names — "Warrior, Soldier, Knight" — for a tooltip
    /// or a chat message. Unknown or out-of-range ids are skipped rather than rendered as "?", since a
    /// requirement naming a class that does not exist is noise to a player either way.
    /// <para>Returns an empty string when nothing is restricted; callers already test
    /// <see cref="IsRestricted"/> before showing a line.</para></summary>
    public static string Describe(IReadOnlyList<short>? allowedClasses, IReadOnlyList<ClassRecord?> classes)
    {
        if (allowedClasses is not { Count: > 0 }) return "";

        var names = new List<string>(allowedClasses.Count);
        foreach (short id in allowedClasses)
        {
            if (id <= 0 || id >= classes.Count) continue;
            string? name = classes[id]?.TrimmedName;
            if (!string.IsNullOrEmpty(name)) names.Add(name);
        }
        return string.Join(", ", names);
    }
}
