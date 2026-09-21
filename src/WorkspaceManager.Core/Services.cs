namespace WorkspaceManager.Core;

public interface IDesktopService
{
    Task<DesktopSnapshot> GetSnapshotAsync();
    Task<DesktopMembership> GetMembershipAsync(nint window);
    Task<Guid> CreateAsync();
    Task SwitchAsync(Guid id);
    Task RenameAsync(Guid id, string name);
    Task RemoveAsync(Guid id, Guid fallback);
    Task MoveWindowAsync(WindowIdentity window, Guid id);
}
public interface IMonitorService
{
    IReadOnlyList<MonitorInfo> GetMonitors();
    MonitorInfo GetCursorMonitor();
}
public interface IWindowService
{
    Task<IReadOnlyList<WindowInfo>> GetWindowsAsync();
    Task<WindowInfo?> GetWindowAsync(WindowIdentity identity);
    bool IsValid(WindowIdentity identity);
    Task ApplyPlacementAsync(WindowIdentity identity, PixelRect restoreBounds, WindowShowState state);
    Task ActivateAsync(WindowIdentity identity);
    Task RequestCloseAsync(WindowIdentity identity);
}
public interface IWindowMoveCoordinator
{
    Task<MoveResult> MoveAsync(MoveRequest request);
}
public interface IWindowIconProvider
{
    Task<byte[]?> GetIconAsync(WindowInfo window);
}
