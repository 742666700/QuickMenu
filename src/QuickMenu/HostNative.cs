using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickMenu;

internal static class HostNative
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, nint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool join);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nint id, nint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nint id, nint data);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    // WPF's IME support crashes the whole process (AccessViolationException in
    // ImmComposition.OnWmImeRequest_ReconvertString) when some Chinese IMEs ask a text box for reconversion or
    // document-feed text. Answering those requests with "not supported" before WPF sees them keeps typing,
    // candidates and commits working; only re-converting already committed text is lost.
    private const uint WmImeRequest = 0x0288;
    private static readonly SubclassProc ImeGuard = (hwnd, message, wParam, lParam, _, _) =>
        message == WmImeRequest && wParam is 4 or 5 or 7 ? 0 : DefSubclassProc(hwnd, message, wParam, lParam);

    /// <summary>Installs the IME guard on a window. The newest subclass runs first, so this goes after WPF's own.</summary>
    public static void GuardImeRequests(Window window) => SetWindowSubclass(new WindowInteropHelper(window).EnsureHandle(), ImeGuard, 0x514D, 0);

    /// <summary>A window that is never activated by clicks and never appears in Alt+Tab.</summary>
    public static void MakeNonActivating(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | 0x08000000 | 0x80); // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
    }

    /// <summary>
    /// Puts <paramref name="window"/> beside <paramref name="anchor"/> in device pixels: its left edge
    /// <paramref name="overlap"/> DIPs inside the anchor's right edge and its bottom <paramref name="drop"/> DIPs below the anchor's bottom.
    /// </summary>
    public static void PlaceAtRightEdge(Window window, Window anchor, double overlap, double drop)
    {
        var anchorHandle = new WindowInteropHelper(anchor).Handle;
        if (anchorHandle == 0 || !GetWindowRect(anchorHandle, out var rect)) return;
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(anchor).DpiScaleX;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var height = (int)Math.Round(window.Height * scale);
        // HWND_TOPMOST, SWP_NOSIZE | SWP_NOACTIVATE
        SetWindowPos(hwnd, new nint(-1), rect.Right - (int)Math.Round(overlap * scale), rect.Bottom + (int)Math.Round(drop * scale) - height, 0, 0, 0x0001 | 0x0010);
    }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    /// <summary>Puts the menu back above every window, as when it is opened (SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE).</summary>
    public static void BringToTop(Window window) =>
        SetWindowPos(new WindowInteropHelper(window).Handle, new nint(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);

    /// <summary>
    /// Lets the menu stop floating above everything and slips it just behind the window that now has focus, so it no
    /// longer covers what was clicked. HWND_NOTOPMOST alone would leave it on top of all ordinary windows.
    /// </summary>
    public static void SendBehindForeground(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;
        SetWindowPos(hwnd, new nint(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        var foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == hwnd) return;
        GetWindowThreadProcessId(foreground, out uint owner);
        if (owner == (uint)Environment.ProcessId) return;
        SetWindowPos(hwnd, foreground, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    /// <summary>
    /// Window.Activate can be refused when another app holds the foreground. Joining that app's input queue for
    /// the call lets the menu take focus; the queues are detached again immediately.
    /// </summary>
    public static void EnsureForeground(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var foreground = GetForegroundWindow();
        if (hwnd == 0 || foreground == hwnd) return;
        var current = GetCurrentThreadId();
        var other = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, 0);
        var attached = other != 0 && other != current && AttachThreadInput(current, other, true);
        try { BringWindowToTop(hwnd); SetForegroundWindow(hwnd); }
        finally { if (attached) AttachThreadInput(current, other, false); }
    }

    /// <summary>Dark or light window chrome with round corners and no frame line. Every theme paints its own background, so no system glass.</summary>
    public static void ApplyFrame(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var darkValue = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref darkValue, 4);
        var corner = 2;
        DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
        // No 1px frame line around the menu; Claude娘 stands across its edge instead.
        var borderNone = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(hwnd, 34, ref borderNone, 4);
        var backdrop = 1;
        DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4);
        var margins = new Margins();
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = System.Windows.Media.Colors.Transparent;
    }

    /// <summary>
    /// Sizes and places the menu on the pointer's monitor (see <see cref="MenuPlacement.Plan"/>). Returns how big the
    /// <paramref name="companion"/> (her full-size area beyond the menu's right edge and above its bottom) may be
    /// drawn beside it, or 0 when she does not fit.
    /// </summary>
    public static double CenterOnPointerMonitor(Window window, double preferredWidth = 820, Size companion = default)
    {
        GetCursorPos(out var point);
        var monitor = MonitorFromPoint(point, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return 0;
        GetDpiForMonitor(monitor, 0, out var dpi, out _);
        var scale = (dpi == 0 ? 96 : dpi) / 96.0;
        var plan = MenuPlacement.Plan(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top, scale, preferredWidth, companion);
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        // Step onto the target monitor first, keeping the size (SWP_NOSIZE | SWP_NOACTIVATE). Crossing to a monitor
        // with another scale makes WPF rescale the window by the DPI ratio; that has to happen before the final size
        // is set. Otherwise the correct pixel size is rescaled again: 125 % → 100 % shrank the menu to 80 %, and
        // 100 % → 125 % made it 125 % too large, after which it lost activation and hid, leaving only her on screen.
        SetWindowPos(hwnd, new nint(-1), plan.Left, plan.Top, 0, 0, 0x0001 | 0x0010);
        window.MinWidth = Math.Min(520, plan.Width);
        window.MinHeight = Math.Min(380, plan.Height);
        window.Width = plan.Width;
        window.Height = plan.Height;
        SetWindowPos(hwnd, new nint(-1), plan.Left, plan.Top, plan.PixelWidth, plan.PixelHeight, 0x0010);
        return plan.CompanionScale;
    }
}
