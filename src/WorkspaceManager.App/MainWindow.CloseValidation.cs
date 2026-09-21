using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using WorkspaceManager.Core;
using WorkspaceManager.Windows;

namespace WorkspaceManager.App;

public sealed partial class MainWindow
{
    public async Task ValidateCloseLifecycleAsync(int fixtureProcessId, string token)
    {
        using var fixtureProcess = Process.GetProcessById(fixtureProcessId);
        if (fixtureProcess.ProcessName != "WorkspaceManager.Diagnostics" || !Guid.TryParseExact(token, "N", out _))
            throw new InvalidOperationException("Close test requires its dedicated diagnostic fixture process.");
        Activate(); await ShowManagerAsync(); refreshTimer.Stop(); await Task.Delay(400);
        var targets = previewCards.Values.Where(v => v.Window.Identity.ProcessId == fixtureProcessId &&
            v.Window.Title.StartsWith($"[WM-close-{token}] ", StringComparison.Ordinal)).OrderBy(v => v.Window.Title).ToArray();
        if (targets.Length != 7) throw new InvalidOperationException($"Expected 7 dedicated fixture cards, found {targets.Length}.");
        var unrelated = previewCards.Where(p => p.Key.ProcessId != fixtureProcessId).ToDictionary();
        var guarded = targets[0];
        InvokeClose(guarded);
        await WaitUntilAsync(() => !busy && !refreshing, "cancelled close did not finish");
        await Task.Delay(700);
        if (!windows.IsValid(guarded.Window.Identity) || !previewCards.ContainsKey(guarded.Window.Identity) || !guarded.Preview.IsAvailable)
            throw new InvalidOperationException("An application cancelling close lost its window, card or preview.");
        uint handlesBefore = ShellHost.GetUserHandleCount();
        // Keep a dead source card on screen before reconciliation: this used to
        // loop forever in LayoutUpdated when the DWM fallback text was applied.
        var external = targets[^1];
        await windows.RequestCloseAsync(external.Window.Identity);
        await WaitUntilAsync(() => !windows.IsValid(external.Window.Identity), "external fixture close failed");
        await Task.Delay(750);
        if (external.Preview.IsAvailable || external.Fallback.Visibility != Visibility.Visible || external.Status.Text.Length == 0)
            throw new InvalidOperationException("Closed source did not transition to a stable unavailable-preview state.");
        // Exercise layout repeatedly while the fallback remains on screen.
        for (int i = 0; i < 20; i++) { Root.InvalidateMeasure(); Root.UpdateLayout(); await Task.Delay(10); }
        await RefreshAsync(true);
        if (!external.Disposed || previewCards.ContainsKey(external.Window.Identity) || external.Card.Parent is not null)
            throw new InvalidOperationException("External closure retained its card.");
        foreach (var target in targets.Skip(1).SkipLast(1).Append(guarded))
        {
            InvokeClose(target);
            await WaitUntilAsync(() => !busy && !refreshing && !previewCards.ContainsKey(target.Window.Identity), "closed card was not removed");
            await Task.Delay(500);
            if (windows.IsValid(target.Window.Identity) || !target.Disposed || target.Card.Parent is not null || target.Preview.IsVisible)
                throw new InvalidOperationException("Closed source retained a native window, XAML card or preview host.");
            foreach (var pair in unrelated)
                if (windows.IsValid(pair.Key) && (!previewCards.TryGetValue(pair.Key, out var current) || !ReferenceEquals(current, pair.Value)))
                    throw new InvalidOperationException("Closing a fixture rebuilt an unrelated card.");
        }
        // Wait beyond the notice timeout and several refresh/layout/render passes.
        await Task.Delay(3600); await RefreshAsync(true);
        uint handlesAfter = ShellHost.GetUserHandleCount();
        if (Notice.IsOpen || previewCards.Keys.Any(id => id.ProcessId == fixtureProcessId))
            throw new InvalidOperationException("Close operation left a stale notice or card.");
        if (handlesAfter >= handlesBefore) throw new InvalidOperationException("Closing fixtures did not release native preview handles.");
        File.WriteAllText(Path.Combine(AppLog.DataDirectory, "ui-close-lifecycle-test.json"), JsonSerializer.Serialize(new {
            Success = true, WindowsClosedThroughButton = targets.Length - 1, ExternalCloseRemoved = true,
            UnavailablePreviewLayoutPasses = 20, CancelledClosePreservesCard = true,
            ClosedCardsAndPreviewsRemoved = true, UnrelatedCardsRetained = true, NonErrorNoticesSuppressed = true,
            UserHandlesBefore = handlesBefore, UserHandlesAfter = handlesAfter, Timestamp = DateTimeOffset.Now
        }, new JsonSerializerOptions { WriteIndented = true }));
        HideManager();
    }

    private static void InvokeClose(WindowCardView view)
    {
        view.PointerOver = true; UpdateCloseButton(view);
        view.CloseButton.Focus(FocusState.Programmatic);
        var peer = new ButtonAutomationPeer(view.CloseButton);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private static async Task WaitUntilAsync(Func<bool> ready, string error)
    {
        // Invoke is dispatched asynchronously; let its Click handler start first.
        await Task.Delay(50);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready())
        {
            if (DateTime.UtcNow >= deadline) throw new InvalidOperationException(error);
            await Task.Delay(50);
        }
    }
}
