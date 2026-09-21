namespace WorkspaceManager.Core;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsValid => Width > 0 && Height > 0;
    public bool Near(PixelRect other, int tolerance = 8) =>
        Math.Abs(X - other.X) <= tolerance && Math.Abs(Y - other.Y) <= tolerance &&
        Math.Abs(Width - other.Width) <= tolerance && Math.Abs(Height - other.Height) <= tolerance;
}
public sealed record MonitorInfo(string Id, string Name, PixelRect Bounds, PixelRect WorkArea, uint Dpi, bool IsPrimary);
public sealed record DesktopInfo(Guid Id, int Index, string Name, bool IsCurrent);
public sealed record DesktopSnapshot(IReadOnlyList<DesktopInfo> Desktops, bool IsAvailable, string? UnavailableReason = null)
{
    public Guid CurrentId => Desktops.FirstOrDefault(d => d.IsCurrent)?.Id ?? Guid.Empty;
}
// HWND + PID + process creation time protects against a handle recycled during a drag.
public readonly record struct WindowIdentity(nint Handle, uint ProcessId, long ProcessStarted);
public enum WindowShowState { Normal, Minimized, Maximized }
public sealed record WindowInfo(WindowIdentity Identity, string Title, string ProcessName, string? ExecutablePath,
    Guid DesktopId, string MonitorId, PixelRect Bounds, PixelRect RestoreBounds, WindowShowState State,
    bool IsPinned, bool IsCloaked, bool IsProtected, bool CanIdentifyProcess);
public sealed record MoveRequest(WindowIdentity Window, Guid TargetDesktopId, string? TargetMonitorId = null);
public enum MoveStatus { Success, Failed, Partial }
public sealed record MoveResult(MoveStatus Status, string Message, WindowInfo? ActualWindow);
public sealed record DesktopMembership(Guid Id, bool IsPinned);
public sealed record PreviewPlacement(PixelRect ScreenBounds, PixelRect ClipBounds);
public interface IWindowPreview : IDisposable
{
    bool IsAvailable { get; }
    bool IsVisible { get; }
    string? UnavailableReason { get; }
    void Update(PreviewPlacement placement);
    void SetVisible(bool visible);
}
public interface IWindowPreviewProvider
{
    event Action<int>? MouseWheel;
    IWindowPreview Create(nint owner, WindowInfo window);
}
