using System.Runtime.InteropServices;
using WorkspaceManager.Core;

namespace WorkspaceManager.Windows;

public sealed class MonitorService : IMonitorService
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(0, 0, (nint handle, nint dc, ref Native.RECT rect, nint data) =>
        {
            result.Add(FromHandle(handle)); return true;
        }, 0);
        return result.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToArray();
    }
    public MonitorInfo GetCursorMonitor()
    {
        Native.GetCursorPos(out var point);
        return FromHandle(Native.MonitorFromPoint(point, 2));
    }
    internal static MonitorInfo FromHandle(nint handle)
    {
        var info = new Native.MONITORINFOEX { Size = (uint)Marshal.SizeOf<Native.MONITORINFOEX>(), Device = "" };
        if (!Native.GetMonitorInfo(handle, ref info)) throw new InvalidOperationException(Localization.T("无法读取显示器信息。", "Monitor information could not be read."));
        Native.GetDpiForMonitor(handle, 0, out uint dpi, out _);
        var number = new string(info.Device.Where(char.IsDigit).ToArray());
        return new(info.Device, Localization.T($"显示器 {number}", $"Monitor {number}"), info.Monitor.ToRect(), info.Work.ToRect(), dpi == 0 ? 96 : dpi, (info.Flags & 1) != 0);
    }
}
