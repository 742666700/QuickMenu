using System.Collections.Concurrent;
using System.Windows.Automation;
using QuickMenu.Interop;
using QuickMenu.Models;

namespace QuickMenu.Services;

public sealed class TrayService
{
    private readonly ConcurrentDictionary<string, byte> _itemIds = new();
    private readonly object _gate = new();
    private Task<IReadOnlyList<RunningApp>>? _query;
    private Task<OperationResult>? _invocation;

    public async Task<IReadOnlyList<RunningApp>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<IReadOnlyList<RunningApp>> query;
        lock (_gate)
        {
            // A stalled external accessibility provider may keep one background worker busy; never queue another behind it.
            if (_query is null || _query.IsCompleted)
                _query = StaWorker.Run<IReadOnlyList<RunningApp>>(Enumerate);
            query = _query;
        }
        try { return await query.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken); }
        catch (TimeoutException) { return Array.Empty<RunningApp>(); }
    }

    private IReadOnlyList<RunningApp> Enumerate()
    {
        var result = new List<RunningApp>();
        var fresh = new Dictionary<string, byte>();
        try
        {
            var hwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (hwnd == 0) return result;
            var taskbar = AutomationElement.FromHandle(hwnd);
            foreach (AutomationElement button in GetCandidates(taskbar))
            {
                try
                {
                    var current = button.Current;
                    if (current.IsOffscreen || !current.IsEnabled || string.IsNullOrWhiteSpace(current.Name)) continue;
                    var id = "tray:" + string.Join(".", button.GetRuntimeId());
                    if (!fresh.TryAdd(id, 0)) continue;
                    // UIA ProcessId describes the provider host, not necessarily the icon's application.
                    result.Add(new(id, current.Name.Replace("\r\n", " · ").Replace("\n", " · ").Trim(), null, null,
                        Array.Empty<WindowEntry>(), false, true));
                }
                catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            }
            _itemIds.Clear();
            foreach (var pair in fresh) _itemIds[pair.Key] = 0;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public OperationResult TryActivate(RunningApp item)
    {
        Task<OperationResult> invocation;
        lock (_gate)
        {
            if (_invocation is { IsCompleted: false }) return new(false, Loc.T("Tray.NotResponding"));
            _invocation = StaWorker.Run(() => Invoke(item));
            invocation = _invocation;
        }
        // Calling automation synchronously can block in third-party providers; a bounded wait keeps the menu responsive.
        return invocation.Wait(TimeSpan.FromMilliseconds(750)) ? invocation.GetAwaiter().GetResult()
            : new(false, Loc.T("Tray.Pending"));
    }

    private OperationResult Invoke(RunningApp item)
    {
        if (!_itemIds.ContainsKey(item.Id)) return new(false, Loc.T("Tray.Changed"));
        try
        {
            var hwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (hwnd == 0) return new(false, Loc.T("Tray.Unavailable"));
            // Resolve the stable runtime ID anew in this apartment; do not retain provider objects from a terminated thread.
            var element = GetCandidates(AutomationElement.FromHandle(hwnd)).FirstOrDefault(button =>
                "tray:" + string.Join(".", button.GetRuntimeId()) == item.Id);
            if (element is null || element.Current.IsOffscreen || !element.Current.IsEnabled)
                return new(false, Loc.T("Tray.Changed"));
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)) ((InvokePattern)invoke).Invoke();
            else return new(false, Loc.T("Tray.NoOpen"));
            return new(true, Loc.T("Tray.Opened"));
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { return new(false, Loc.T("Tray.OpenFailed") + ex.Message); }
    }

    private static IReadOnlyList<AutomationElement> GetCandidates(AutomationElement taskbar)
    {
        // Windows 11 application icons are direct notification buttons; SystemTrayIcon identifies system/overflow buttons.
        var candidates = new List<AutomationElement>();
        foreach (AutomationElement button in taskbar.FindAll(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, "NotifyItemIcon"),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))))
            candidates.Add(button);
        var hosts = taskbar.FindAll(TreeScope.Descendants, new OrCondition(
            new PropertyCondition(AutomationElement.ClassNameProperty, "TrayNotifyWnd"),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTray.NormalItems")));
        foreach (AutomationElement host in hosts)
            foreach (AutomationElement button in host.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
                candidates.Add(button);
        return candidates;
    }
}
