using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

public sealed class SpellSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly CombatSystem _combat;
    private readonly ItemSystem _items;

    public SpellSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                       CombatSystem combat, ItemSystem items,
                       IClock? clock = null, IRandomSource? rng = null)
        : base(dispatcher, ChatChannel.Combat, clock: clock, rng: rng)
    {
        _world = world;
        _pm = pm;
        _combat = combat;
        _items = items;
    }

    /// <summary>Returns 1-based spell slot index of the first empty slot, or 0 if all full.</summary>
    public static int FindOpenSpellSlot(PlayerRecord p)
    {
        for (int i = 1; i <= Constants.MaxPlayerSpells; i++)
            if (p.Spell[i] == 0) return i;
        return 0;
    }

    public static bool HasSpell(PlayerRecord p, int spellNum)
    {
        for (int i = 1; i <= Constants.MaxPlayerSpells; i++)
            if (p.Spell[i] == spellNum) return true;
        return false;
    }

    /// <summary>Why a spell cannot be learned, or <see cref="Ok"/>.</summary>
    public enum LearnResult
    {
        Ok = 0,
        WrongClass,
        LevelTooLow,
        IntTooLow,
        BookFull,
        AlreadyKnown,
    }

    /// <summary>
    /// Whether <paramref name="p"/> may learn <paramref name="spell"/>, in the order the refusals are worth
    /// reporting. The scroll path and the editor's account browser both call this, so there is ONE answer to
    /// "may this character have this spell" — an operator handing a spell over is handing over a thing, and
    /// what can be used with it is the game's decision, exactly as it is for gear.
    /// </summary>
    /// <param name="cls">The character's class, for the affinity head-start on the INT requirement.</param>
    public static LearnResult CanLearn(PlayerRecord p, int spellNum, SpellRecord spell, ClassRecord cls)
    {
        if (!ClassGate.Allows(spell.AllowedClasses, p.Class)) return LearnResult.WrongClass;
        // The SPELL's own level gate, distinct from the scroll's: a scroll is a delivery mechanism and could
        // be handed out early, while the spell on it is tied to a tier. INT decides WHO may learn it, this
        // decides WHEN.
        if (spell.LevelReq > p.Level) return LearnResult.LevelTooLow;
        if (CombatFormulas.GetSpellIntRequirement(spell, cls.Int) > p.Int) return LearnResult.IntTooLow;
        // Known before full: a full book already holding this spell needs no slot, so "your book is full"
        // would name the wrong reason.
        if (HasSpell(p, spellNum)) return LearnResult.AlreadyKnown;
        if (FindOpenSpellSlot(p) == 0) return LearnResult.BookFull;
        return LearnResult.Ok;
    }

    // ── CastSpell ────────────────────────────────────────────────────────────
    //
    // Gate order (each step exits early on failure):
    //   1. Bounds check on slot
    //   2. HasSpell check (message if missing)
    //   3. Compute intReq / mpCost
    //   4. MP check          → "Not enough mana points!"
    //   5. Level check       → "You must be level X to cast this spell."
    //   6. INT check         → "You need X INT to cast this spell."
    //   7. Timer check       → silent exit (no message)
    //   8. GiveItem branch   → early return after handling
    //   9. Player-target branch (TargetType = 0)
    //      - PvP conditions met  → SUBHP/SUBMP/SUBSP + deduct MP + Casted=true
    //      - PvP conditions fail, ADD spell, same map → ADDHP/ADDMP/ADDSP + deduct MP + Casted=true
    //      - Otherwise → "Could not cast spell!"
    //   9. NPC-target branch
    //      - Non-friendly/shopkeeper → all six spell types + deduct MP + Casted=true
    //      - Friendly/shopkeeper     → "Could not cast spell!"
    //  10. If Casted: set AttackTimer. A cast that finds NO TARGET is a whiff — it pays the same cooldown a
    //      swing into thin air does, stamped by Whiff() where it is discovered. A REFUSAL (unknown spell,
    //      level, INT, mana, range, line of sight, aiming a harmful spell at yourself) pays nothing,
    //      because nothing was attempted.

    public void CastSpell(int index, int spellSlot, bool forceSelf = false)
    {
        if (!_pm[index].IsPlaying) return;
        if (_pm[index].Char.Dead) return;  // a corpse can't cast
        // Every spell, not only the harmful ones: an observer that could still heal or buff would be taking
        // part in the fight it is there to watch. Same throttled line the swing gets, so both read alike.
        if (_pm[index].Char.GodMode) { _combat.SayGodModeRefusal(index); return; }
        if (!SlotValidation.IsValidSpellSlot(spellSlot)) return;

        var sp = _pm[index];
        var p = sp.Char;

        // Snapshot the target BEFORE the spell resolves: a killing blow clears sp.Target* during resolution, so
        // the cast-FX packet (emitted at each success path below) must carry the ORIGINAL target for the
        // observer's projectile to fly to it — and its impact/death to line up there — not onto the caster.
        // Ctrl+Cast (forceSelf) is a transient self-cast: the FX must home to the caster, and we must NOT read
        // the selected target here so it survives the cast untouched.
        var fxTarget = forceSelf
            ? new CastFxTarget(2, index, 0, 0, 0)
            : new CastFxTarget(sp.TargetType, sp.Target, sp.TargetMap, sp.TargetSpawnMap, sp.TargetSpawnSlot);

        // 2. HasSpell — checked BEFORE the cast timer
        int spellNum = p.Spell[spellSlot];
        if (spellNum < 1) return;
        if (!HasSpell(p, spellNum))
        {
            SendMsg(index, ServerStrings.SpellSystem_NoSpell, GameColor.BrightRed, ChatChannel.System);
            return;
        }

        var spell = _world.Spells[spellNum];
        var cls = _world.Classes[p.Class];

        // A non-GiveItem spell with no VitalAmount is misconfigured; fizzle silently.
        if (spell.Type != SpellType.GiveItem && spell.VitalAmount == 0) return;
        int intReq = CombatFormulas.GetSpellIntRequirement(spell, cls.Int);
        // SubHp (the caster's "weapon") pays only a trivial pool-fraction MP cost plus a per-cast reagent; every
        // other spell type (heals, drains, GiveItem) keeps the full utility MP cost.
        // p.Int (not cls.Int) because that is exactly what RollSpellEffect hands RawSpellPower, and AddMp's
        // cost is priced off its own restore — the two must read the same stat or the margin drifts.
        int mpCost = spell.Type == SpellType.SubHp
            ? CombatFormulas.GetSubHpSpellMpCost(p.MaxMp)
            : CombatFormulas.GetSpellMpCost(spell, p.Int);

        // 4. MP check
        if (p.Mp < mpCost)
        {
            SendMsg(index, ServerStrings.SpellSystem_NotEnoughMana, GameColor.BrightRed, ChatChannel.System);
            return;
        }

        // 4b. Reagent check — a SubHp cast needs (and later consumes) its casting reagents (item index 2), the magic
        // mirror of keeping a weapon repaired.  Cost is DOUBLED in the rain (as rain doubles weapon wear) and WAIVED
        // for PvP casts on an Arena map — a cast at another PLAYER when the caster or that player is on an Arena (the
        // reagent mirror of arena gear wear being free).  Casting at an NPC or self ALWAYS pays: arena changes PvP,
        // never PvE.  The waiver is decided here, pre-resolution, so a killing blow that warps the victim to their
        // spawn map can't change what the later consumption charges.  No reagents, no cast (like a broken weapon).
        int reagentCost = 0;
        if (spell.Type == SpellType.SubHp)
        {
            bool pvpArenaFree = !forceSelf
                && sp.TargetType == 0 && sp.Target >= 1 && sp.Target <= _pm.Slots && _pm[sp.Target].IsPlaying
                && (_world.MoralOf(p.Map) == MapMoral.Arena || _world.MoralOf(_pm[sp.Target].Char.Map) == MapMoral.Arena);
            double exactReagents = pvpArenaFree ? 0 : SubHpReagentCostNow(p.Map, spell.LevelReq);
            reagentCost = CombatFormulas.RollReagents(exactReagents, Rng);
            // Hold what a cast CAN take, not what this one rolled: a roll of zero must not let an empty
            // pouch cast, any more than a weapon at 0 durability can swing. The arena waiver is free by
            // rule rather than by roll, so it needs no pouch.
            int reagentsNeeded = CombatFormulas.ReagentCostPerCast(exactReagents);
            if (ItemSystem.CountItem(p, _world.Items, Constants.CastingReagentItemIndex) < reagentsNeeded)
            {
                SendMsg(index, ServerStrings.SpellSystem_NotEnoughReagents, GameColor.BrightRed, ChatChannel.System,
                    ("Count", reagentsNeeded), ("Reagent", _world.Items[Constants.CastingReagentItemIndex].TrimmedName));
                return;
            }
        }

        // 5. Level check. Re-tested on every CAST, not only at learn time, for the same reason the INT
        // check below is: a delevel can drop a player under a spell they already know, and the spell book
        // has no sweep that unlearns it (gear gets one — ItemSystem.RevalidateEquipmentRequirements —
        // because a worn piece keeps applying its stats, whereas a spell only matters when it is cast).
        // So the cast path is where a spell stops being castable.
        if (spell.LevelReq > p.Level)
        {
            SendMsg(index, ServerStrings.SpellSystem_LevelRequired, GameColor.BrightRed, ChatChannel.System, ("Level", spell.LevelReq));
            return;
        }

        // 6. INT check
        if (intReq > p.Int)
        {
            SendMsg(index, ServerStrings.SpellSystem_IntRequired, GameColor.BrightRed, ChatChannel.System, ("Int", intReq));
            return;
        }

        // 7. Timer check — exits silently (no message).  Heavy Wind doubles the cast cooldown.
        long windMult = _world.WeatherOn(p.Map) == WeatherType.HeavyWind ? Constants.WeatherHeavyWindCooldownMultiplier : 1L;
        if (Environment.TickCount64 < sp.AttackTimer + Constants.SpellCastCooldownMs * windMult)
            return;

        // 7. GiveItem — give to targeted player if one is set, otherwise give to caster.  Ctrl+Cast (forceSelf)
        // always delivers to the caster: skip the "can't give to an NPC" rejection and force self as the
        // recipient, leaving the selected target untouched.
        if (spell.Type == SpellType.GiveItem)
        {
            if (!forceSelf && sp.TargetType is 1 or 3)
            {
                SendMsg(index, ServerStrings.SpellSystem_CannotCastOnNpc, GameColor.BrightRed, ChatChannel.System);
                return;
            }
            int targetIdx = (!forceSelf && sp.TargetType == 0 && sp.Target >= 1 && sp.Target <= _pm.Slots
                             && _pm[sp.Target].IsPlaying)
                            ? sp.Target
                            : index;
            var targetChar = _pm[targetIdx].Char;
            // Range + LoS gate: any *other* player target — same map or across a seam — must be
            // inside the caster's R=5 spell circle AND have a clear tile-line (no Blocked / closed
            // Key in the way).  TargetInRange resolves world coords through the 3×3 observable area
            // so cross-map deliveries get the same check.  Self-target GiveItem (targetIdx == index)
            // skips both gates — you can always hand an item to yourself.
            if (targetIdx != index)
            {
                if (targetChar.Dead)  // a corpse isn't a valid spell target — no item delivery to the dead
                {
                    SendMsg(index, ServerStrings.SpellSystem_CannotTargetDead, GameColor.BrightRed, ChatChannel.System);
                    return;
                }
                if (!TargetInRange(index, targetChar.Map, targetChar.X, targetChar.Y))
                {
                    SendMsg(index, ServerStrings.SpellSystem_OutOfRange, GameColor.BrightRed, ChatChannel.System);
                    return;
                }
                if (!HasLineOfSight(index, targetChar.Map, targetChar.X, targetChar.Y, targetChar.Layer))
                {
                    SendMsg(index, ServerStrings.SpellSystem_NoLineOfSight, GameColor.BrightRed, ChatChannel.System);
                    return;
                }
            }
            int slot = ItemSystem.FindOpenInvSlot(targetChar, _world.Items, spell.ItemNum);
            if (slot > 0)
            {
                // Bolt first, delivery second: the wind takes a committed cast in flight, so the
                // projectile has to be away before it does.
                SendToMap(_world, p.Map, new PlayerCastPacket
                {
                    Index = index, SpellNum = spellNum,
                    TargetType = (byte)(targetIdx == index ? 2 : 0), // self, or a player recipient
                    Target = targetIdx, TargetMap = targetChar.Map,
                });
                if (!WindTakesTheSpell(index, targetChar.Map, isNpc: false, targetIndex: targetIdx))
                    _items.GiveItem(targetIdx, spell.ItemNum, spell.ItemQuantity);
                SpendCastCost(index, mpCost, spell, reagentCost);
                sp.AttackTimer = Environment.TickCount64;
            }
            else
            {
                if (targetIdx == index)
                {
                    SendMsg(index, ServerStrings.Common_InventoryFull, GameColor.BrightRed, ChatChannel.System);
                }
                else
                {
                    SendMsg(index, ServerStrings.SpellSystem_TargetInventoryFull, GameColor.BrightRed, ChatChannel.System,
                        ("TargetName", targetChar.TrimmedName));
                }
            }
            return;
        }

        // 8/9. Target-based branches. Each success path calls BroadcastCastFx AFTER its gates pass but before
        // the effect resolves — so only a genuine cast produces a projectile (and combat bar), and the client
        // still gets the bolt ahead of the damage. The NPC branches emit from inside CastSpellOnNpc (past its
        // own friendly-target gate); the player/self branches emit inline below.
        int n = sp.Target;
        bool casted = false;
        // For the player/self branches: whether this cast draws the caster into combat, passed to the cast-FX
        // broadcast so observer clients stamp the bar. Sub spells always set it; Add (heal) spells only when the
        // target is already fighting — healing in peace is a non-combat action.
        bool inCombat = false;
        int mapNum = p.Map;
        // Cross-map: an NPC target (native slot or traversal guest) may stand on a neighbor map — resolve
        // the spell to that map.  We DON'T gate on the observer set here: right after a seam cross the
        // client grid and the server's observer set can disagree for a frame, which would wrongly resolve
        // to the caster's map and report "no longer valid".  TargetInRange (world-distance) is the real
        // gate below — an out-of-view target simply reads "out of range", and a still-visible one casts.
        // Ctrl+Cast resolves on the caster's OWN map — don't let a cross-map NPC target redirect a self-cast.
        if (!forceSelf && (sp.TargetType == 1 || sp.TargetType == 3) && sp.TargetMap > 0 && sp.TargetMap <= _world.Limits.Maps)
            mapNum = sp.TargetMap;

        if (sp.TargetType == 3 && !forceSelf)
        {
            // ── Traversal (chasing) NPC target — addressed by identity, no fixed slot ──
            var guest = FindTraversalTarget(sp);
            if (guest is null || guest.Num <= 0)
            {
                SendMsg(index, ServerStrings.SpellSystem_TargetInvalid, GameColor.BrightRed, ChatChannel.System);
                sp.Target = 0;
                sp.TargetType = 0;
                sp.TargetSpawnSlot = 0;
            }
            else
            {
                // Resolve against the guest's CURRENT map (it roams) and keep sp.TargetMap in step.
                sp.TargetMap = guest.CurrentMapNum;
                if (!TargetInRange(index, guest.CurrentMapNum, guest.X, guest.Y, _world.Npcs[guest.Num].EffectiveSize))
                    SendMsg(index, ServerStrings.SpellSystem_OutOfRange, GameColor.BrightRed, ChatChannel.System);
                else if (!HasLineOfSight(index, guest.CurrentMapNum, guest.X, guest.Y, guest.Layer))
                    SendMsg(index, ServerStrings.SpellSystem_NoLineOfSight, GameColor.BrightRed, ChatChannel.System);
                else
                    casted = CastSpellOnNpc(index, p, spell, mpCost, guest.CurrentMapNum, guest, npcSlot: 0, spellNum, fxTarget, reagentCost);
            }
        }
        else if (forceSelf || n == 0 || sp.TargetType == 2)
        {
            // No target: Add spells land on caster; Sub spells report no target.
            // Self target (TargetType == 2) and Ctrl+Cast (forceSelf): same outcome — Add spells only, land on
            // caster.  forceSelf reaches here regardless of the selected target, which is left untouched.
            bool addType = spell.Type >= SpellType.AddHp && spell.Type <= SpellType.AddSp;
            if (!addType)
            {
                // Casting a harmful spell with nothing selected is a WHIFF and pays the cooldown; aiming one
                // at yourself is a refusal and pays nothing. The difference is whether there was a target at
                // all — a refusal tells you the target is wrong, a whiff tells you there wasn't one.
                bool noTarget = !forceSelf && n == 0;
                SendMsg(index, noTarget ? ServerStrings.SpellSystem_NoTarget : ServerStrings.SpellSystem_CannotHarmSelf,
                    GameColor.BrightRed, ChatChannel.System);
                if (noTarget) Whiff(index, p.Map, spellNum);
                return;
            }
            // Self-target heal: target == caster, so the "target in combat" rule resolves to the caster's own
            // state — a peaceful self-heal stays peaceful. Decide it up front so the FX broadcast (which must
            // precede the heal's number) carries the right combat flag.
            long nowSelf = Environment.TickCount64;
            inCombat = sp.IsInCombat(nowSelf);
            BroadcastCastFx(index, p.Map, spellNum, inCombat, fxTarget);
            if (!WindTakesTheSpell(index, p.Map, isNpc: false, targetIndex: index))
                ApplyAddSpellToCaster(index, p, spell, mapNum);
            SpendCastCost(index, mpCost, spell, reagentCost);
            casted = true;
            if (inCombat) _combat.MarkPlayerCombat(index, nowSelf, asAttacker: false);
        }
        else if (sp.TargetType == 0)
        {
            // ── Player target ───────────────────────────────────────────────
            if (n >= 1 && n <= _pm.Slots && _pm[n].IsPlaying)
            {
                var tp = _pm[n].Char;
                int tMap = tp.Map;

                // Range/observability gate. The spell circle is the SAME size whether the target is
                // on the caster's map or a neighbor — a caster standing near a seam can cast across it
                // (their circle already straddles the border, and blocking it would dead-zone half
                // the legitimate reach).  The circle is symmetric so range is naturally two-way for
                // PvP; observability rejects anything outside the caster's 9-map region.
                if (!_world.IsObserving(index, tMap)
                    || !TargetInRange(index, tMap, tp.X, tp.Y))
                {
                    SendMsg(index, ServerStrings.SpellSystem_OutOfRange, GameColor.BrightRed, ChatChannel.System);
                    return;
                }
                if (!HasLineOfSight(index, tMap, tp.X, tp.Y, tp.Layer))
                {
                    SendMsg(index, ServerStrings.SpellSystem_NoLineOfSight, GameColor.BrightRed, ChatChannel.System);
                    return;
                }

                // The PvP exemption is about taking part at all, so it gates every spell aimed at another
                // player rather than only the harmful ones: an administrator neither harms nor helps, and
                // nobody reaches an administrator either. Sub spells re-read the same block below for the
                // rest of its cases (safe zone, level, party), which admin short-circuits ahead of.
                if (n != index)
                {
                    var adminBlock = _combat.GetPvpBlock(index, n);
                    if (adminBlock is PvpBlock.AttackerAdmin or PvpBlock.VictimAdmin)
                    {
                        var (adminKey, adminArgs) = CombatSystem.PvpBlockMessage(adminBlock, tp.TrimmedName);
                        SendMsg(index, adminKey, CombatSystem.PvpBlockColor(adminBlock), ChatChannel.System, adminArgs);
                        return;
                    }
                }

                // Bug fix: Add spells bypass pvpOk — they reach any same-map player.
                // Add spells live in the Else branch, so when pvpOk is true (non-safe zone,
                // level 10+) they silently consumed MP and did nothing (modGameLogic.bas line 1952).
                bool addType = spell.Type is SpellType.AddHp or SpellType.AddMp or SpellType.AddSp;
                if (addType)
                {
                    if (tp.Dead)  // a corpse isn't a valid spell target — no heal/buff on the dead (mirrors the harmful branch's Hp > 0 gate)
                    {
                        SendMsg(index, ServerStrings.SpellSystem_CannotTargetDead, GameColor.BrightRed, ChatChannel.System);
                        return;
                    }
                    // Heal-while-target-fights draws the caster into combat; a peaceful heal leaves both players
                    // untouched. Decide it up front so the FX broadcast (which must precede the heal's number)
                    // carries the right combat flag.
                    long nowAdd = Environment.TickCount64;
                    inCombat = _pm[n].IsInCombat(nowAdd);
                    BroadcastCastFx(index, p.Map, spellNum, inCombat, fxTarget);
                    var observers = _world.MapObservers[tMap];
                    if (!WindTakesTheSpell(index, tMap, isNpc: false, targetIndex: n))
                    {
                        switch (spell.Type)
                        {
                            case SpellType.AddHp:
                            {
                                var (amount, wasCrit) = RollSpellEffect(index, p, spell,
                                    critSelfKey: ServerStrings.CombatSystem_SpellSurge,
                                    critOtherKey: ServerStrings.CombatSystem_SpellSurgeOnYou,
                                    critOtherArgs: [("PlayerName", p.TrimmedName)],
                                    otherIndex: n, critOtherColor: GameColor.BrightCyan);
                                int delta = Math.Min(amount, Math.Max(0, tp.MaxHp - tp.Hp));
                                tp.Hp += delta;
                                _dispatcher.SendToObservers(observers, PacketBuilder.SendHp(n, tp.Hp, tp.MaxHp, showFloat: true, isCrit: wasCrit));
                                if (delta > 0) SendRestoredPlayerMsgs(index, n, p, tp, delta, ServerStrings.CombatSystem_VitalHp);
                                break;
                            }
                            case SpellType.AddMp:
                            {
                                var (amount, _) = RollSpellEffect(index, p, spell,
                                    critSelfKey: ServerStrings.CombatSystem_SpellSurge,
                                    critOtherKey: ServerStrings.CombatSystem_SpellSurgeOnYou,
                                    critOtherArgs: [("PlayerName", p.TrimmedName)],
                                    otherIndex: n, critOtherColor: GameColor.BrightCyan);
                                int delta = Math.Min(amount, Math.Max(0, tp.MaxMp - tp.Mp));
                                tp.Mp += delta;
                                _dispatcher.SendToObservers(observers, PacketBuilder.SendMp(n, tp.Mp, tp.MaxMp, showFloat: true));
                                if (delta > 0) SendRestoredPlayerMsgs(index, n, p, tp, delta, ServerStrings.CombatSystem_VitalMp);
                                break;
                            }
                            case SpellType.AddSp:
                            {
                                var (amount, _) = RollSpellEffect(index, p, spell,
                                    critSelfKey: ServerStrings.CombatSystem_SpellSurge,
                                    critOtherKey: ServerStrings.CombatSystem_SpellSurgeOnYou,
                                    critOtherArgs: [("PlayerName", p.TrimmedName)],
                                    otherIndex: n, critOtherColor: GameColor.BrightCyan);
                                int delta = Math.Min(CombatFormulas.ScaleMpEffectToSp(amount, tp.MaxMp, tp.MaxSp), Math.Max(0, tp.MaxSp - tp.Sp));
                                tp.Sp += delta;
                                _dispatcher.SendToObservers(observers, PacketBuilder.SendSp(n, tp.Sp, tp.MaxSp, showFloat: true));
                                if (delta > 0) SendRestoredPlayerMsgs(index, n, p, tp, delta, ServerStrings.CombatSystem_VitalSp);
                                break;
                            }
                        }
                    }
                    SpendCastCost(index, mpCost, spell, reagentCost);
                    casted = true;
                    if (inCombat) _combat.MarkPlayerCombat(index, nowAdd, asAttacker: false);
                }
                else
                {
                    // Sub spells require PvP conditions. Same-side (party or guild) is classified
                    // first via the shared CombatSystem.GetFriendlyRelation — same order + arena
                    // carve-out as melee.
                    var relation = _combat.GetFriendlyRelation(index, n);
                    if (relation != FriendlyRelation.None)
                    {
                        SendMsg(index, relation == FriendlyRelation.Guild
                                    ? ServerStrings.SpellSystem_CannotHarmGuild
                                    : ServerStrings.SpellSystem_CannotHarmParty,
                                GameColor.BrightRed, ChatChannel.System);
                    }
                    else if (tp.Hp > 0)
                    {
                        var block = _combat.GetPvpBlock(index, n);
                        if (block == PvpBlock.None)
                        {
                            {
                                long now = Environment.TickCount64;
                                _combat.MarkPlayerCombat(index, now, asAttacker: true);
                                _combat.MarkPlayerCombat(n, now, asAttacker: false);
                                _combat.MarkPvpInitiator(index, n, now);
                                inCombat = true;
                            }
                            // PvP allowed + committed: emit the bolt now, before the damage switch below sends
                            // its number/death, so the client defers them onto the projectile's arrival.
                            BroadcastCastFx(index, p.Map, spellNum, inCombat, fxTarget);
                            if (!WindTakesTheSpell(index, tMap, isNpc: false, targetIndex: n))
                            {
                                switch (spell.Type)
                                {
                                    case SpellType.SubHp:
                                    {
                                        var (amount, wasCrit) = RollSpellEffect(index, p, spell,
                                            critSelfKey: ServerStrings.CombatSystem_SpellForce,
                                            critOtherKey: ServerStrings.CombatSystem_SpellForceOnYou,
                                            critOtherArgs: [("PlayerName", p.TrimmedName)],
                                            otherIndex: n);
                                        if (_combat.TryPlayerNegateMagicFromPlayer(index, n)) break;   // shield blocks / no-shield dodges the spell (mirror of melee)
                                        int damage = CombatFormulas.ResolveDamage(amount, _combat.GetPlayerProtection(n, _pm[index].Char.Map), CombatFormulas.PvpDamageMultiplier);
                                        if (damage > 0)
                                        {
                                            _combat.ApplyPlayerDamage(index, n, damage, isCrit: wasCrit);
                                        }
                                        else
                                        {
                                            SendMsg(index, ServerStrings.CombatSystem_SpellTooWeak, GameColor.BrightRed, ("TargetName", tp.TrimmedName));
                                            SendMsg(n, ServerStrings.CombatSystem_SpellNoPhase, GameColor.BrightBlue, ("AttackerName", p.TrimmedName));
                                            _combat.BroadcastCombatText(tMap, isNpc: false, index: n, CombatTextKind.ZeroHit);
                                        }
                                        break;
                                    }
                                    case SpellType.SubMp:
                                    {
                                        var (amount, _) = RollSpellEffect(index, p, spell, critSelfKey: ServerStrings.CombatSystem_SpellForce);
                                        if (_combat.TryPlayerNegateMagicFromPlayer(index, n)) break;   // shield blocks / no-shield dodges the spell (mirror of melee)
                                        int drain = CombatFormulas.ResolveDamage(amount, _combat.GetPlayerProtection(n, _pm[index].Char.Map));
                                        if (drain > 0)
                                        {
                                            int delta = Math.Min(drain, tp.Mp);
                                            tp.Mp -= delta;
                                            SendToMap(_world, tMap, PacketBuilder.SendMp(n, tp.Mp, tp.MaxMp, showFloat: true));
                                            if (delta > 0) SendDrainedPlayerMsgs(index, n, p, tp, delta, ServerStrings.CombatSystem_VitalMp);
                                        }
                                        else
                                        {
                                            _combat.BroadcastCombatText(tMap, isNpc: false, index: n, CombatTextKind.ZeroHit, vital: CombatVital.Mp);
                                        }

                                        break;
                                    }
                                    case SpellType.SubSp:
                                    {
                                        var (amount, _) = RollSpellEffect(index, p, spell, critSelfKey: ServerStrings.CombatSystem_SpellForce);
                                        if (_combat.TryPlayerNegateMagicFromPlayer(index, n)) break;   // shield blocks / no-shield dodges the spell (mirror of melee)
                                        int drain = CombatFormulas.ScaleMpEffectToSp(CombatFormulas.ResolveDamage(amount, _combat.GetPlayerProtection(n, _pm[index].Char.Map)), tp.MaxMp, tp.MaxSp);
                                        if (drain > 0)
                                        {
                                            int delta = Math.Min(drain, tp.Sp);
                                            tp.Sp -= delta;
                                            SendToMap(_world, tMap, PacketBuilder.SendSp(n, tp.Sp, tp.MaxSp, showFloat: true));
                                            if (delta > 0) SendDrainedPlayerMsgs(index, n, p, tp, delta, ServerStrings.CombatSystem_VitalSp);
                                        }
                                        else
                                        {
                                            _combat.BroadcastCombatText(tMap, isNpc: false, index: n, CombatTextKind.ZeroHit, vital: CombatVital.Sp);
                                        }

                                        break;
                                    }
                                }
                            }

                            SpendCastCost(index, mpCost, spell, reagentCost);
                            casted = true;
                        }
                        else
                        {
                            var (pvpKey, pvpArgs) = CombatSystem.PvpBlockMessage(block, tp.TrimmedName);
                            SendMsg(index, pvpKey, CombatSystem.PvpBlockColor(block), ChatChannel.System, pvpArgs);
                        }
                    }
                    else
                    {
                        SendMsg(index, ServerStrings.SpellSystem_CannotHarmPlayer, GameColor.BrightRed, ChatChannel.System);
                    }
                }
            }
            else
            {
                SendMsg(index, ServerStrings.SpellSystem_TargetInvalid, GameColor.BrightRed, ChatChannel.System);
                sp.Target = 0;
                sp.TargetType = 0;
            }
        }
        else
        {
            // ── Native slot NPC target ──────────────────────────────────────
            if (n > 0 && n <= Constants.MaxMapNpcs)
            {
                var mapNpc = _world.MapNpcs[mapNum, n];
                if (mapNpc.Num > 0)
                {
                    if (!TargetInRange(index, mapNum, mapNpc.X, mapNpc.Y, _world.Npcs[mapNpc.Num].EffectiveSize))
                    {
                        SendMsg(index, ServerStrings.SpellSystem_OutOfRange, GameColor.BrightRed, ChatChannel.System);
                        return;
                    }
                    if (!HasLineOfSight(index, mapNum, mapNpc.X, mapNpc.Y, mapNpc.Layer))
                    {
                        SendMsg(index, ServerStrings.SpellSystem_NoLineOfSight, GameColor.BrightRed, ChatChannel.System);
                        return;
                    }
                    casted = CastSpellOnNpc(index, p, spell, mpCost, mapNum, mapNpc, n, spellNum, fxTarget, reagentCost);
                }
                else
                {
                    SendMsg(index, ServerStrings.SpellSystem_TargetInvalid, GameColor.BrightRed, ChatChannel.System);
                    sp.Target = 0;
                    sp.TargetType = 0;
                }
            }
            else
            {
                SendMsg(index, ServerStrings.SpellSystem_NoTarget, GameColor.BrightRed, ChatChannel.System);
                Whiff(index, p.Map, spellNum);
            }
        }

        if (casted)
        {
            // Cast succeeded: stamp the cooldown. The cast FX packet already went out above (before the
            // damage) so observers could time the number/death to the projectile. A cast that found nothing
            // is stamped by Whiff at the point it is discovered, since those paths return early.
            sp.AttackTimer = Environment.TickCount64;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Identity of a cast's target, snapshotted before the spell resolves (a killing blow clears
    /// sp.Target* mid-resolution). Carried on the cast-FX packet so the observer's projectile homes to the right
    /// entity — and its impact/death lines up there — even after that entity dies.</summary>
    private readonly record struct CastFxTarget(byte TargetType, int Target, int TargetMap, int SpawnMap, int SpawnSlot);

    /// <summary>Broadcasts the cast animation + typed projectile FX to the caster's map observers. Sent ONLY on a
    /// cast's success path — after every range/LoS/target/PvP gate passes but BEFORE the effect sends its
    /// damage/death — so a rejected cast produces no phantom projectile and no combat bar, while a valid one still
    /// lets the client register the in-flight bolt and defer the damage number / death onto its arrival.</summary>
    /// <summary>A cast that found nothing to land on: the caster pays the cooldown and nothing else.
    ///
    /// <para>🔴 A whiff is not a refusal. Everything the spell can be turned down for — not known, level,
    /// INT, mana, out of range, no line of sight — costs nothing, because nothing was attempted. Here the
    /// player did cast and there was simply no one to cast at, which is a swing into thin air and is priced
    /// like one.</para>
    ///
    /// <para>Broadcast rather than only stamped, because the cooldown BAR is drawn from the client's own
    /// copy of the timer and the client learns it from this packet. <see cref="Constants.CastTargetNone"/>
    /// says there is nothing to fly at, so the pose and the timer play and no projectile does — a bolt with
    /// nowhere to go bursts on the caster, which reads as the spell landing on them. No mana, no reagent,
    /// and no combat flag: a whiff starts no fight.</para></summary>
    private void Whiff(int index, int casterMap, int spellNum)
    {
        SendToMap(_world, casterMap, new PlayerCastPacket
        {
            Index = index, SpellNum = spellNum, TargetType = Constants.CastTargetNone,
        });
        _pm[index].AttackTimer = Environment.TickCount64;
    }

    private void BroadcastCastFx(int index, int casterMap, int spellNum, bool inCombat, CastFxTarget t) =>
        SendToMap(_world, casterMap, new PlayerCastPacket
        {
            Index = index, SpellNum = spellNum, InCombat = inCombat,
            TargetType = t.TargetType, Target = t.Target, TargetMap = t.TargetMap,
            SpawnMap = t.SpawnMap, SpawnSlot = t.SpawnSlot,
        });

    /// <summary>Rolls the wind against a cast that is already committed and away. Called immediately after
    /// the cast-FX broadcast and in place of the effect, so a torn spell keeps full parity with one that was
    /// blocked or dodged: the projectile still flies, the fizzle floats over the TARGET, the fight is already
    /// marked, and the caller still charges the cast.</summary>
    private bool WindTakesTheSpell(int index, int targetMap, bool isNpc, int targetIndex, int x = 0, int y = 0)
    {
        if (!_combat.WindTearsItAway(_pm[index].Char.Map)) return false;
        SendMsg(index, ServerStrings.CombatSystem_YourSpellMissed, GameColor.BrightCyan);
        // A player on the receiving end reads it from their side, the way a melee miss messages both.
        if (!isNpc && targetIndex != index)
            SendMsg(targetIndex, ServerStrings.CombatSystem_AttackerSpellMissed, GameColor.BrightCyan,
                    ("AttackerName", _pm[index].Char.TrimmedName));
        _combat.BroadcastCombatText(targetMap, isNpc, targetIndex, CombatTextKind.Miss, x, y);
        return true;
    }

    /// <summary>The shared kernel for every Add/Sub spell. Computes
    /// <c>raw = caster.Int / 3 + spell.VitalAmount</c> (via <see cref="CombatFormulas.RawSpellPower"/>),
    /// rolls a spell crit, drains SP and emits the configured crit-messages if it lands,
    /// applies <see cref="CombatFormulas.Vary"/>, and returns the final amount + crit flag.
    /// The CALLER applies that amount to whatever vital/target it's working on and runs any
    /// post-amount messaging / broadcast — keeps Healing-radiant-energy vs Magic-surges-with-
    /// overwhelming-force as caller decisions, while the math collapses.</summary>
    private (int amount, bool wasCrit) RollSpellEffect(
        int casterIndex, PlayerRecord caster, SpellRecord spell,
        string? critSelfKey = null, string? critOtherKey = null, int otherIndex = 0,
        int critOtherColor = GameColor.BrightRed,
        (string Key, object? Value)[]? critOtherArgs = null)
    {
        int raw = CombatFormulas.RawSpellPower(caster.Int, spell.VitalAmount);
        bool wasCrit = _combat.CanSpellCritical(caster);
        if (wasCrit)
        {
            _combat.DrainSpForCrit(casterIndex);
            raw = CombatFormulas.CritDamage(raw);
            if (critSelfKey is not null) SendMsg(casterIndex, critSelfKey, GameColor.BrightCyan);
            if (critOtherKey is not null && otherIndex > 0)
                SendMsg(otherIndex, critOtherKey, critOtherColor, critOtherArgs ?? []);
        }
        return (CombatFormulas.Vary(raw), wasCrit);
    }

    private void ApplyAddSpellToCaster(int index, PlayerRecord p, SpellRecord spell, int mapNum)
    {
        var observers = _world.MapObservers[mapNum];
        switch (spell.Type)
        {
            case SpellType.AddHp:
            {
                var (amount, wasCrit) = RollSpellEffect(index, p, spell, critSelfKey: ServerStrings.CombatSystem_SpellSurge);
                int delta = Math.Min(amount, Math.Max(0, p.MaxHp - p.Hp));
                p.Hp += delta;
                _dispatcher.SendToObservers(observers, PacketBuilder.SendHp(index, p.Hp, p.MaxHp, showFloat: true, isCrit: wasCrit));
                if (delta > 0) SendRestoredSelfMsg(index, delta, ServerStrings.CombatSystem_VitalHp);
                break;
            }
            case SpellType.AddMp:
            {
                var (amount, _) = RollSpellEffect(index, p, spell, critSelfKey: ServerStrings.CombatSystem_SpellSurge);
                int delta = Math.Min(amount, Math.Max(0, p.MaxMp - p.Mp));
                p.Mp += delta;
                _dispatcher.SendToObservers(observers, PacketBuilder.SendMp(index, p.Mp, p.MaxMp, showFloat: true));
                if (delta > 0) SendRestoredSelfMsg(index, delta, ServerStrings.CombatSystem_VitalMp);
                break;
            }
            case SpellType.AddSp:
            {
                var (amount, _) = RollSpellEffect(index, p, spell, critSelfKey: ServerStrings.CombatSystem_SpellSurge);
                int delta = Math.Min(CombatFormulas.ScaleMpEffectToSp(amount, p.MaxMp, p.MaxSp), Math.Max(0, p.MaxSp - p.Sp));
                p.Sp += delta;
                _dispatcher.SendToObservers(observers, PacketBuilder.SendSp(index, p.Sp, p.MaxSp, showFloat: true));
                if (delta > 0) SendRestoredSelfMsg(index, delta, ServerStrings.CombatSystem_VitalSp);
                break;
            }
        }
    }

    private void SetMp(int index, int newMp)
    {
        var p = _pm[index].Char;
        p.Mp = Math.Max(newMp, 0);
        SendToMap(_world, p.Map, PacketBuilder.SendMp(index, p.Mp, p.MaxMp));
    }

    // What a SubHp cast costs on average right now, DOUBLED while it's raining — the magic-side mirror of
    // Rain doubling weapon-durability wear (CombatSystem.DegradeItemDurability). Arena's reagent waiver is
    // PvP-only and decided at the cast gate (a cast at an NPC always pays), so this is just the rain cost.
    // Fractional: CombatFormulas turns it into a flat charge on a chance.
    private double SubHpReagentCostNow(int casterMap, int spellLevelReq) =>
        CombatFormulas.SubHpReagentCostExact(spellLevelReq)
        * (_world.WeatherOn(casterMap) == WeatherType.Rain ? Constants.WeatherRainReagentMultiplier : 1);

    // Spend a successful cast's cost: deduct MP always, and — for SubHp only — consume its reagents (the per-cast
    // sink) in the amount computed up front (0 when arena-involved).  One chokepoint so every success path
    // (self / player / NPC target) spends both together.  The reagent stock was already verified up front.
    private void SpendCastCost(int index, int mpCost, SpellRecord spell, int reagentCost)
    {
        SetMp(index, _pm[index].Char.Mp - mpCost);
        if (spell.Type != SpellType.SubHp || reagentCost <= 0) return;
        // L2 guild perk: a chance to spend no reagents for this cast (mirrors the durability-wear skip).
        if (GuildPerks.IsActive(_world.Guilds.GetValueOrDefault(_pm[index].Guild), Constants.GuildPerkLevelPreventWear)
            && Rng.Percent() < Constants.GuildPerkPreventWearChancePercent)
        {
            return;
        }

        _items.TakeItem(index, Constants.CastingReagentItemIndex, reagentCost);
    }

    // Unified vital-change messages: one template per direction (heal / drain), one vital-noun
    // token swapped in ("hit/mana/stamina points") so HP, MP and SP all read the same way. The
    // vital noun is resolved per recipient so each side sees the line — outer template AND vital
    // noun — in their own session locale.
    private void SendRestoredPlayerMsgs(int casterIndex, int targetIndex, PlayerRecord caster, PlayerRecord target, int amount, string vitalKey)
    {
        SendMsg(casterIndex, ServerStrings.CombatSystem_YouRestored, GameColor.White,
            ("Amount", amount), ("Vital", ServerStrings.ForPlayer(casterIndex, vitalKey)), ("Target", target.TrimmedName));
        SendMsg(targetIndex, ServerStrings.CombatSystem_TheyRestoredYou, GameColor.BrightGreen,
            ("Caster", caster.TrimmedName), ("Amount", amount), ("Vital", ServerStrings.ForPlayer(targetIndex, vitalKey)));
    }

    private void SendRestoredSelfMsg(int casterIndex, int amount, string vitalKey) =>
        SendMsg(casterIndex, ServerStrings.CombatSystem_YouRestored, GameColor.White,
            ("Amount", amount), ("Vital", ServerStrings.ForPlayer(casterIndex, vitalKey)), ("Target", "yourself"));

    private void SendRestoredNpcMsg(int casterIndex, string npcName, int amount, string vitalKey) =>
        SendMsg(casterIndex, ServerStrings.CombatSystem_YouRestored, GameColor.White,
            ("Amount", amount), ("Vital", ServerStrings.ForPlayer(casterIndex, vitalKey)), ("Target", $"a {npcName}"));

    private void SendDrainedPlayerMsgs(int casterIndex, int targetIndex, PlayerRecord caster, PlayerRecord target, int amount, string vitalKey)
    {
        SendMsg(casterIndex, ServerStrings.CombatSystem_YouDrained, GameColor.White,
            ("Target", target.TrimmedName), ("Amount", amount), ("Vital", ServerStrings.ForPlayer(casterIndex, vitalKey)));
        SendMsg(targetIndex, ServerStrings.CombatSystem_TheyDrainedYou, GameColor.BrightRed,
            ("Attacker", caster.TrimmedName), ("Amount", amount), ("Vital", ServerStrings.ForPlayer(targetIndex, vitalKey)));
    }

    private void SendDrainedNpcMsg(int casterIndex, string npcName, int amount, string vitalKey) =>
        SendMsg(casterIndex, ServerStrings.CombatSystem_YouDrained, GameColor.White,
            ("Target", $"a {npcName}"), ("Amount", amount), ("Vital", ServerStrings.ForPlayer(casterIndex, vitalKey)));

    // Spell-circle range gate, world-space so it is correct across a seamless border: is the target
    // on <paramref name="targetMap"/> at (targetX,targetY) within the caster's spell circle?
    // The R=5 circle is symmetric so the check is inherently two-way (PvP-fair without a mutual
    // variant).  Mirrors the client's gray-arrow check.  Returns false if the target isn't observable.
    private bool TargetInRange(int index, int targetMap, int targetX, int targetY, int targetSize = 1)
    {
        var p = _pm[index].Char;
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, p.Map);
        var (myWX, myWY) = grid.CenterToWorld(p.X, p.Y);  // caster (a player) sits at the center cell, size 1
        var tw = grid.ToWorldRelative(targetMap, targetX, targetY);
        if (tw is null) return false;
        // Footprint-aware: an oversize NPC is in range when ANY tile of its body is in the circle, not just (X,Y).
        return WorldCoordHelper.IsInSpellRange(myWX, myWY, 1, tw.Value.worldX, tw.Value.worldY, targetSize);
    }

    // Authoritative spell line-of-sight: a straight tile-line from caster to target may not cross any
    // Blocked or closed-Key tile. Paired with TargetInRange at every cast site so a target the player
    // can "see" but cannot actually shoot at (wall in the way) is rejected with a distinct message.
    // Mirrored on the client to color the target arrow gray — same WorldCoordHelper algorithm.
    private bool HasLineOfSight(int index, int targetMap, int targetX, int targetY, WorldLayer targetLayer)
    {
        var p = _pm[index].Char;
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, p.Map);
        var (myWX, myWY) = grid.CenterToWorld(p.X, p.Y);
        var tw = grid.ToWorldRelative(targetMap, targetX, targetY);
        if (tw is null) return false;

        // Two-layer world: the caster and target must first CONNECT across layers — same layer always; across
        // layers only when one of them stands on a ramp (a person on a ramp can see/shoot both the ground and the
        // deck; a plain ground and a plain fringe point never see each other). If they connect, the normal
        // straight tile-line LoS (walls / closed doors, read on the caster's layer) applies — no cross-layer
        // exception, per the design rule.
        if (!LayerLogic.LayerConnects(new ServerTileView(_world, grid), myWX, myWY, p.Layer, tw.Value.worldX, tw.Value.worldY, targetLayer))
            return false;

        // A CROSS-LAYER cast (connected via a ramp) treats ramp tiles on the line as walls, so you can't cast
        // through a ramp to a target behind/under it — only a clean shot at the ramp foot lands (endpoints are
        // excluded from the trace). A same-layer cast reads the plain caster-layer obstacles.
        bool crossLayer = p.Layer != targetLayer;
        return WorldCoordHelper.HasClearSpellLineOfSight(
            myWX, myWY, tw.Value.worldX, tw.Value.worldY,
            new WorldLosPredicate(_world, grid, p.Layer, blockRamps: crossLayer));   // obstacles read on the caster's layer
    }

    /// <summary>Locates the player's targeted traversal guest by its (SpawnMap, SpawnSlot) identity.</summary>
    private TraversalNpcRecord? FindTraversalTarget(ServerPlayer sp)
    {
        if (sp.TargetSpawnSlot <= 0 || sp.TargetSpawnMap <= 0) return null;
        // A guest ROAMS between maps as it chases, so sp.TargetMap goes stale and a single-map lookup
        // loses it the moment it crosses another seam.  Search the caster's observable region by the
        // guest's PERMANENT (SpawnMap, SpawnSlot) identity, so the lock follows it wherever it stands.
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, sp.Char.Map);
        for (int c = 0; c < 3; c++)
        {
            for (int r = 0; r < 3; r++)
            {
                int m = grid[c, r];
                if (m <= 0 || m > _world.Limits.Maps) continue;
                var list = _world.MapTraversalNpcs[m];
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].SpawnMapNum == sp.TargetSpawnMap && list[i].SpawnSlot == sp.TargetSpawnSlot)
                        return list[i];
                }
            }
        }

        return null;
    }

    // Casts the resolved spell on an NPC record — native slot or traversal guest (npcSlot 0).
    // Caller has already range-gated and confirmed the record is alive.  Returns (casted, inCombat):
    // casted=true if MP was spent, inCombat=true if this cast should mark the caster as in combat.
    // The slot-agnostic combat overloads route damage/aggro for both kinds.
    private bool CastSpellOnNpc(int index, PlayerRecord p, SpellRecord spell, int mpCost, int mapNum,
                                MapNpcRecord mapNpc, int npcSlot, int spellNum, CastFxTarget fxTarget,
                                int reagentCost)
    {
        var npcRec = _world.Npcs[mapNpc.Num];
        if (npcRec.Behavior is NpcBehavior.Friendly or NpcBehavior.Stationary)
        {
            SendMsg(index, ServerStrings.SpellSystem_CannotCastOnFriendlyNpc, GameColor.BrightRed, ChatChannel.System);
            return false;
        }

        // Sub-type spells mark combat on both sides before the roll.  Add-type spells only draw the
        // caster in when the NPC is already fighting — buffing a peaceful NPC stays peaceful.
        long now = Environment.TickCount64;
        bool isSub = spell.Type is SpellType.SubHp or SpellType.SubMp or SpellType.SubSp;
        bool inCombat = false;
        if (isSub)
        {
            _combat.MarkPlayerCombat(index, now, asAttacker: true);
            _combat.MarkNpcCombat(mapNpc, now);
            inCombat = true;
        }
        else if (mapNpc.IsInCombat(now))
        {
            _combat.MarkPlayerCombat(index, now, asAttacker: false);
            inCombat = true;
        }
        // Cast is committed (target valid + not friendly): emit the projectile FX now — before the damage
        // switch below sends its number/death — so the client can defer them onto the bolt's arrival.
        BroadcastCastFx(index, p.Map, spellNum, inCombat, fxTarget);
        if (WindTakesTheSpell(index, mapNum, isNpc: true, targetIndex: npcSlot, mapNpc.X, mapNpc.Y))
        {
            // Parity with a blocked or dodged spell: the cast registered, so the NPC reacts.
            _combat.AlertNpc(mapNum, npcSlot, mapNpc, index);
            SpendCastCost(index, mpCost, spell, reagentCost);
            return true;
        }
        // NPC-cast crit lines go to the casting player only (no other recipient), so the key alone
        // suffices — RollSpellEffect resolves it per recipient via SendMsg.
        string magicAddCritKey = ServerStrings.CombatSystem_SpellSurge;
        string magicSubCritKey = ServerStrings.CombatSystem_SpellForce;
        switch (spell.Type)
        {
            case SpellType.AddHp:
            {
                var (amount, wasCrit) = RollSpellEffect(index, p, spell, critSelfKey: magicAddCritKey);
                int maxHp = _world.EffectiveNpcMaxHp(npcRec);
                int healed = Math.Min(amount, Math.Max(0, maxHp - mapNpc.Hp));
                mapNpc.Hp += healed;
                if (healed > 0)
                {
                    // Heal undoes accumulated damage credit proportionally so EXP shares can't
                    // sum past 100% if the NPC is killed later — the credit ledger and the
                    // (MaxHp − currentHp) it implicitly tracks must stay in sync.
                    _combat.ScaleDownNpcDamageCredit(mapNpc, healed);
                    // Broadcast a heal: native slot uses NpcDamage with negative Damage (client
                    // adds it to Hp and floats a green heal number); traversal uses the full state.
                    _combat.BroadcastNpcHeal(mapNum, mapNpc, npcSlot, healed, wasCrit);
                    SendRestoredNpcMsg(index, npcRec.TrimmedName, healed, ServerStrings.CombatSystem_VitalHp);
                }
                break;
            }
            case SpellType.SubHp:
            {
                var (amount, wasCrit) = RollSpellEffect(index, p, spell, critSelfKey: magicSubCritKey);
                if (_combat.TryNpcNegateMagic(mapNum, npcSlot, mapNpc, npcRec, index)) break;   // NPC blocks or dodges the spell (mirror of melee)
                int damage = CombatFormulas.ResolvePlayerVsNpcDamage(amount, CombatFormulas.NpcProtection(npcRec));
                if (damage > 0)
                {
                    _combat.ApplyNpcDamage(index, mapNum, mapNpc, npcSlot, damage, isCrit: wasCrit);
                }
                else
                {
                    SendMsg(index, ServerStrings.CombatSystem_SpellTooWeak, GameColor.BrightRed, ("TargetName", npcRec.TrimmedName));
                    _combat.BroadcastCombatText(mapNum, isNpc: true, index: npcSlot, CombatTextKind.ZeroHit, mapNpc.X, mapNpc.Y);
                    // Parity with melee 0-dmg: the hit registered, so the NPC reacts.  For non-
                    // guards this acquires the caster as target; for guards it consumes one tick
                    // of grace and fires the "Watch it!" warning.
                    _combat.AlertNpc(mapNum, npcSlot, mapNpc, index);
                }
                break;
            }
            case SpellType.AddMp:
            {
                var (amount, _) = RollSpellEffect(index, p, spell, critSelfKey: magicAddCritKey);
                int maxMp = _world.EffectiveNpcMaxMp(npcRec);
                int delta = Math.Min(amount, Math.Max(0, maxMp - mapNpc.Mp));
                mapNpc.Mp += delta;
                if (delta > 0) SendRestoredNpcMsg(index, npcRec.TrimmedName, delta, ServerStrings.CombatSystem_VitalMp);
                break;
            }
            case SpellType.SubMp:
            {
                var (amount, _) = RollSpellEffect(index, p, spell, critSelfKey: magicSubCritKey);
                if (_combat.TryNpcNegateMagic(mapNum, npcSlot, mapNpc, npcRec, index)) break;   // NPC blocks or dodges the spell (mirror of melee)
                int drain = CombatFormulas.ResolveDamage(amount, CombatFormulas.NpcProtection(npcRec));
                if (drain > 0)
                {
                    int delta = Math.Min(drain, mapNpc.Mp);
                    mapNpc.Mp -= delta;
                    if (delta > 0) SendDrainedNpcMsg(index, npcRec.TrimmedName, delta, ServerStrings.CombatSystem_VitalMp);
                }
                else
                {
                    _combat.BroadcastCombatText(mapNum, isNpc: true, index: npcSlot, CombatTextKind.ZeroHit, mapNpc.X, mapNpc.Y, CombatVital.Mp);
                }

                break;
            }
            case SpellType.AddSp:
            {
                var (amount, _) = RollSpellEffect(index, p, spell, critSelfKey: magicAddCritKey);
                int maxSp = _world.EffectiveNpcMaxSp(npcRec);
                int delta = Math.Min(CombatFormulas.ScaleMpEffectToSp(amount, _world.EffectiveNpcMaxMp(npcRec), maxSp), Math.Max(0, maxSp - mapNpc.Sp));
                mapNpc.Sp += delta;
                if (delta > 0) SendRestoredNpcMsg(index, npcRec.TrimmedName, delta, ServerStrings.CombatSystem_VitalSp);
                break;
            }
            case SpellType.SubSp:
            {
                var (amount, _) = RollSpellEffect(index, p, spell, critSelfKey: magicSubCritKey);
                if (_combat.TryNpcNegateMagic(mapNum, npcSlot, mapNpc, npcRec, index)) break;   // NPC blocks or dodges the spell (mirror of melee)
                int drain = CombatFormulas.ScaleMpEffectToSp(CombatFormulas.ResolveDamage(amount, CombatFormulas.NpcProtection(npcRec)), _world.EffectiveNpcMaxMp(npcRec), _world.EffectiveNpcMaxSp(npcRec));
                if (drain > 0)
                {
                    int delta = Math.Min(drain, mapNpc.Sp);
                    mapNpc.Sp -= delta;
                    if (delta > 0) SendDrainedNpcMsg(index, npcRec.TrimmedName, delta, ServerStrings.CombatSystem_VitalSp);
                }
                else
                {
                    _combat.BroadcastCombatText(mapNum, isNpc: true, index: npcSlot, CombatTextKind.ZeroHit, mapNpc.X, mapNpc.Y, CombatVital.Sp);
                }

                break;
            }
        }

        // The amount rolled at the cast gate, never a fresh roll: the gate refused the cast unless the caster
        // held this many, so charging a different number could take more than was checked for.
        SpendCastCost(index, mpCost, spell, reagentCost);
        return true;
    }
}
