using NUnit.Framework;

namespace Mirage.Shared.Tests.Accounts;

/// <summary>The shared naming rules (<see cref="NameRules"/>) that govern player, account, and guild names:
/// underscores are cosmetic — ignored (with case) for uniqueness and not counted toward the length limit.
/// These are the pure core of "can't spoof TheGathering with The_Gathering" and "A__ is too short".</summary>
[TestFixture]
public class NameRulesTests
{
    // Every underscore/case variant collapses to the same identity key, so only one can be created.
    [TestCase("TheGathering")]
    [TestCase("thegathering")]
    [TestCase("THEGATHERING")]
    [TestCase("The_Gathering")]
    [TestCase("The__Gathering")]
    [TestCase("_TheGathering_")]
    [TestCase("_T_h_e_G_a_t_h_e_r_i_n_g_")]
    public void Key_SpoofVariants_ShareCanonicalKey(string variant)
        => Assert.That(NameRules.Key(variant), Is.EqualTo("thegathering"));

    [Test]
    public void Key_DistinctNames_DifferentKeys()
    {
        Assert.That(NameRules.Key("Bob"), Is.Not.EqualTo(NameRules.Key("Bob2")));
        Assert.That(NameRules.Key("Clan_99"), Is.EqualTo("clan99"));   // digits are part of identity
    }

    [Test]
    public void Key_NoAlphanumerics_IsEmpty()
    {
        Assert.That(NameRules.Key("___"), Is.Empty);
        Assert.That(NameRules.Key(""), Is.Empty);
    }

    // EffectiveLength = alphanumeric count only (the minimum-length input). "A__" has one, all-underscore
    // has none, so both are too little real content.
    [TestCase("A__", ExpectedResult = 1)]
    [TestCase("___", ExpectedResult = 0)]
    [TestCase("Bob", ExpectedResult = 3)]
    [TestCase("My_Cool_Name", ExpectedResult = 10)]
    public int EffectiveLength_CountsAlphanumericsOnly(string name) => NameRules.EffectiveLength(name);

    // Length validation keeps the two concerns separate: the MAX bounds the whole string, the MIN bounds
    // only alphanumerics.
    [TestCase("Bob", ExpectedResult = NameLengthResult.Ok)]
    [TestCase("abc_def", ExpectedResult = NameLengthResult.Ok)]
    [TestCase("A__", ExpectedResult = NameLengthResult.TooShort)]   // 1 alphanumeric < 3
    [TestCase("___", ExpectedResult = NameLengthResult.TooShort)]   // 0 alphanumeric
    public NameLengthResult CheckLength_Basics(string name) => NameRules.CheckLength(name, 3, 30);

    // Underscores DO count toward the maximum (length and uniqueness are separate concerns): an
    // underscore-padded name over the cap is too long, but padding within the cap is fine.
    [Test]
    public void CheckLength_UnderscoresCountTowardMax()
    {
        Assert.That(NameRules.CheckLength("abc" + new string('_', 40), minAlphanumeric: 3, maxTotal: 30),
            Is.EqualTo(NameLengthResult.TooLong));
        Assert.That(NameRules.CheckLength("abc" + new string('_', 20), minAlphanumeric: 3, maxTotal: 30),
            Is.EqualTo(NameLengthResult.Ok));
        Assert.That(NameRules.CheckLength(new string('a', 31), minAlphanumeric: 3, maxTotal: 30),
            Is.EqualTo(NameLengthResult.TooLong));
    }

    [TestCase("Bob_99", ExpectedResult = true)]
    [TestCase("under_score", ExpectedResult = true)]
    [TestCase("has space", ExpectedResult = false)]
    [TestCase("bad!", ExpectedResult = false)]
    [TestCase("hyphen-name", ExpectedResult = false)]
    public bool HasValidChars_LettersDigitsUnderscoreOnly(string name) => NameRules.HasValidChars(name);
}
