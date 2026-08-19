using Microsoft.Win32;

namespace TextAutoCorrect.Infrastructure.AutoStart;

internal static class WindowsAutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TextAutoCorrect";

    public static void SetEnabled(bool enabled)
    {
        try
        {
            var exePath = GetExecutablePath();
            if (string.IsNullOrWhiteSpace(exePath))
                return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null)
                return;

            if (enabled)
                key.SetValue(ValueName, exePath);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Do not crash if registry access is blocked.
        }
    }

    private static string? GetExecutablePath()
    {
        // .NET 8 single-file: Environment.ProcessPath is the most reliable.
        var path = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}

