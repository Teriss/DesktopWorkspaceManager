using Microsoft.UI.Xaml;
using WorkspaceManager.Windows;

namespace WorkspaceManager.App;

public partial class App : Application
{
    private MainWindow? window;
    private SingleInstance? instance;
    public App()
    {
        ShellHost.SetApplicationIdentity();
        InitializeComponent();
        UnhandledException += (_, e) => AppLog.Write("unhandled", e.Exception);
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Environment.GetCommandLineArgs().Any(a => a is "--smoke-test" or "--interaction-test" or "--close-lifecycle-test"))
        {
            _ = ValidateAsync(); return;
        }
        instance = new SingleInstance();
        if (!instance.IsFirst) { instance.Dispose(); Exit(); return; }
        window = new MainWindow();
        window.ExitRequested += () => { window.Dispose(); instance.Dispose(); Exit(); };
        window.Activate();
        _ = window.ShowManagerAsync();
    }
    private async Task ValidateAsync()
    {
        bool visible = Environment.GetCommandLineArgs().Contains("--interaction-test");
        var arguments = Environment.GetCommandLineArgs();
        int closeIndex = Array.IndexOf(arguments, "--close-lifecycle-test");
        string report = closeIndex >= 0 ? "ui-close-lifecycle-test.json" : visible ? "ui-interaction-test.json" : "ui-smoke-test.json";
        try
        {
            window = new MainWindow(diagnostic: true);
            if (closeIndex >= 0) await window.ValidateCloseLifecycleAsync(int.Parse(arguments[closeIndex + 1]), arguments[closeIndex + 2]);
            else if (visible) await window.ValidateVisibleInteractionsAsync();
            else await window.ValidateUiWithoutShowingAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(report, ex);
            Directory.CreateDirectory(AppLog.DataDirectory);
            File.WriteAllText(Path.Combine(AppLog.DataDirectory, report), System.Text.Json.JsonSerializer.Serialize(new { Success = false, Error = ex.ToString() }));
            Environment.ExitCode = 1;
        }
        finally { window?.Dispose(); Exit(); }
    }
}
