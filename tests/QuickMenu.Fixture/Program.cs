using System.Text.Json;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Windows;
using System.Windows.Interop;

namespace QuickMenu.Fixture;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var index = Array.IndexOf(args, "--output");
        var directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : AppContext.BaseDirectory;
        Directory.CreateDirectory(directory);
        if (args.Contains("--deny-terminate"))
        {
            var descriptor = new RawSecurityDescriptor("D:(D;;0x1;;;WD)(A;;0x1FFFFE;;;WD)");
            var bytes = new byte[descriptor.BinaryLength]; descriptor.GetBinaryForm(bytes, 0);
            if (!SetKernelObjectSecurity(GetCurrentProcess(), 4, bytes)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var main = new Window { Title = "快捷菜单测试窗口甲", Width = 260, Height = 160, Content = "仅用于快捷菜单自动验证", ShowActivated = false };
        var minimized = new Window { Title = "快捷菜单测试窗口乙", Width = 260, Height = 160, ShowActivated = false };
        var tool = new Window { Title = "快捷菜单测试工具窗口", Width = 100, Height = 80, WindowStyle = WindowStyle.ToolWindow, ShowActivated = false };
        main.Show(); minimized.Show(); tool.Show(); minimized.WindowState = WindowState.Minimized;
        File.WriteAllText(Path.Combine(directory, "handles-ready.json"), JsonSerializer.Serialize(new { ProcessId = Environment.ProcessId, MainHandle = new WindowInteropHelper(main).Handle.ToInt64(), MinimizedHandle = new WindowInteropHelper(minimized).Handle.ToInt64(), ToolHandle = new WindowInteropHelper(tool).Handle.ToInt64() }));
        var deadline = DateTime.UtcNow.AddSeconds(45);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => { if (DateTime.UtcNow > deadline || File.Exists(Path.Combine(directory, "exit.request"))) application.Shutdown(); }; timer.Start();
        application.Run();
    }
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError=true)] private static extern bool SetKernelObjectSecurity(nint handle, uint securityInformation, byte[] securityDescriptor);
}
