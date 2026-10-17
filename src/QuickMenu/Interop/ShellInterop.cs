using System.Runtime.InteropServices;

namespace QuickMenu.Interop;

internal static class ShellInterop
{
    [DllImport("shell32.dll")] internal static extern int SHOpenFolderAndSelectItems(nint pidlFolder, uint count, nint items, uint flags);
    [DllImport("ole32.dll")] internal static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] internal static extern void CoUninitialize();
    [DllImport("shell32.dll")] internal static extern int SHGetDesktopFolder(out IShellFolder folder);
    [DllImport("shell32.dll")] internal static extern int SHGetNameFromIDList(nint pidl, uint nameType, out nint name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attrs, out uint outAttrs);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern nint SHGetFileInfo(nint pidl, uint attrs, out SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll")] internal static extern int SHGetImageList(int imageList, ref Guid iid, out IImageList images);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFILEINFO
    {
        internal nint Icon;
        internal int Index;
        internal uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string TypeName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHELLEXECUTEINFO
    {
        internal int Size;
        internal uint Mask;
        internal nint Hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Verb;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? File;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Parameters;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Directory;
        internal int Show;
        internal nint Instance;
        internal nint IdList;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Class;
        internal nint ClassKey;
        internal uint HotKey;
        internal nint IconOrMonitor;
        internal nint Process;
    }
}

// IImageList's first eight slots, through GetIcon; only the read operation is used.
[ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IImageList
{
    [PreserveSig] int Add(nint image, nint mask, out int index);
    [PreserveSig] int ReplaceIcon(int index, nint icon, out int result);
    [PreserveSig] int SetOverlayImage(int image, int overlay);
    [PreserveSig] int Replace(int index, nint image, nint mask);
    [PreserveSig] int AddMasked(nint image, int maskColor, out int index);
    [PreserveSig] int Draw(nint parameters);
    [PreserveSig] int Remove(int index);
    [PreserveSig] int GetIcon(int index, uint flags, out nint icon);
}

internal readonly struct ComScope : IDisposable
{
    private readonly bool _initialized;
    internal ComScope(uint flags = 0)
    {
        var result = ShellInterop.CoInitializeEx(0, flags);
        _initialized = result >= 0;
        // A pre-existing STA is already initialized and may call Shell APIs too.
        if (result < 0 && result != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(result);
    }
    public void Dispose() { if (_initialized) ShellInterop.CoUninitialize(); }
}

[ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    [PreserveSig] int ParseDisplayName(nint hwnd, nint context, [MarshalAs(UnmanagedType.LPWStr)] string name, ref uint eaten, out nint pidl, ref uint attrs);
    [PreserveSig] int EnumObjects(nint hwnd, uint flags, out IEnumIDList? enumerator);
    [PreserveSig] int BindToObject(nint pidl, nint context, ref Guid iid, out nint result);
    [PreserveSig] int BindToStorage(nint pidl, nint context, ref Guid iid, out nint result);
    [PreserveSig] int CompareIDs(nint lParam, nint pidl1, nint pidl2);
    [PreserveSig] int CreateViewObject(nint hwnd, ref Guid iid, out nint result);
    [PreserveSig] int GetAttributesOf(uint count, nint pidls, ref uint attrs);
    [PreserveSig] int GetUIObjectOf(nint hwnd, uint count, nint pidls, ref Guid iid, nint reserved, out nint result);
    [PreserveSig] int GetDisplayNameOf(nint pidl, uint flags, nint result);
    [PreserveSig] int SetNameOf(nint hwnd, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out nint newPidl);
}

[ComImport, Guid("000214F2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumIDList
{
    [PreserveSig] int Next(uint count, out nint pidl, out uint fetched);
    [PreserveSig] int Skip(uint count);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumIDList enumerator);
}
