using System.Security;
using Microsoft.Win32;

namespace utc_clock.Services;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "UTC Clock";

    public static void EnsureCurrentUserRunEntry()
    {
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        TrySetRunEntry(BuildStartupCommand(executablePath));
    }

    internal static string BuildStartupCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(executablePath));
        }

        return $"\"{executablePath}\"";
    }

    private static void TrySetRunEntry(string command)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (!string.Equals(key?.GetValue(ValueName) as string, command, StringComparison.Ordinal))
            {
                key?.SetValue(ValueName, command, RegistryValueKind.String);
            }
        }
        catch (IOException)
        {
        }
        catch (SecurityException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}