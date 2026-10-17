using System.Runtime.InteropServices;
using System.Windows.Interop;
using QuickMenu.Interop;
using QuickMenu.Models;

namespace QuickMenu.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly nint _handle;
    private readonly HwndSource _source;
    private int _activeId;
    private int _nextId = 0x5100;
    private uint _modifiers;
    private uint _key;
    private bool _disposed;
    public event EventHandler? Pressed;

    public HotkeyService(nint handle)
    {
        _handle = handle;
        _source = HwndSource.FromHwnd(handle) ?? throw new ArgumentException(Loc.T("Hotkey.NoWindow"), nameof(handle));
        _source.AddHook(Hook);
    }

    public OperationResult TryRegister(uint modifiers, uint key)
    {
        if (_disposed) return new(false, Loc.T("Hotkey.Closed"));
        if (modifiers is 0 or > 15 || key is 0 or > 254) return new(false, Loc.T("Hotkey.Invalid"));
        if (_activeId != 0 && _modifiers == modifiers && _key == key) return new(true, Loc.T("Hotkey.Enabled"));
        var candidate = _nextId++;
        if (_nextId > 0x5fff) _nextId = 0x5100;
        // Register the replacement before releasing the working binding; conflicts leave it intact.
        if (!NativeMethods.RegisterHotKey(_handle, candidate, modifiers | 0x4000, key))
        {
            var error = Marshal.GetLastWin32Error();
            return new(false, error == 1409 ? Loc.T("Hotkey.Taken") : Loc.F("Hotkey.Failed", error));
        }
        if (_activeId != 0) NativeMethods.UnregisterHotKey(_handle, _activeId);
        _activeId = candidate;
        _modifiers = modifiers;
        _key = key;
        return new(true, Loc.T("Hotkey.Enabled"));
    }

    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == _activeId && !_disposed)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_activeId != 0) NativeMethods.UnregisterHotKey(_handle, _activeId);
        _source.RemoveHook(Hook);
    }
}
