using Microsoft.Xna.Framework;
using Mirage.Client.Shell.Config;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Platform;

/// <summary>The per-account config's in-memory accessors (no filesystem): per-character panel bounds + social
/// tab + saved table-column layouts round-trip, unknown lookups return null/0, a Set auto-creates the
/// character entry, column state is stored as a defensive COPY, and CharacterConfig's UX defaults hold.</summary>
[TestFixture]
public class AccountConfigTests
{
    [Test]
    public void PanelBounds_RoundTripPerCharacter()
    {
        var cfg = new AccountConfig();
        var r = new Rectangle(10, 20, 300, 200);
        cfg.SetPanelBounds("Alice", "inventory", r);
        Assert.That(cfg.GetPanelBounds("Alice", "inventory"), Is.EqualTo(r));
    }

    [Test]
    public void GetPanelBounds_UnknownCharacterOrPanel_Null()
    {
        var cfg = new AccountConfig();
        cfg.SetPanelBounds("Alice", "inventory", new Rectangle(1, 2, 3, 4));
        Assert.Multiple(() =>
        {
            Assert.That(cfg.GetPanelBounds("Bob", "inventory"), Is.Null, "unknown character");
            Assert.That(cfg.GetPanelBounds("Alice", "shop"), Is.Null, "unknown panel");
        });
    }

    [Test]
    public void SocialTab_DefaultsToZero_ThenRoundTrips()
    {
        var cfg = new AccountConfig();
        Assert.That(cfg.GetSocialTab("Alice"), Is.EqualTo(0), "default before any set");
        cfg.SetSocialTab("Alice", 2);
        Assert.That(cfg.GetSocialTab("Alice"), Is.EqualTo(2));
    }

    // Column layout is stored as a defensive copy, so mutating the caller's lists afterward can't corrupt it.
    [Test]
    public void SetTableColumns_StoresDefensiveCopy()
    {
        var cfg = new AccountConfig();
        var order = new List<int> { 0, 1, 2 };
        var widths = new List<int> { 50, 60, 70 };
        cfg.SetTableColumns("Alice", "social.roster", order, widths, sortColumn: 1, sortAscending: false);

        order[0] = 999;
        widths[0] = 999;  // mutate the sources after storing

        var saved = cfg.GetTableColumns("Alice", "social.roster");
        Assert.That(saved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(saved!.Order![0], Is.EqualTo(0), "stored order is a copy");
            Assert.That(saved.Widths[0], Is.EqualTo(50), "stored widths are a copy");
            Assert.That(saved.SortColumn, Is.EqualTo(1));
            Assert.That(saved.SortAscending, Is.False);
        });
    }

    [Test]
    public void GetTableColumns_NullWhenUnset()
    {
        var cfg = new AccountConfig();
        cfg.SetSocialTab("Alice", 1);   // the character exists, but no columns were saved
        Assert.Multiple(() =>
        {
            Assert.That(cfg.GetTableColumns("Alice", "social.roster"), Is.Null, "no columns saved for this id");
            Assert.That(cfg.GetTableColumns("Bob", "mail.messages"), Is.Null, "unknown character");
        });
    }

    // A fixed-order table persists widths + sort but NO order: a null order is stored as null (omitted on disk).
    [Test]
    public void SetTableColumns_NullOrder_StoresNoOrder()
    {
        var cfg = new AccountConfig();
        cfg.SetTableColumns("Alice", "social.territory", order: null, new List<int> { 40, 50 }, sortColumn: 0, sortAscending: true);
        var saved = cfg.GetTableColumns("Alice", "social.territory");
        Assert.That(saved, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(saved!.Order, Is.Null, "fixed table stores no order");
            Assert.That(saved.Widths, Is.EqualTo(new[] { 40, 50 }));
            Assert.That(saved.SortColumn, Is.EqualTo(0));
        });
    }

    [Test]
    public void PanelBounds_RectangleRoundTrip()
    {
        var r = new Rectangle(5, 6, 7, 8);
        Assert.That(AccountConfig.PanelBounds.From(r).ToRectangle(), Is.EqualTo(r));
    }

    // UX-meaningful defaults: bars/combat-numbers/names ON; other-cooldown-bars + 24h clock + channel labels
    // OFF; the input channel starts on Say.
    [Test]
    public void CharacterConfig_Defaults()
    {
        var cc = new AccountConfig.CharacterConfig();
        Assert.Multiple(() =>
        {
            Assert.That(cc.AlwaysShowBars, Is.True);
            Assert.That(cc.ShowCombatNumbers, Is.True);
            Assert.That(cc.ShowNpcNames, Is.True);
            Assert.That(cc.ShowPlayerName, Is.True);
            Assert.That(cc.ShowCooldownBar, Is.True);
            Assert.That(cc.ShowOtherCooldownBars, Is.False);
            Assert.That(cc.Use24HourClock, Is.False);
            Assert.That(cc.ShowChannelLabels, Is.False);
            Assert.That(cc.ActiveChatChannel, Is.EqualTo("Say"));
        });
    }
}
