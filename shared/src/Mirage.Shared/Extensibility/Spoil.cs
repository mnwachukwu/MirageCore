namespace Mirage.Shared.Extensibility;

/// <summary>
/// One line of a slain creature's drop table, on its way to the ground.
///
/// <para>The engine reads a creature's table, and each line is one item at one chance. What is authored
/// there is the world's own answer; a game gets to change it for this kill before the roll happens, and
/// to say who the thing belongs to if it lands.</para>
///
/// <para>⚠ <b>Handed over while the body is still on its tile.</b> <see cref="Body"/> still resolves and
/// still names where it fell, which is the only moment that is true — the slot is cleared immediately
/// afterwards. A policy that wants to know where the loot is landing asks then.</para>
/// </summary>
public sealed class Spoil
{
    /// <summary>What was killed.</summary>
    public required EntityHandle Body { get; init; }

    /// <summary>Who killed it. Nobody when the world itself did, which
    /// <see cref="EntityHandle.IsSet"/> answers.</summary>
    public required EntityHandle Killer { get; init; }

    /// <summary>Which creature record the body is a copy of — what a rule about a KIND of creature keys
    /// on, since the body is about to stop existing.</summary>
    public required int Kind { get; init; }

    /// <summary>The item this line names.</summary>
    public required int ItemNum { get; init; }

    /// <summary>How many. Read only for an item that stacks; anything else lands as one however large
    /// this is.</summary>
    public int Quantity { get; set; }

    /// <summary>How often the line lands, as a direct percent — 1 is one time in a hundred, 100 or more
    /// is every time, and nothing at or below zero ever lands.</summary>
    public int ChancePercent { get; set; }

    /// <summary>Who may pick this up while <see cref="ClaimSeconds"/> lasts. Nobody by default, which
    /// leaves the drop free to whoever reaches it first.</summary>
    public EntityHandle ClaimedBy { get; set; }

    /// <summary>How long that claim holds. Nothing at or below zero leaves the drop unclaimed however
    /// <see cref="ClaimedBy"/> reads.</summary>
    public int ClaimSeconds { get; set; }
}

/// <summary>
/// What a slain creature leaves behind.
///
/// <para><b>The table is the engine's, and what it is worth to THIS kill is a game's.</b> Core rolls
/// every line of a creature's authored drops independently and puts what lands on the tile the body
/// fell on. It has no opinion about whether a guild's privilege lifts the rate, whether a party shares
/// the purse, or whether the person who did the work gets first refusal — those are all a game's, and
/// they are all changes to a line before it is rolled.</para>
///
/// <para>Every policy sees every line, in the order their modules were configured, and each writes on
/// what the one before it left. A module that has nothing to say about a line leaves it alone.</para>
/// </summary>
public interface ILootPolicy
{
    /// <summary>What this line is actually worth, for this kill.
    ///
    /// <para>Write on <paramref name="spoil"/> and return; the engine rolls what is left there. Setting
    /// <see cref="Spoil.ChancePercent"/> to nothing drops the line without the roll happening at
    /// all.</para></summary>
    void Weigh(Spoil spoil);
}
