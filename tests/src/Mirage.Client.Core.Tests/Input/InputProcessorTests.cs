using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Input;

/// <summary>Input → C2S packet translation + client-side movement prediction/collision: a clear step predicts
/// and sends a move; a wall / another player / an NPC on the destination faces-only; safe zones let players
/// pass through; hold-to-attack respects the cooldown; and nothing is sent while not in game.</summary>
[TestFixture]
public class InputProcessorTests
{
    // Local player named + placed on center map 1 at (meX,meY), facing Down, in game.
    static (ClientState s, FakeTransport t, ClientPacketSender sender) Setup(int meX, int meY)
    {
        var s = new ClientState { MyIndex = 1, InGame = true, CenterMapNum = 1 };
        s.NeighborMapNums[1, 1] = 1;
        var me = s.Me;
        me.Name = "Me";
        me.Map = 1;
        me.X = meX;
        me.Y = meY;
        me.Dir = Direction.Down;
        var t = new FakeTransport();
        return (s, t, new ClientPacketSender(t));
    }

    [Test]
    public void Process_ClearMove_SendsMoveAndPredicts()
    {
        var (s, t, sender) = Setup(5, 5);
        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, s, sender, 0);
        Assert.Multiple(() =>
        {
            Assert.That(t.Sent.OfType<PlayerMovePacket>().Count(), Is.EqualTo(1));
            Assert.That(s.Me.Y, Is.EqualTo(6), "the client predicts the step immediately");
        });
    }

    [Test]
    public void Process_WallBlocksMove_FacesOnly()
    {
        var (s, t, sender) = Setup(5, 5);
        s.Me.Dir = Direction.Up;                     // so facing Down is a change
        s.Map.EditTile(5, 6, t => t with { Type = TileType.Blocked });    // the tile below is a wall
        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, s, sender, 0);
        Assert.Multiple(() =>
        {
            Assert.That(s.Me.Y, Is.EqualTo(5), "a wall blocks the step");
            Assert.That(t.Sent.OfType<PlayerDirPacket>().Count(), Is.EqualTo(1), "it faces the wall instead");
            Assert.That(t.Sent.OfType<PlayerMovePacket>(), Is.Empty);
        });
    }

    // On a non-safe map another player on the destination tile blocks the step.
    [Test]
    public void Process_AnotherPlayerBlocks_OnNonSafeMap()
    {
        var (s, t, sender) = Setup(5, 5);          // Map.Moral null => non-safe => players collide
        s.Me.Dir = Direction.Up;
        var other = s.Players[2];
        other.Name = "Blocker";
        other.Map = 1;
        other.X = 5;
        other.Y = 6;
        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, s, sender, 0);
        Assert.Multiple(() =>
        {
            Assert.That(s.Me.Y, Is.EqualTo(5));
            Assert.That(t.Sent.OfType<PlayerMovePacket>(), Is.Empty);
        });
    }

    // In a safe zone players pass through each other (unless PK).
    [Test]
    public void Process_SafeZone_PlayersPassThrough()
    {
        var (s, t, sender) = Setup(5, 5);
        s.Map.Moral = MapMoral.Safe;
        var other = s.Players[2];
        other.Name = "Blocker";
        other.Map = 1;
        other.X = 5;
        other.Y = 6;
        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, s, sender, 0);
        Assert.That(s.Me.Y, Is.EqualTo(6), "in a safe zone players pass through each other");
    }

    // A safe zone INHERITED from the map's group (map's own Moral unset) must be honored by collision prediction
    // too — the client resolves effective Moral via its cached group (ClientState.MoralOf), so a group-defined
    // safe zone lets players pass through exactly like a map-defined one. Guards the group-resolve swap here.
    [Test]
    public void Process_SafeZone_InheritedFromGroup_PlayersPassThrough()
    {
        var (s, t, sender) = Setup(5, 5);
        s.Map.MapGroup = 5;                                          // map's own Moral stays null → inherit
        s.MapGroups[5] = new MapGroupRecord { Index = 5, Moral = MapMoral.Safe };
        var other = s.Players[2];
        other.Name = "Blocker";
        other.Map = 1;
        other.X = 5;
        other.Y = 6;
        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, s, sender, 0);
        Assert.That(s.Me.Y, Is.EqualTo(6), "a group-inherited safe zone also lets players pass through");
    }

    // A live NPC on the destination tile blocks (safe zone or not).
    [Test]
    public void Process_NpcBlocks()
    {
        var (s, t, sender) = Setup(5, 5);
        s.Me.Dir = Direction.Up;
        s.MapNpcs[1].Num = 3;
        s.MapNpcs[1].X = 5;
        s.MapNpcs[1].Y = 6;
        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, s, sender, 0);
        Assert.That(s.Me.Y, Is.EqualTo(5), "an NPC blocks the tile");
    }

    // Facing input with no movement direction just broadcasts the new facing.
    [Test]
    public void Process_DirFaceOnly_SendsPlayerDir()
    {
        var (s, t, sender) = Setup(5, 5);
        s.Me.Dir = Direction.Down;
        InputProcessor.Process(new InputSnapshot { DirFace = Direction.Left }, s, sender, 0);
        Assert.Multiple(() =>
        {
            Assert.That(s.Me.Dir, Is.EqualTo(Direction.Left));
            Assert.That(t.Sent.OfType<PlayerDirPacket>().Count(), Is.EqualTo(1));
        });
    }

    // Hold-to-attack fires exactly once per cooldown window.
    [Test]
    public void Process_Attack_RespectsCooldown()
    {
        var (s, t, sender) = Setup(5, 5);
        InputProcessor.Process(new InputSnapshot { Attack = true }, s, sender, 0);
        InputProcessor.Process(new InputSnapshot { Attack = true }, s, sender, 0);
        Assert.That(t.Sent.OfType<AttackPacket>().Count(), Is.EqualTo(1));
    }

    // Melee talk-first fires for a conversation-ONLY NPC (no shop, no quest). A guard that checks only
    // the keeper/quest glyphs lets a talk-only NPC fall through to a swing.
    [Test]
    public void Process_MeleeInteract_FiresForConversationOnlyNpc()
    {
        var (s, t, sender) = Setup(5, 5);                                  // facing Down → front tile (5,6)
        s.MapNpcs[1].Num = 9;
        s.MapNpcs[1].X = 5;
        s.MapNpcs[1].Y = 6;  // same layer as the player (Ground)
        s.NpcConvGlyph[9] = 2;                                             // has a conversation, but no shop/quest

        InputProcessor.Process(new InputSnapshot { Attack = true, AttackPressed = true }, s, sender, 0);

        Assert.Multiple(() =>
        {
            Assert.That(t.Sent.OfType<NpcInteractPacket>().Count(), Is.EqualTo(1), "melee opens the conversation (talk-first)");
            Assert.That(t.Sent.OfType<AttackPacket>(), Is.Empty, "no swing at a talk-only NPC");
        });
    }

    // Two NPCs share the front tile on DIFFERENT layers (a bridge keeper on the deck + a wanderer beneath it): the
    // melee must resolve the one on the PLAYER'S layer, not the first by slot order. Guards the two-layer fix.
    [Test]
    public void Process_MeleeInteract_PrefersFacingNpcOnPlayersLayer()
    {
        var (s, t, sender) = Setup(5, 5);                                  // facing Down → front tile (5,6)
        s.Me.Layer = WorldLayer.Fringe;                                    // the player stands on the bridge deck
        // A ground wanderer beneath the bridge in a LOWER slot (a layer-blind scan would pick it)...
        s.MapNpcs[1].Num = 7;
        s.MapNpcs[1].X = 5;
        s.MapNpcs[1].Y = 6;
        s.MapNpcs[1].Layer = WorldLayer.Ground;
        // ...and the fringe keeper the player is actually facing, in a higher slot.
        s.MapNpcs[2].Num = 8;
        s.MapNpcs[2].X = 5;
        s.MapNpcs[2].Y = 6;
        s.MapNpcs[2].Layer = WorldLayer.Fringe;
        s.NpcKeeperShop[8] = 1;                                            // the keeper is interactable

        InputProcessor.Process(new InputSnapshot { Attack = true, AttackPressed = true }, s, sender, 0);

        var interact = t.Sent.OfType<NpcInteractPacket>().SingleOrDefault();
        Assert.Multiple(() =>
        {
            Assert.That(interact, Is.Not.Null, "melee interacts instead of swinging");
            Assert.That(interact!.NpcSlot, Is.EqualTo(2), "the keeper on the player's layer wins, not the wanderer beneath");
            Assert.That(t.Sent.OfType<AttackPacket>(), Is.Empty, "no swing");
        });
    }

    // Interaction reaches only a plane the player's connects to: facing an interactable NPC across a gap with no
    // ramp refuses instead of opening its menu, and flags the refusal for the Shell. No swing either — keepers
    // aren't melee targets. Mirrors the server's own interact gate.
    [Test]
    public void Process_MeleeInteract_CrossLayerNoRamp_RefusesAndFlags()
    {
        var (s, t, sender) = Setup(5, 5);                                  // facing Down → front tile (5,6)
        s.Me.Layer = WorldLayer.Ground;
        s.MapNpcs[1].Num = 9;
        s.MapNpcs[1].X = 5;
        s.MapNpcs[1].Y = 6;
        s.MapNpcs[1].Layer = WorldLayer.Fringe;                            // on the bridge deck above the player
        s.NpcKeeperShop[9] = 1;                                            // it would otherwise open its shop

        InputProcessor.Process(new InputSnapshot { Attack = true, AttackPressed = true }, s, sender, 0);

        Assert.Multiple(() =>
        {
            Assert.That(t.Sent.OfType<NpcInteractPacket>(), Is.Empty, "no interact across disconnected planes");
            Assert.That(t.Sent.OfType<AttackPacket>(), Is.Empty, "and still no swing at a keeper");
            Assert.That(s.NpcInteractWrongLayer, Is.True, "the Shell is told to voice the refusal");
        });
    }

    // The ramp carve-out on the client: a fringe keeper standing ON a ramp is reachable from the ground at its
    // foot, so the interact goes through and nothing is flagged. Keeps the key in step with the server's gate.
    [Test]
    public void Process_MeleeInteract_CrossLayerOntoRamp_Interacts()
    {
        var (s, t, sender) = Setup(5, 5);                                  // facing Down → front tile (5,6)
        s.Me.Layer = WorldLayer.Ground;
        s.MapNpcs[1].Num = 9;
        s.MapNpcs[1].X = 5;
        s.MapNpcs[1].Y = 6;
        s.MapNpcs[1].Layer = WorldLayer.Fringe;
        s.NpcKeeperShop[9] = 1;
        // The faced tile is a ramp whose ground side faces Up — back toward the player standing at its foot.
        s.Map.EditTile(5, 6, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Up } });

        InputProcessor.Process(new InputSnapshot { Attack = true, AttackPressed = true }, s, sender, 0);

        Assert.Multiple(() =>
        {
            Assert.That(t.Sent.OfType<NpcInteractPacket>().Count(), Is.EqualTo(1), "the ramp connects the planes");
            Assert.That(s.NpcInteractWrongLayer, Is.False, "nothing to refuse, so no chat line");
        });
    }

    // The refusal is per-PRESS, not per-frame: holding the key after a refused press must not re-raise it (the
    // Shell drains the flag, so a re-set on every held frame would spam the chat log).
    [Test]
    public void Process_MeleeInteract_CrossLayerHeld_DoesNotReflag()
    {
        var (s, t, sender) = Setup(5, 5);
        s.Me.Layer = WorldLayer.Ground;
        s.MapNpcs[1].Num = 9;
        s.MapNpcs[1].X = 5;
        s.MapNpcs[1].Y = 6;
        s.MapNpcs[1].Layer = WorldLayer.Fringe;
        s.NpcKeeperShop[9] = 1;

        InputProcessor.Process(new InputSnapshot { Attack = true, AttackPressed = true }, s, sender, 0);
        s.NpcInteractWrongLayer = false;                                   // the Shell drained it into chat
        InputProcessor.Process(new InputSnapshot { Attack = true }, s, sender, 0);   // key still held, no new edge

        Assert.That(s.NpcInteractWrongLayer, Is.False, "a held key repeats neither the interact nor the refusal");
    }

    // A cross-layer Friendly NPC must not swallow the swing: the server's melee gate rejects it before the rebuff,
    // so there's no AttackSay coming and the whiff animation should play (a suppressed one looked like a lost key).
    [Test]
    public void Process_MeleeAtCrossLayerFriendlyNpc_StillSwings()
    {
        var (s, t, sender) = Setup(5, 5);                                 // facing Down → front tile (5,6)
        s.Me.Layer = WorldLayer.Ground;
        s.MapNpcs[1].Num = 12;
        s.MapNpcs[1].X = 5;
        s.MapNpcs[1].Y = 6;
        s.MapNpcs[1].Layer = WorldLayer.Fringe;                           // up on the bridge, out of reach
        s.NpcDefs[12] = new NpcRecord { Behavior = NpcBehavior.Friendly, AttackSay = "Leave me be." };

        InputProcessor.Process(new InputSnapshot { Attack = true, AttackPressed = true }, s, sender, 0);

        Assert.Multiple(() =>
        {
            Assert.That(t.Sent.OfType<AttackPacket>().Count(), Is.EqualTo(1), "the attack is still sent");
            Assert.That(s.Me.Attacking, Is.True, "and the whiff swing plays -- nothing on this layer rebuffs it");
        });
    }

    // Meleeing a non-combat NPC (Friendly/Stationary) sends the attack (so the server can issue its AttackSay
    // rebuff) but plays NO local swing — the Attacking flag stays clear. The server likewise skips the whiff.
    [Test]
    public void Process_MeleeAtFriendlyNpc_SendsAttackButSuppressesSwing()
    {
        var (s, t, sender) = Setup(5, 5);                                 // facing Down → front tile (5,6)
        s.MapNpcs[1].Num = 12;
        s.MapNpcs[1].X = 5;
        s.MapNpcs[1].Y = 6;  // same layer as the player
        s.NpcDefs[12] = new NpcRecord { Behavior = NpcBehavior.Friendly, AttackSay = "Leave me be." };

        InputProcessor.Process(new InputSnapshot { Attack = true, AttackPressed = true }, s, sender, 0);

        Assert.Multiple(() =>
        {
            Assert.That(t.Sent.OfType<AttackPacket>().Count(), Is.EqualTo(1), "the attack is sent so the rebuff say can fire");
            Assert.That(t.Sent.OfType<NpcInteractPacket>(), Is.Empty, "a friendly NPC is not an interact target");
            Assert.That(s.Me.Attacking, Is.False, "but the local swing is suppressed");
        });
    }

    [Test]
    public void Process_PickUp_SendsGetItem()
    {
        var (s, t, sender) = Setup(5, 5);
        InputProcessor.Process(new InputSnapshot { PickUp = true }, s, sender, 0);
        Assert.That(t.Sent.OfType<MapGetItemPacket>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Process_NotInGame_SendsNothing()
    {
        var (s, t, sender) = Setup(5, 5);
        s.InGame = false;
        InputProcessor.Process(new InputSnapshot { Move = Direction.Down, Attack = true, PickUp = true }, s, sender, 0);
        Assert.That(t.Sent, Is.Empty);
    }

    // ── God mode ──────────────────────────────────────────────────────────────
    // The server exempts an observer in CanPlayerWalkOnTile, but the client decides whether a move packet is
    // sent at all — a step the prediction refuses never reaches that exemption. Each obstacle is therefore
    // stepped into twice, ordinary and god mode, so the PAIR proves the exemption and not a missing obstacle.

    [Test]
    public void GodMode_PassesThroughWhatBlocksAnOrdinaryPlayer()
    {
        (string What, Action<ClientState> Place)[] obstacles =
        [
            ("a wall", s => s.Map.EditTile(5, 6, t => t with { Type = TileType.Blocked })),
            ("a closed door", s =>
            {
                s.Map.EditTile(5, 6, t => t with { Type = TileType.Key });
                s.TempTile.Set(5, 6, (int)WorldLayer.Ground, false);
            }),
            ("another player", s =>
            {
                var o = s.Players[2];
                o.Name = "Blocker";
                o.Map = 1;
                o.X = 5;
                o.Y = 6;
            }),
            ("an NPC", s =>
            {
                s.MapNpcs[1].Num = 3;
                s.MapNpcs[1].X = 5;
                s.MapNpcs[1].Y = 6;
            }),
        ];

        Assert.Multiple(() =>
        {
            foreach (var (what, place) in obstacles)
            {
                Assert.That(StepsSouth(place, godMode: false), Is.False, $"{what} blocks an ordinary player");
                Assert.That(StepsSouth(place, godMode: true), Is.True, $"god mode walks through {what}");
            }
        });
    }

    // Sprinting costs SP and the CLIENT picks the movement type it asks for, so an empty bar downgrades the
    // request to a walk unless the server's cost exemption is mirrored here.
    [Test]
    public void GodMode_SprintsOnAnEmptyStaminaBar()
    {
        static MovementType? Sprint(bool godMode)
        {
            var (s, t, sender) = Setup(5, 5);
            s.Me.GodMode = godMode;
            s.Me.Sp = 0;
            InputProcessor.Process(new InputSnapshot { Move = Direction.Down, Running = true }, s, sender, 0);
            return t.Sent.OfType<PlayerMovePacket>().SingleOrDefault()?.Movement;
        }

        Assert.Multiple(() =>
        {
            Assert.That(Sprint(godMode: false), Is.EqualTo(MovementType.Walking), "an empty bar downgrades an ordinary sprint");
            Assert.That(Sprint(godMode: true), Is.EqualTo(MovementType.Running), "god mode keeps running on nothing");
        });
    }

    // One step south into a freshly placed obstacle. True only when the client both predicted the step AND
    // sent it — a prediction that moves the sprite without telling the server would rubber-band.
    private static bool StepsSouth(Action<ClientState> placeObstacle, bool godMode)
    {
        var (s, t, sender) = Setup(5, 5);
        s.Me.GodMode = godMode;
        placeObstacle(s);
        InputProcessor.Process(new InputSnapshot { Move = Direction.Down }, s, sender, 0);
        return s.Me.Y == 6 && t.Sent.OfType<PlayerMovePacket>().Count() == 1;
    }
}

/// <summary>Captures every packet the sender hands to the transport, so a test can assert what was sent.</summary>
sealed class FakeTransport : IClientTransport
{
    public readonly List<IPacket> Sent = new();
    public bool IsConnected => true;
    public bool DroppedUnexpectedly => false;
    public Task ConnectAsync(string host, int port, CancellationToken ct = default) => Task.CompletedTask;
    public void Send(IPacket packet) => Sent.Add(packet);
    public void Disconnect() { }
    public bool TryDequeue(out string line)
    {
        line = "";
        return false;
    }
}
