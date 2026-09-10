using System;
using System.IO;
using System.Linq;

namespace BetterSS;

internal static class SoundCatalog
{
    internal const string BuiltInPrefix = "builtin:";
    internal const string DefaultFileName = "capture.mp3";
    internal static readonly string[] BuiltInFileNames =
    {
        DefaultFileName,
        "Shutter Click .mp3",
        "Shutter Fast.mp3",
        "Shutter Slow .mp3",
        "Whaaf.mp3",
        "Hee Hee.mp3",
        "Meow .mp3"
    };

    internal static string SettingValue(string fileName) => BuiltInPrefix + fileName;

    internal static string SelectedFileName(string setting)
    {
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
        if (string.IsNullOrWhiteSpace(setting)) return BuiltInPath(DefaultFileName);
        if (setting.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase)) return BuiltInPath(setting[BuiltInPrefix.Length..]);
        return setting;
    }

    internal static string BuiltInPath(string fileName) => fileName.Equals(DefaultFileName, StringComparison.OrdinalIgnoreCase)
        ? Path.Combine(AppContext.BaseDirectory, "Assets", fileName)
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Custom Sounds", fileName);
}
