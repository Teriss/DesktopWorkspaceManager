using WorkspaceManager.Core;
using Xunit;

namespace WorkspaceManager.Tests;

public class MoveTests
{
    private sealed class Fake : IDesktopService, IWindowService, IMonitorService
    {
        public readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
        public WindowInfo? Window;
        public bool MissingTargetDesktop, MissingMonitor, FailPlacement, FailRollback, InvalidIdentity;
        public int Placements, Moves;
        public Action? AfterDesktopMove;
        public Fake(WindowShowState state = WindowShowState.Normal)
        {
            Window = new(new((nint)123, 456, 789), "Test", "Test", "test.exe", A, "m1", new(100, 100, 800, 600), new(100, 100, 800, 600), state, false, false, false, true);
        }
        public Task<DesktopSnapshot> GetSnapshotAsync() => Task.FromResult(new DesktopSnapshot(MissingTargetDesktop ? [new(A, 0, "a", true)] : [new(A, 0, "a", true), new(B, 1, "b", false)], true));
        public Task<DesktopMembership> GetMembershipAsync(nint window) => Task.FromResult(new DesktopMembership(Window!.DesktopId, Window.IsPinned));
        public Task<Guid> CreateAsync() => Task.FromResult(B);
        public Task SwitchAsync(Guid id) => Task.CompletedTask;
        public Task RenameAsync(Guid id, string name) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, Guid fallback) => Task.CompletedTask;
        public Task MoveWindowAsync(WindowIdentity window, Guid id)
        {
            Moves++;
            if (FailRollback && id == A) throw new InvalidOperationException("rollback refused");
            if (Window is not null) Window = Window with { DesktopId = id };
            if (id == B) AfterDesktopMove?.Invoke();
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<WindowInfo>> GetWindowsAsync() => Task.FromResult<IReadOnlyList<WindowInfo>>(Window is null ? [] : [Window]);
        public Task<WindowInfo?> GetWindowAsync(WindowIdentity identity) => Task.FromResult(InvalidIdentity ? null : Window);
        public bool IsValid(WindowIdentity identity) => !InvalidIdentity && Window is not null;
        public Task ApplyPlacementAsync(WindowIdentity identity, PixelRect bounds, WindowShowState state)
        {
            Placements++;
            if (FailPlacement && bounds.X >= 1920) throw new InvalidOperationException("placement refused");
            Window = Window! with { RestoreBounds = bounds, Bounds = bounds, State = state, MonitorId = bounds.X >= 1920 ? "m2" : "m1" };
            return Task.CompletedTask;
        }
        public Task ActivateAsync(WindowIdentity identity) => Task.CompletedTask;
        public Task RequestCloseAsync(WindowIdentity identity) => Task.CompletedTask;
        public IReadOnlyList<MonitorInfo> GetMonitors()
        {
            var first = new MonitorInfo("m1", "m1", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 96, true);
            return MissingMonitor ? [first] : [first, new("m2", "m2", new(1920, 0, 1920, 1080), new(1920, 0, 1920, 1040), 96, false)];
        }
        public MonitorInfo GetCursorMonitor() => GetMonitors()[0];
        public Task<MoveResult> Move(Guid? desktop = null, string? monitor = null) => new WindowMoveCoordinator(this, this, this).MoveAsync(new(Window!.Identity, desktop ?? B, monitor));
    }
    [Fact] public async Task DesktopOnlyDoesNotTouchGeometry()
    {
        var f = new Fake(); var before = f.Window!;
        var result = await f.Move(); Assert.Equal(MoveStatus.Success, result.Status); Assert.Equal(0, f.Placements); Assert.Equal(before.RestoreBounds, f.Window!.RestoreBounds);
    }
    [Theory] [InlineData(WindowShowState.Normal)] [InlineData(WindowShowState.Minimized)] [InlineData(WindowShowState.Maximized)]
    public async Task CombinedMovePreservesState(WindowShowState state)
    {
        var f = new Fake(state); var result = await f.Move(monitor: "m2");
        Assert.Equal(MoveStatus.Success, result.Status); Assert.Equal(state, f.Window!.State); Assert.Equal("m2", f.Window.MonitorId); Assert.Equal(f.B, f.Window.DesktopId);
    }
    [Fact] public async Task MissingTargetStopsBeforeMutation()
    {
        var f = new Fake { MissingTargetDesktop = true }; Assert.Equal(MoveStatus.Failed, (await f.Move()).Status); Assert.Equal(0, f.Moves);
    }
    [Fact] public async Task DisconnectStopsBeforeMutation()
    {
        var f = new Fake { MissingMonitor = true }; Assert.Equal(MoveStatus.Failed, (await f.Move(monitor: "m2")).Status); Assert.Equal(0, f.Moves);
    }
    [Fact] public async Task MonitorDisconnectAfterDesktopMoveRollsBackDesktop()
    {
        var f = new Fake(); f.AfterDesktopMove = () => f.MissingMonitor = true;
        Assert.Equal(MoveStatus.Failed, (await f.Move(monitor: "m2")).Status); Assert.Equal(f.A, f.Window!.DesktopId);
    }
    [Fact] public async Task FailedPlacementRestoresOriginalDesktop()
    {
        var f = new Fake { FailPlacement = true }; var result = await f.Move(monitor: "m2");
        Assert.Equal(MoveStatus.Failed, result.Status); Assert.Equal(f.A, f.Window!.DesktopId); Assert.Contains("已恢复", result.Message);
    }
    [Fact] public async Task FailedCompensationReportsActualPartialState()
    {
        var f = new Fake { FailPlacement = true, FailRollback = true }; var result = await f.Move(monitor: "m2");
        Assert.Equal(MoveStatus.Partial, result.Status); Assert.Equal(f.B, result.ActualWindow!.DesktopId);
    }
    [Fact] public async Task RecycledHandleIsNotMoved()
    {
        var f = new Fake { InvalidIdentity = true }; Assert.Equal(MoveStatus.Failed, (await f.Move()).Status); Assert.Equal(0, f.Moves);
    }
    [Fact] public async Task PinnedWindowCannotChangeDesktop()
    {
        var f = new Fake(); f.Window = f.Window! with { IsPinned = true };
        Assert.Equal(MoveStatus.Failed, (await f.Move()).Status); Assert.Equal(0, f.Moves);
        Assert.Equal(MoveStatus.Success, (await f.Move(f.A, "m2")).Status);
    }
    [Fact] public async Task WindowClosedDuringMoveReturnsPartialWithoutTouchingReplacement()
    {
        var f = new Fake(); f.AfterDesktopMove = () => f.Window = null;
        var result = await f.Move(); Assert.Equal(MoveStatus.Partial, result.Status); Assert.Null(result.ActualWindow);
    }
}
