namespace Mirage.Shared.Extensibility;

/// <summary>
/// Somebody is about to use something out of their bag.
///
/// <para>Carried as a value rather than as three arguments, so a policy reads what it needs and a
/// question added later does not change every signature that asks it.</para>
/// </summary>
/// <param name="Who">The body doing it.</param>
/// <param name="ItemNum">Which item, by its record number — what a rule about a kind of thing keys on.</param>
/// <param name="InvSlot">Which bag slot it is sitting in, for a rule about this particular copy.</param>
public readonly record struct Use(EntityHandle Who, int ItemNum, int InvSlot);

/// <summary>
/// Whether a game lets somebody use a thing.
///
/// <para>🔴 <b>The engine owns two of the meanings and a game owns the rest.</b> Core knows how to put
/// a piece of gear on and how to open a door with a key, because both are about the world rather than
/// about the game. Everything else an item might mean is a game's — and so is whether it may happen at
/// all. A sword restricted to one class, a potion a corpse cannot drink, a relic that answers only to
/// whoever earned it: none of those is a rule Core could have.</para>
///
/// <para>⚠ <b>Asked BEFORE anything happens, which is what makes it different from hearing about
/// it.</b> <see cref="IWorldObserver.OnItemUsed"/> runs after the gear is already on, so a rule there
/// can only take it off again — which the player sees as a flicker, and which leaves the moment between
/// the two with the wrong body wearing the wrong thing. A refusal here means it never went on.</para>
///
/// <para>Every policy must allow it. The first refusal stops the use and is the answer — the same
/// order, and the same reason, as <see cref="IDeathPolicy.MayDie"/>.</para>
/// </summary>
public interface IUsePolicy
{
    /// <summary>Whether it happens. Refuse with a reason worth telling the player, or
    /// <see cref="Refusal.Allow"/>.</summary>
    Refusal MayUse(in Use use) => Refusal.Allow;
}
