using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.System;
using WorkspaceManager.Core;
using WorkspaceManager.Windows;

namespace WorkspaceManager.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly DesktopService desktops = new();
    private readonly MonitorService monitors = new();
    private readonly WindowService windows;
    private readonly WindowMoveCoordinator mover;
    private readonly IWindowPreviewProvider previews = new DwmPreviewProvider();
    private readonly IWindowIconProvider icons = new WindowIconProvider();
    private readonly ShellHost shell;
    private AppSettings settings = AppSettings.Load();
    private DesktopSnapshot snapshot = new([], false);
    private IReadOnlyList<WindowInfo> windowList = [];
    private IReadOnlyList<MonitorInfo> monitorList = [];
    private readonly Dictionary<WindowIdentity, WindowCardView> previewCards = [];
    private readonly Dictionary<string, Grid> monitorCardGrids = [];
    private readonly Dictionary<string, Border> monitorDropPanels = [];
    private IWindowPreview? dragPreview;
    private SoftwareBitmap? transparentDragImage;
    private string? activeDropMonitor;
    private sealed class WindowCardView(WindowInfo window, long order)
    {
        public WindowInfo Window = window;
        public readonly long Order = order;
        public Border Card = null!;
        public Grid Element = null!;
        public IWindowPreview Preview = null!;
        public TextBlock Title = null!, Detail = null!, Status = null!;
        public StackPanel Fallback = null!;
        public Image Icon = null!;
        public Button CloseButton = null!;
        public bool PointerOver, ClosePressed;
        public string MenuSignature = "";
        public bool Disposed, PreviewStatusUpdateQueued;
    }
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer dragPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private Guid selectedDesktop, hoverDesktop;
    private WindowIdentity? draggedWindow;
    private bool shown, busy, refreshing, disposed, dragActive, dirty = true;
    private int overlayDepth, menuDepth;
    private bool renderingSubscribed, updatingPreviews;
    private bool modal => overlayDepth > 0;
    private long cardSequence;
    private string layoutSignature = "", desktopSignature = "";
    private double lastWidth;
    private int refreshTicks;
    public event Action? ExitRequested;

    public MainWindow(bool diagnostic = false)
    {
        Localization.Set(settings.Language);
        InitializeComponent();
        ApplyLanguage();
        SystemBackdrop = new MicaBackdrop();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = false; presenter.IsMaximizable = false;
        }
        windows = new(desktops); mover = new(desktops, windows, monitors);
        shell = new(WinRT.Interop.WindowNative.GetWindowHandle(this), !diagnostic);
        shell.ShowRequested += () => _ = ShowManagerAsync();
        shell.ToggleRequested += () => { if (shown) HideManager(); else _ = ShowManagerAsync(); };
        shell.HideRequested += HideManager;
        shell.SettingsRequested += async () => { await ShowManagerAsync(); await OpenSettingsAsync(); };
        shell.ExitRequested += () => ExitRequested?.Invoke();
        shell.Changed += () => DispatcherQueue.TryEnqueue(() => dirty = true);
        refreshTimer.Tick += async (_, _) => { if (shown && !busy && !modal && menuDepth == 0 && !dragActive && (dirty || ++refreshTicks % 2 == 0)) await RefreshAsync(); };
        previews.MouseWheel += ScrollList;
        dragPreviewTimer.Tick += (_, _) => UpdateDragPreview();
        ContentScroll.DragOver += OnMonitorDragOver;
        ContentScroll.Drop += OnMonitorDrop;
        ContentScroll.DragLeave += (_, e) =>
        {
            if (FindDropMonitor(e.GetPosition(ContentScroll)) is null) SetDropMonitor(null);
        };
        ContentScroll.SizeChanged += (_, _) => MonitorPanels.MinHeight = Math.Max(0, ContentScroll.ActualHeight);
        noticeTimer.Tick += (_, _) => { noticeTimer.Stop(); Notice.IsOpen = false; };
        Root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        Root.LayoutUpdated += (_, _) => UpdatePreviews();
        hoverTimer.Tick += (_, _) =>
        {
            hoverTimer.Stop();
            if (dragActive && selectedDesktop != hoverDesktop) { selectedDesktop = hoverDesktop; Render(); }
        };
        Root.SizeChanged += (_, _) =>
        {
            if (Math.Abs(Root.ActualWidth - lastWidth) > 30 && shown && !dragActive && !modal && menuDepth == 0)
            { lastWidth = Root.ActualWidth; Render(); }
        };
        ApplyTheme();
        if (!diagnostic && !shell.RegisterHotkey(settings)) Inform(Localization.T("快捷键被其他应用占用，请在设置中修改；托盘入口仍可使用。", "The shortcut is already in use. Change it in Settings; the tray remains available."), InfoBarSeverity.Warning);
        ShortcutHint.Text = $"{settings.HotkeyLabel} {Localization.T("呼出 / 收起", "show / hide")}";
    }
    public async Task ValidateUiWithoutShowingAsync()
    {
        // Build the real XAML card tree and native preview registrations without showing a window,
        // registering a hotkey, synthesizing input or changing any external window.
        snapshot = await desktops.GetSnapshotAsync(); monitorList = monitors.GetMonitors();
        windowList = await windows.GetWindowsAsync(); selectedDesktop = snapshot.CurrentId;
        Render(); Root.Measure(new Size(1600, 1000)); Root.Arrange(new Rect(0, 0, 1600, 1000));
        int available = previewCards.Values.Count(p => p.Preview.IsAvailable);
        bool incrementalRefresh = ValidateIncrementalRefresh();
        var originalTheme = Root.RequestedTheme;
        Root.RequestedTheme = ElementTheme.Light; Root.UpdateLayout(); await Task.Delay(30);
        var lightPanelColor = ((MonitorPanels.Children.FirstOrDefault() as Border)?.Background as SolidColorBrush)?.Color.ToString();
        Root.RequestedTheme = ElementTheme.Dark; Root.UpdateLayout(); await Task.Delay(30);
        var darkPanelColor = ((MonitorPanels.Children.FirstOrDefault() as Border)?.Background as SolidColorBrush)?.Color.ToString();
        bool themeChanges = lightPanelColor != darkPanelColor;
        Root.RequestedTheme = originalTheme;
        if (monitorList.Count > 0 && !themeChanges) throw new InvalidOperationException("Light/dark theme brushes did not update.");
        await Task.Delay(500); // Let asynchronous icon decoding finish before resource accounting.
        uint handlesBefore = ShellHost.GetUserHandleCount(), handlesAfter = handlesBefore;
        int lifecycleIterations = 0;
        var candidate = windowList.FirstOrDefault(w => w.State != WindowShowState.Minimized && !w.IsProtected);
        if (candidate is not null)
        {
            for (int i = 0; i < 100; i++)
            {
                using var preview = previews.Create(shell.Handle, candidate);
                if (!preview.IsAvailable) throw new InvalidOperationException("DWM preview lifecycle initialization failed.");
                lifecycleIterations++;
            }
            handlesAfter = ShellHost.GetUserHandleCount();
            if (handlesAfter > handlesBefore + 2) throw new InvalidOperationException("DWM preview USER handle count grew across create/dispose cycles.");
        }
        Directory.CreateDirectory(AppLog.DataDirectory);
        File.WriteAllText(Path.Combine(AppLog.DataDirectory, "ui-smoke-test.json"), System.Text.Json.JsonSerializer.Serialize(new {
            Success = true, DesktopAvailable = snapshot.IsAvailable, DesktopCards = DesktopStrip.Children.Count,
            MonitorPanels = MonitorPanels.Children.Count, WindowCards = previewCards.Count, DwmRegistrations = available,
            LifecycleIterations = lifecycleIterations, UserHandlesBefore = handlesBefore, UserHandlesAfter = handlesAfter,
            ThemeBrushesChange = themeChanges, IncrementalRefreshPreservesPreviews = incrementalRefresh,
            VisibleUiTested = false, Timestamp = DateTimeOffset.Now
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private bool ValidateIncrementalRefresh()
    {
        var originalWindows = windowList;
        var originalCards = previewCards.ToDictionary(p => p.Key, p => (View: p.Value, Preview: p.Value.Preview));
        var originalPanels = MonitorPanels.Children.ToArray();
        // A changing title and changing EnumWindows Z-order must not rebuild any card.
        windowList = windowList.Reverse().Select(w => w with { Title = w.Title + " [refresh test]" }).ToArray();
        Render();
        bool stable = originalCards.All(p => previewCards.TryGetValue(p.Key, out var current) &&
            ReferenceEquals(current, p.Value.View) && ReferenceEquals(current.Preview, p.Value.Preview)) &&
            originalPanels.SequenceEqual(MonitorPanels.Children);
        if (originalCards.Count > 0 && monitorList.Count > 1)
        {
            var moved = originalCards.Keys.First();
            string target = monitorList.First(m => m.Id != previewCards[moved].Window.MonitorId).Id;
            windowList = windowList.Select(w => w.Identity == moved ? w with { MonitorId = target } : w).ToArray(); Render();
            stable &= originalPanels.SequenceEqual(MonitorPanels.Children) && originalCards.All(p =>
                ReferenceEquals(previewCards[p.Key].Preview, p.Value.Preview) && ReferenceEquals(previewCards[p.Key], p.Value.View));
        }
        // Removing one application must release only that application's preview.
        if (originalCards.Count > 1)
        {
            var removed = originalCards.Keys.First();
            windowList = windowList.Where(w => w.Identity != removed).ToArray(); Render();
            stable &= !previewCards.ContainsKey(removed) && originalCards[removed].View.Disposed &&
                originalCards.Where(p => p.Key != removed).All(p => ReferenceEquals(previewCards[p.Key].Preview, p.Value.Preview));
        }
        windowList = originalWindows; Render();
        if (!stable) throw new InvalidOperationException("Refreshing one window recreated unrelated cards or preview resources.");
        return stable;
    }

    public async Task ValidateVisibleInteractionsAsync()
    {
        // Explicit diagnostic mode: exercises this application's own surfaces only.
        Activate(); await ShowManagerAsync(); refreshTimer.Stop(); await Task.Delay(250);
        var visible = previewCards.Values.Where(v => v.Preview.IsVisible).ToArray();
        if (visible.Length == 0) throw new InvalidOperationException("No visible previews available for interaction regression.");
        void CheckVisible(string operation)
        {
            if (visible.Any(v => !v.Preview.IsAvailable || !v.Preview.IsVisible))
                throw new InvalidOperationException(operation + " hid or invalidated a visible preview.");
        }
        var hoverCard = visible[0];
        hoverCard.PointerOver = false; UpdateCloseButton(hoverCard);
        bool closeButtonHover = hoverCard.CloseButton.Opacity == 0 && !hoverCard.CloseButton.IsHitTestVisible;
        hoverCard.PointerOver = true; UpdateCloseButton(hoverCard);
        closeButtonHover &= hoverCard.CloseButton.Opacity == 1 && hoverCard.CloseButton.IsHitTestVisible;
        var closePosition = hoverCard.CloseButton.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        var previewPosition = hoverCard.Element.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        bool closeAbovePreview = closePosition.Y + hoverCard.CloseButton.ActualHeight <= previewPosition.Y;
        hoverCard.PointerOver = false; UpdateCloseButton(hoverCard);
        if (!closeButtonHover || !closeAbovePreview) throw new InvalidOperationException("Close button hover visibility or native-preview separation failed.");
        bool blankAreaTargets = true;
        for (int index = 0; index < monitorList.Count; index++)
        {
            var point = new Point(ContentScroll.ActualWidth * (index + 0.5) / monitorList.Count, ContentScroll.ActualHeight - 2);
            blankAreaTargets &= FindDropMonitor(point)?.Id == monitorList[index].Id;
        }
        if (!blankAreaTargets) throw new InvalidOperationException("Blank monitor-column area was not a drop target.");
        if (monitorDropPanels.Values.Any(p => p.ActualHeight + 14 < ContentScroll.ActualHeight))
            throw new InvalidOperationException("Monitor background does not cover its viewport.");
        dragActive = true; StartDragPreview(visible[0].Window); UpdatePreviews(); CheckVisible("drag");
        await Task.Delay(100);
        bool liveDragPreview = dragPreview?.IsAvailable == true && dragPreview.IsVisible;
        if (!liveDragPreview) throw new InvalidOperationException("Live drag preview did not render in its own DWM host.");
        EndDragPreview(); dragActive = false;
        CheckVisible("drag completion");
        var menu = (MenuFlyout)visible[0].Card.ContextFlyout;
        menu.ShowAt(visible[0].Card); await Task.Delay(200);
        CheckVisible("context menu");
        bool windowedMenu = !menu.IsConstrainedToRootBounds;
        if (!windowedMenu) throw new InvalidOperationException("Context menu is not using a separate popup window.");
        menu.Hide(); await Task.Delay(100);
        var dialog = new ContentDialog { Title = "预览持续显示回归测试", Content = new TextBox { Text = "只测试管理器，不操作外部窗口", MinWidth = 340 }, CloseButtonText = "关闭" };
        var dialogTask = DialogAsync(dialog); await Task.Delay(250);
        CheckVisible("owned dialog"); dialog.Hide(); await dialogTask;
        CheckVisible("dialog close");
        Inform("提示自动关闭测试", InfoBarSeverity.Success); await Task.Delay(150);
        if (Notice.IsOpen) throw new InvalidOperationException("Non-error notice was shown.");
        Inform("错误保持显示测试", InfoBarSeverity.Error); await Task.Delay(500);
        if (!Notice.IsOpen) throw new InvalidOperationException("Error notice disappeared unexpectedly.");
        Notice.IsOpen = false;
        Directory.CreateDirectory(AppLog.DataDirectory);
        File.WriteAllText(Path.Combine(AppLog.DataDirectory, "ui-interaction-test.json"), System.Text.Json.JsonSerializer.Serialize(new {
            Success = true, VisiblePreviews = visible.Length, PreviewsRetainedDuringDrag = true,
            PreviewsRetainedDuringMenu = true, WindowedMenu = windowedMenu, PreviewsRetainedDuringDialog = true,
            LiveDragPreview = liveDragPreview, BlankMonitorAreaAcceptsDrop = blankAreaTargets,
            CloseButtonHover = closeButtonHover, CloseButtonOutsideNativePreview = closeAbovePreview,
            ColumnsPerMonitor = monitorCardGrids.Values.Select(g => g.ColumnDefinitions.Count).ToArray(),
            NonErrorNoticeSuppressed = true, ErrorNoticePersists = true, Timestamp = DateTimeOffset.Now
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        HideManager();
    }

    public async Task ShowManagerAsync()
    {
        if (disposed) return;
        try
        {
            var current = await desktops.GetSnapshotAsync();
            if (current.IsAvailable)
            {
                // Move our own window before showing; invoking the hotkey must not switch the user's desktop.
                try { shell.MoveOwnWindowToDesktop(current.CurrentId); }
                catch (Exception ex) { AppLog.Write("move-manager", ex); Inform("管理器未能跟随当前桌面，请收起后重新呼出。", InfoBarSeverity.Warning); }
                selectedDesktop = current.CurrentId;
            }
            shown = true; shell.Show(monitors.GetCursorMonitor());
            Root.Focus(FocusState.Programmatic);
            await RefreshAsync(true);
            refreshTimer.Start(); StartPreviewUpdates();
        }
        catch (Exception ex) { shown = true; shell.Show(monitors.GetCursorMonitor()); Inform(ex.Message, InfoBarSeverity.Warning); AppLog.Write("show", ex); }
    }
    private void HideManager()
    {
        if (modal) return;
        dragActive = false; draggedWindow = null; hoverTimer.Stop();
        EndDragPreview();
        shown = false; refreshTimer.Stop(); StopPreviewUpdates(); ClearPreviews(); shell.Hide();
    }
    private async Task RefreshAsync(bool force = false)
    {
        if (refreshing || disposed || (dragActive && !force)) return;
        refreshing = true;
        try
        {
            var next = await desktops.GetSnapshotAsync();
            var nextMonitors = monitors.GetMonitors();
            var nextWindows = await windows.GetWindowsAsync();
            if (disposed || !shown) return;
            if (modal || menuDepth > 0 || dragActive) { dirty = true; return; }
            snapshot = next; monitorList = nextMonitors; windowList = nextWindows;
            if (!snapshot.Desktops.Any(d => d.Id == selectedDesktop)) selectedDesktop = snapshot.CurrentId;
            Render();
            dirty = false;
            if (!snapshot.IsAvailable) Inform(snapshot.UnavailableReason ?? "桌面功能暂不可用。", InfoBarSeverity.Warning);
        }
        catch (Exception ex) { Inform(ex.Message, InfoBarSeverity.Error); AppLog.Write("refresh", ex); }
        finally { refreshing = false; }
    }

    private static SolidColorBrush ColorBrush(byte r, byte g, byte b, byte a = 255) => new(global::Windows.UI.Color.FromArgb(a, r, g, b));
    private object Resource(string key) => Application.Current.Resources[key];
    private Brush Brush(string key) => (Brush)Resource(key);
    private Style LocalStyle(string key) => (Style)Root.Resources[key];
    private void Render()
    {
        if (disposed) return;
        CreateButton.IsEnabled = snapshot.IsAvailable && !busy;
        EnterButton.IsEnabled = snapshot.IsAvailable && !busy;
        var viewed = snapshot.Desktops.FirstOrDefault(d => d.Id == selectedDesktop);
        Subtitle.Text = snapshot.IsAvailable
            ? T($"正在查看 {viewed?.Name}  ·  {monitorList.Count} 个显示器  ·  {VisibleWindows().Count()} 个窗口",
                $"Viewing {viewed?.Name}  ·  {monitorList.Count} monitor" + (monitorList.Count == 1 ? "" : "s") + $"  ·  {VisibleWindows().Count()} window" + (VisibleWindows().Count() == 1 ? "" : "s"))
            : T($"显示器管理  ·  {windowList.Count} 个窗口",
                $"Monitor management  ·  {windowList.Count} window" + (windowList.Count == 1 ? "" : "s"));
        string nextDesktopSignature = selectedDesktop + ":" + snapshot.IsAvailable + ":" +
            string.Join("|", snapshot.Desktops.Select(d => $"{d.Id}:{d.Name}:{d.IsCurrent}:{windowList.Count(w => w.DesktopId == d.Id || w.IsPinned)}"));
        if (desktopSignature != nextDesktopSignature)
        {
        desktopSignature = nextDesktopSignature;
        DesktopStrip.Children.Clear();
        foreach (var desktop in snapshot.Desktops)
        {
            int count = windowList.Count(w => w.DesktopId == desktop.Id || w.IsPinned);
            var title = new TextBlock { Text = desktop.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, MaxWidth = 210, TextTrimming = TextTrimming.CharacterEllipsis };
            var caption = new TextBlock { Text = WindowCount(count) + (desktop.IsCurrent ? T("  ·  当前所在", "  ·  current") : ""), FontSize = 12, Style = LocalStyle("SecondaryTextStyle") };
            var stack = new StackPanel { Spacing = 7 }; stack.Children.Add(title); stack.Children.Add(caption);
            var button = new Button { Content = stack, Padding = new Thickness(18, 13, 18, 13), MinWidth = 164, Height = 80,
                BorderThickness = new Thickness(desktop.Id == selectedDesktop ? 2 : 1), AllowDrop = true, CornerRadius = new CornerRadius(10) };
            if (desktop.Id == selectedDesktop) button.BorderBrush = Brush("SystemControlHighlightAccentBrush");
            AutomationProperties.SetName(button, T($"查看{desktop.Name}", $"View {desktop.Name}"));
            button.Click += (_, _) => { selectedDesktop = desktop.Id; Render(); };
            button.DragOver += (_, e) =>
            {
                SetDropMonitor(null);
                AcceptDrag(e, T($"移动到 {desktop.Name}", $"Move to {desktop.Name}"));
                if (draggedWindow is not null && (!hoverTimer.IsEnabled || hoverDesktop != desktop.Id))
                { hoverDesktop = desktop.Id; hoverTimer.Stop(); hoverTimer.Start(); }
            };
            button.DragLeave += (_, _) => hoverTimer.Stop();
            button.Drop += async (_, e) => { e.Handled = true; hoverTimer.Stop(); await DropAsync(desktop.Id, null); };
            var menu = new MenuFlyout();
            var rename = new MenuFlyoutItem { Text = T("重命名", "Rename") }; rename.Click += async (_, _) => await RenameDesktopAsync(desktop);
            var remove = new MenuFlyoutItem { Text = T("删除桌面…", "Delete desktop…"), IsEnabled = snapshot.Desktops.Count > 1 }; remove.Click += async (_, _) => await RemoveDesktopAsync(desktop);
            menu.Items.Add(rename); menu.Items.Add(remove); ConfigureMenu(menu); button.ContextFlyout = menu;
            DesktopStrip.Children.Add(button);
        }
        if (!snapshot.IsAvailable) DesktopStrip.Children.Add(new TextBlock { Text = T("虚拟桌面暂不可用", "Virtual desktops are unavailable"), Margin = new Thickness(0, 24, 0, 24) });
        }
        var desired = VisibleWindows().OrderBy(w => previewCards.TryGetValue(w.Identity, out var card) ? card.Order : long.MaxValue).ToArray();
        var identities = desired.Select(w => w.Identity).ToHashSet();
        foreach (var identity in previewCards.Keys.Where(id => !identities.Contains(id)).ToArray())
        {
            var card = previewCards[identity]; card.Disposed = true; card.Preview.Dispose(); previewCards.Remove(identity);
        }
        foreach (var window in desired)
        {
            if (!previewCards.TryGetValue(window.Identity, out var card))
            {
                card = BuildCard(window); previewCards.Add(window.Identity, card);
            }
            UpdateCard(card, window);
        }
        double available = Math.Max(300, (Root.ActualWidth > 0 ? Root.ActualWidth : 1280) - 108);
        int columns = MonitorPanelLayout.ColumnCount(available / Math.Max(1, monitorList.Count));
        string nextLayoutSignature = columns + ":" +
            string.Join("|", monitorList.Select(m => $"{m.Id}:{m.Bounds}:{m.WorkArea}:{m.Dpi}:{m.IsPrimary}"));
        if (layoutSignature != nextLayoutSignature)
        {
            layoutSignature = nextLayoutSignature;
            foreach (var card in previewCards.Values)
                if (card.Card.Parent is Panel parent) parent.Children.Remove(card.Card);
            MonitorPanels.Children.Clear(); MonitorPanels.ColumnDefinitions.Clear(); monitorCardGrids.Clear(); monitorDropPanels.Clear(); activeDropMonitor = null;
            foreach (var monitor in monitorList)
            {
                MonitorPanels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var panel = BuildMonitor(monitor, available / Math.Max(1, monitorList.Count));
                Grid.SetColumn(panel, MonitorPanels.ColumnDefinitions.Count - 1); MonitorPanels.Children.Add(panel);
            }
        }
        UpdateMonitorCards();
    }
    private IEnumerable<WindowInfo> VisibleWindows() => windowList.Where(w => !snapshot.IsAvailable || w.DesktopId == selectedDesktop || w.IsPinned);
    private Border BuildMonitor(MonitorInfo monitor, double availableWidth)
    {
        var stack = new StackPanel { Spacing = 18 };
        var heading = new StackPanel { Spacing = 5 };
        heading.Children.Add(new TextBlock { Text = monitor.Name + (monitor.IsPrimary ? T("  ·  主屏幕", "  ·  primary") : ""), FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = $"{monitor.Bounds.Width} × {monitor.Bounds.Height}  ·  {monitor.Dpi * 100 / 96}% {T("缩放", "scale")}", FontSize = 12, Style = LocalStyle("SecondaryTextStyle") });
        stack.Children.Add(heading);
        int columns = MonitorPanelLayout.ColumnCount(availableWidth);
        var cards = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        for (int col = 0; col < columns; col++) cards.ColumnDefinitions.Add(new ColumnDefinition());
        monitorCardGrids.Add(monitor.Id, cards);
        stack.Children.Add(cards);
        var panel = new Border { Child = stack, Style = LocalStyle("MonitorPanelStyle"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(18), VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 260 };
        monitorDropPanels.Add(monitor.Id, panel);
        return panel;
    }
    private MonitorInfo? FindDropMonitor(Point point)
    {
        int index = MonitorPanelLayout.HitTest(point.X, point.Y, ContentScroll.ActualWidth, ContentScroll.ActualHeight, monitorList.Count);
        return index < 0 ? null : monitorList[index];
    }
    private void SetDropMonitor(string? id)
    {
        if (activeDropMonitor == id) return;
        if (activeDropMonitor is not null && monitorDropPanels.TryGetValue(activeDropMonitor, out var previous)) previous.ClearValue(Border.BorderBrushProperty);
        activeDropMonitor = id;
        if (id is not null && monitorDropPanels.TryGetValue(id, out var current)) current.BorderBrush = Brush("SystemControlHighlightAccentBrush");
    }
    private void OnMonitorDragOver(object sender, DragEventArgs e)
    {
        var monitor = FindDropMonitor(e.GetPosition(ContentScroll));
        if (monitor is null || draggedWindow is null || busy) { e.AcceptedOperation = DataPackageOperation.None; SetDropMonitor(null); return; }
        hoverTimer.Stop(); SetDropMonitor(monitor.Id);
        AcceptDrag(e, T($"移动到 {monitor.Name}", $"Move to {monitor.Name}"));
    }
    private async void OnMonitorDrop(object sender, DragEventArgs e)
    {
        var monitor = FindDropMonitor(e.GetPosition(ContentScroll));
        if (monitor is null || draggedWindow is null || busy) return;
        e.Handled = true; e.AcceptedOperation = DataPackageOperation.Move;
        SetDropMonitor(null); await DropAsync(selectedDesktop, monitor.Id);
    }
    private void UpdateMonitorCards()
    {
        foreach (var (monitorId, grid) in monitorCardGrids)
        {
            var desired = previewCards.Values.Where(c => c.Window.MonitorId == monitorId).OrderBy(c => c.Order).Select(c => c.Card).ToArray();
            foreach (var child in grid.Children.ToArray())
                if (child is not Border border || !desired.Contains(border)) grid.Children.Remove(child);
            int columns = grid.ColumnDefinitions.Count, rows = (desired.Length + columns - 1) / columns;
            while (grid.RowDefinitions.Count > rows) grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
            while (grid.RowDefinitions.Count < rows) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < desired.Length; i++)
            {
                var card = desired[i];
                Grid.SetRow(card, i / columns); Grid.SetColumn(card, i % columns);
                if (card.Parent == grid) continue;
                if (card.Parent is Panel previous) previous.Children.Remove(card);
                grid.Children.Add(card);
            }
            if (desired.Length == 0) grid.Children.Add(new TextBlock { Text = T("屏幕已留空\n将窗口拖到这里", "This monitor is empty\nDrop a window here"), TextAlignment = TextAlignment.Center,
                Margin = new Thickness(12, 72, 12, 72), Style = LocalStyle("SecondaryTextStyle"), FontSize = 15 });
        }
    }
    private WindowCardView BuildCard(WindowInfo window)
    {
        var view = new WindowCardView(window, ++cardSequence);
        var preview = new Grid { Height = 170, Background = ColorBrush(26, 30, 38), CornerRadius = new CornerRadius(8) };
        var fallback = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12 };
        fallback.Children.Add(new FontIcon { Glyph = "\uE737", FontSize = 30, Foreground = ColorBrush(161, 197, 221) });
        var previewHandle = previews.Create(shell.Handle, window);
        var previewStatus = new TextBlock { Text = previewHandle.UnavailableReason ?? "", Foreground = ColorBrush(187, 193, 205), FontSize = 12 };
        fallback.Children.Add(previewStatus);
        preview.Children.Add(fallback);
        view.Element = preview; view.Preview = previewHandle; view.Status = previewStatus; view.Fallback = fallback;
        var iconImage = new Image { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        view.Icon = iconImage;
        var title = new TextBlock { Text = window.Title, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var titleRow = new Grid { ColumnSpacing = 8 };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); titleRow.ColumnDefinitions.Add(new ColumnDefinition());
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.VerticalAlignment = VerticalAlignment.Center;
        titleRow.Children.Add(iconImage); Grid.SetColumn(title, 1); titleRow.Children.Add(title);
        var close = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 11 }, Width = 28, Height = 28,
            Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = ColorBrush(0, 0, 0, 0),
            CornerRadius = new CornerRadius(5), Opacity = 0, IsHitTestVisible = false };
        close.Resources["ButtonBackgroundPointerOver"] = ColorBrush(196, 43, 28);
        close.Resources["ButtonForegroundPointerOver"] = ColorBrush(255, 255, 255);
        close.Resources["ButtonBackgroundPressed"] = ColorBrush(161, 33, 24);
        close.Resources["ButtonForegroundPressed"] = ColorBrush(255, 255, 255);
        AutomationProperties.SetName(close, T($"关闭 {window.Title}", $"Close {window.Title}")); ToolTipService.SetToolTip(close, T("关闭窗口", "Close window"));
        view.CloseButton = close; Grid.SetColumn(close, 2); titleRow.Children.Add(close);
        close.Click += async (_, _) => { view.ClosePressed = false; if (!dragActive && !busy) await CloseWindowAsync(view.Window); };
        close.Tapped += (_, e) => e.Handled = true;
        close.PointerPressed += (_, _) => view.ClosePressed = true;
        close.PointerReleased += (_, _) => view.ClosePressed = false;
        close.PointerCanceled += (_, _) => view.ClosePressed = false;
        close.GotFocus += (_, _) => UpdateCloseButton(view);
        close.LostFocus += (_, _) => UpdateCloseButton(view);
        var detail = new TextBlock { Text = window.ProcessName + (window.IsPinned ? T("  ·  所有桌面", "  ·  all desktops") : "") + (window.IsCloaked ? T("  ·  后台预览可能暂停", "  ·  background preview may pause") : ""),
            FontSize = 12, Style = LocalStyle("SecondaryTextStyle"), TextTrimming = TextTrimming.CharacterEllipsis };
        // Keep the close affordance in the header, outside the native DWM image.
        var content = new StackPanel { Spacing = 9 }; content.Children.Add(titleRow); content.Children.Add(preview); content.Children.Add(detail);
        view.Title = title; view.Detail = detail;
        _ = LoadIconAsync(view, iconImage);
        var card = new Border { Child = content, Padding = new Thickness(10), Style = LocalStyle("WindowCardStyle"), CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1), CanDrag = window.CanIdentifyProcess };
        view.Card = card;
        AutomationProperties.SetName(card, window.Title);
        ToolTipService.SetToolTip(card, window.Title);
        card.PointerEntered += (_, _) => { view.PointerOver = true; UpdateCloseButton(view); };
        card.PointerExited += (_, e) =>
        {
            var point = e.GetCurrentPoint(card).Position;
            view.PointerOver = point.X >= 0 && point.Y >= 0 && point.X < card.ActualWidth && point.Y < card.ActualHeight;
            UpdateCloseButton(view);
        };
        card.Tapped += async (_, e) => { if (!dragActive && !busy) await ActivateWindowAsync(view.Window); e.Handled = true; };
        card.DragStarting += (_, e) =>
        {
            if (busy || view.ClosePressed) { e.Cancel = true; return; }
            draggedWindow = view.Window.Identity; dragActive = true;
            e.Data.RequestedOperation = DataPackageOperation.Move;
            e.Data.SetText("WorkspaceManager.Window");
            StartDragPreview(view.Window);
            if (dragPreview?.IsAvailable == true)
            {
                // The default XAML drag snapshot contains no native DWM pixels.
                // Replace it with transparent content and draw a live native thumbnail.
                transparentDragImage = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 1, 1, BitmapAlphaMode.Premultiplied);
                using (var writer = new global::Windows.Storage.Streams.DataWriter())
                { writer.WriteUInt32(0); transparentDragImage.CopyFromBuffer(writer.DetachBuffer()); }
                e.DragUI.SetContentFromSoftwareBitmap(transparentDragImage, new Point(0, 0));
            }
            else if (view.Icon.Source is BitmapImage icon) e.DragUI.SetContentFromBitmapImage(icon);
            else e.DragUI.SetContentFromDataPackage();
            Hint.Text = T($"正在移动：{view.Window.Title}  ·  拖到桌面或显示器，Esc 取消", $"Moving {view.Window.Title}  ·  drop on a desktop or monitor, Esc to cancel");
        };
        card.DropCompleted += async (_, _) =>
        {
            hoverTimer.Stop(); dragActive = false; draggedWindow = null;
            EndDragPreview();
            Hint.Text = T("拖动窗口到显示器或上方桌面 · 悬停桌面可展开跨屏目标 · 右键查看更多", "Drag a window to a monitor or desktop · hover a desktop for cross-monitor targets · right-click for more");
            await RefreshAsync(true);
        };
        return view;
    }
    private static void UpdateCloseButton(WindowCardView view)
    {
        bool visible = view.PointerOver || view.CloseButton.FocusState == FocusState.Keyboard;
        view.CloseButton.Opacity = visible ? 1 : 0;
        view.CloseButton.IsHitTestVisible = visible;
    }
    private async Task CloseWindowAsync(WindowInfo window)
    {
        await ExecuteAsync(async () =>
        {
            await windows.RequestCloseAsync(window.Identity);
            AppLog.Write("close-window", result: "requested");
            await Task.Delay(180);
            bool pending = windows.IsValid(window.Identity);
            Inform(pending ? T("已发送关闭请求；若应用有保存提示，请点击该窗口处理。", "Close requested; handle any save prompt in the application.") : T("窗口已关闭。", "The window was closed."),
                pending ? InfoBarSeverity.Informational : InfoBarSeverity.Success);
        });
    }
    private void UpdateCard(WindowCardView view, WindowInfo window)
    {
        if (view.Window.IsProtected != window.IsProtected)
        {
            view.Preview.Dispose(); view.Preview = previews.Create(shell.Handle, window);
        }
        view.Window = window;
        if (view.Title.Text != window.Title)
        {
            view.Title.Text = window.Title; AutomationProperties.SetName(view.Card, window.Title);
            AutomationProperties.SetName(view.CloseButton, T($"关闭 {window.Title}", $"Close {window.Title}"));
            ToolTipService.SetToolTip(view.Card, window.Title);
        }
        view.Detail.Text = window.ProcessName + (window.IsPinned ? T("  ·  所有桌面", "  ·  all desktops") : "") +
            (window.State == WindowShowState.Minimized ? T("  ·  已最小化 · 保留画面可能暂停", "  ·  minimized · cached frame may pause") : window.IsCloaked ? T("  ·  后台预览可能暂停", "  ·  background preview may pause") : "");
        view.Card.CanDrag = window.CanIdentifyProcess;
        view.CloseButton.IsEnabled = window.CanIdentifyProcess;
        UpdatePreviewStatus(view);
        string menuSignature = $"{snapshot.IsAvailable}:{window.IsPinned}:{window.CanIdentifyProcess}:{window.DesktopId}:" +
            string.Join("|", snapshot.Desktops.Select(d => $"{d.Id}:{d.Name}")) + ":" + string.Join("|", monitorList.Select(m => $"{m.Id}:{m.Name}"));
        if (view.MenuSignature != menuSignature) { view.MenuSignature = menuSignature; view.Card.ContextFlyout = BuildWindowMenu(view); }
    }
    private MenuFlyout BuildWindowMenu(WindowCardView view)
    {
        var window = view.Window;
        var menu = new MenuFlyout();
        var activate = new MenuFlyoutItem { Text = T("打开窗口", "Open window") }; activate.Click += async (_, _) => await ActivateWindowAsync(window); menu.Items.Add(activate);
        if (!window.IsPinned && snapshot.IsAvailable)
        {
            foreach (var desktop in snapshot.Desktops)
            {
                var submenu = new MenuFlyoutSubItem { Text = T($"移动到 {desktop.Name}", $"Move to {desktop.Name}") };
                var same = new MenuFlyoutItem { Text = T("保留当前显示器", "Keep current monitor"), IsEnabled = window.CanIdentifyProcess };
                same.Click += async (_, _) => await MoveAsync(new(window.Identity, desktop.Id)); submenu.Items.Add(same);
                foreach (var monitor in monitorList)
                {
                    var item = new MenuFlyoutItem { Text = monitor.Name, IsEnabled = window.CanIdentifyProcess };
                    item.Click += async (_, _) => await MoveAsync(new(window.Identity, desktop.Id, monitor.Id)); submenu.Items.Add(item);
                }
                menu.Items.Add(submenu);
            }
        }
        else
        {
            foreach (var monitor in monitorList)
            {
                var item = new MenuFlyoutItem { Text = T($"移动到 {monitor.Name}", $"Move to {monitor.Name}"), IsEnabled = window.CanIdentifyProcess };
                item.Click += async (_, _) => await MoveAsync(new(window.Identity, window.DesktopId, monitor.Id)); menu.Items.Add(item);
            }
        }
        ConfigureMenu(menu);
        return menu;
    }
    private async Task LoadIconAsync(WindowCardView view, Image target)
    {
        try
        {
            var bytes = await icons.GetIconAsync(view.Window);
            if (bytes is null || disposed || view.Disposed) return;
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            if (view.Disposed) return;
            target.Source = bitmap;
            view.Fallback.Children[0] = new Image { Source = bitmap, Width = 36, Height = 36 };
        }
        catch (Exception ex) { AppLog.Write("window-icon", ex); }
    }
    private void ConfigureMenu(MenuFlyout menu)
    {
        // Windowed flyouts render above native preview hosts without hiding every card.
        menu.ShouldConstrainToRootBounds = false;
        bool opened = false;
        menu.Opening += (_, _) => { if (!opened) { opened = true; menuDepth++; } };
        menu.Closed += (_, _) => { if (opened) { opened = false; menuDepth = Math.Max(0, menuDepth - 1); } UpdatePreviews(); };
    }
    private void AcceptDrag(DragEventArgs e, string caption)
    {
        if (draggedWindow is null || busy) { e.AcceptedOperation = DataPackageOperation.None; return; }
        e.AcceptedOperation = DataPackageOperation.Move; e.DragUIOverride.Caption = caption;
        if (dragPreview?.IsAvailable == true) e.DragUIOverride.IsContentVisible = false;
        UpdateDragPreview(); e.Handled = true;
    }
    private void StartDragPreview(WindowInfo window)
    {
        EndDragPreview();
        dragPreview = previews.Create(shell.Handle, window);
        UpdateDragPreview(); dragPreviewTimer.Start();
    }
    private void UpdateDragPreview()
    {
        if (!shown || !dragActive || dragPreview is null || monitorList.Count == 0) return;
        var cursor = shell.GetCursorScreenPosition();
        var pointerMonitor = monitorList.FirstOrDefault(m => cursor.X >= m.Bounds.X && cursor.X < m.Bounds.Right && cursor.Y >= m.Bounds.Y && cursor.Y < m.Bounds.Bottom) ?? monitorList[0];
        double scale = pointerMonitor.Dpi / 96.0;
        var bounds = new PixelRect(cursor.X + (int)(18 * scale), cursor.Y + (int)(24 * scale), (int)(280 * scale), (int)(180 * scale));
        var desktopBounds = pointerMonitor.WorkArea;
        // Shift back at the outer screen edges without moving or activating the source.
        bounds = bounds with { X = Math.Clamp(bounds.X, desktopBounds.X, Math.Max(desktopBounds.X, desktopBounds.Right - bounds.Width)),
            Y = Math.Clamp(bounds.Y, desktopBounds.Y, Math.Max(desktopBounds.Y, desktopBounds.Bottom - bounds.Height)) };
        dragPreview.Update(new(bounds, desktopBounds));
    }
    private void EndDragPreview()
    {
        dragPreviewTimer.Stop(); dragPreview?.Dispose(); dragPreview = null;
        transparentDragImage?.Dispose(); transparentDragImage = null;
        SetDropMonitor(null);
    }
    private async Task DropAsync(Guid desktop, string? monitor)
    {
        if (draggedWindow is not { } identity) return;
        var original = windowList.FirstOrDefault(w => w.Identity == identity);
        if (original is not null && monitor is not null && (original.IsPinned || !snapshot.IsAvailable)) desktop = original.DesktopId;
        draggedWindow = null; hoverTimer.Stop();
        await MoveAsync(new(identity, desktop, monitor));
    }
    private async Task MoveAsync(MoveRequest request)
    {
        if (busy) return; busy = true;
        try
        {
            var result = await mover.MoveAsync(request);
            AppLog.Write("move", result: result.Status.ToString());
            Inform(LocalizeMessage(result.Message), result.Status == MoveStatus.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex) { Inform(LocalizeMessage(ex.Message), InfoBarSeverity.Error); AppLog.Write("move", ex); }
        finally { busy = false; await RefreshAsync(true); }
    }
    private async Task ActivateWindowAsync(WindowInfo window)
    {
        await ExecuteAsync(async () =>
        {
            if (!window.IsPinned && window.DesktopId != Guid.Empty) await desktops.SwitchAsync(window.DesktopId);
            await windows.ActivateAsync(window.Identity); HideManager();
        });
    }
    private async Task ExecuteAsync(Func<Task> action)
    {
        if (busy) return; busy = true;
        try { await action(); }
        catch (Exception ex) { Inform(ex.Message, InfoBarSeverity.Error); AppLog.Write("command", ex); }
        finally { busy = false; if (shown) await RefreshAsync(true); }
    }
    private async void CreateDesktop_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { selectedDesktop = await desktops.CreateAsync(); });
    private async void EnterDesktop_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { await desktops.SwitchAsync(selectedDesktop); HideManager(); });
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(true);
    private void Hide_Click(object sender, RoutedEventArgs e) => HideManager();
    private async void Settings_Click(object sender, RoutedEventArgs e) => await OpenSettingsAsync();
    private async Task RenameDesktopAsync(DesktopInfo desktop)
    {
        var input = new TextBox { Text = desktop.Name, MaxLength = 80, MinWidth = 340, Header = T("桌面名称", "Desktop name") };
        var result = await DialogAsync(new ContentDialog { Title = T("重命名桌面", "Rename desktop"), Content = input, PrimaryButtonText = T("保存", "Save"), CloseButtonText = T("取消", "Cancel"), DefaultButton = ContentDialogButton.Primary });
        if (result == ContentDialogResult.Primary) await ExecuteAsync(() => desktops.RenameAsync(desktop.Id, input.Text));
    }
    private async Task RemoveDesktopAsync(DesktopInfo desktop)
    {
        var latest = await desktops.GetSnapshotAsync();
        int index = latest.Desktops.ToList().FindIndex(d => d.Id == desktop.Id);
        if (index < 0 || latest.Desktops.Count < 2) { Inform(T("不能删除最后一个桌面。", "The last desktop cannot be deleted."), InfoBarSeverity.Warning); return; }
        var fallback = latest.Desktops[index > 0 ? index - 1 : 1];
        var result = await DialogAsync(new ContentDialog { Title = T($"删除“{desktop.Name}”？", $"Delete “{desktop.Name}” ?"), Content = T($"窗口会移到“{fallback.Name}”，应用会继续运行。", $"Windows will move to “{fallback.Name}”; applications will keep running."), PrimaryButtonText = T("删除桌面", "Delete desktop"), CloseButtonText = T("取消", "Cancel") });
        if (result == ContentDialogResult.Primary) await ExecuteAsync(() => desktops.RemoveAsync(desktop.Id, fallback.Id));
    }
    private async Task<ContentDialogResult> DialogAsync(ContentDialog dialog)
    {
        overlayDepth++;
        // ContentDialog inside the manager's HWND would sit below DWM hosts. Use
        // an owned WinUI window so previews continue behind a genuine modal surface.
        var dialogRoot = new Grid { RequestedTheme = Root.RequestedTheme };
        var rootLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialogRoot.Loaded += (_, _) => rootLoaded.TrySetResult();
        var dialogWindow = new Window { Title = dialog.Title?.ToString() ?? T("桌面工作区", "Desktop Workspace"), Content = dialogRoot, SystemBackdrop = new MicaBackdrop() };
        if (dialogWindow.AppWindow.Presenter is OverlappedPresenter presenter)
        { presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false; }
        double height = 340;
        if (dialog.Content is FrameworkElement content)
        { content.Measure(new Size(480, double.PositiveInfinity)); height = Math.Clamp(content.DesiredSize.Height + 200, 300, 720); }
        bool closing = false;
        dialogWindow.AppWindow.Closing += (_, e) => { if (!closing) { e.Cancel = true; dialog.Hide(); } };
        try
        {
            shell.AttachDialog(WinRT.Interop.WindowNative.GetWindowHandle(dialogWindow), 600, height);
            dialogWindow.Activate();
            await rootLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            dialog.XamlRoot = dialogRoot.XamlRoot; dialog.RequestedTheme = Root.RequestedTheme;
            return await dialog.ShowAsync();
        }
        finally
        {
            closing = true; dialogWindow.Close(); shell.DetachDialog();
            overlayDepth = Math.Max(0, overlayDepth - 1);
        }
    }
    private async Task OpenSettingsAsync()
    {
        if (modal || busy) return;
        var mods = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        var win = new CheckBox { Content = "Win", IsChecked = (settings.HotkeyModifiers & 8) != 0 };
        var ctrl = new CheckBox { Content = "Ctrl", IsChecked = (settings.HotkeyModifiers & 2) != 0 };
        var alt = new CheckBox { Content = "Alt", IsChecked = (settings.HotkeyModifiers & 1) != 0 };
        var shift = new CheckBox { Content = "Shift", IsChecked = (settings.HotkeyModifiers & 4) != 0 };
        foreach (var box in new[] { win, ctrl, alt, shift }) mods.Children.Add(box);
        var keys = new List<(string Label, uint Key)> { (T("`（反引号）", "` (backtick)"), 0xC0) };
        keys.AddRange(Enumerable.Range(0x41, 26).Select(k => (((char)k).ToString(), (uint)k)));
        keys.AddRange(Enumerable.Range(1, 12).Select(k => ($"F{k}", (uint)(0x6F + k))));
        var keyCombo = new ComboBox { Header = T("主键", "Key"), ItemsSource = keys.Select(k => k.Label).ToArray(), SelectedIndex = Math.Max(0, keys.FindIndex(k => k.Key == settings.HotkeyKey)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var themes = new[] { "Default", "Light", "Dark" };
        var themeCombo = new ComboBox { Header = T("外观", "Theme"), ItemsSource = new[] { T("跟随系统", "System default"), T("浅色", "Light"), T("深色", "Dark") }, SelectedIndex = Math.Max(0, Array.IndexOf(themes, settings.Theme)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var languages = new[] { Localization.Chinese, Localization.English };
        var languageCombo = new ComboBox { Header = T("语言", "Language"), ItemsSource = languages.Select(Localization.LanguageName).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(languages, settings.Language)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var content = new StackPanel { Spacing = 16, MinWidth = 380 };
        content.Children.Add(new TextBlock { Text = T("呼出快捷键", "Show / hide shortcut"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }); content.Children.Add(mods); content.Children.Add(keyCombo); content.Children.Add(themeCombo); content.Children.Add(languageCombo);
        content.Children.Add(new TextBlock { Text = T("关闭面板后常驻托盘；右键托盘图标可退出。\n兼容基准：Windows 11 25H2 · 26200.6899 · x64", "The manager stays in the tray when hidden; right-click the tray icon to exit.\nValidated on Windows 11 25H2 · 26200.6899 · x64"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Style = LocalStyle("SecondaryTextStyle") });
        var result = await DialogAsync(new ContentDialog { Title = T("设置", "Settings"), Content = content, PrimaryButtonText = T("保存", "Save"), CloseButtonText = T("取消", "Cancel"), DefaultButton = ContentDialogButton.Primary });
        if (result != ContentDialogResult.Primary) return;
        uint modifiers = (win.IsChecked == true ? 8u : 0) | (ctrl.IsChecked == true ? 2u : 0) | (alt.IsChecked == true ? 1u : 0) | (shift.IsChecked == true ? 4u : 0);
        if ((modifiers & 11) == 0) { Inform(T("快捷键至少需要 Win、Ctrl、Alt 中的一个修饰键。", "The shortcut needs at least one of Win, Ctrl, or Alt."), InfoBarSeverity.Warning); return; }
        var language = languages[Math.Clamp(languageCombo.SelectedIndex, 0, languages.Length - 1)];
        var candidate = new AppSettings(modifiers, keys[Math.Clamp(keyCombo.SelectedIndex, 0, keys.Count - 1)].Key, themes[Math.Clamp(themeCombo.SelectedIndex, 0, themes.Length - 1)], language);
        if (!shell.RegisterHotkey(candidate))
        {
            shell.RegisterHotkey(settings); Inform(T("该快捷键已被占用，原设置已保留。", "That shortcut is already in use; the previous setting was kept."), InfoBarSeverity.Warning); return;
        }
        try { candidate.Save(); settings = candidate; Localization.Set(settings.Language); shell.UpdateLanguage(); ApplyLanguage(); ApplyTheme(); Render(); }
        catch (Exception ex) { shell.RegisterHotkey(settings); Inform(T($"设置保存失败：{ex.Message}", $"Settings could not be saved: {ex.Message}"), InfoBarSeverity.Error); }
    }
    private static string T(string chinese, string english) => Localization.T(chinese, english);
    private static string LocalizeMessage(string message) => message switch
    {
        "窗口已关闭或无法确认进程身份。" => T("窗口已关闭或无法确认进程身份。", "The window is closed or its process identity could not be verified."),
        "此窗口固定在所有桌面，只能移动显示器。" => T("此窗口固定在所有桌面，只能移动显示器。", "This window is pinned to all desktops and can only move between monitors."),
        "目标桌面已消失或当前系统不支持桌面操作。" => T("目标桌面已消失或当前系统不支持桌面操作。", "The target desktop disappeared or desktop operations are unavailable."),
        "显示器已断开，请刷新后重试。" => T("显示器已断开，请刷新后重试。", "The monitor was disconnected. Refresh and try again."),
        "窗口已关闭。" => T("窗口已关闭。", "The window is closed."),
        "目标显示器已断开。" => T("目标显示器已断开。", "The target monitor was disconnected."),
        "系统未应用完整的目标位置或窗口状态。" => T("系统未应用完整的目标位置或窗口状态。", "Windows did not apply the complete target position or window state."),
        _ when message.StartsWith("移动失败，已恢复原位置：", StringComparison.Ordinal) => T(message, "The move failed; the original position was restored: " + message["移动失败，已恢复原位置：".Length..]),
        _ when message.StartsWith("移动未完全完成，请查看窗口当前所在位置：", StringComparison.Ordinal) => T(message, "The move was only partially completed. Check the window's current location: " + message["移动未完全完成，请查看窗口当前所在位置：".Length..]),
        "窗口或显示器尺寸无效。" => T("窗口或显示器尺寸无效。", "The window or monitor dimensions are invalid."),
        _ => message
    };
    private string WindowCount(int count) => T($"{count} 个窗口", $"{count} window" + (count == 1 ? "" : "s"));
    private void ApplyLanguage()
    {
        Title = T("桌面工作区管理器", "Desktop Workspace Manager");
        AppTitle.Text = T("桌面工作区", "Desktop Workspace");
        EnterButton.Content = T("进入桌面", "Enter desktop");
        RefreshButton.Content = T("刷新", "Refresh");
        SettingsButton.Content = T("设置", "Settings");
        HideButton.Content = T("收起", "Hide");
        ToolTipService.SetToolTip(HideButton, T("Esc · 收起到托盘", "Esc · Hide to tray"));
        CreateButton.Content = T("＋ 新建桌面", "＋ New desktop");
        Hint.Text = T("拖动窗口到显示器或上方桌面 · 悬停桌面可展开跨屏目标 · 右键查看更多",
            "Drag a window to a monitor or desktop · hover a desktop for cross-monitor targets · right-click for more");
        ShortcutHint.Text = $"{settings.HotkeyLabel} {T("呼出 / 收起", "show / hide")}";
        desktopSignature = "";
        layoutSignature = "";
        foreach (var card in previewCards.Values) card.MenuSignature = "";
    }
    private void ApplyTheme() => Root.RequestedTheme = settings.Theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
    private void Inform(string text, InfoBarSeverity severity)
    {
        // Routine success, informational and warning states are reflected by the
        // refreshed UI itself. Keep the banner reserved for actionable errors.
        if (severity != InfoBarSeverity.Error) return;
        noticeTimer.Stop();
        Notice.Message = text; Notice.Severity = severity; Notice.IsOpen = true;
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape || menuDepth > 0) return;
        if (dragActive) { hoverTimer.Stop(); draggedWindow = null; dragActive = false; EndDragPreview(); }
        else HideManager();
        e.Handled = true;
    }
    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (modal || menuDepth > 0) return;
        var point = e.GetCurrentPoint(ContentScroll);
        if (point.Position.X < 0 || point.Position.Y < 0 || point.Position.X >= ContentScroll.ActualWidth || point.Position.Y >= ContentScroll.ActualHeight) return;
        if (point.Properties.IsHorizontalMouseWheel) return;
        ScrollList(point.Properties.MouseWheelDelta); e.Handled = true;
    }
    private void ScrollList(int delta)
    {
        if (!shown || modal || menuDepth > 0 || delta == 0) return;
        double offset = Math.Clamp(ContentScroll.VerticalOffset - delta / 120.0 * 96, 0, ContentScroll.ScrollableHeight);
        // HWND movement cannot follow a separate compositor-only scroll animation.
        ContentScroll.ChangeView(null, offset, null, disableAnimation: true);
        Root.UpdateLayout(); UpdatePreviews();
    }
    private void StartPreviewUpdates()
    {
        if (renderingSubscribed) return;
        CompositionTarget.Rendering += OnRendering; renderingSubscribed = true;
    }
    private void StopPreviewUpdates()
    {
        if (!renderingSubscribed) return;
        CompositionTarget.Rendering -= OnRendering; renderingSubscribed = false;
    }
    private void OnRendering(object? sender, object e) { UpdatePreviews(); UpdateDragPreview(); }
    private void ContentScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) => UpdatePreviews();
    private static void UpdatePreviewStatus(WindowCardView view)
    {
        // Setting Text even to the same value invalidates TextBlock's measure.
        // Never keep invalidating layout for an unavailable/closed source.
        var visibility = view.Preview.IsAvailable ? Visibility.Collapsed : Visibility.Visible;
        if (view.Fallback.Visibility != visibility) view.Fallback.Visibility = visibility;
        string status = view.Preview.UnavailableReason ?? T("系统暂时无法提供预览", "The system cannot provide a preview right now");
        if (!view.Preview.IsAvailable && view.Status.Text != status) view.Status.Text = status;
    }
    private void QueuePreviewStatusUpdate(WindowCardView view)
    {
        if (view.PreviewStatusUpdateQueued) return;
        view.PreviewStatusUpdateQueued = true;
        // LayoutUpdated/render callbacks only position native hosts. Apply XAML
        // state changes after that pass, once per transition, not once per frame.
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            view.PreviewStatusUpdateQueued = false;
            if (!disposed && !view.Disposed) UpdatePreviewStatus(view);
        })) view.PreviewStatusUpdateQueued = false;
    }
    private void UpdatePreviews()
    {
        if (!shown || disposed || updatingPreviews || Root.XamlRoot is null) return;
        updatingPreviews = true;
        try
        {
            double scale = Root.XamlRoot.RasterizationScale;
            var clipPoint = ContentScroll.TransformToVisual(Root).TransformPoint(new Point(0, 0));
            var clip = shell.ToScreen(new((int)Math.Ceiling(clipPoint.X * scale), (int)Math.Ceiling(clipPoint.Y * scale),
                (int)Math.Floor(ContentScroll.ActualWidth * scale), (int)Math.Floor(ContentScroll.ActualHeight * scale)));
            foreach (var view in previewCards.Values)
            {
                var element = view.Element;
                if (!element.IsLoaded || element.ActualWidth < 1 || element.ActualHeight < 1) { view.Preview.SetVisible(false); continue; }
                var point = element.TransformToVisual(Root).TransformPoint(new Point(0, 0));
                var rect = shell.ToScreen(new((int)Math.Round(point.X * scale), (int)Math.Round(point.Y * scale),
                    (int)Math.Round(element.ActualWidth * scale), (int)Math.Round(element.ActualHeight * scale)));
                bool available = view.Preview.IsAvailable;
                view.Preview.Update(new(rect, clip)); view.Preview.SetVisible(true);
                if (view.Preview.IsAvailable != available) QueuePreviewStatusUpdate(view);
            }
        }
        finally { updatingPreviews = false; }
    }
    private void ClearPreviews()
    {
        foreach (var view in previewCards.Values) { view.Disposed = true; view.Preview.Dispose(); }
        previewCards.Clear(); layoutSignature = "";
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        refreshTimer.Stop(); StopPreviewUpdates(); hoverTimer.Stop(); noticeTimer.Stop(); EndDragPreview(); ClearPreviews(); shell.Dispose(); desktops.Dispose();
    }
}
