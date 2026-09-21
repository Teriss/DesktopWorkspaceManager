using System.Runtime.InteropServices;
using System.Text.Json;
using WorkspaceManager.Core;
using WorkspaceManager.Windows;
using Native = WorkspaceManager.Windows.Native;

namespace WorkspaceManager.Diagnostics;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--close-fixture")
        {
            // Separate process: the manager must discover and close real foreign HWNDs.
            using var context = new ApplicationContext();
            var forms = Enumerable.Range(0, 7).Select(i => TestWindow($"[WM-close-{args[1]}] {i}", Color.FromArgb(25 + i * 10, 76, 115))).ToArray();
            int remaining = forms.Length, guardedAttempts = 0;
            forms[0].FormClosing += (_, e) => e.Cancel = ++guardedAttempts == 1;
            foreach (var form in forms)
            {
                form.FormClosed += (_, _) => { if (--remaining == 0) context.ExitThread(); };
                form.Show();
                Native.ShowWindow(form.Handle, 4);
            }
            Application.Run(context);
            foreach (var form in forms) form.Dispose();
        }
        else if (args.Contains("--close-check"))
        {
            using var form = TestWindow("关闭操作回归测试 · 仅操作测试窗口", Color.FromArgb(25, 76, 115));
            form.Shown += async (_, _) =>
            {
                try { await CloseChecksAsync(form); }
                catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
                finally { form.Close(); }
            };
            Application.Run(form);
        }
        else if (args.Contains("--preview-check"))
        {
            using var form = TestWindow("预览回归测试 · 仅操作测试窗口", Color.FromArgb(25, 76, 115));
            form.Shown += async (_, _) =>
            {
                try { await PreviewChecksAsync(form); }
                catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
                finally { form.Close(); }
            };
            Application.Run(form);
        }
        else if (args.Contains("--exercise"))
        {
            using var form = TestWindow("验证窗口 · 仅操作此测试窗口", Color.FromArgb(25, 76, 115));
            form.Shown += async (_, _) =>
            {
                try { await ExerciseAsync(form); }
                catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
                finally { form.Close(); }
            };
            Application.Run(form);
        }
        else if (args.Contains("--fixture"))
        {
            using var form = TestWindow("预览测试 A · 动态时钟", Color.FromArgb(25, 76, 115));
            using var other = TestWindow("预览测试 B · 动态时钟", Color.FromArgb(77, 43, 91));
            other.Location = new Point(form.Left + 130, form.Top + 160);
            form.Shown += (_, _) => other.Show();
            Application.Run(form);
        }
        else
        {
            using var desktops = new DesktopService();
            var snapshot = desktops.GetSnapshotAsync().GetAwaiter().GetResult();
            var monitors = new MonitorService().GetMonitors();
            var windows = new WindowService(desktops).GetWindowsAsync().GetAwaiter().GetResult();
            Console.WriteLine(JsonSerializer.Serialize(new { snapshot.IsAvailable, snapshot.UnavailableReason,
                DesktopCount = snapshot.Desktops.Count, Monitors = monitors, WindowCount = windows.Count,
                ClassifiedWindowCount = windows.Count(w => w.DesktopId != Guid.Empty || w.IsPinned),
                MinimisedCount = windows.Count(w => w.State == WindowShowState.Minimized), PinnedCount = windows.Count(w => w.IsPinned)
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (!snapshot.IsAvailable) Environment.ExitCode = 2;
        }
    }
    private static Form TestWindow(string title, Color color)
    {
        var form = new Form { Text = title, BackColor = color, Size = new Size(720, 500), StartPosition = FormStartPosition.Manual, Location = new Point(100, 100) };
        var timer = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += (_, _) => form.Invalidate();
        form.Paint += (_, e) =>
        {
            using var font = new Font("Segoe UI", 24); using var small = new Font("Segoe UI", 14);
            e.Graphics.DrawString("真实窗口预览", font, Brushes.White, 40, 45);
            e.Graphics.DrawString(DateTime.Now.ToString("HH:mm:ss.fff"), font, Brushes.White, 40, 112);
            e.Graphics.DrawString("仅用于管理器集成测试，可随时关闭。", small, Brushes.LightBlue, 40, 210);
            e.Graphics.FillRectangle(Brushes.Coral, 40, 300, DateTime.Now.Second * 8 + 20, 35);
        };
        form.FormClosed += (_, _) => timer.Dispose(); timer.Start(); return form;
    }
    private static async Task ExerciseAsync(Form form)
    {
        using var desktopService = new DesktopService(); var monitorService = new MonitorService(); var windowService = new WindowService(desktopService);
        var mover = new WindowMoveCoordinator(desktopService, windowService, monitorService);
        var initial = await desktopService.GetSnapshotAsync();
        Require(initial.IsAvailable, "desktop adapter available");
        var identity = WindowService.GetIdentity(form.Handle, out _);
        var before = (await windowService.GetWindowAsync(identity))!;
        var screens = monitorService.GetMonitors(); Guid temporary = Guid.Empty;
        try
        {
            temporary = await desktopService.CreateAsync(); Require(temporary != Guid.Empty, "create temporary desktop");
            await desktopService.RenameAsync(temporary, "WorkspaceManager 验证");
            Require((await desktopService.GetSnapshotAsync()).Desktops.Any(d => d.Id == temporary && d.Name == "WorkspaceManager 验证"), "rename desktop");
            var targetMonitor = screens.FirstOrDefault(m => m.Id != before.MonitorId) ?? screens[0];
            var result = await mover.MoveAsync(new(identity, temporary, targetMonitor.Id));
            Require(result.Status == MoveStatus.Success, $"combined desktop / monitor move: {result.Message}");
            Require((await desktopService.GetSnapshotAsync()).CurrentId == initial.CurrentId, "move does not switch desktop");
            await desktopService.SwitchAsync(temporary); await Task.Delay(250);
            Require((await desktopService.GetSnapshotAsync()).CurrentId == temporary, "switch desktop");
            await desktopService.SwitchAsync(initial.CurrentId);
            await desktopService.MoveWindowAsync(identity, initial.CurrentId);
            await windowService.ApplyPlacementAsync(identity, before.RestoreBounds, before.State); await Task.Delay(150);
            var states = new[] { FormWindowState.Maximized, FormWindowState.Minimized, FormWindowState.Normal };
            foreach (var state in states)
            {
                form.WindowState = state; await Task.Delay(150);
                var current = (await windowService.GetWindowAsync(identity))!;
                var destination = screens.FirstOrDefault(m => m.Id != current.MonitorId) ?? screens[0];
                var move = await mover.MoveAsync(new(identity, initial.CurrentId, destination.Id));
                Require(move.Status == MoveStatus.Success, $"{state} across monitors: {move.Message}");
                Require(move.ActualWindow!.State == current.State, $"preserve {state}");
            }
            form.WindowState = FormWindowState.Normal; await Task.Delay(100);
            // Verify DWM relationships and cleanup using a separate owned destination window.
            using var destinationForm = new Form { Text = "DWM 验证目标", Size = new Size(340, 260), StartPosition = FormStartPosition.Manual, Location = new Point(50, 50) };
            destinationForm.Show();
            var provider = new DwmPreviewProvider();
            var source = (await windowService.GetWindowAsync(identity))!;
            uint handlesBefore = GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 1);
            for (int i = 0; i < 40; i++)
            {
                using var preview = provider.Create(destinationForm.Handle, source);
                Require(preview.IsAvailable, $"DWM registration {i + 1}", quiet: i > 0);
                preview.Update(new(new(70, 90, 290, 170), new(50, 50, 340, 260)));
                if (i == 0) await Task.Delay(500);
            }
            uint handlesAfter = GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 1);
            Require(handlesAfter <= handlesBefore + 2, $"preview resource cleanup ({handlesBefore} -> {handlesAfter} USER handles)");
            destinationForm.Close();
            await desktopService.MoveWindowAsync(identity, temporary);
            await desktopService.RemoveAsync(temporary, initial.CurrentId); temporary = Guid.Empty;
            await Task.Delay(200);
            Require(Native.IsWindow(form.Handle), "delete non-empty desktop preserves application");
            Require((await desktopService.GetMembershipAsync(form.Handle)).Id == initial.CurrentId, "desktop deletion migrates window to fallback");
            Require((await desktopService.GetSnapshotAsync()).Desktops.Count == initial.Desktops.Count, "original desktop count restored");
            Console.WriteLine("PASS integration suite");
        }
        finally
        {
            await desktopService.SwitchAsync(initial.CurrentId);
            if (Native.IsWindow(form.Handle))
            {
                await desktopService.MoveWindowAsync(identity, initial.CurrentId);
                await windowService.ApplyPlacementAsync(identity, before.RestoreBounds, before.State);
            }
            if (temporary != Guid.Empty && (await desktopService.GetSnapshotAsync()).Desktops.Any(d => d.Id == temporary))
                await desktopService.RemoveAsync(temporary, initial.CurrentId);
        }
    }
    private static async Task PreviewChecksAsync(Form sourceForm)
    {
        Require(Marshal.SizeOf<Native.THUMBNAIL_PROPERTIES>() == 45 &&
            Marshal.OffsetOf<Native.THUMBNAIL_PROPERTIES>(nameof(Native.THUMBNAIL_PROPERTIES.Visible)).ToInt32() == 37,
            "DWM native ABI uses packed 45-byte properties");
        using var desktops = new DesktopService();
        var windowService = new WindowService(desktops);
        var identity = WindowService.GetIdentity(sourceForm.Handle, out _);
        using var destination = new Form { Text = "裁剪与滚轮回归测试", Size = new Size(450, 410), Location = new Point(40, 40), StartPosition = FormStartPosition.Manual };
        destination.Show();
        var provider = new DwmPreviewProvider();
        var source = (await windowService.GetWindowAsync(identity))!;
        using (var preview = provider.Create(destination.Handle, source))
        {
            Require(preview.IsAvailable, "normal preview registration");
            var clip = new PixelRect(65, 140, 360, 230);
            for (int y = -80; y < 430; y += 7)
            {
                preview.Update(new(new(65, y, 360, 230), clip));
                foreach (var host in PreviewHosts(destination.Handle).Where(Native.IsWindowVisible))
                {
                    Native.GetWindowRect(host, out var nativeRect); var r = nativeRect.ToRect();
                    Require(r.X >= clip.X && r.Y >= clip.Y && r.Right <= clip.Right && r.Bottom <= clip.Bottom,
                        "clipped native host remains inside viewport", quiet: true);
                }
            }
            Require(PreviewHosts(destination.Handle).Count == 1, "scroll reuses one native host");
            preview.Update(new(new(65, 160, 360, 180), clip));
            int wheelDelta = 0; provider.MouseWheel += delta => wheelDelta += delta;
            Native.PostMessage(PreviewHosts(destination.Handle).Single(), 0x20A, unchecked((nuint)((uint)(ushort)-120 << 16)), 0);
            await Task.Delay(80);
            Require(wheelDelta == -120, "native preview forwards wheel to the list");
            preview.SetVisible(false); preview.SetVisible(true);
            Require(PreviewHosts(destination.Handle).All(Native.IsWindowVisible), "preview restores visibility without re-registration");
        }
        Require(PreviewHosts(destination.Handle).Count == 0, "normal preview releases native host");
        sourceForm.WindowState = FormWindowState.Minimized; await Task.Delay(150);
        source = (await windowService.GetWindowAsync(identity))!;
        Require(source.State == WindowShowState.Minimized, "test source minimized");
        var foreground = Native.GetForegroundWindow();
        using (var minimized = provider.Create(destination.Handle, source))
        {
            Require(minimized.IsAvailable, "minimized window accepts DWM preview");
            minimized.Update(new(new(65, 150, 350, 220), new(50, 100, 400, 300)));
            Require(PreviewHosts(destination.Handle).Any(Native.IsWindowVisible), "minimized thumbnail has visible native host");
            Require(Native.IsIconic(sourceForm.Handle) && Native.GetForegroundWindow() == foreground, "preview does not restore or activate minimized source");
        }
        Require(PreviewHosts(destination.Handle).Count == 0, "minimized preview releases native host");
        Console.WriteLine("PASS preview regression suite");
    }
    private static List<nint> PreviewHosts(nint owner)
    {
        var result = new List<nint>();
        Native.EnumWindows((hwnd, _) =>
        {
            var name = new System.Text.StringBuilder(100); Native.GetClassName(hwnd, name, name.Capacity);
            if (Native.GetWindow(hwnd, 4) == owner && name.ToString() == "WorkspaceManager.Preview") result.Add(hwnd);
            return true;
        }, 0);
        return result;
    }
    private static async Task CloseChecksAsync(Form controller)
    {
        using var desktops = new DesktopService();
        var service = new WindowService(desktops);
        using var target = TestWindow("关闭验证 · 普通窗口", Color.Navy);
        using var sibling = TestWindow("关闭验证 · 同应用其他窗口", Color.Teal);
        using var guarded = TestWindow("关闭验证 · 取消关闭", Color.Purple);
        target.Show(); sibling.Show(); guarded.Show();
        var targetId = WindowService.GetIdentity(target.Handle, out _);
        var siblingId = WindowService.GetIdentity(sibling.Handle, out _);
        var guardedId = WindowService.GetIdentity(guarded.Handle, out _);
        int closeAttempts = 0;
        FormClosingEventHandler cancel = (_, e) => { closeAttempts++; e.Cancel = true; };
        guarded.FormClosing += cancel;
        await service.RequestCloseAsync(targetId); await Task.Delay(150);
        Require(!Native.IsWindow(targetId.Handle), "normal WM_CLOSE closes the selected test window");
        Require(service.IsValid(siblingId) && Native.IsWindow(controller.Handle), "other windows in the same application remain open");
        await service.RequestCloseAsync(guardedId); await Task.Delay(150);
        Require(closeAttempts == 1 && service.IsValid(guardedId), "application can cancel the close request");
        bool rejected = false;
        try { await service.RequestCloseAsync(guardedId with { ProcessStarted = guardedId.ProcessStarted + 1 }); }
        catch (InvalidOperationException) { rejected = true; }
        await Task.Delay(50);
        Require(rejected && closeAttempts == 1, "stale identity never receives WM_CLOSE");
        guarded.FormClosing -= cancel;
        await service.RequestCloseAsync(guardedId); await Task.Delay(150);
        Require(!Native.IsWindow(guardedId.Handle), "application closes after its cancellation is removed");
        Console.WriteLine("PASS close-window regression suite");
    }
    private static void Require(bool passed, string label, bool quiet = false)
    {
        if (!passed) throw new InvalidOperationException("FAIL " + label);
        if (!quiet) Console.WriteLine("PASS " + label);
    }
    [DllImport("user32.dll")] private static extern uint GetGuiResources(nint process, uint flags);
}
