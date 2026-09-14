using System.Collections.Generic;
using Mirage.Shared.Extensibility;

namespace Mirage.Editor.Localization;

/// <summary>
/// The shape behind every name in <see cref="GameIcon.Offered"/>, as this editor draws it.
///
/// <para><b>Vector geometry rather than an icon font.</b> A font renders differently — or not at all —
/// off Windows, and a rail has to look the same everywhere. Each glyph is drawn on a 16x16 box.</para>
///
/// <para>🔴 <b>This is one half of a pair.</b> The names are Core's and the client holds its own table
/// for the same set; a name offered with no shape here draws nothing at all. <c>GameIconTests</c> holds
/// the three sets equal, which is the only thing that catches a glyph added to the vocabulary and
/// drawn nowhere.</para>
/// </summary>
public static class GameIconPaths
{
    private static readonly Dictionary<string, string> ByName = new(System.StringComparer.Ordinal)
    {
        // ── Places and collections ──────────────────────────────────────────────
        ["grid"] =
            "M2,2h3v3h-3z M6.5,2h3v3h-3z M11,2h3v3h-3z " +
            "M2,6.5h3v3h-3z M6.5,6.5h3v3h-3z M11,6.5h3v3h-3z " +
            "M2,11h3v3h-3z M6.5,11h3v3h-3z M11,11h3v3h-3z",

        ["quads"] = "M2,2h5.5v5.5h-5.5z M8.5,2h5.5v5.5h-5.5z M2,8.5h5.5v5.5h-5.5z M8.5,8.5h5.5v5.5h-5.5z",

        ["pin"] =
            "M8,1.2a4.8,4.8 0 0 0-4.8,4.8c0,3.5 4.8,8.8 4.8,8.8s4.8,-5.3 4.8,-8.8a4.8,4.8 0 0 0-4.8,-4.8z "
            + "M8,4.1a1.9,1.9 0 1 0 0,3.8a1.9,1.9 0 0 0 0,-3.8z",

        ["flag"] = "M2.8,1.4h1.7v13.2h-1.7z M5.4,2.2h8.4l-2.2,3l2.2,3h-8.4z",

        // ── Things somebody carries, makes, or spends ───────────────────────────
        ["bag"] = "M5,5V4.2A3,3 0 0 1 11,4.2V5h2.2l0.9,9H1.9L2.8,5z M6.6,5h2.8V4.2a1.4,1.4 0 0 0-2.8,0z",

        ["gem"] = "M4.6,2.2h6.8l3.2,4.2l-6.6,7.8L1.4,6.4z M1.9,5.9h12.2v0.9H1.9z",

        ["coin"] =
            "M8,1.6a6.4,6.4 0 1 0 0,12.8a6.4,6.4 0 0 0 0,-12.8z "
            + "M8,3.8a4.2,4.2 0 1 0 0,8.4a4.2,4.2 0 0 0 0,-8.4z "
            + "M8,5a3,3 0 1 0 0,6a3,3 0 0 0 0,-6z",

        ["sword"] = "M8,0.6l2.2,3.6v6.4h-4.4V4.2z M2.6,10.6h10.8v1.7H2.6z M7.1,12.3h1.8v2.2H7.1z M6.2,14.5h3.6v1.1H6.2z",

        ["flask"] =
            "M6.2,1.4h3.6v1.5h-0.7v3.3l4.3,6.7a1.1,1.1 0 0 1-0.95,1.7H3.5a1.1,1.1 0 0 1-0.95,-1.7l4.3,-6.7V2.9h-0.7z",

        ["cog"] =
            "M6.9,1h2.2l0.35,1.9l1.45,0.6l1.6,-1.1l1.55,1.55l-1.1,1.6l0.6,1.45L15,7.45v2.2l-1.9,0.35l-0.6,1.45l1.1,1.6"
            + "l-1.55,1.55l-1.6,-1.1l-1.45,0.6L9.1,15.6H6.9l-0.35,-1.9l-1.45,-0.6l-1.6,1.1L1.95,12.65l1.1,-1.6L2.45,9.6"
            + "L0.55,9.25v-2.2L2.45,6.7l0.6,-1.45l-1.1,-1.6L3.5,2.1l1.6,1.1l1.45,-0.6z "
            + "M8,5.6a2.5,2.5 0 1 0 0,5a2.5,2.5 0 0 0 0,-5z",

        // ── Living things ───────────────────────────────────────────────────────
        ["person"] =
            "M8,1.8a2.6,2.6 0 1 1 0,5.2A2.6,2.6 0 0 1 8,1.8z M8,8.3c3.1,0 5.3,1.7 5.3,3.7V14.2H2.7v-2.2c0-2 2.2-3.7 5.3-3.7z",

        ["paw"] =
            "M3.7,4.4a1.5,1.9 0 1 1 0,3.8a1.5,1.9 0 0 1 0,-3.8z "
            + "M6.6,2.2a1.5,1.9 0 1 1 0,3.8a1.5,1.9 0 0 1 0,-3.8z "
            + "M9.4,2.2a1.5,1.9 0 1 1 0,3.8a1.5,1.9 0 0 1 0,-3.8z "
            + "M12.3,4.4a1.5,1.9 0 1 1 0,3.8a1.5,1.9 0 0 1 0,-3.8z "
            + "M8,8.2c2.5,0 4.5,1.6 4.5,3.2s-2,2.7-4.5,2.7s-4.5,-1.1-4.5,-2.7s2,-3.2 4.5,-3.2z",

        ["leaf"] =
            "M14.2,1.6C7.4,1.6 2,5.5 2,10.3c0,1.3 0.4,2.4 1,3.3C4.9,8.3 8.4,5.2 12.2,3.9"
            + "C8.8,5.8 5.9,9 4.2,14c1,0.4 2.1,0.6 3.3,0.6C12.2,14.6 14.2,8.4 14.2,1.6z",

        ["heart"] =
            "M8,14.4l-1.1,-1C3,9.7 1,7.9 1,5.6A3.9,3.9 0 0 1 4.9,1.7c1.3,0 2.5,0.6 3.1,1.6a3.9,3.9 0 0 1 3.1,-1.6"
            + "A3.9,3.9 0 0 1 15,5.6c0,2.3-2,4.1-5.9,7.8z",

        // ── What is written down ────────────────────────────────────────────────
        ["book"] =
            "M2.2,1.8h1.9v12.4H2.2z M4.7,1.8h9.1v12.4H4.7z "
            + "M6.3,4.4h5.9v1.2H6.3z M6.3,7h5.9v1.2H6.3z M6.3,9.6h5.9v1.2H6.3z",

        // One roll, at the top, with the sheet hanging from it. Two rolls read as a dumbbell: the
        // bars dominate and the sheet between them becomes the waist of an I-beam.
        ["scroll"] =
            "M2.6,1.6h10.8a1.5,1.5 0 0 1 0,3H2.6a1.5,1.5 0 0 1 0,-3z "
            + "M3.6,4.6h8.8v9.8H3.6z "
            + "M5.4,6.4h5.2v1.1H5.4z M5.4,8.8h5.2v1.1H5.4z M5.4,11.2h3.4v1.1H5.4z",

        ["list"] = "M3,2.6h10v1.5H3z M3,5.8h10v1.5H3z M3,9h10v1.5H3z M3,12.2h6v1.5H3z",

        ["bubble"] = "M2,2.6h12v7.8H8.6l-3.4,3.2v-3.2H2z",

        // ── Everything else a game counts ───────────────────────────────────────
        // Deliberately not another person glyph for accounts: at rail size two figures are one
        // silhouette, and what that section is about is access.
        ["key"] =
            "M9.8,1.6a4.6,4.6 0 1 1-3.1,8L5.4,10.9H3.6v1.8H1.8v1.8H0v-2.6l6-6a4.6,4.6 0 0 1 3.8-4.3z "
            + "M10.6,4a1.3,1.3 0 1 0 0,2.6a1.3,1.3 0 0 0 0-2.6z",

        ["shield"] = "M8,1.4l5.6,2.2v4.2c0,3.3-2.4,6.2-5.6,6.8c-3.2-0.6-5.6-3.5-5.6-6.8V3.6z",

        ["star"] =
            "M8,1.1l2.05,4.3l4.75,0.63l-3.5,3.3l0.88,4.7L8,11.8l-4.18,2.23l0.88,-4.7l-3.5,-3.3l4.75,-0.63z",

        ["spark"] = "M8,1.4l1.7,4.9l4.9,1.7l-4.9,1.7L8,14.6l-1.7-4.9L1.4,8l4.9-1.7z",

        ["flame"] =
            "M8.4,0.8c2.4,2.7 4.4,4.5 4.4,7.5A4.8,4.8 0 0 1 8,15.2a4.8,4.8 0 0 1-4.8,-6.9c0.5,-1.6 1.6,-2.6 2.6,-3.6"
            + "c-0.2,1.8 0.6,2.7 1.4,2.9C7.6,5.2 6.9,3 8.4,0.8z",

        ["clock"] =
            "M8,1.3a6.7,6.7 0 1 0 0,13.4a6.7,6.7 0 0 0 0,-13.4z M8,3.1a4.9,4.9 0 1 1 0,9.8a4.9,4.9 0 0 1 0,-9.8z "
            + "M7.3,4.6h1.4v3.6h3.1v1.4H7.3z",

        ["note"] =
            "M5.6,10.4a2.7,2.3 0 1 0 0,4.6a2.7,2.3 0 0 0 0,-4.6z M8.3,1.2l5.1,1.7v2.3L10,4.1v8.3H8.3z",

        ["dice"] =
            "M2.4,2.4h11.2v11.2H2.4z "
            + "M5.2,5a1.25,1.25 0 1 0 0,2.5a1.25,1.25 0 0 0 0,-2.5z "
            + "M10.8,5a1.25,1.25 0 1 0 0,2.5a1.25,1.25 0 0 0 0,-2.5z "
            + "M5.2,8.5a1.25,1.25 0 1 0 0,2.5a1.25,1.25 0 0 0 0,-2.5z "
            + "M10.8,8.5a1.25,1.25 0 1 0 0,2.5a1.25,1.25 0 0 0 0,-2.5z",

        ["shop"] =
            "M2,2.4h12l1,3.1a2,2 0 0 1-4,0a2,2 0 0 1-4,0a2,2 0 0 1-4,0z "
            + "M3,8.2h10V14H9.4v-3.6h-2.8V14H3z",
    };

    /// <summary>Every name this editor can draw, for the test that holds it against the vocabulary.</summary>
    public static IReadOnlyCollection<string> Drawn => ByName.Keys;

    /// <summary>The path data for a glyph, falling back to <see cref="GameIcon.Default"/> for one this
    /// build has never heard of — a game naming a glyph from a newer engine still gets a usable rail.</summary>
    public static string For(string? icon) =>
        ByName.TryGetValue(GameIcon.Or(icon), out string? path) ? path : ByName[GameIcon.Default];
}
