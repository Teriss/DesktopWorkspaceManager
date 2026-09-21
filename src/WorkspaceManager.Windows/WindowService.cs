using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using WorkspaceManager.Core;

namespace WorkspaceManager.Windows;

public sealed class WindowService(IDesktopService desktops) : IWindowService
{
    public async Task<IReadOnlyList<WindowInfo>> GetWindowsAsync()
    {
        var handles = new List<nint>();
        Native.EnumWindows((hwnd, _) => { if (IsAppWindow(hwnd)) handles.Add(hwnd); return true; }, 0);
        var result = new List<WindowInfo>();
        foreach (var hwnd in handles)
        {
            try { var window = await ReadAsync(hwnd); if (window is not null) result.Add(window); }
            catch (Exception ex) { AppLog.Write("enumerate-window", ex); }
        }
        return result;
    }
    private static bool IsAppWindow(nint hwnd)
    {
        if (!Native.IsWindowVisible(hwnd)) return false;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId) return false;
        long style = (long)Native.GetWindowLongPtr(hwnd, -20);
        if ((style & 0x80) != 0 || (style & 0x08000000) != 0) return false; // tool / noactivate
        if (Native.GetWindow(hwnd, 4) != 0 && (style & 0x40000) == 0) return false;
        var name = new StringBuilder(256); Native.GetClassName(hwnd, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow") return false;
        var title = new StringBuilder(512); Native.GetWindowText(hwnd, title, title.Capacity);
        if (title.Length == 0) return false;
        Native.DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int));
        // DWM_CLOAKED_SHELL (2) is normal for another virtual desktop; never discard it.
        if ((cloaked & 1) != 0) return false;
        return true;
    }
    public bool IsValid(WindowIdentity identity)
    {
        if (!Native.IsWindow(identity.Handle)) return false;
        var current = GetIdentity(identity.Handle, out _);
        return identity.ProcessStarted != 0 && identity == current;
    }
    public async Task<WindowInfo?> GetWindowAsync(WindowIdentity identity)
    {
        if (!IsValid(identity)) return null;
        return await ReadAsync(identity.Handle);
    }
    private async Task<WindowInfo?> ReadAsync(nint hwnd)
    {
        if (!Native.IsWindow(hwnd)) return null;
        var identity = GetIdentity(hwnd, out string? path);
        var text = new StringBuilder(1024); Native.GetWindowText(hwnd, text, text.Capacity);
        var placement = new Native.WINDOWPLACEMENT { Length = (uint)Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
        if (!Native.GetWindowPlacement(hwnd, ref placement) || !Native.GetWindowRect(hwnd, out var rect)) return null;
        bool minimized = Native.IsIconic(hwnd);
        var normalRect = placement.Normal;
        var screen = MonitorService.FromHandle(minimized ? Native.MonitorFromRect(ref normalRect, 2) : Native.MonitorFromWindow(hwnd, 2));
        var restore = placement.Normal.ToRect();
        restore = restore with { X = restore.X + screen.WorkArea.X - screen.Bounds.X, Y = restore.Y + screen.WorkArea.Y - screen.Bounds.Y };
        Native.DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int));
        Native.GetWindowDisplayAffinity(hwnd, out uint affinity);
        DesktopMembership membership;
        try { membership = await desktops.GetMembershipAsync(hwnd); }
        catch { membership = new(Guid.Empty, false); }
        if (!Native.IsWindow(hwnd) || (identity.ProcessStarted != 0 && !IsValid(identity))) return null;
        return new(identity, text.ToString(), path is null ? Localization.T("应用", "Application") : Path.GetFileNameWithoutExtension(path), path,
            membership.Id, screen.Id, rect.ToRect(), restore,
            minimized ? WindowShowState.Minimized : Native.IsZoomed(hwnd) ? WindowShowState.Maximized : WindowShowState.Normal,
            membership.IsPinned, cloaked != 0, affinity != 0, identity.ProcessStarted != 0);
    }
    internal static WindowIdentity GetIdentity(nint hwnd, out string? path)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid); path = null;
        var process = Native.OpenProcess(0x1000, false, pid);
        if (process == 0) return new(hwnd, pid, 0);
        try
        {
            var name = new StringBuilder(32768); uint size = (uint)name.Capacity;
            if (Native.QueryFullProcessImageName(process, 0, name, ref size)) path = name.ToString();
            Native.GetProcessTimes(process, out long created, out _, out _, out _);
            return new(hwnd, pid, created);
        }
        finally { Native.CloseHandle(process); }
    }
    public Task ApplyPlacementAsync(WindowIdentity identity, PixelRect bounds, WindowShowState state)
    {
        if (!IsValid(identity)) throw new InvalidOperationException(Localization.T("窗口身份已变化。", "The window identity changed."));
        var placement = new Native.WINDOWPLACEMENT { Length = (uint)Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
        if (!Native.GetWindowPlacement(identity.Handle, ref placement)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var targetRect = Native.RECT.From(bounds);
        var monitor = MonitorService.FromHandle(Native.MonitorFromRect(ref targetRect, 2));
        var workspaceBounds = bounds with { X = bounds.X - (monitor.WorkArea.X - monitor.Bounds.X), Y = bounds.Y - (monitor.WorkArea.Y - monitor.Bounds.Y) };
        placement.Normal = Native.RECT.From(workspaceBounds);
        placement.Flags |= 4; // WPF_ASYNCWINDOWPLACEMENT avoids blocking on a hung foreign thread.
        // A maximized window must first be positioned in restored state on the target monitor.
        // Merely editing rcNormalPosition while showCmd=3 leaves its maximized frame on the old screen.
        // This is also the two-step strategy used by PowerToys Workspaces' WindowArranger.
        placement.ShowCmd = state == WindowShowState.Minimized ? 7u : 4u;
        var foreground = Native.GetForegroundWindow();
        if (!Native.SetWindowPlacement(identity.Handle, ref placement))
            throw new Win32Exception(Marshal.GetLastWin32Error(), Localization.T("无法移动窗口，可能需要与目标应用相同的权限。", "The window could not be moved; it may require the same permissions as the target application."));
        if (state == WindowShowState.Maximized)
        {
            placement.ShowCmd = 3;
            if (!Native.SetWindowPlacement(identity.Handle, ref placement))
                throw new Win32Exception(Marshal.GetLastWin32Error(), Localization.T("目标位置已改变，但无法恢复最大化状态。", "The target position changed, but the maximized state could not be restored."));
        }
        // SetWindowPlacement can activate a maximized window. Keep the user's manager focused.
        if (foreground != 0 && Native.GetForegroundWindow() != foreground) Native.SetForegroundWindow(foreground);
        return Task.CompletedTask;
    }
    public Task ActivateAsync(WindowIdentity identity)
    {
        if (!IsValid(identity)) throw new InvalidOperationException(Localization.T("窗口已关闭。", "The window is closed."));
        if (Native.IsIconic(identity.Handle)) Native.ShowWindowAsync(identity.Handle, 9);
        if (!Native.SetForegroundWindow(identity.Handle)) throw new InvalidOperationException(Localization.T("Windows 暂时未允许激活该窗口，请再次点击。", "Windows did not allow the window to activate. Try again."));
        return Task.CompletedTask;
    }
    public Task RequestCloseAsync(WindowIdentity identity)
    {
        if (!IsValid(identity)) throw new InvalidOperationException(Localization.T("窗口已关闭或身份已变化。", "The window is closed or its identity changed."));
        // Post WM_CLOSE rather than terminating the process: the application's
        // own save prompts, cancellation and close-to-tray behavior remain intact.
        if (!Native.PostMessage(identity.Handle, 0x0010, 0, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), Localization.T("无法请求关闭窗口，目标应用可能以更高权限运行。", "The close request could not be sent; the target application may be running with higher permissions."));
        return Task.CompletedTask;
    }
}
