using System.Diagnostics;
using System.Windows;
using QuickMenu.Services;

namespace QuickMenu;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _activation;
    private CancellationTokenSource? _shutdown;
    private System.Windows.Forms.NotifyIcon? _tray;
    private MainWindow? _window;
    private const string InstanceName = "Local\\QuickMenu.Instance.v1";
    private const string ActivationName = "Local\\QuickMenu.Activate.v1";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception); args.Handled = true;
            if (e.Args.Contains("--qa") || e.Args.Contains("--diagnostics") || e.Args.Contains("--lifecycle-smoke")) Shutdown(1);
            else System.Windows.MessageBox.Show(Loc.T("App.Error"), Loc.T("App.Name"));
        };
        if (e.Args.Contains("--qa") || e.Args.Contains("--diagnostics"))
        {
            _ = Diagnostics.RunAsync(e.Args);
            return;
        }
        _mutex = new Mutex(true, InstanceName, out var created);
        if (!created)
        {
            // A second login-time start (e.g. both a Startup shortcut and the Run key) must not pop the menu open;
            // only a launch the user made on purpose activates the running copy.
            if (e.Args.Contains("--background")) { Shutdown(); return; }
            try { using var signal = EventWaitHandle.OpenExisting(ActivationName); signal.Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationName);
        _shutdown = new CancellationTokenSource();
        // The chosen language, or Windows' own, before any window is built.
        Loc.Use(new SettingsService(manageStartup: false).Load().Language);
        _window = new MainWindow();
        MainWindow = _window;
        _window.InitializeNative();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var open = menu.Items.Add(Loc.T("Tray.Open"), null, (_, _) => Dispatcher.Invoke(_window.InvokeMenu));
        var settings = menu.Items.Add(Loc.T("Tray.Settings"), null, (_, _) => Dispatcher.Invoke(_window.OpenSettings));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var exit = menu.Items.Add(Loc.T("Tray.Exit"), null, (_, _) => Dispatcher.Invoke(() => Shutdown()));
        void UpdateTrayTheme(bool dark)
        {
            menu.BackColor = ThemePalette.Drawing("PopupBrush", dark);
            menu.ForeColor = ThemePalette.Drawing("TextBrush", dark);
            menu.Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new TrayColors(dark));
            foreach (System.Windows.Forms.ToolStripItem item in menu.Items) item.ForeColor = menu.ForeColor;
        }
        UpdateTrayTheme(_window.IsDarkAppearance);
        _window.AppearanceChanged += UpdateTrayTheme;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "quick-menu.ico");
        var trayIcon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        _tray = new System.Windows.Forms.NotifyIcon { Text = Loc.T("App.Name"), Icon = trayIcon, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(_window.InvokeMenu);
        Loc.Changed += () => { open.Text = Loc.T("Tray.Open"); settings.Text = Loc.T("Tray.Settings"); exit.Text = Loc.T("Tray.Exit"); _tray.Text = Loc.T("App.Name"); };
        var token = _shutdown.Token;
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                var result = WaitHandle.WaitAny(new[] { _activation, token.WaitHandle });
                if (result != 0) break;
                Dispatcher.BeginInvoke(() => _window.InvokeMenu());
            }
        }, token);
        if (!e.Args.Contains("--background") || !_window.HotkeyRegistered) _window.InvokeMenu();
        _ = _window.WarmDesktopAsync();
        if (e.Args.Contains("--lifecycle-smoke")) _ = Diagnostics.ObserveLifecycleAsync(_window, () => _tray.Visible, e.Args);
    }

    public static void Log(Exception exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickMenu", "Logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "app.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {exception}\n");
        }
        catch { }
    }

    private void Terminate_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button { Tag: Models.RunningApp app } button && Window.GetWindow(button) is MainWindow window)
            window.HandleTermination(app);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown?.Cancel();
        _window?.DisposeServices();
        if (_tray is not null) { _tray.Visible = false; _tray.Icon?.Dispose(); _tray.Dispose(); }
        _activation?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private sealed class TrayColors(bool dark) : System.Windows.Forms.ProfessionalColorTable
    {
        private System.Drawing.Color Surface => ThemePalette.Drawing("PopupBrush", dark);
        private System.Drawing.Color Border => ThemePalette.Drawing("BorderBrush", dark);
        public override System.Drawing.Color ToolStripDropDownBackground => Surface;
        public override System.Drawing.Color ImageMarginGradientBegin => Surface;
        public override System.Drawing.Color ImageMarginGradientMiddle => Surface;
        public override System.Drawing.Color ImageMarginGradientEnd => Surface;
        public override System.Drawing.Color MenuBorder => Border;
        public override System.Drawing.Color MenuItemBorder => Border;
        public override System.Drawing.Color SeparatorDark => Border;
        public override System.Drawing.Color SeparatorLight => Surface;
        public override System.Drawing.Color MenuItemSelected => ThemePalette.Drawing("SelectedBrush", dark);
    }
}
