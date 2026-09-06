using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary>What an exchange sends out: the per-recipient hit lines, each resolved in the recipient's
/// own locale so attacker and victim read the same exchange in their own language, and the traversal
/// guest's state broadcast. Combat lines default to <c>ChatChannel.Combat</c>; EXP and loot pass
/// Rewards, level and durability notices pass System, and the server-wide death and Player-Killer
/// broadcasts pass Notice.</summary>
public sealed partial class CombatSystem : GameSystem
{
    // Per-recipient localized hit messages. The {Suffix} value ("(killed)") is resolved with
    // ForPlayer so it lands in the recipient's locale alongside the outer template.
    private void SendYouHitMsg(int index, string target, string weapName, int damage, int color, bool killing = false)
    {
        string suffix = killing ? ServerStrings.ForPlayer(index, ServerStrings.CombatSystem_KillSuffix) : "";
        if (weapName.Length > 0)
        {
            SendMsg(index, ServerStrings.CombatSystem_YouHitWithWeapon, color,
                ("Target", target), ("Weapon", weapName), ("Damage", damage), ("Suffix", suffix));
        }
        else
        {
            SendMsg(index, ServerStrings.CombatSystem_YouHit, color,
                ("Target", target), ("Damage", damage), ("Suffix", suffix));
        }
    }

    private void SendTheyHitYouMsg(int index, string attacker, string weapName, int damage, int color)
    {
        if (weapName.Length > 0)
        {
            SendMsg(index, ServerStrings.CombatSystem_TheyHitYouWithWeapon, color,
                ("Attacker", attacker), ("Weapon", weapName), ("Damage", damage));
        }
        else
        {
            SendMsg(index, ServerStrings.CombatSystem_TheyHitYou, color,
                ("Attacker", attacker), ("Damage", damage));
        }
    }

    // Broadcasts a traversal guest's full state to observers of its current map.  Optional
    // damage/dead flags drive the client's floating combat number and (on death) its removal.
    private void SendTraversalState(TraversalNpcRecord t, int damage = 0, bool isCrit = false, bool dead = false)
    {
        long now = Environment.TickCount64;
        var npc = _world.Npcs[t.Num];
        SendToMap(_world, t.CurrentMapNum, new TraversalNpcPacket
        {
            SpawnMapNum = t.SpawnMapNum,
            SpawnSlot = t.SpawnSlot,
            CurrentMapNum = t.CurrentMapNum,
            Num = t.Num,
            X = t.X,
            Y = t.Y,
            Dir = t.Dir,
            Movement = t.Moving,
            Hp = Math.Max(t.Hp, 0),
            MaxHp = _world.EffectiveNpcMaxHp(npc),
            MsSinceCombat = PacketBuilder.MsSinceCombat(t.CombatExpiresAt, now, CombatDurationMs),
            HasTarget = t.Target > 0,
            Attacking = t.Attacking,
            Damage = damage,
            IsCrit = isCrit,
            Dead = dead,
            Layer = t.Layer,
        });
    }
}
