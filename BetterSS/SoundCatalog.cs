using System;
using System.IO;
using System.Linq;
using System.Media;

namespace BetterSS;

internal static class SoundCatalog
{
    internal const string BuiltInPrefix = "builtin:";
    internal const string SystemSoundSetting = "system:windows";
    internal const string SystemSoundName = "Windows system sound";
    internal const string DefaultFileName = "capture.mp3";
    internal static readonly string[] BuiltInFileNames =
    {
        DefaultFileName,
        "Shutter Click.mp3",
        "Shutter Fast.mp3",
        "Shutter Slow.mp3",
        "Whaad.mp3",
        "Hee Hee.mp3",
        "Meow.mp3"
    };

    internal static string[] AvailableBuiltInFileNames => BuiltInFileNames.Where(fileName => File.Exists(BuiltInPath(fileName))).ToArray();

    internal static string SettingValue(string fileName) => fileName.Equals(SystemSoundName, StringComparison.OrdinalIgnoreCase)
        ? SystemSoundSetting
        : BuiltInPrefix + fileName;

    internal static bool UsesSystemSound(string setting)
    {
        if (string.IsNullOrWhiteSpace(setting)) return !File.Exists(BuiltInPath(DefaultFileName));
        if (setting.Equals(SystemSoundSetting, StringComparison.OrdinalIgnoreCase)) return true;

        if (setting.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string fileName = setting[BuiltInPrefix.Length..];
            return !BuiltInFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase) || !File.Exists(BuiltInPath(fileName));
        }

        return false;
    }

    internal static string SelectedFileName(string setting)
    {
        if (UsesSystemSound(setting)) return SystemSoundName;
        if (string.IsNullOrWhiteSpace(setting)) return DefaultFileName;
        if (setting.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var fileName = setting[BuiltInPrefix.Length..];
            if (BuiltInFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)) return fileName;
        }
        return Path.GetFileName(setting);
    }

    internal static string ResolvePath(string setting)
    {
        if (string.Equals(setting, SystemSoundSetting, StringComparison.OrdinalIgnoreCase)) return "";
        if (string.IsNullOrWhiteSpace(setting)) return BuiltInPath(DefaultFileName);
        if (setting.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase)) return BuiltInPath(setting[BuiltInPrefix.Length..]);
        return setting;
    }

    internal static void PlaySystemSound() => SystemSounds.Asterisk.Play();

    internal static string BuiltInPath(string fileName) => fileName.Equals(DefaultFileName, StringComparison.OrdinalIgnoreCase)
        ? Path.Combine(AppContext.BaseDirectory, "Assets", fileName)
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Custom Sounds", fileName);
}
