using Microsoft.Xna.Framework;
using Mirage.Client.Shell.Panels;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>
/// The <see cref="IGamePanel"/> contract exercised BEHAVIORALLY on every registry panel, not just
/// checked for declaration (see GamePanelContractTests for that half).
///
/// <para>GameplayScreen's panel plumbing calls these four members generically now — z-order, focus,
/// hit-testing and bounds persistence all go through the interface rather than through a switch per
/// operation. That only works if every panel honors the same contract, and the two properties the
/// plumbing leans on hardest are: a CLOSED panel must not claim the pointer (or it would swallow world
/// clicks while invisible), and <c>SetBounds</c> must round-trip through <c>Bounds</c> (or a panel's
/// saved position would silently fail to restore).</para>
///
/// <para>Panels are constructed directly — no graphics device is required for these members, which is
/// what makes this reachable at all. Panels needing a constructor argument are excluded and named.</para>
/// </summary>
[TestFixture]
public class GamePanelBehaviorTests
{
    // Every registry panel with a parameterless constructor. ControlsPanel takes a GraphicsDevice and
    // OptionsPanel is owned by ShellContext, so both are covered structurally by
    // GamePanelContractTests instead.
    static IEnumerable<IGamePanel> Panels()
    {
        yield return new InventoryPanel();
        yield return new SpellPanel();
        yield return new TrainingPanel();
        yield return new ShopPanel();
        yield return new StatsPanel();
        yield return new HelpPanel();
        yield return new BankPanel();
        yield return new InnPanel();
        yield return new MailPanel();
        yield return new SocialPanel();
        yield return new MarketPanel();
        yield return new TradePanel();
        yield return new QuestLogPanel();
        yield return new QuestDialogPanel();
        yield return new ConversationPanel();
        yield return new ModerationPanel();
        // Owned by MirageGame rather than the GameplayScreen registry — backtick opens it on every
        // screen — but it is a DraggablePanel like the rest and answers the contract the same way.
        yield return new ConsolePanel();
    }

    static string Name(IGamePanel p) => p.GetType().Name;

    [Test]
    public void EveryPanel_StartsClosed()
    {
        Assert.Multiple(() =>
        {
            foreach (var p in Panels())
                Assert.That(p.IsOpen, Is.False, $"{Name(p)} must start closed");
        });
    }

    // The load-bearing one. GameplayScreen asks every panel in z-order whether the pointer is over it,
    // and treats a true as "the world does not get this click". A closed panel answering true would
    // swallow world input from behind an invisible rectangle.
    [Test]
    public void ClosedPanel_NeverClaimsThePointer()
    {
        Assert.Multiple(() =>
        {
            foreach (var p in Panels())
            {
                p.SetBounds(new Rectangle(0, 0, 400, 300));
                Assert.That(p.ContainsMouse(new Point(10, 10)), Is.False,
                            $"{Name(p)} is closed, so it must not claim a point inside its own bounds");
                Assert.That(p.ContainsMouse(new Point(200, 150)), Is.False,
                            $"{Name(p)} is closed, so it must not claim its own center");
            }
        });
    }

    // SavePanelConfig writes Bounds and OnEnter feeds it back through SetBounds. If a panel does not
    // round-trip its position, a player's saved layout silently fails to restore.
    [Test]
    public void EveryPanel_RoundTripsItsPositionThroughSetBounds()
    {
        var target = new Rectangle(37, 91, 260, 180);
        Assert.Multiple(() =>
        {
            foreach (var p in Panels())
            {
                p.SetBounds(target);
                Assert.That(p.Bounds.Location, Is.EqualTo(target.Location),
                            $"{Name(p)} must restore the persisted POSITION exactly");
            }
        });
    }

    // A second SetBounds must win — restoring a layout twice (relog, or a config reload) must not
    // accumulate or latch the first value.
    [Test]
    public void SetBounds_IsIdempotentAndLastWriteWins()
    {
        Assert.Multiple(() =>
        {
            foreach (var p in Panels())
            {
                p.SetBounds(new Rectangle(10, 10, 200, 150));
                p.SetBounds(new Rectangle(80, 60, 200, 150));
                Assert.That(p.Bounds.Location, Is.EqualTo(new Point(80, 60)),
                            $"{Name(p)} must take the most recent SetBounds");
            }
        });
    }

    // Bounds must be a usable rectangle before anything positions the panel — the config restore reads
    // Bounds for panels the player has never moved, and a degenerate rect there would persist garbage.
    [Test]
    public void EveryPanel_HasANonDegenerateDefaultBounds()
    {
        Assert.Multiple(() =>
        {
            foreach (var p in Panels())
            {
                Assert.That(p.Bounds.Width, Is.GreaterThan(0), $"{Name(p)} default width");
                Assert.That(p.Bounds.Height, Is.GreaterThan(0), $"{Name(p)} default height");
            }
        });
    }

    // The registry indexes panels by the Panel* slot constants, so the set it addresses must be
    // exactly the set of panel types that exist. A panel added without a registry row would be
    // invisible to z-order, hit-testing and persistence — the failure mode the registry replaced
    // twelve hand-maintained switches to prevent.
    [Test]
    public void EveryIGamePanelType_IsCoveredByThisFixtureOrExplicitlyExcluded()
    {
        // Constructor-argument panels, covered structurally in GamePanelContractTests.
        string[] excluded = ["ControlsPanel", "OptionsPanel"];

        var declared = typeof(IGamePanel).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IGamePanel).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToHashSet();

        var exercised = Panels().Select(Name).ToHashSet();
        foreach (string e in excluded) exercised.Add(e);

        Assert.That(declared.Except(exercised), Is.Empty,
                    "a panel implementing IGamePanel that this fixture neither exercises nor excludes — "
                    + "add it to Panels() (or to the excluded list, with a reason)");
    }
}
