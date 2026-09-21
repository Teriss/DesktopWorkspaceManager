using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WorkspaceManager.Core;

namespace WorkspaceManager.Windows;

public sealed class WindowIconProvider : IWindowIconProvider
{
    private readonly ConcurrentDictionary<string, byte[]> cache = new(StringComparer.OrdinalIgnoreCase);
    public Task<byte[]?> GetIconAsync(WindowInfo window) => Task.Run(() =>
    {
        if (window.ExecutablePath is not { } path) return null;
        if (cache.TryGetValue(path, out var cached)) return cached;
        nint icon = 0;
        try
        {
            if (SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100) == 0) return null;
            icon = info.Icon;
            if (icon == 0) return null;
            using var wrapper = Icon.FromHandle(icon);
            using var bitmap = wrapper.ToBitmap();
            using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
            var bytes = stream.ToArray();
            if (cache.Count < 128) cache.TryAdd(path, bytes);
            return bytes;
        }
        catch { return null; }
        finally { if (icon != 0) Native.DestroyIcon(icon); }
    });
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SHFILEINFO
    {
        public nint Icon; public int IconIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, out SHFILEINFO info, uint size, uint flags);
}
