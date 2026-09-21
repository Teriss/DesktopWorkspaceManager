namespace WorkspaceManager.Windows;

/// <summary>Small runtime catalog shared by the WinUI surface and the tray host.</summary>
public static class Localization
{
    public const string Chinese = "zh-CN";
    public const string English = "en-US";
    private static string language = Chinese;

    public static string Current => language;
    public static bool IsEnglish => language == English;

    public static void Set(string? value) => language = string.Equals(value, English, StringComparison.OrdinalIgnoreCase) ? English : Chinese;
    public static string T(string chinese, string english) => IsEnglish ? english : chinese;
    public static string LanguageName(string value) => value == English ? "English" : "简体中文";
}
