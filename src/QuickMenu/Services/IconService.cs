using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickMenu.Interop;

namespace QuickMenu.Services;

public sealed class IconService
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ImageListLock = new();
    public ImageSource? GetForPath(string path) => GetForParsingName(path);

    public ImageSource? GetForParsingName(string parsingName)
    {
        if (Cache.TryGetValue(parsingName, out var cached)) return cached;
        nint pidl = 0;
        try
        {
            // Pass the flag explicitly: new ComScope() zero-initializes this struct without entering COM.
            using var apartment = new ComScope(0);
            if (ShellInterop.SHParseDisplayName(parsingName, 0, out pidl, 0, out _) < 0) return null;
            if (ShellInterop.SHGetFileInfo(pidl, 0, out var info, (uint)Marshal.SizeOf<ShellInterop.SHFILEINFO>(), 0x100 | 0x8 | 0x4000) == 0)
                return null;
            var icon = info.Icon;
            var larger = GetLargerIcon(info.Index);
            if (larger != 0) { if (icon != 0) NativeMethods.DestroyIcon(icon); icon = larger; }
            var image = FromOwnedIcon(icon, 0);
            if (image != null) Cache.TryAdd(parsingName, image);
            return image;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or InvalidCastException or InvalidComObjectException) { return null; }
        finally { if (pidl != 0) Marshal.FreeCoTaskMem(pidl); }
    }

    private static nint GetLargerIcon(int index)
    {
        // Shell can return the same RCW to concurrent callers; one must not release it during another's GetIcon.
        lock (ImageListLock)
        {
            IImageList? images = null;
            try
            {
                var iid = typeof(IImageList).GUID;
                return ShellInterop.SHGetImageList(2, ref iid, out images) == 0 && images.GetIcon(index, 1, out var icon) == 0 ? icon : 0;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException) { return 0; }
            finally { if (images != null) Marshal.ReleaseComObject(images); }
        }
    }

    public ImageSource? GetForWindow(nint hwnd)
    {
        NativeMethods.SendMessageTimeout(hwnd, 0x7f, 2, 0, 2, 100, out var icon);
        if (icon == 0) NativeMethods.SendMessageTimeout(hwnd, 0x7f, 0, 0, 2, 100, out icon);
        if (icon == 0) icon = NativeMethods.GetClassLongPtr(hwnd, -34);
        return icon == 0 ? null : FromOwnedIcon(NativeMethods.CopyIcon(icon));
    }

    private static ImageSource? FromOwnedIcon(nint icon, int size = 32)
    {
        if (icon == 0) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, size > 0 ? BitmapSizeOptions.FromWidthAndHeight(size, size) : BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally { NativeMethods.DestroyIcon(icon); }
    }
}
