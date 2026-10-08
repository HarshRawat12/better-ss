using System;
using System.IO;
using System.Text.Json;

namespace BetterSS;

public sealed class Settings
{
    public string Theme { get; set; } = "System";
    public string Material { get; set; } = "Solid";
    public double Duration { get; set; } = 5;
    public double PreviewWidth { get; set; } = 300;
    public bool CaptureAnimation { get; set; } = true;
    public double AnimationDuration { get; set; } = 320;
    public double FlashOpacity { get; set; } = 65;
    public double Radius { get; set; }
    public double Shadow { get; set; } = 24;
    public double Stroke { get; set; } = 1;
    public bool SoundEnabled { get; set; } = true;
    public double Volume { get; set; } = 0.65;
    public string SoundPath { get; set; } = "";
    public bool AutoSave { get; set; } = true;
    public string SaveFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Better SS");
    public string Hotkey { get; set; } = "Ctrl + Shift + S";
    public bool UseWindowsCaptureShortcut { get; set; }
    public int Delay { get; set; }
    public string ExportFormat { get; set; } = "PNG";
    public string ExportColorSpace { get; set; } = "RGB";
    public int JpegQuality { get; set; } = 95;
    public bool StartOnLogin { get; set; }
    public bool IntroductionSeen { get; set; }
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Better SS");
    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");
    public static Settings Load(string? file = null)
    {
        try
        {
            string json = File.ReadAllText(file ?? SettingsFile);
            var settings = JsonSerializer.Deserialize<Settings>(json) ?? new();
            using var document = JsonDocument.Parse(json);
            // Existing users can replay the guide, without seeing an unsolicited first-run tour.
            if (!document.RootElement.TryGetProperty(nameof(IntroductionSeen), out _)) settings.IntroductionSeen = true;
            settings.Normalize(); return settings;
        }
        catch { return new(); }
    }
    public void Normalize()
    {
        Duration = Clamp(Duration, 2, 30, 5); PreviewWidth = Clamp(PreviewWidth, 220, 440, 300);
        AnimationDuration = Clamp(AnimationDuration, 160, 700, 320); FlashOpacity = Clamp(FlashOpacity, 0, 100, 65);
        Radius = 0; Material = "Solid"; Shadow = Clamp(Shadow, 0, 48, 24);
        if (Array.IndexOf(ExportService.Formats, ExportFormat) < 0) ExportFormat = "PNG";
        if (ExportColorSpace is not ("RGB" or "CMYK")) ExportColorSpace = "RGB";
        JpegQuality = Math.Clamp(JpegQuality, 1, 100);
        Stroke = Clamp(Stroke, 0, 3, 1); Volume = Clamp(Volume, 0, 1, .65);
        Delay = Delay is 0 or 3 or 5 or 10 ? Delay : 0;
        if (!HotkeyBinding.TryParse(Hotkey, out _, out _)) Hotkey = new Settings().Hotkey;
        if (string.IsNullOrWhiteSpace(SaveFolder)) SaveFolder = new Settings().SaveFolder;
    }
    private static double Clamp(double n, double min, double max, double fallback) => double.IsFinite(n) ? Math.Clamp(n, min, max) : fallback;
    public void Save(string? file = null)
    {
        file = Path.GetFullPath(file ?? SettingsFile);
        Normalize(); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, file, true);
    }
}
