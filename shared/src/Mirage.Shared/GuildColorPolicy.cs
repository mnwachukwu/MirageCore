namespace Mirage.Shared;

/// <summary>
/// A guild's overhead color is a free 24-bit RGB value (packed <c>0xRRGGBB</c>), with one restriction:
/// it may not land on — or within a small tolerance of — any of the 16 named <see cref="GameColor"/>
/// palette entries. Those carry game-semantic meaning (PK/combat red, system yellow, the access-rank
/// name colors, ...), so guild colors are kept visibly distinct from them rather than being near-
/// duplicates. Freedom is bounded <i>by</i> the palette, not limited <i>to</i> it.
///
/// The reserved set and the packed-RGB helpers come from <see cref="GameColor"/> (the single source of
/// truth for the palette), so this can't drift from what the client renders. Lives in Shared so the
/// SERVER is authoritative: <c>GuildSystem</c> rejects a reserved value on receipt; the client
/// mirrors <see cref="IsReserved"/> only to block the picker's Confirm early for UX.
/// </summary>
public static class GuildColorPolicy
{
    /// <summary>True when <paramref name="rgb"/> is a reserved palette color or within
    /// <see cref="Constants.GuildColorReservedDistanceSq"/> (squared-Euclidean RGB) of one — i.e. not a
    /// legal guild color.</summary>
    public static bool IsReserved(int rgb)
    {
        int r = GameColor.RedOf(rgb), g = GameColor.GreenOf(rgb), b = GameColor.BlueOf(rgb);
        foreach (int p in GameColor.Rgb)
        {
            int dr = r - GameColor.RedOf(p), dg = g - GameColor.GreenOf(p), db = b - GameColor.BlueOf(p);
            if (dr * dr + dg * dg + db * db <= Constants.GuildColorReservedDistanceSq) return true;
        }
        return false;
    }
}
