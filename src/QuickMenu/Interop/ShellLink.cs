using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace QuickMenu.Interop;

/// <summary>
/// Reads and writes .lnk shortcuts through the shell's own IShellLinkW, which is Unicode throughout. (WScript.Shell's
/// CreateShortcut rejects names outside the system code page, e.g. a Chinese shortcut name on English Windows.)
/// </summary>
public static class ShellLink
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLinkObject;

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, nint findData, uint flags);
        void GetIDList(out nint pidl);
        void SetIDList(nint pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    /// <summary>The file or folder a shortcut points to, or "" when it points at no file (a shell item, an installer
    /// entry). Throws for a file that is not a readable shortcut.</summary>
    public static string Target(string path)
    {
        var link = (IShellLinkW)new ShellLinkObject();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, 0, 0);
            return target.ToString();
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    public static void Create(string path, string target, string? arguments = null, string? workingDirectory = null)
    {
        var link = (IShellLinkW)new ShellLinkObject();
        try
        {
            link.SetPath(target);
            if (arguments != null) link.SetArguments(arguments);
            if (workingDirectory != null) link.SetWorkingDirectory(workingDirectory);
            ((IPersistFile)link).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }
}
