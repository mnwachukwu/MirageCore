using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Input;

/// <summary>The pre-game menu state machine. Shell-driven Go* methods and server-driven events (character
/// list, in-game, class list) move MenuState. The class list arrives in two situations, so it must open the
/// new-char screen ONLY when Loading was entered for that purpose.</summary>
[TestFixture]
public class MenuLogicTests
{
    [Test]
    public void InitialState_IsMainMenu()
        => Assert.That(new MenuLogic(new TestClientEvents()).CurrentState, Is.EqualTo(MenuState.MainMenu));

    [Test]
    public void ShellTransition_FiresStateChanged()
    {
        var logic = new MenuLogic(new TestClientEvents());
        MenuState? seen = null;
        logic.StateChanged += st => seen = st;
        logic.GoToLogin();
        Assert.Multiple(() =>
        {
            Assert.That(logic.CurrentState, Is.EqualTo(MenuState.Login));
            Assert.That(seen, Is.EqualTo(MenuState.Login));
        });
    }

    [Test]
    public void RedundantTransition_DoesNotRefire()
    {
        var logic = new MenuLogic(new TestClientEvents());
        int count = 0;
        logic.StateChanged += _ => count++;
        logic.GoToMainMenu();   // already MainMenu
        Assert.That(count, Is.EqualTo(0), "transitioning to the current state is a no-op");
    }

    [Test]
    public void CharacterListReceived_GoesToCharSelect()
    {
        var ev = new TestClientEvents();
        var logic = new MenuLogic(ev);
        ev.RaiseCharacterList();
        Assert.That(logic.CurrentState, Is.EqualTo(MenuState.CharSelect));
    }

    [Test]
    public void InGame_GoesToInGame()
    {
        var ev = new TestClientEvents();
        var logic = new MenuLogic(ev);
        ev.RaiseInGame();
        Assert.That(logic.CurrentState, Is.EqualTo(MenuState.InGame));
    }

    // The class list arrives both on the new-char flow and as part of normal join data. It must advance to
    // NewChar only when Loading was entered specifically for that purpose.
    [Test]
    public void ClassList_AdvancesToNewChar_OnlyWhenLoadingForNewChar()
    {
        var ev = new TestClientEvents();
        var logic = new MenuLogic(ev);

        logic.GoToLoading();   // generic loading (join data)
        ev.RaiseClassList();
        Assert.That(logic.CurrentState, Is.EqualTo(MenuState.Loading), "generic-loading class list does NOT open new-char");

        logic.GoToLoadingForNewChar();
        ev.RaiseClassList();
        Assert.That(logic.CurrentState, Is.EqualTo(MenuState.NewChar), "loading-for-new-char class list opens new-char");
    }

    [Test]
    public void Alert_IsForwardedToSubscribers()
    {
        var ev = new TestClientEvents();
        var logic = new MenuLogic(ev);
        string? gotMsg = null;
        logic.AlertReceived += (m, _) => gotMsg = m;
        ev.RaiseAlert("Bad password", default);
        Assert.That(gotMsg, Is.EqualTo("Bad password"));
    }
}

/// <summary>Hand-raisable <see cref="IClientEvents"/> stub. Only the events MenuLogic subscribes to have Raise
/// helpers; the rest exist to satisfy the interface.</summary>
#pragma warning disable CS0067 // events are declared to satisfy the interface; most are never raised in tests
sealed class TestClientEvents : IClientEvents
{
    public event Action<string, AlertCode>? AlertMessage;
    public event Action? InGame;
    public event Action? MapReady;
    public event Action<ChatMsgPacket>? ChatMessage;
    public event Action? InventoryChanged;
    public event Action<int>? VitalsChanged;
    public event Action? CharacterListReceived;
    public event Action? ClassListReceived;
    public event Action<int>? MapItemChanged;
    public event Action<int>? MapNpcChanged;
    public event Action<int>? ShopOpened;
    public event Action? OpenInn;
    public event Action<int, int>? OpenNpcQuestMenu;
    public event Action<int, int, int>? OpenNpcConversation;
    public event Action? TrainingReady;
    public event Action<int>? PreparedSpellReceived;
    public event Action<string, int>? PartyRequest;
    public event Action<GuildOfferNotifyPacket>? GuildOffer;
    public event Action<string>? TradeInvite;
    public event Action<int>? PlayersOnlineChanged;
    public event Action? LevelUp;
    public event Action<TargetRef>? TargetAssigned;
    public event Action<int, int, VitalType, bool, bool, int>? VitalDelta;
    public event Action<CombatTextPacket>? CombatText;

    public void RaiseAlert(string msg, AlertCode code) => AlertMessage?.Invoke(msg, code);
    public void RaiseInGame() => InGame?.Invoke();
    public void RaiseCharacterList() => CharacterListReceived?.Invoke();
    public void RaiseClassList() => ClassListReceived?.Invoke();
}
#pragma warning restore CS0067
