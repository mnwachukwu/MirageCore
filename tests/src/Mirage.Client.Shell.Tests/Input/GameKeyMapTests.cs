using Mirage.Client.Shell.Input;
using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Input;

/// <summary>
/// The two halves of a bindable key, held against each other.
///
/// <para>🔴 <b>Either half missing is silent.</b> The engine decides which key names a game may declare;
/// the client decides what pressing one means. A name the engine offers with nothing behind it is a
/// shortcut that never fires, and a key the client knows that the engine refuses is a binding nobody can
/// ever ask for. Neither reports anything — a shortcut that does not work looks exactly like a player
/// who has not pressed it.</para>
///
/// <para>They live in different assemblies for good reason: the engine cannot name a MonoGame key, and
/// the wire cannot carry one. So the pair is checked here rather than made impossible.</para>
/// </summary>
[TestFixture]
public class GameKeyMapTests
{
    [Test]
    public void EveryKeyTheEngineOffers_TheClientCanPress()
    {
        Assert.That(GameKeyMap.Names, Is.EquivalentTo(GameKey.Offered));
    }

    [Test]
    public void ABlankKey_ResolvesToNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GameKeyMap.TryResolve("", out _), Is.False, "no key at all is the common case");
            Assert.That(GameKeyMap.TryResolve(null, out _), Is.False);
            Assert.That(GameKeyMap.Hint(""), Is.Empty, "a caption appends this unconditionally");
        });
    }

    [Test]
    public void AKeyCoreAlreadyUses_IsNotOffered()
    {
        // Movement, running, picking up and the action bar. A game taking one of these would take it
        // away from the player with nothing anywhere reporting the conflict.
        //
        // C is NOT among them. Core's only use of it is Ctrl+C, and a game's key never fires while
        // Ctrl is held - see ProcessGameKeys - so the letter on its own is free to be bound.
        foreach (string taken in new[] { "W", "A", "S", "D", "F", "I", "M", "1", "2", "3", "4" })
        {
            Assert.That(GameKey.IsOffered(taken), Is.False, $"'{taken}' is Core's");
        }
    }

    [Test]
    public void TheReachKey_IsOfferedSoAGameCanTakeIt()
    {
        // E is Core's own reaching-for-what-you-face, and it is on the list deliberately: a game whose
        // verb belongs on that key should have it, and Core stands down when one does.
        Assert.That(GameKey.IsOffered("E"), Is.True);
    }
}
