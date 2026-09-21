using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WorkspaceManager.Core;

namespace WorkspaceManager.Windows;

public sealed class DesktopService : IDesktopService, IDisposable
{
    private StaQueue queue = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private uint explorerPid;
    public string? CompatibilityReason { get; }
    public DesktopService()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = key?.GetValue("CurrentBuildNumber")?.ToString();
        var revision = Convert.ToInt32(key?.GetValue("UBR") ?? 0);
        if (!Environment.Is64BitProcess || build != "26200" || revision != 6899)
            CompatibilityReason = $"系统构建 {build}.{revision} 尚未验证。首版桌面适配仅启用 26200.6899 x64；显示器管理仍可使用。";
    }
    private async Task<T> Invoke<T>(Func<T> call)
    {
        if (CompatibilityReason is not null) throw new NotSupportedException(CompatibilityReason);
        await gate.WaitAsync();
        try
        {
            Native.GetWindowThreadProcessId(Native.GetShellWindow(), out uint current);
            if (current == 0) throw new InvalidOperationException("Explorer 正在重启，请稍后重试。");
            if (explorerPid != 0 && current != explorerPid) { queue.Dispose(); queue = new(); }
            explorerPid = current;
            return await queue.Run(call);
        }
        finally { gate.Release(); }
    }
    public async Task<DesktopSnapshot> GetSnapshotAsync()
    {
        try { return await Invoke(() => new DesktopSnapshot(ReadDesktops(), true)); }
        catch (Exception ex) { return new([], false, ex.Message); }
    }
    private static List<DesktopInfo> ReadDesktops()
    {
        int count = Vda.GetDesktopCount(), current = Vda.GetCurrentDesktopNumber();
        if (count <= 0 || current < 0 || current >= count) throw new InvalidOperationException("虚拟桌面接口不可用，请稍后重试。");
        var result = new List<DesktopInfo>();
        for (int i = 0; i < count; i++)
        {
            Guid id = Vda.GetDesktopIdByNumber(i);
            if (id == Guid.Empty) throw new InvalidOperationException("虚拟桌面列表正在变化，请重试。");
            byte[] text = new byte[2048];
            int status = Vda.GetDesktopName(i, text, (nuint)text.Length);
            int end = Array.IndexOf(text, (byte)0);
            string name = status == 1 ? Encoding.UTF8.GetString(text, 0, end < 0 ? text.Length : end) : "";
            result.Add(new(id, i, string.IsNullOrWhiteSpace(name) ? $"桌面 {i + 1}" : name, i == current));
        }
        return result;
    }
    public async Task<DesktopMembership> GetMembershipAsync(nint window)
    {
        if (CompatibilityReason is not null) return new(Guid.Empty, false);
        return await Invoke(() => new DesktopMembership(Vda.GetWindowDesktopId(window), Vda.IsPinnedWindow(window) == 1 || Vda.IsPinnedApp(window) == 1));
    }
    private static int Index(Guid id) => ReadDesktops().FirstOrDefault(d => d.Id == id)?.Index ?? throw new InvalidOperationException("目标桌面已不存在。");
    private static int Check(int result) => result < 0 ? throw new InvalidOperationException("Windows 拒绝桌面操作，可能是权限不足或桌面已变化。") : result;
    public Task<Guid> CreateAsync() => Invoke(() =>
    {
        var id = Vda.GetDesktopIdByNumber(Check(Vda.CreateDesktop()));
        return id != Guid.Empty ? id : throw new InvalidOperationException("桌面已创建，但系统未返回身份，请刷新列表确认。");
    });
    public async Task SwitchAsync(Guid id) => await Invoke(() => Check(Vda.GoToDesktopNumber(Index(id))));
    public async Task RenameAsync(Guid id, string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 80 || name.Contains('\0')) throw new ArgumentException("桌面名称需为 1–80 个字符。");
        await Invoke(() => Check(Vda.SetDesktopName(Index(id), name)));
    }
    public async Task RemoveAsync(Guid id, Guid fallback) => await Invoke(() =>
    {
        var list = ReadDesktops();
        if (list.Count <= 1 || id == fallback) throw new InvalidOperationException("不能删除最后一个桌面。");
        var from = list.FirstOrDefault(d => d.Id == id) ?? throw new InvalidOperationException("桌面已不存在。");
        var to = list.FirstOrDefault(d => d.Id == fallback) ?? throw new InvalidOperationException("接收窗口的桌面已不存在。");
        return Check(Vda.RemoveDesktop(from.Index, to.Index));
    });
    public async Task MoveWindowAsync(WindowIdentity window, Guid id) => await Invoke(() =>
    {
        int index = Index(id);
        if (window.ProcessStarted == 0 || !Native.IsWindow(window.Handle) || WindowService.GetIdentity(window.Handle, out _) != window)
            throw new InvalidOperationException("窗口身份已变化，移动已取消。");
        return Check(Vda.MoveWindowToDesktopNumber(window.Handle, index));
    });
    public void Dispose() => queue.Dispose();

    private static class Vda
    {
        private const string Lib = "VirtualDesktopAccessor.dll";
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int GetDesktopCount();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int GetCurrentDesktopNumber();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern Guid GetDesktopIdByNumber(int number);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern Guid GetWindowDesktopId(nint hwnd);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int GetDesktopName(int number, [Out] byte[] name, nuint length);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int SetDesktopName(int number, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int IsPinnedWindow(nint hwnd);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int IsPinnedApp(nint hwnd);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int CreateDesktop();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int GoToDesktopNumber(int number);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RemoveDesktop(int number, int fallback);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int MoveWindowToDesktopNumber(nint hwnd, int number);
    }
    private sealed class StaQueue : IDisposable
    {
        private readonly BlockingCollection<Action> jobs = new();
        public StaQueue()
        {
            var thread = new Thread(() => { foreach (var job in jobs.GetConsumingEnumerable()) job(); }) { IsBackground = true, Name = "Virtual desktop COM" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
        public Task<T> Run<T>(Func<T> work)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.Add(() => { try { completion.SetResult(work()); } catch (Exception ex) { completion.SetException(ex); } });
            return completion.Task;
        }
        public void Dispose() => jobs.CompleteAdding();
    }
}
