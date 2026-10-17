using QuickMenu.Services;
using Xunit;
namespace QuickMenu.Tests;

public class GamepadTests
{
    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);
    private const ushort Combo = GamepadInterpreter.Menu | GamepadInterpreter.View;

    [Fact]
    public void MenuAndViewTogetherToggleOncePerPressEvenWhileHidden()
    {
        var pad = new GamepadInterpreter();
        Assert.False(pad.Read(GamepadInterpreter.Menu, 0, 0, Ms(0), navigating: false).Toggle);
        Assert.True(pad.Read(Combo, 0, 0, Ms(50), navigating: false).Toggle);
        Assert.False(pad.Read(Combo, 0, 0, Ms(100), navigating: false).Toggle);
        // Letting go of one button is not a new press; nothing else counts until both are up.
        Assert.False(pad.Read(GamepadInterpreter.View, 0, 0, Ms(150), navigating: true).Toggle);
        Assert.Empty(pad.Read(GamepadInterpreter.View | GamepadInterpreter.ButtonA, 0, 0, Ms(200), navigating: true).Presses);
        Assert.False(pad.Read(0, 0, 0, Ms(250), navigating: true).Toggle);
        Assert.True(pad.Read(Combo, 0, 0, Ms(300), navigating: true).Toggle);
    }

    [Fact]
    public void DirectionsRepeatWhileHeldAndButtonsFireOnPress()
    {
        var pad = new GamepadInterpreter();
        Assert.Equal([GamepadButton.Down], pad.Read(GamepadInterpreter.DPadDown, 0, 0, Ms(0), true).Presses);
        Assert.Empty(pad.Read(GamepadInterpreter.DPadDown, 0, 0, Ms(300), true).Presses);
        Assert.Equal([GamepadButton.Down], pad.Read(GamepadInterpreter.DPadDown, 0, 0, Ms(400), true).Presses);
        Assert.Empty(pad.Read(GamepadInterpreter.DPadDown, 0, 0, Ms(450), true).Presses);
        Assert.Equal([GamepadButton.Down], pad.Read(GamepadInterpreter.DPadDown, 0, 0, Ms(520), true).Presses);
        Assert.Equal([GamepadButton.A], pad.Read(GamepadInterpreter.ButtonA, 0, 0, Ms(600), true).Presses);
        Assert.Empty(pad.Read(GamepadInterpreter.ButtonA, 0, 0, Ms(650), true).Presses);
        Assert.Equal([GamepadButton.B], pad.Read(GamepadInterpreter.ButtonB, 0, 0, Ms(700), true).Presses);
        // Nothing is reported while the menu is hidden.
        Assert.Empty(pad.Read(GamepadInterpreter.ButtonA | GamepadInterpreter.DPadUp, 0, 0, Ms(800), false).Presses);
    }

    [Fact]
    public void StickActsAsDPadWithoutFlickeringAtTheEdge()
    {
        var pad = new GamepadInterpreter();
        Assert.Empty(pad.Read(0, 15000, 0, Ms(0), true).Presses);
        Assert.Equal([GamepadButton.Right], pad.Read(0, 20000, 0, Ms(20), true).Presses);
        // Dropping a little below the "on" point keeps the direction held (no second press).
        Assert.Empty(pad.Read(0, 15000, 0, Ms(40), true).Presses);
        Assert.Empty(pad.Read(0, 5000, 0, Ms(60), true).Presses);
        Assert.Equal([GamepadButton.Up], pad.Read(0, 0, 25000, Ms(80), true).Presses);
        Assert.Equal([GamepadButton.Down], pad.Read(0, 0, -25000, Ms(100), true).Presses);
    }

    [Fact]
    public void TriggersFireOncePerPull()
    {
        var pad = new GamepadInterpreter();
        Assert.Empty(pad.Read(0, 0, 0, Ms(0), true, leftTrigger: 90).Presses);
        Assert.Equal([GamepadButton.LeftTrigger], pad.Read(0, 0, 0, Ms(20), true, leftTrigger: 200).Presses);
        // Easing off a little is still the same pull; letting go and pulling again is a new one.
        Assert.Empty(pad.Read(0, 0, 0, Ms(40), true, leftTrigger: 80).Presses);
        Assert.Empty(pad.Read(0, 0, 0, Ms(60), true, leftTrigger: 10).Presses);
        Assert.Equal([GamepadButton.LeftTrigger], pad.Read(0, 0, 0, Ms(80), true, leftTrigger: 255).Presses);
        Assert.Equal([GamepadButton.RightTrigger], pad.Read(0, 0, 0, Ms(100), true, leftTrigger: 255, rightTrigger: 255).Presses);
        Assert.Empty(pad.Read(0, 0, 0, Ms(120), false, rightTrigger: 255).Presses);
    }
}
