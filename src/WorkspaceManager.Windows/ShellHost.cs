using System.Runtime.InteropServices;
using System.Text.Json;
using WorkspaceManager.Core;

namespace WorkspaceManager.Windows;

public sealed record AppSettings(uint HotkeyModifiers = 8, uint HotkeyKey = 0xC0, string Theme = "Default", string Language = Localization.Chinese)
{
    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(AppLog.DataDirectory, "settings.json"))) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(AppLog.DataDirectory);
        var path = Path.Combine(AppLog.DataDirectory, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
    public string HotkeyLabel => string.Join(" + ", new[] { (HotkeyModifiers & 8) != 0 ? "Win" : null, (HotkeyModifiers & 2) != 0 ? "Ctrl" : null,
        (HotkeyModifiers & 1) != 0 ? "Alt" : null, (HotkeyModifiers & 4) != 0 ? "Shift" : null,
        HotkeyKey == 0xC0 ? "`" : HotkeyKey >= 0x70 && HotkeyKey <= 0x7B ? $"F{HotkeyKey - 0x6F}" : ((char)HotkeyKey).ToString() }.Where(x => x is not null));
}

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly bool owns;
    internal static readonly uint ShowMessage = Native.RegisterWindowMessage("DesktopWorkspaceManager.Show.0187F68C");
    public bool IsFirst => owns;
    public SingleInstance()
    {
        mutex = new Mutex(true, @"Local\DesktopWorkspaceManager.0187F68C", out owns);
        if (!owns) Native.PostMessage((nint)0xffff, ShowMessage, 0, 0);
    }
    public void Dispose() { if (owns) mutex.ReleaseMutex(); mutex.Dispose(); }
}

// HWND ownership, Win32 notifications, tray and hotkey details never leak into UI code.
public sealed class ShellHost : IDisposable
{
    public static void SetApplicationIdentity() => Marshal.ThrowExceptionForHR(Native.SetCurrentProcessExplicitAppUserModelID("DesktopWorkspaceManager.App"));
    private readonly nint hwnd, oldProc;
    private readonly Native.WindowProc procedure;
    private readonly Native.WinEventProc eventProcedure;
    private readonly List<nint> hooks = [];
    private readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    private Native.NOTIFYICONDATA tray;
    private bool disposed, hotkeyRegistered;
    public event Action? ToggleRequested, ShowRequested, HideRequested, SettingsRequested, ExitRequested, Changed;
    public nint Handle => hwnd;
    public WindowIdentity Identity => WindowService.GetIdentity(hwnd, out _);
    public static uint GetUserHandleCount() => Native.GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 1);
    public ShellHost(nint handle, bool registerNotifications = true)
    {
        hwnd = handle;
        procedure = WndProc;
        oldProc = Native.SetWindowLongPtr(hwnd, -4, Marshal.GetFunctionPointerForDelegate(procedure));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        var icon = Native.LoadImage(0, iconPath, 1, 32, 32, 0x10);
        tray = new() { Size = (uint)Marshal.SizeOf<Native.NOTIFYICONDATA>(), Window = hwnd, Id = 1, Flags = 1 | 2 | 4,
            CallbackMessage = 0x8001, Icon = icon != 0 ? icon : Native.LoadIcon(0, (nint)32512), Tip = Localization.T("桌面工作区管理器", "Desktop Workspace Manager"), Info = "", InfoTitle = "" };
        if (registerNotifications) Native.Shell_NotifyIcon(0, ref tray);
        eventProcedure = (_, evt, window, objectId, childId, thread, time) =>
        {
            if (objectId == 0 && window != hwnd) Changed?.Invoke();
        };
        // OUTOFCONTEXT | SKIPOWNPROCESS: never inject code into other applications.
        if (registerNotifications)
        {
            hooks.Add(Native.SetWinEventHook(0x8000, 0x8003, 0, eventProcedure, 0, 0, 2));
            hooks.Add(Native.SetWinEventHook(0x800B, 0x800C, 0, eventProcedure, 0, 0, 2));
            hooks.Add(Native.SetWinEventHook(0x0016, 0x0017, 0, eventProcedure, 0, 0, 2));
        }
    }
    public bool RegisterHotkey(AppSettings settings)
    {
        if (hotkeyRegistered) Native.UnregisterHotKey(hwnd, 1);
        hotkeyRegistered = Native.RegisterHotKey(hwnd, 1, settings.HotkeyModifiers | 0x4000, settings.HotkeyKey);
        return hotkeyRegistered;
    }
    public void Show(MonitorInfo monitor)
    {
        var r = monitor.WorkArea;
        Native.SetWindowPos(hwnd, 0, r.X, r.Y, r.Width, r.Height, 0x14);
        Native.ShowWindow(hwnd, 5); Native.SetForegroundWindow(hwnd);
    }
    public void Hide() => Native.ShowWindow(hwnd, 0);
    public void UpdateLanguage()
    {
        tray.Tip = Localization.T("桌面工作区管理器", "Desktop Workspace Manager");
        Native.Shell_NotifyIcon(0, ref tray);
    }
    public (int X, int Y) GetCursorScreenPosition()
    {
        Native.GetCursorPos(out var point);
        return (point.X, point.Y);
    }
    public void AttachDialog(nint dialog, double logicalWidth, double logicalHeight)
    {
        Native.SetWindowLongPtr(dialog, -8, hwnd); // GWLP_HWNDPARENT: owned top-level window.
        Native.GetWindowRect(hwnd, out var owner);
        double scale = Native.GetDpiForWindow(hwnd) / 96.0;
        int width = Math.Min(owner.Right - owner.Left, (int)Math.Ceiling(logicalWidth * scale));
        int height = Math.Min(owner.Bottom - owner.Top, (int)Math.Ceiling(logicalHeight * scale));
        Native.SetWindowPos(dialog, 0, owner.Left + (owner.Right - owner.Left - width) / 2,
            owner.Top + (owner.Bottom - owner.Top - height) / 2, width, height, 0x14);
        Native.EnableWindow(hwnd, false);
    }
    public void DetachDialog()
    {
        Native.EnableWindow(hwnd, true);
        Native.SetForegroundWindow(hwnd);
    }
    public void MoveOwnWindowToDesktop(Guid desktop)
    {
        // The documented interface is sufficient for our own HWND, including when it is hidden.
        // The private foreign-window API requires an ApplicationView and can reject a newly created overlay.
        var type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        try
        {
            var manager = (IPublicDesktopManager)instance;
            if (manager.GetWindowDesktopId(hwnd, out var current) >= 0 && current == desktop) return;
            Marshal.ThrowExceptionForHR(manager.MoveWindowToDesktop(hwnd, in desktop));
        }
        finally { Marshal.ReleaseComObject(instance); }
    }
    public PixelRect ToScreen(PixelRect clientRect)
    {
        var p = new Native.POINT { X = clientRect.X, Y = clientRect.Y }; Native.ClientToScreen(hwnd, ref p);
        return clientRect with { X = p.X, Y = p.Y };
    }
    private nint WndProc(nint window, uint message, nuint wp, nint lp)
    {
        try
        {
            if (message == SingleInstance.ShowMessage) { ShowRequested?.Invoke(); return 0; }
            if (message == taskbarCreated) { Native.Shell_NotifyIcon(0, ref tray); Changed?.Invoke(); }
            if (message == 0x312) { ToggleRequested?.Invoke(); return 0; }
            if (message == 0x10) { HideRequested?.Invoke(); return 0; }
            if (message is 0x7E or 0x02E0) Changed?.Invoke();
            if (message == 0x8001)
            {
                if ((int)lp == 0x202) ShowRequested?.Invoke();
                if ((int)lp == 0x205)
                {
                    var menu = Native.CreatePopupMenu();
                    Native.AppendMenu(menu, 0, 1, Localization.T("打开管理器", "Open manager")); Native.AppendMenu(menu, 0, 2, Localization.T("设置", "Settings"));
                    Native.AppendMenu(menu, 0x800, 0, null); Native.AppendMenu(menu, 0, 3, Localization.T("退出", "Exit"));
                    Native.GetCursorPos(out var p); Native.SetForegroundWindow(hwnd);
                    uint command = Native.TrackPopupMenu(menu, 0x100 | 2, p.X, p.Y, 0, hwnd, 0);
                    Native.DestroyMenu(menu); Native.PostMessage(hwnd, 0, 0, 0);
                    if (command == 1) ShowRequested?.Invoke();
                    if (command == 2) SettingsRequested?.Invoke();
                    if (command == 3) ExitRequested?.Invoke();
                }
                return 0;
            }
        }
        catch (Exception ex) { AppLog.Write("shell-message", ex); }
        return Native.CallWindowProc(oldProc, window, message, wp, lp);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var hook in hooks) if (hook != 0) Native.UnhookWinEvent(hook);
        if (hotkeyRegistered) Native.UnregisterHotKey(hwnd, 1);
        Native.Shell_NotifyIcon(2, ref tray);
        if (tray.Icon != 0) Native.DestroyIcon(tray.Icon);
        Native.SetWindowLongPtr(hwnd, -4, oldProc);
    }
    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPublicDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(nint window, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(nint window, in Guid desktop);
    }
}
