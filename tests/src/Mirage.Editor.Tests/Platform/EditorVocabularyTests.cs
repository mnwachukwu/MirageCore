using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Platform;

/// <summary>
/// The editor's structural vocabulary — layer types, tile attributes, tool enums — is English in
/// every language, so it lives in <see cref="EditorVocabulary"/> rather than the string tables. That
/// makes two things worth pinning.
///
/// <para>First, coverage: every overload falls back to <c>ToString()</c> for a member it does not
/// know, which keeps a new enum value from crashing the editor but also lets it slip in reading
/// "NpcAvoid" while its neighbors read "NPC Avoid". The fallback is the safety net; this is the
/// alarm.</para>
///
/// <para>Second, that a name never reappears in the string tables. A key like
/// <c>MapEditor_GroundColumn</c> coming back would restore exactly the split this replaced — a
/// picker saying "Ground" beside a header saying "Sol".</para>
/// </summary>
[TestFixture]
public class EditorVocabularyTests
{
    // Members deliberately absent from the vocabulary, with the reason. TileType.Walkable is not a
    // name but the ABSENCE of an attribute, which is a phrase and stays localized as
    // MapEditor_AttrText_None.
    private static bool IsDeliberatelyUnnamed(object member) =>
        member is TileType.Walkable;

    private static IEnumerable<object> AllVocabularyMembers()
    {
        foreach (var v in Enum.GetValues<AttributeTool>()) yield return v;
        foreach (var v in Enum.GetValues<TileType>()) yield return v;
        foreach (var v in Enum.GetValues<LayerType>()) yield return v;
        foreach (var v in Enum.GetValues<WorldLayer>()) yield return v;
        foreach (var v in Enum.GetValues<Direction>()) yield return v;
        foreach (var v in Enum.GetValues<AnimStyle>()) yield return v;
        foreach (var v in Enum.GetValues<FlickerStyle>()) yield return v;
    }

    /// <summary>A member with no entry falls through to its identifier. Comparing against
    /// <c>ToString()</c> catches that — except where the identifier IS the intended name (Blocked,
    /// Ground, Up), which no rule can distinguish from a miss, so those simply pass.</summary>
    [Test]
    public void EveryVocabularyMember_HasAName()
    {
        var unnamed = AllVocabularyMembers()
            .Where(m => !IsDeliberatelyUnnamed(m))
            .Where(m => EditorVocabulary.NameOfValue(m).Length == 0)
            .Select(m => $"{m.GetType().Name}.{m}")
            .ToList();

        Assert.That(unnamed, Is.Empty,
            "These vocabulary members resolve to nothing: " + string.Join(", ", unnamed));
    }

    /// <summary>The compound identifiers are the ones the fallback would render badly, so they are
    /// pinned by value. This is also the test that fails if someone "simplifies" the vocabulary to a
    /// bare <c>ToString()</c>.</summary>
    [TestCase(AttributeTool.NpcAvoid, "NPC Avoid")]
    [TestCase(AttributeTool.NpcSpawn, "NPC Spawn")]
    [TestCase(AttributeTool.LayerRamp, "Layer Ramp")]
    [TestCase(AttributeTool.Item, "Item Spawn")]
    public void CompoundNames_ReadAsWordsNotIdentifiers(AttributeTool tool, string expected)
        => Assert.That(EditorVocabulary.NameOf(tool), Is.EqualTo(expected));

    /// <summary>The tool the author paints with and the attribute that gets stored are the same
    /// concept, so they must present the same word — otherwise the picker and the hover readout
    /// disagree about what was just placed.</summary>
    [TestCase(AttributeTool.Blocked, TileType.Blocked)]
    [TestCase(AttributeTool.Warp, TileType.Warp)]
    [TestCase(AttributeTool.Item, TileType.Item)]
    [TestCase(AttributeTool.NpcAvoid, TileType.NpcAvoid)]
    [TestCase(AttributeTool.Key, TileType.Key)]
    [TestCase(AttributeTool.KeyOpen, TileType.KeyOpen)]
    [TestCase(AttributeTool.LayerRamp, TileType.LayerRamp)]
    public void ToolAndStoredAttribute_ShareOneName(AttributeTool tool, TileType type)
        => Assert.That(EditorVocabulary.NameOf(tool), Is.EqualTo(EditorVocabulary.NameOf(type)));

    /// <summary>Vocabulary belongs to the code, not the string tables. A translated layer name is
    /// what made the pickers and the rest of the editor disagree in the first place.</summary>
    [Test]
    public void NoLanguageFile_TranslatesAVocabularyName()
    {
        string[] retired =
        [
            "MapEditor_GroundColumn", "MapEditor_FringeColumn", "MapEditor_CanopyColumn",
            "MapEditor_AttrText_Blocked", "MapEditor_AttrText_NpcAvoid",
            "HelpDialog_Attr_Blocked", "HelpDialog_Attr_Warp", "HelpDialog_Attr_Item",
            "HelpDialog_Attr_NpcAvoid", "HelpDialog_Attr_NpcSpawn", "ItemSpawnDialog_Title",
        ];

        string langDir = Path.Combine(AppContext.BaseDirectory, "lang");
        var offenders = new List<string>();
        foreach (string file in Directory.GetFiles(langDir, "*.json"))
        {
            string src = File.ReadAllText(file);
            foreach (string key in retired)
                if (src.Contains($"\"{key}\"", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)}:{key}");
        }

        Assert.That(offenders, Is.Empty,
            "These keys name a layer or attribute, which EditorVocabulary owns in English. Resolve "
            + "the name through EditorVocabulary and pass it in as {Name}: " + string.Join(", ", offenders));
    }

    // Terms distinctive enough that seeing one in the English text means the construct is being
    // NAMED. Bare "Key" and "Item" are deliberately absent: English uses them as ordinary words
    // ("the key item", "Key Item #"), so requiring them verbatim in a translation would flag correct
    // prose. Everything below is unambiguous.
    private static readonly string[] ProseTerms =
    [
        "Ground", "Fringe", "Canopy", "Warp", "KeyOpen",
        "Layer Ramp", "NPC Avoid", "NPC Spawn", "Item Spawn",
    ];

    /// <summary>
    /// The help text names these constructs mid-sentence, and a translator's instinct is to translate
    /// the word — which is how the French help came to say "Sol" and "Frange" while the pickers said
    /// Ground and Fringe. Rather than list the forbidden word in each language, this asserts the
    /// positive: if the English value names a construct, the translated value must contain that same
    /// English name. That needs no knowledge of French, Portuguese or Spanish, so it keeps working for
    /// a language nobody here reads.
    /// </summary>
    [Test]
    public void TranslatedProse_KeepsVocabularyNamesInEnglish()
    {
        string langDir = Path.Combine(AppContext.BaseDirectory, "lang");
        var en = Load(Path.Combine(langDir, "en.json"));

        var offenders = new List<string>();
        foreach (string file in Directory.GetFiles(langDir, "*.json"))
        {
            if (Path.GetFileNameWithoutExtension(file) == "en") continue;
            var other = Load(file);
            foreach (var (key, enValue) in en)
            {
                if (!other.TryGetValue(key, out string? value)) continue;   // parity tests own that
                foreach (string term in ProseTerms)
                {
                    if (!ContainsWord(enValue, term)) continue;
                    if (!ContainsWord(value, term))
                        offenders.Add($"{Path.GetFileName(file)}:{key} drops \"{term}\"");
                }
            }
        }

        Assert.That(offenders, Is.Empty,
            "These translations rename a layer or attribute that EditorVocabulary owns in English. "
            + "Keep the English term inside the translated sentence: " + string.Join("; ", offenders));
    }

    private static Dictionary<string, string> Load(string path) =>
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;

    // Whole-word, case-sensitive: "Ground" must not match "background", and lowercase "ground" is the
    // ordinary English noun rather than the layer.
    private static bool ContainsWord(string haystack, string term) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            haystack, $@"(?<![\w-]){System.Text.RegularExpressions.Regex.Escape(term)}(?![\w])");
}
