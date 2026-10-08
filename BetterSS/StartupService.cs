using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace BetterSS;

internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static string Command(string executable) => "\"" + executable + "\" --background";
    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true) ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
        WriteSetting(key, enabled, Environment.ProcessPath ?? throw new InvalidOperationException("The app location is unavailable."));
    }
    internal static void SetDevelopmentEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true) ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
        const string name = "Better SS Development";
        if (enabled) key.SetValue(name, Command(Environment.ProcessPath ?? throw new InvalidOperationException("The app location is unavailable.")) + " --dev", RegistryValueKind.String);
        else key.DeleteValue(name, false);
    }
    internal static void WriteSetting(RegistryKey key, bool enabled, string executable)
    {
        if (enabled)
        {
            string command = Command(executable);
            if (command.Length > 260) throw new InvalidOperationException("Move Better SS to a shorter folder path before enabling startup.");
            key.SetValue("Better SS", command, RegistryValueKind.String);
        }
        else key.DeleteValue("Better SS", false);
    }
    internal static void OpenTaskbarSettings() => Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true });
}
