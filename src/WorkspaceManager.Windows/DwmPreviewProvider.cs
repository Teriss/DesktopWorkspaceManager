using System.Runtime.InteropServices;
using WorkspaceManager.Core;

namespace WorkspaceManager.Windows;

public sealed class DwmPreviewProvider : IWindowPreviewProvider
{
    public event Action<int>? MouseWheel;
    public IWindowPreview Create(nint owner, WindowInfo window) => new DwmPreview(owner, window, delta => MouseWheel?.Invoke(delta));
    private sealed class DwmPreview : IWindowPreview
    {
        private static readonly Native.WindowProc Procedure = WndProc;
        private static readonly Dictionary<nint, DwmPreview> Hosts = [];
        private static ushort atom;
        private readonly Action<int> wheel;
        private nint host, thumbnail;
        private PreviewGeometry? last;
        private Native.SIZE sourceSize;
        private long sizeChecked;
        private bool visible = true, displayed;
        public bool IsAvailable { get; private set; }
        public bool IsVisible => host != 0 && Native.IsWindowVisible(host);
        public string? UnavailableReason { get; private set; }
        public DwmPreview(nint owner, WindowInfo window, Action<int> wheel)
        {
            this.wheel = wheel;
            if (window.IsProtected) { UnavailableReason = "此窗口不允许预览"; return; }
            // DWM may retain a minimized window's last frame. Never restore it to capture.
            if (atom == 0)
            {
                var cls = new Native.WNDCLASSEX { Size = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(), ClassName = "WorkspaceManager.Preview", Proc = Procedure,
                    Instance = Native.GetModuleHandle(null) };
                atom = Native.RegisterClassEx(ref cls);
            }
            // Disabled display-only HWNDs are skipped by WindowFromPoint/OLE drop
            // targeting. Keep previews visible while XAML receives drag and wheel input.
            host = Native.CreateWindowEx(0x08000080, "WorkspaceManager.Preview", "", 0x88000000, 0, 0, 1, 1, owner, 0, Native.GetModuleHandle(null), 0);
            if (host == 0) { UnavailableReason = "无法创建预览"; return; }
            Hosts.Add(host, this);
            int hr = Native.DwmRegisterThumbnail(host, window.Identity.Handle, out thumbnail);
            IsAvailable = hr >= 0;
            if (!IsAvailable) UnavailableReason = "系统暂时无法提供预览";
        }
        private static nint WndProc(nint hwnd, uint msg, nuint wp, nint lp)
        {
            if (msg == 0x20A && Hosts.TryGetValue(hwnd, out var preview))
            {
                try { preview.wheel(unchecked((short)((ulong)wp >> 16))); }
                catch (Exception ex) { AppLog.Write("preview-wheel", ex); }
                return 0;
            }
            return msg switch
            {
                0x84 => -1, // HTTRANSPARENT: clicks and drags reach XAML.
                0x21 => 3, // MA_NOACTIVATE
                0x14 => 1, // Suppress background erase; DWM paints the entire host.
                _ => Native.DefWindowProc(hwnd, msg, wp, lp)
            };
        }
        public void Update(PreviewPlacement placement)
        {
            if (!IsAvailable || host == 0) return;
            long now = Environment.TickCount64;
            if (sourceSize.Width == 0 || now - sizeChecked >= 250)
            {
                sizeChecked = now;
                if (Native.DwmQueryThumbnailSourceSize(thumbnail, out sourceSize) < 0)
                { Fail("窗口预览已失效"); return; }
            }
            var geometry = PreviewGeometry.Calculate(placement, sourceSize.Width, sourceSize.Height);
            if (geometry is null) { last = null; Show(false); return; }
            if (geometry != last)
            {
                var r = geometry.HostBounds;
                // Clip the actual HWND and source pixels, not only its GDI region.
                Native.SetWindowPos(host, 0, r.X, r.Y, r.Width, r.Height, 0x14);
                var props = new Native.THUMBNAIL_PROPERTIES { Flags = 1 | 2 | 4 | 8 | 16,
                    Destination = Native.RECT.From(new(0, 0, r.Width, r.Height)), Source = Native.RECT.From(geometry.SourceBounds),
                    Opacity = 255, Visible = true, ClientOnly = false };
                if (Native.DwmUpdateThumbnailProperties(thumbnail, ref props) < 0)
                { Fail("窗口预览已失效"); return; }
                last = geometry;
            }
            Show(visible);
        }
        private void Fail(string reason) { IsAvailable = false; UnavailableReason = reason; Show(false); }
        private void Show(bool value)
        {
            if (displayed == value || host == 0) return;
            displayed = value;
            // SW_SHOWNA preserves order beneath an already-open native flyout.
            Native.ShowWindow(host, value ? 8 : 0);
        }
        public void SetVisible(bool value) { visible = value; Show(value && IsAvailable && last is not null); }
        public void Dispose()
        {
            if (thumbnail != 0) { Native.DwmUnregisterThumbnail(thumbnail); thumbnail = 0; }
            if (host != 0) { Hosts.Remove(host); Native.DestroyWindow(host); host = 0; }
        }
    }
}
