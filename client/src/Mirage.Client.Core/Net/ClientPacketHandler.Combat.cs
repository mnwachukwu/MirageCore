using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Client.Core.Net;

/// <summary>Attacks, casts, damage numbers, deaths and target assignment — for the local player and
/// for the NPCs on the map — plus the blood and floating combat text a hit leaves behind.</summary>
public sealed partial class ClientPacketHandler : IClientEvents
{
    // ── Combat ────────────────────────────────────────────────────────────────

    /// <summary>Fired on a melee attack packet: (mapNum, tileX, tileY, xOffset, yOffset, dir, sparks) of the
    /// attacker, so the shell can spawn a swing FX over the target tile in the facing direction. sparks is
    /// true when the swing connected (crescent + sparks) and false on a whiff (crescent only).</summary>
    public event Action<int, int, int, float, float, Direction, bool>? MeleeSwing;

    /// <summary>Fired on a cast packet — the shell spawns the typed projectile FX (see <see cref="SpellCastFx"/>).</summary>
    public event Action<SpellCastFx>? SpellCast;

    /// <summary>Fired just before a killed entity vanishes — the shell can hold its sprite until a killing bolt lands.</summary>
    public event Action<EntityDeathFx>? EntityDied;

    private void HandlePlayerAttack(PlayerAttackPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        var player = _state.Players[p.Index];
        player.Attacking = true;
        player.AttackTimer = Environment.TickCount64;
        if (p.InCombat) player.LastCombatMs = Environment.TickCount64;
        // Sparks fire only when the swing connected. InCombat is set exactly when the attacker engaged a
        // target (player or NPC); an empty swing (whiff) arrives with InCombat=false → crescent, no sparks.
        MeleeSwing?.Invoke(player.Map, player.X, player.Y, player.XOffset, player.YOffset, player.Dir, p.InCombat);
    }

    private void HandlePlayerCast(PlayerCastPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        var player = _state.Players[p.Index];
        player.Attacking = true;
        player.AttackTimer = Environment.TickCount64;
        // Heals on a peaceful target don't flip combat status — server gates this with InCombat.
        if (p.InCombat) player.LastCombatMs = Environment.TickCount64;

        // A cast that found nothing still costs the pose and the cooldown, both stamped above — the same
        // way a swing into thin air does. What it does NOT get is the spell itself: a bolt with nowhere to
        // fly would burst on the caster, which reads as the spell landing on them.
        var target = TargetRefFrom(p.TargetType, p.Target, p.TargetMap, p.SpawnMap, p.SpawnSlot);
        if (target.Kind == TargetKind.None) return;

        SpellCast?.Invoke(new SpellCastFx(player.Map, player.X, player.Y, player.XOffset, player.YOffset, 1,
            SpellTypeFor(p.SpellNum), target));
    }

    private void HandlePlayerDeath(PlayerDeathPacket p)
    {
        if (!SlotValidation.IsValidPlayerSlot(p.Index)) return;
        var player = _state.Players[p.Index];
        if (player.Sprite < 0) return;
        // Hand the shell the death render state so it can hold a delayed-death sprite until a killing bolt lands
        // (the shell excludes the LOCAL player, so your own death never lags).
        EntityDied?.Invoke(new EntityDeathFx(new TargetRef(TargetKind.Player, p.Index, 0),
            player.Sprite, p.MapNum, p.X, p.Y, 0f, 0f, p.Dir));
    }

    private void HandleSetTarget(SetTargetPacket p)
        => TargetAssigned?.Invoke(TargetRefFrom(p.TargetType, p.Target, p.TargetMap, p.SpawnMap, p.SpawnSlot));

    // Translates the server's TargetType convention (0=player, 1=npc, 2=self, 3=traversal, and
    // Constants.CastTargetNone for a cast that found nothing) into the client's TargetRef shape. Self
    // collapses to a Player ref on the local index, matching the click-own-tile path in HandleSearch.
    // Shared by target assignment and the cast-FX projectile homing.
    private TargetRef TargetRefFrom(byte targetType, int target, int targetMap, int spawnMap, int spawnSlot)
        => targetType switch
        {
            0 => new TargetRef(TargetKind.Player, target, 0),
            1 => new TargetRef(TargetKind.Npc, target, targetMap),
            2 => new TargetRef(TargetKind.Player, _state.MyIndex, 0),
            3 => new TargetRef(TargetKind.Traversal, spawnMap, spawnSlot),
            _ => default,
        };

    // Cached spell type for a spell number (client has all spell defs); falls back to SubHp for a stray num.
    private SpellType SpellTypeFor(int spellNum)
        => spellNum >= 1 && spellNum < _state.SpellDefs.Length && _state.SpellDefs[spellNum] is { } rec
            ? rec.Type : SpellType.SubHp;

    // Server rejected a client-proposed target (entity gone, slot mismatch, not observable).
    // Drop the local guess so the arrow disappears and the tab/click target state matches the
    // server's authoritative "no target" state.
    private void HandleClearTarget() => TargetAssigned?.Invoke(default);

    private void HandleNpcAttack(NpcAttackPacket p)
    {
        // Resolve native-or-guest by universal identity (mirrors HandleNpcCast) so a guest's swing spawns the
        // crescent swoosh + sparks exactly like a native's — players have no concept of native vs guest.
        var n = ResolveNpc(p.MapNum, p.NpcSlot, out int attackerMap, out bool isNative);
        if (n is null) return;
        n.Attacking = true;
        n.AttackTimer = Environment.TickCount64;
        n.LastCombatMs = Environment.TickCount64;
        if (isNative && IsCenter(p.MapNum)) MapNpcChanged?.Invoke(p.NpcSlot);
        // NPCs broadcast a swing only when actually striking a player, so always show sparks.
        MeleeSwing?.Invoke(attackerMap, n.X, n.Y, n.XOffset, n.YOffset, n.Dir, true);
    }

    // Visual is identical to a melee swing for v1: the NPC sprite plays its attack frame.  No
    // separate "casting" pose since NPC sprites don't have a dedicated cast frame, and the
    // viewport-scoped "A X casts a spell on Y." chat line tells observers what just happened.
    private void HandleNpcCast(NpcCastPacket p)
    {
        // The caster is addressed by its universal (mapNum, slot): a native sits at its home slot, a guest lives
        // in TraversalNpcs. Resolve whichever via the shared helper so the bolt leaves the right place and the
        // caster plays its cast pose whether it's home or chasing abroad.
        var n = ResolveNpc(p.MapNum, p.NpcSlot, out int casterMap, out bool isNative);
        if (n is null) return;
        n.Attacking = true;
        n.AttackTimer = Environment.TickCount64;
        n.LastCombatMs = Environment.TickCount64;
        if (isNative && IsCenter(p.MapNum)) MapNpcChanged?.Invoke(p.NpcSlot);
        // NPC magic is always an HP-drain bolt → SubHp (red) projectile homing to the target. TargetType 3
        // (a guest victim) resolves through SpawnMap/SpawnSlot — a native slot lookup would miss it.
        int casterSize = _state.NpcDefs[n.Num]?.EffectiveSize ?? 1;   // center the bolt origin on a big caster's body
        SpellCast?.Invoke(new SpellCastFx(casterMap, n.X, n.Y, n.XOffset, n.YOffset, casterSize,
            SpellType.SubHp, TargetRefFrom(p.TargetType, p.Target, p.TargetMap, p.SpawnMap, p.SpawnSlot)));
    }

    private void HandleNpcDamage(NpcDamagePacket p)
    {
        if (!SlotValidation.IsValidNpcSlot(p.NpcSlot)) return;
        var npcs = _state.NpcsForMap(p.MapNum);
        if (npcs is null) return;
        var n = npcs[p.NpcSlot];
        n.Hp -= p.Damage;
        // Only damage stamps combat (it's what makes the bar visible).  A negative-Damage heal
        // is just a vital update — refresh the displayed value + float a green number, but don't
        // pop the bar on an out-of-combat NPC.
        if (p.Damage > 0) n.LastCombatMs = Environment.TickCount64;
        // The combat number floats for an NPC on ANY observed map (positioned by its real map below);
        // only the center-map renderer needs the slot-changed notification.
        if (IsCenter(p.MapNum))
            MapNpcChanged?.Invoke(p.NpcSlot);
        if (p.Damage != 0)
            VitalDelta?.Invoke(p.NpcSlot, -p.Damage, VitalType.Hp, true, p.IsCrit, p.MapNum);
    }

    /// <summary>Apply a server blood update for a map: a FULL-LIST REPLACE of that map's pools (5 bytes each:
    /// x, y, size, amount, freshness).  Swapping the whole list is how a merged-away pool drops out (it simply
    /// isn't present).  Reset just marks a snapshot to a new observer; the client applies it identically.  Local
    /// decay (<c>BloodProcessor</c>) fades pools between deposits; the server never sends decay.</summary>
    private void HandleBloodUpdate(BloodUpdatePacket p)
    {
        if (!_state.IsObservedMap(p.MapNum)) return;   // a map we're not currently observing
        var pools = _state.BloodPoolsForMap(p.MapNum);
        pools.Clear();
        var b = p.Pools;
        const int stride = BloodUpdatePacket.BytesPerPool;
        for (int i = 0; i + stride - 1 < b.Length; i += stride)
        {
            var (x, y) = BloodUpdatePacket.PoolTileAt(b, i);
            pools.Add(new ClientState.BloodPool
            {
                X = x,
                Y = y,
                Size = b[i + 4],
                Amount = b[i + 5] / 255f * Constants.BloodMaxTileAmount,
                Freshness = b[i + 6] / 255f,
                Layer = (WorldLayer)b[i + 7],
            });
        }
    }

    private void HandleCombatText(CombatTextPacket p)
    {
        if (p.Kind == CombatTextKind.None) return;
        if (p.IsNpc)
        {
            // Index 0 = traversal guest (positioned by X/Y, no slot); otherwise a native NPC slot.
            if (p.Index != 0 && !SlotValidation.IsValidNpcSlot(p.Index)) return;
        }
        else if (!SlotValidation.IsValidPlayerSlot(p.Index))
        {
            return;
        }

        CombatText?.Invoke(p);
    }
}
