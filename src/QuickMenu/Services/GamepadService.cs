using System.Runtime.InteropServices;

namespace QuickMenu.Services;

public enum GamepadButton { Up, Down, Left, Right, A, B, X, Y, LeftShoulder, RightShoulder, LeftTrigger, RightTrigger }

/// <summary>
/// Turns successive controller readings into menu input: Menu + View pressed together toggles the menu, and while it
/// is shown the D-pad or left stick move (repeating while held), A confirms, B is the right-click, the shoulders switch
/// pages and the triggers switch the tabs within a page. Pure logic, so it can be tested without a controller.
/// </summary>
public sealed class GamepadInterpreter
{
    // XInput button bits.
    public const ushort DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008, Menu = 0x0010, View = 0x0020,
        LeftShoulder = 0x0100, RightShoulder = 0x0200, ButtonA = 0x1000, ButtonB = 0x2000, ButtonX = 0x4000, ButtonY = 0x8000;
    private const short StickOn = 18000, StickOff = 12000;
    // The analog triggers count as buttons: pulled past "on", let go below "off". Above the bits XInput uses.
    private const byte TriggerOn = 120, TriggerOff = 60;
    private const int LeftTriggerBit = 0x10000, RightTriggerBit = 0x20000;
    public static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(380), RepeatInterval = TimeSpan.FromMilliseconds(110);

    private int _previous;
    private bool _comboHeld;
    private GamepadButton? _direction;
    private TimeSpan _nextRepeat;
    private int _stickX, _stickY;

    /// <summary>Feeds one reading; returns whether the menu should toggle, and the navigation presses to apply.</summary>
    public (bool Toggle, IReadOnlyList<GamepadButton> Presses) Read(ushort pad, short thumbX, short thumbY, TimeSpan now, bool navigating, byte leftTrigger = 0, byte rightTrigger = 0)
    {
        var buttons = pad | Trigger(LeftTriggerBit, leftTrigger) | Trigger(RightTriggerBit, rightTrigger);
        var presses = new List<GamepadButton>();
        var toggle = false;
        // Menu + View: one toggle per press of the pair; nothing else counts until both are let go.
        if ((buttons & (Menu | View)) == (Menu | View))
        {
            if (!_comboHeld) { _comboHeld = true; toggle = true; }
            _previous = buttons; _direction = null;
            return (toggle, presses);
        }
        if (_comboHeld) { if ((buttons & (Menu | View)) == 0) _comboHeld = false; _previous = buttons; return (false, presses); }

        // The stick counts as a D-pad, with a gap between on and off so it does not flicker at the edge.
        _stickX = Hysteresis(_stickX, thumbX);
        _stickY = Hysteresis(_stickY, thumbY);
        GamepadButton? direction = (buttons & DPadUp) != 0 || _stickY > 0 ? GamepadButton.Up
            : (buttons & DPadDown) != 0 || _stickY < 0 ? GamepadButton.Down
            : (buttons & DPadLeft) != 0 || _stickX < 0 ? GamepadButton.Left
            : (buttons & DPadRight) != 0 || _stickX > 0 ? GamepadButton.Right : null;
        if (navigating)
        {
            if (direction != _direction) { if (direction is { } pressed) { presses.Add(pressed); _nextRepeat = now + RepeatDelay; } }
            else if (direction is { } held && now >= _nextRepeat) { presses.Add(held); _nextRepeat = now + RepeatInterval; }
            foreach (var (bit, button) in new[] { ((int)ButtonA, GamepadButton.A), (ButtonB, GamepadButton.B), (ButtonX, GamepadButton.X), (ButtonY, GamepadButton.Y), (LeftShoulder, GamepadButton.LeftShoulder), (RightShoulder, GamepadButton.RightShoulder), (LeftTriggerBit, GamepadButton.LeftTrigger), (RightTriggerBit, GamepadButton.RightTrigger) })
                if ((buttons & bit) != 0 && (_previous & bit) == 0) presses.Add(button);
        }
        _direction = direction;
        _previous = buttons;
        return (false, presses);

        static int Hysteresis(int state, short value) => value >= StickOn ? 1 : value <= -StickOn ? -1 : Math.Abs((int)value) < StickOff ? 0 : state;
        int Trigger(int bit, byte value) => value >= TriggerOn || (value >= TriggerOff && (_previous & bit) != 0) ? bit : 0;
    }
}

/// <summary>
/// Reads Xbox-compatible controllers (and Windows handhelds, which present one) through XInput on a background thread:
/// every 16 ms while the menu is shown, every 50 ms while it is hidden (only the Menu + View pair matters then), and
/// once a second while no controller is connected. Events arrive on that thread.
/// </summary>
public sealed class GamepadService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState { public uint Packet; public ushort Buttons; public byte LeftTrigger, RightTrigger; public short ThumbLX, ThumbLY, ThumbRX, ThumbRY; }
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint index, out XInputState state);

    private readonly GamepadInterpreter[] _interpreters = [new(), new(), new(), new()];
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;
    public event Action? Toggle;
    public event Action<GamepadButton>? Pressed;
    /// <summary>The menu is shown: read fast and report navigation.</summary>
    public volatile bool Navigating;
    public volatile bool Enabled = true;

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "QuickMenu gamepad", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    private void Run()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var connected = new bool[4];
        var nextProbe = TimeSpan.Zero;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var now = clock.Elapsed;
                // Asking about an empty slot is slow, so empty slots are only checked once a second.
                var probe = now >= nextProbe;
                if (probe) nextProbe = now + TimeSpan.FromSeconds(1);
                for (uint index = 0; index < 4; index++)
                {
                    if (!Enabled || (!connected[index] && !probe)) continue;
                    connected[index] = XInputGetState(index, out var state) == 0;
                    if (!connected[index]) continue;
                    var (toggle, presses) = _interpreters[index].Read(state.Buttons, state.ThumbLX, state.ThumbLY, now, Navigating, state.LeftTrigger, state.RightTrigger);
                    if (toggle) Toggle?.Invoke();
                    foreach (var press in presses) Pressed?.Invoke(press);
                }
                _stop.Token.WaitHandle.WaitOne(!Enabled || !connected.Any(c => c) ? 1000 : Navigating ? 16 : 50);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { App.Log(ex); }
    }

    public void Dispose() { _stop.Cancel(); _thread?.Join(500); _stop.Dispose(); }
}
