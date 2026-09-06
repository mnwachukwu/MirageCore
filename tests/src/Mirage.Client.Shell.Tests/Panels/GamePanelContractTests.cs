using Mirage.Client.Shell.Panels;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Shell.Tests.Panels;

/// <summary>
/// Guards the <see cref="IGamePanel"/> contract that GameplayScreen's panel registry depends on.
///
/// <para>The registry replaced twelve separate switches and boolean chains, each of which had to be
/// edited in lockstep when a panel was added — and missing one failed silently rather than at build
/// time. Two such omissions were found while building it (the quest log absent from the open/close
/// dispatch, the Controls panel absent from the Escape check). These tests make the equivalent
/// mistake at the type level impossible to miss: a new panel that forgets to declare the interface,
/// or a panel that quietly loses one of the five members the plumbing calls.</para>
/// </summary>
[TestFixture]
public class GamePanelContractTests
{
    // The floating panels GameplayScreen drives through its registry. Not every type in the Panels
    // namespace belongs here: the HUD, party overlay, contest HUD, chat, chat options and the death
    // overlay are drawn directly rather than being z-ordered, hit-tested and persisted as a set.
    private static readonly string[] RegistryPanels =
    [
        "InventoryPanel", "SpellPanel", "TrainingPanel", "ShopPanel", "OptionsPanel", "StatsPanel",
        "HelpPanel", "ControlsPanel", "BankPanel", "InnPanel", "MailPanel", "SocialPanel",
        "MarketPanel", "TradePanel", "QuestLogPanel", "QuestDialogPanel", "ConversationPanel",
    ];

    [Test]
    public void EveryRegistryPanel_ImplementsIGamePanel()
    {
        var asm = typeof(IGamePanel).Assembly;
        Assert.Multiple(() =>
        {
            foreach (string name in RegistryPanels)
            {
                var t = asm.GetType("Mirage.Client.Shell.Panels." + name);
                Assert.That(t, Is.Not.Null, $"{name} not found — renamed or moved?");
                Assert.That(typeof(IGamePanel).IsAssignableFrom(t!), Is.True,
                    $"{name} must declare : IGamePanel — GameplayScreen's registry addresses it through that interface");
            }
        });
    }

    // If a panel type exists alongside the registry ones and looks like a floating panel (it has the
    // full five-member shape) but does not declare the interface, it is almost certainly a new panel
    // whose author did not know about the registry. Fail loudly rather than let it drift.
    [Test]
    public void NoPanelHasTheFullShapeWithoutDeclaringTheInterface()
    {
        var offenders = typeof(IGamePanel).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.Namespace == "Mirage.Client.Shell.Panels"
                        && !typeof(IGamePanel).IsAssignableFrom(t)
                        && HasFullPanelShape(t))
            .Select(t => t.Name)
            .ToList();

        Assert.That(offenders, Is.Empty,
            "these types have IsOpen/Bounds/SetBounds/ResetBounds/ContainsMouse but do not declare IGamePanel — "
            + "if they are floating panels they belong in GameplayScreen's registry");
    }

    private static bool HasFullPanelShape(Type t) =>
        t.GetProperty("IsOpen", typeof(bool)) is not null
        && t.GetProperty("Bounds") is not null
        && t.GetMethod("SetBounds") is not null
        && t.GetMethod("ResetBounds") is not null
        && t.GetMethod("ContainsMouse") is not null;

    // The five members must stay exactly as the plumbing calls them. An implicit implementation that
    // drifted (a renamed parameter type, a non-public setter turning into something else) would still
    // compile against the interface only if the interface itself were edited — so pin the shapes.
    [Test]
    public void InterfaceExposesExactlyTheFiveUniformMembers()
    {
        var members = typeof(IGamePanel).GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name).OrderBy(n => n).ToArray();

        Assert.That(members, Is.EquivalentTo(new[]
        {
            "get_IsOpen", "IsOpen", "get_Bounds", "Bounds", "SetBounds", "ResetBounds", "ContainsMouse",
        }), "IGamePanel should carry only the shape-invariant members; Update/Draw vary per panel and "
          + "live in the registry instead");
    }
}
