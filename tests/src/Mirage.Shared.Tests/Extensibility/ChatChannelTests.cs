using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// The channels a game declares, and the five Core keeps for itself.
///
/// <para>🔴 <b>Core owns the audience; a channel is only what kind of line it is.</b> Who hears something
/// is the engine's to work out. Whether a world reads its fighting apart from its talking is that world's
/// decision, so the taxonomy is declared rather than compiled in — and an engine that shipped a "Combat"
/// bucket would be an engine with an opinion about whether its games have fighting in them.</para>
/// </summary>
[TestFixture]
public class ChatChannelTests
{
    private sealed class Module(params ChatChannelSpec[] channels) : ICoreModule
    {
        public string Name => "Brawler";

        public void Configure(ICoreBuilder builder)
        {
            foreach (var channel in channels) builder.AddChatChannel(channel);
        }
    }

    private static ChatChannelSpec Blows(string id = "combat") => new()
    {
        Id = id,
        LabelKey = "Combat",
        Rgb = GameColor.Pack(230, 130, 130),
        OwnTabKey = "Combat",
    };

    [Test]
    public void AnEngineWithNoGameLoaded_DeclaresNoneOfItsOwn()
        => Assert.That(CoreRegistry.CoreOnly.ChatChannels.Channels, Is.Empty);

    [Test]
    public void AChannelIsFoundByTheIdThatTravels()
    {
        var channels = CoreRegistry.Build(new Module(Blows())).ChatChannels;

        Assert.Multiple(() =>
        {
            Assert.That(channels.Count, Is.EqualTo(1));
            Assert.That(channels.Find("combat")?.LabelKey, Is.EqualTo("Combat"));
            Assert.That(channels.Find("rewards"), Is.Null, "a channel nobody declared");
        });
    }

    /// <summary>🔴 A declaration that took one of Core's names would answer for lines the engine sends on
    /// its own account — so a player switching off a game's feed would lose their tells with it.</summary>
    [TestCase(ChatChannels.Global)]
    [TestCase(ChatChannels.System)]
    [TestCase(ChatChannels.Tell)]
    [TestCase(ChatChannels.Guild)]
    [TestCase(ChatChannels.Admin)]
    [TestCase(ChatChannels.Always)]
    public void TakingOneOfCoresOwnNamesIsRefused(string taken)
    {
        var refused = Assert.Throws<CoreModuleException>(
            () => CoreRegistry.Build(new Module(Blows(taken))));

        Assert.That(refused!.Message, Does.Contain(taken));
    }

    [Test]
    public void DeclaringTheSameChannelTwiceIsRefused()
    {
        var refused = Assert.Throws<CoreModuleException>(
            () => CoreRegistry.Build(new Module(Blows(), Blows())));

        Assert.That(refused!.Message, Does.Contain("already declared"));
    }

    [Test]
    public void AChannelWithNoIdIsRefused()
        => Assert.Throws<CoreModuleException>(
            () => CoreRegistry.Build(new Module(new ChatChannelSpec { LabelKey = "Nameless" })));

    /// <summary>Channels naming the same tab key share one tab, and a channel naming none belongs in the
    /// main one. The client builds a fresh account's tabs from exactly this.</summary>
    [Test]
    public void TabKeysAreListedOnceEachInDeclarationOrder()
    {
        var channels = CoreRegistry.Build(new Module(
            Blows(),
            new ChatChannelSpec { Id = "rewards", LabelKey = "Rewards", OwnTabKey = "Combat" },
            new ChatChannelSpec { Id = "trade", LabelKey = "Trade", OwnTabKey = "Market" },
            new ChatChannelSpec { Id = "weather", LabelKey = "Weather" })).ChatChannels;

        Assert.That(channels.OwnTabKeys, Is.EqualTo(new[] { "Combat", "Market" }));
    }

    /// <summary>🔴 The wire carries a NAME, not a number. A game's channels and Core's live in one
    /// namespace, and a number would have to be assigned by somebody.</summary>
    [Test]
    public void CoresOwnChannelsTravelUnderTheirEnumNames()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ChatChannels.Name(ChatChannel.System), Is.EqualTo("System"));
            Assert.That(ChatChannels.Name(ChatChannel.Global), Is.EqualTo("Global"));
            Assert.That(ChatChannels.IsCore("System"), Is.True);
            Assert.That(ChatChannels.IsCore("combat"), Is.False, "case matters: ids compare ordinally");
        });
    }
}
