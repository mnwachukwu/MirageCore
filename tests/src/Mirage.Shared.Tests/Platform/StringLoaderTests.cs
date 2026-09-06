using Mirage.Shared.Localization;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Platform;

/// <summary>The shared localization engine (Mirage.Shared.StringLoader). The LocalizationParity suites lean on
/// <see cref="StringLoader.Validate"/> to guard translations, but they only assert it finds NOTHING wrong in
/// the real files — so a false-negative in Validate would let translation rot pass silently. These pin that
/// Validate actually CATCHES each defect (missing/unknown key, mismatched placeholder token), plus the pure
/// Format substitution + ValuesInTemplateOrder helpers.</summary>
[TestFixture]
public class StringLoaderTests
{
    static Dictionary<string, string> Dict(params (string k, string v)[] entries)
    {
        var d = new Dictionary<string, string>();
        foreach (var (k, v) in entries) d[k] = v;
        return d;
    }

    // ── Validate: the safety net must actually catch problems ─────────────────────

    [Test]
    public void Validate_MatchingKeysAndTokens_ReportsNothing()
    {
        var en = Dict(("greet", "Hello {Name}"), ("farewell", "Goodbye"));
        var es = Dict(("greet", "Hola {Name}"), ("farewell", "Adios"));
        Assert.That(StringLoader.Validate(en, es, "es"), Is.Empty);
    }

    [Test]
    public void Validate_UntranslatedKey_IsReported()
    {
        var en = Dict(("greet", "Hello"), ("farewell", "Goodbye"));
        var es = Dict(("greet", "Hola"));   // farewell missing
        var errors = StringLoader.Validate(en, es, "es");
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(errors[0], Does.Contain("farewell"));
            Assert.That(errors[0], Does.Contain("not translated"));
        });
    }

    [Test]
    public void Validate_UnknownKey_IsReported()
    {
        var en = Dict(("greet", "Hello"));
        var es = Dict(("greet", "Hola"), ("phantom", "extra"));   // phantom has no English source
        var errors = StringLoader.Validate(en, es, "es");
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("Unknown key").And.Contain("phantom"));
    }

    [Test]
    public void Validate_DroppedPlaceholderToken_IsReported()
    {
        var en = Dict(("cost", "You have {Gold} gold"));
        var es = Dict(("cost", "Tienes oro"));   // {Gold} dropped
        var errors = StringLoader.Validate(en, es, "es");
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("missing token").And.Contain("Gold"));
    }

    [Test]
    public void Validate_AddedPlaceholderToken_IsReported()
    {
        var en = Dict(("greet", "Hello"));
        var es = Dict(("greet", "Hola {Name}"));   // {Name} not in the English source
        var errors = StringLoader.Validate(en, es, "es");
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("unexpected token").And.Contain("Name"));
    }

    // ── Format ────────────────────────────────────────────────────────────────────

    [Test]
    public void Format_SubstitutesNamedPlaceholders_InAnyArgOrder()
        => Assert.That(StringLoader.Format("{A} and {B}", ("B", "y"), ("A", "x")), Is.EqualTo("x and y"));

    // A format spec ({V:X}) is applied via string.Format; hex is culture-independent so the test is stable.
    [Test]
    public void Format_AppliesFormatSpec()
        => Assert.That(StringLoader.Format("{V:X}", ("V", 255)), Is.EqualTo("FF"));

    [Test]
    public void Format_NullValue_BecomesEmpty()
        => Assert.That(StringLoader.Format("[{X}]", ("X", null)), Is.EqualTo("[]"));

    [Test]
    public void Format_NoPlaceholders_ReturnsTemplateVerbatim()
        => Assert.That(StringLoader.Format("plain text"), Is.EqualTo("plain text"));

#if DEBUG
    // In DEBUG a placeholder with no supplied arg is a bug, not a silent passthrough.
    [Test]
    public void Format_MissingArg_ThrowsInDebug()
        => Assert.Throws<InvalidOperationException>(() => StringLoader.Format("{Missing}", ("Other", "x")));
#endif

    // ── ValuesInTemplateOrder ─────────────────────────────────────────────────────

    // Values come back in the order the placeholders appear in the TEMPLATE, not the order the args were passed.
    [Test]
    public void ValuesInTemplateOrder_FollowsTemplateOrder()
    {
        var vals = StringLoader.ValuesInTemplateOrder("{B} before {A}", ("A", 1), ("B", 2));
        Assert.That(vals, Is.EqualTo(new object?[] { 2, 1 }));
    }

    [Test]
    public void ValuesInTemplateOrder_NoPlaceholders_Empty()
        => Assert.That(StringLoader.ValuesInTemplateOrder("nothing here"), Is.Empty);
}
