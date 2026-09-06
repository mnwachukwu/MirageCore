using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Input;

/// <summary>Regression net for "typing into a panel text box leaks gameplay hotkeys". GameplayScreen
/// gates its menu/potion hotkeys, pickup, and movement on <see cref="WorldInputGate.IsSuppressed"/>;
/// a panel's number-prompt text box raises the capture signal that feeds it. Disconnect those two and
/// the prompt captures the keystroke while the hotkeys still fire underneath.</summary>
[TestFixture]
public class WorldInputSuppressionTests
{
    [Test]
    public void NoUiFocus_WorldInputFlows()
        => Assert.That(WorldInputGate.IsSuppressed(chatFocused: false, chatOptionsTyping: false, anyPanelCapturingInput: false), Is.False);

    // The reported bug: while a panel's text box owns the keyboard, the world hotkeys must be gated —
    // even though chat is not focused.
    [Test]
    public void PanelCapturingInput_SuppressesWorldInput()
        => Assert.That(WorldInputGate.IsSuppressed(chatFocused: false, chatOptionsTyping: false, anyPanelCapturingInput: true), Is.True);

    [Test]
    public void ChatFocused_SuppressesWorldInput()
        => Assert.That(WorldInputGate.IsSuppressed(chatFocused: true, chatOptionsTyping: false, anyPanelCapturingInput: false), Is.True);

    [Test]
    public void ChatOptionsTyping_SuppressesWorldInput()
        => Assert.That(WorldInputGate.IsSuppressed(chatFocused: false, chatOptionsTyping: true, anyPanelCapturingInput: false), Is.True);

    // The panel text box (Inventory drop X, Bank deposit/withdraw X) that raises the capture signal
    // GameplayScreen folds into anyPanelCapturingInput.
    [Test]
    public void NumberPrompt_DoesNotCaptureWhenClosed()
    {
        var dlg = new NumberPromptDialog();
        Assert.That(dlg.IsCapturingInput, Is.False);
    }

    [Test]
    public void NumberPrompt_CapturesInputWhileOpen()
    {
        var dlg = new NumberPromptDialog();
        dlg.Open("Drop item:", "Gold", max: 100, _ => { });
        Assert.That(dlg.IsOpen, Is.True);
        Assert.That(dlg.IsCapturingInput, Is.True,
            "an open number prompt must flag capturing so gameplay hotkeys are gated while the amount is typed");
    }

    [Test]
    public void NumberPrompt_ReleasesCaptureWhenClosed()
    {
        var dlg = new NumberPromptDialog();
        dlg.Open("Drop item:", "Gold", max: 100, _ => { });
        dlg.Close();
        Assert.That(dlg.IsOpen, Is.False);
        Assert.That(dlg.IsCapturingInput, Is.False);
    }

    // TextPromptDialog is the guild name / MOTD text-entry twin of NumberPromptDialog; it must raise the
    // same capture signal so the guild-name and MOTD fields gate world hotkeys while being typed.
    [Test]
    public void TextPrompt_DoesNotCaptureWhenClosed()
        => Assert.That(new TextPromptDialog().IsCapturingInput, Is.False);

    [Test]
    public void TextPrompt_CapturesInputWhileOpen()
    {
        var dlg = new TextPromptDialog();
        dlg.Open("Guild name:", "", maxLength: 30, allowEmpty: false, _ => { });
        Assert.That(dlg.IsOpen, Is.True);
        Assert.That(dlg.IsCapturingInput, Is.True,
            "an open text prompt must flag capturing so gameplay hotkeys are gated while the text is typed");
    }

    [Test]
    public void TextPrompt_ReleasesCaptureWhenClosed()
    {
        var dlg = new TextPromptDialog();
        dlg.Open("Guild name:", "", maxLength: 30, allowEmpty: false, _ => { });
        dlg.Close();
        Assert.That(dlg.IsOpen, Is.False);
        Assert.That(dlg.IsCapturingInput, Is.False);
    }

    // Opening the Social panel must NOT capture input — only a prompt/label-editor inside it does. A panel
    // that captured on open would freeze movement the moment the player pressed G.
    [Test]
    public void SocialPanel_FreshInstance_DoesNotCaptureInput()
        => Assert.That(new SocialPanel().IsCapturingInput, Is.False);

    [Test]
    public void SocialPanel_OpenedWithNoPrompt_DoesNotCaptureInput()
    {
        var panel = new SocialPanel();
        panel.Toggle(); // open
        Assert.That(panel.IsOpen, Is.True);
        Assert.That(panel.IsCapturingInput, Is.False,
            "an open Social panel with no active prompt or label editor must let world input flow");
    }
}
