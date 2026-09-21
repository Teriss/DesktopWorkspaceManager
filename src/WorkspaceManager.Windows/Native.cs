using System.Runtime.InteropServices;
using System.Text;
using WorkspaceManager.Core;

namespace WorkspaceManager.Windows;

internal static class Native
{
    internal delegate bool EnumWindowsProc(nint hwnd, nint param);
    internal delegate bool MonitorEnumProc(nint monitor, nint dc, ref RECT rect, nint data);
    internal delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    internal delegate void WinEventProc(nint hook, uint evt, nint hwnd, int objectId, int childId, uint threadId, uint time);
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly PixelRect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
        public static RECT From(PixelRect r) => new() { Left = r.X, Top = r.Y, Right = r.Right, Bottom = r.Bottom };
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MONITORINFOEX
    {
        public uint Size; public RECT Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct WINDOWPLACEMENT
    {
        public uint Length, Flags, ShowCmd; public POINT Min, Max; public RECT Normal;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WNDCLASSEX
    {
        public uint Size, Style; public WindowProc Proc; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background; public string? MenuName; public string ClassName; public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct NOTIFYICONDATA
    {
        public uint Size; public nint Window; public uint Id, Flags, CallbackMessage; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    // dwmapi.h includes pshpack1.h: BOOL fields immediately follow the opacity byte.
    [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct THUMBNAIL_PROPERTIES
    {
        public uint Flags; public RECT Destination, Source; public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct SIZE { public int Width, Height; }

    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, nint data);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd, ref POINT point);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint hwnd, nint insert, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool EnableWindow(nint hwnd, bool enable);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint GetShellWindow();
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] internal static extern nint MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll")] internal static extern bool GetProcessTimes(nint process, out long created, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern nint CallWindowProc(nint proc, nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool AppendMenu(nint menu, uint flags, nuint id, string? label);
    [DllImport("user32.dll")] internal static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] internal static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint LoadImage(nint instance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] internal static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] internal static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref THUMBNAIL_PROPERTIES props);
    [DllImport("dwmapi.dll")] internal static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out SIZE size);
    [DllImport("gdi32.dll")] internal static extern nint CreateRectRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] internal static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("user32.dll")] internal static extern uint GetGuiResources(nint process, uint flags);
}
