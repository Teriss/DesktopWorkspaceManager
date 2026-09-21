namespace WorkspaceManager.Windows;

public static class AppLog
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopWorkspaceManager");
    private static readonly object Gate = new();
    public static void Write(string operation, Exception? error = null, string? result = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DataDirectory);
                var path = Path.Combine(DataDirectory, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".previous", true);
                // Do not log titles, executable paths or pixel contents.
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {operation} {result} {error?.GetType().Name} HRESULT={error?.HResult:X8}{Environment.NewLine}{error?.StackTrace}{(error is null ? "" : Environment.NewLine)}");
            }
        }
        catch { /* A logging failure must not break window management. */ }
    }
}
