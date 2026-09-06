using Mirage.Client.Shell.Input;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests.Input;

/// <summary>
/// Which device the hotkey badges name. Two properties look similar and answer different questions:
/// <see cref="InputState.IsGamepadActive"/> arbitrates which device may ACT and changes hands as the
/// player switches devices. <see cref="InputState.ShowGamepadPrompts"/> answers a display question and
/// follows the "Use Gamepad" option alone, so the badges hold one reading for as long as it is set.
/// </summary>
[TestFixture]
public class GamepadPromptTests
{
    [Test]
    public void WithTheGamepadTurnedOffThePromptsAreNeverGamepadPrompts()
    {
        var input = new InputState { UseGamepad = false };

        Assert.That(input.ShowGamepadPrompts, Is.False);
    }

    [Test]
    public void WithTheGamepadTurnedOnThePromptsAreGamepadPromptsBeforeAnythingIsPressed()
    {
        var input = new InputState { UseGamepad = true };

        Assert.That(input.ActiveDevice, Is.EqualTo(ActiveInputDevice.None), "nothing has claimed the frame");
        Assert.That(input.ShowGamepadPrompts, Is.True, "so the badges read X/Y/B/A, not 1-4");
    }

    [Test]
    public void TheOptionSwitchAloneFlipsThePrompts()
    {
        var input = new InputState { UseGamepad = true };
        Assert.That(input.ShowGamepadPrompts, Is.True);

        input.UseGamepad = false;

        Assert.That(input.ShowGamepadPrompts, Is.False, "no button press is needed either way");
    }

    [Test]
    public void TheArbiterStillLetsBothDevicesActBeforeEitherClaimsAFrame()
    {
        var input = new InputState { UseGamepad = true };

        Assert.That(input.IsKeyboardActive, Is.True);
        Assert.That(input.IsGamepadActive, Is.True,
            "double-fire suppression depends on this staying permissive; the display question is separate");
    }

    [Test]
    public void ResetClearsTheActiveDeviceWithoutDisturbingThePrompts()
    {
        var input = new InputState { UseGamepad = true };
        input.Reset();

        Assert.That(input.ActiveDevice, Is.EqualTo(ActiveInputDevice.None));
        Assert.That(input.ShowGamepadPrompts, Is.True);
    }
}
