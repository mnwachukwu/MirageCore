using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Mirage.Server.Tests.Economy;

/// <summary>
/// Drinking runs on its own clock, apart from the beat attacking and casting share.
///
/// <para>Two clocks rather than one, and the split is the point: a potion must not cost a swing, or
/// self-healing simply replaces the second body in the fight — but a potion must not be spammable
/// either, or a bar can be drunk back to full faster than anything can empty it.</para>
/// </summary>
[TestFixture]
public class PotionCooldownTests
{
    private const int Idx = 1;
    private const int Map = 1;
    private const int HpPotion = 5;
    private const int Key = 6;

    private static (GameWorld world, PlayerManager pm, ItemSystem items, PlayerRecord p) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var items = new ItemSystem(world, pm, new NoOpDispatcher(), persistence: null!, bg: null!);
        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;

        world.Items[HpPotion].Type = ItemType.PotionAddHp;
        world.Items[HpPotion].VitalAmount = 10;
        sp.Char.MaxHp = 100;
        sp.Char.Hp = 50;
        return (world, pm, items, sp.Char);
    }

    /// <summary>Two potions in the same tick is the exploit this exists to stop.</summary>
    [Test]
    public void ASecondPotion_InsideTheCooldown_IsRefused()
    {
        var (_, pm, items, p) = Setup();
        p.Inv[1].Num = HpPotion;
        p.Inv[2].Num = HpPotion;

        items.UseItem(Idx, 1);
        int afterFirst = p.Hp;
        items.UseItem(Idx, 2);

        Assert.Multiple(() =>
        {
            Assert.That(afterFirst, Is.EqualTo(60), "the first one is drunk");
            Assert.That(p.Hp, Is.EqualTo(60), "the second heals nothing");
            Assert.That(p.Inv[2].Num, Is.EqualTo(HpPotion), "and is not consumed");
        });
    }

    [Test]
    public void OnceTheCooldownHasRun_TheNextPotionIsDrunk()
    {
        var (_, pm, items, p) = Setup();
        p.Inv[1].Num = HpPotion;
        p.Inv[2].Num = HpPotion;

        items.UseItem(Idx, 1);
        pm[Idx].PotionTimer -= Constants.PotionCooldownMs;   // the clock has come round
        items.UseItem(Idx, 2);

        Assert.That(p.Hp, Is.EqualTo(70), "both are drunk");
    }

    /// <summary>The whole reason the clocks are separate: healing must not cost a swing.</summary>
    [Test]
    public void SwingingDoesNotDelayAPotion_AndDrinkingDoesNotDelayASwing()
    {
        var (_, pm, items, p) = Setup();
        p.Inv[1].Num = HpPotion;

        long swungAt = Environment.TickCount64;
        pm[Idx].AttackTimer = swungAt;   // just swung
        items.UseItem(Idx, 1);

        Assert.Multiple(() =>
        {
            Assert.That(p.Hp, Is.EqualTo(60), "a fresh swing does not hold up the potion");
            Assert.That(pm[Idx].AttackTimer, Is.EqualTo(swungAt),
                "and drinking leaves the action beat exactly where the swing left it");
            Assert.That(pm[Idx].PotionTimer, Is.Not.Zero, "while charging its own clock");
        });
    }

    /// <summary>A potion that does nothing must cost nothing — otherwise mis-clicking a full bar
    /// silently locks the next two seconds of healing.</summary>
    [Test]
    public void ARefusedPotion_CostsNeitherTheItemNorTheCooldown()
    {
        var (_, pm, items, p) = Setup();
        p.Hp = p.MaxHp;                 // nothing to heal
        p.Inv[1].Num = HpPotion;
        p.Inv[2].Num = HpPotion;

        items.UseItem(Idx, 1);
        Assert.That(p.Inv[1].Num, Is.EqualTo(HpPotion), "precondition: the full-bar potion is refused");

        p.Hp = 50;                      // now it would help
        items.UseItem(Idx, 2);

        Assert.That(p.Hp, Is.EqualTo(60), "so the very next potion still works");
    }

    /// <summary>A key opens a door mid-fight. That is a legitimate move and must not wait on drinking.</summary>
    [Test]
    public void AKey_IsNeverHeldUpByTheDrinkingClock()
    {
        var (world, pm, items, p) = Setup();
        world.Items[Key].Type = ItemType.Key;
        p.Inv[1].Num = HpPotion;
        p.Inv[2].Num = Key;

        items.UseItem(Idx, 1);
        long afterPotion = pm[Idx].PotionTimer;
        items.UseItem(Idx, 2);          // no door faced, so nothing happens — but nothing is charged either

        Assert.That(pm[Idx].PotionTimer, Is.EqualTo(afterPotion),
            "using a key leaves the drinking clock exactly where it was");
    }

    [Test]
    public void TheDrinkingCooldownIsLongerThanTheActionBeat()
    {
        Assert.That(Constants.PotionCooldownMs, Is.GreaterThan(Constants.PlayerAttackCooldownMs),
            "a potion is meant to be a slower utility than a swing, not a substitute for one");
    }
    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}
