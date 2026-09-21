namespace WorkspaceManager.Core;

public sealed class WindowMoveCoordinator(IDesktopService desktops, IWindowService windows, IMonitorService monitors) : IWindowMoveCoordinator
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<MoveResult> MoveAsync(MoveRequest request)
    {
        await gate.WaitAsync();
        try { return await MoveCoreAsync(request); }
        finally { gate.Release(); }
    }
    private async Task<MoveResult> MoveCoreAsync(MoveRequest request)
    {
        WindowInfo? before = null;
        bool desktopAttempted = false, placementAttempted = false;
        try
        {
            before = await windows.GetWindowAsync(request.Window);
            if (before is null || !before.CanIdentifyProcess) return new(MoveStatus.Failed, "窗口已关闭或无法确认进程身份。", before);
            var snapshot = await desktops.GetSnapshotAsync();
            bool changeDesktop = request.TargetDesktopId != before.DesktopId;
            if (before.IsPinned && changeDesktop)
                return new(MoveStatus.Failed, "此窗口固定在所有桌面，只能移动显示器。", before);
            if (changeDesktop && (!snapshot.IsAvailable || !snapshot.Desktops.Any(d => d.Id == request.TargetDesktopId)))
                return new(MoveStatus.Failed, "目标桌面已消失或当前系统不支持桌面操作。", before);
            var screens = monitors.GetMonitors();
            var source = screens.FirstOrDefault(m => m.Id == before.MonitorId);
            var target = request.TargetMonitorId is null ? source : screens.FirstOrDefault(m => m.Id == request.TargetMonitorId);
            if (source is null || target is null) return new(MoveStatus.Failed, "显示器已断开，请刷新后重试。", before);
            bool changeMonitor = source.Id != target.Id;
            var bounds = changeMonitor ? PlacementCalculator.Move(before.RestoreBounds, source, target) : before.RestoreBounds;
            if (!windows.IsValid(request.Window)) throw new InvalidOperationException("窗口已关闭。");
            if (changeDesktop)
            {
                desktopAttempted = true;
                await desktops.MoveWindowAsync(request.Window, request.TargetDesktopId);
            }
            if (changeMonitor)
            {
                if (!monitors.GetMonitors().Any(m => m.Id == target.Id)) throw new InvalidOperationException("目标显示器已断开。");
                placementAttempted = true;
                await windows.ApplyPlacementAsync(request.Window, bounds, before.State);
            }
            WindowInfo? actual = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                actual = await windows.GetWindowAsync(request.Window);
                if (actual is not null && (!changeDesktop || actual.DesktopId == request.TargetDesktopId) &&
                    (!changeMonitor || (actual.MonitorId == target.Id && actual.RestoreBounds.Near(bounds))) && actual.State == before.State)
                    return new(MoveStatus.Success, "窗口已移动。", actual);
                await Task.Delay(75);
            }
            throw new InvalidOperationException("系统未应用完整的目标位置或窗口状态。");
        }
        catch (Exception error)
        {
            if (before is null) return new(MoveStatus.Failed, error.Message, null);
            if (!desktopAttempted && !placementAttempted) return new(MoveStatus.Failed, error.Message, before);
            // Compensation is best effort. Never claim an atomic rollback that Windows cannot guarantee.
            if (windows.IsValid(request.Window))
            {
                if (desktopAttempted)
                    try { await desktops.MoveWindowAsync(request.Window, before.DesktopId); } catch { }
                if (placementAttempted && monitors.GetMonitors().Any(m => m.Id == before.MonitorId))
                    try { await windows.ApplyPlacementAsync(request.Window, before.RestoreBounds, before.State); } catch { }
            }
            WindowInfo? actual;
            try { actual = await windows.GetWindowAsync(request.Window); } catch { actual = null; }
            bool restored = actual is not null && actual.DesktopId == before.DesktopId && actual.MonitorId == before.MonitorId &&
                actual.State == before.State && actual.RestoreBounds.Near(before.RestoreBounds);
            return new(restored ? MoveStatus.Failed : MoveStatus.Partial,
                restored ? $"移动失败，已恢复原位置：{error.Message}" : $"移动未完全完成，请查看窗口当前所在位置：{error.Message}", actual);
        }
    }
}
