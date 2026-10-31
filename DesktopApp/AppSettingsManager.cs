using System;
using System.IO;
using System.Text.Json;

namespace OpenDualHub
{
    public class AppSettings
    {
        public bool ShowNerdStats { get; set; } = false;
        public bool ClockUseMilitaryTime { get; set; } = false;
        public string ClockDateFormat { get; set; } = "ddd dd/MM";
        public bool LightweightBackground { get; set; } = false;
        public bool EnableVolumeOverlay { get; set; } = false;
        public bool TouchpadGesturesEnabled { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public string ActiveProfileId { get; set; } = "builtin-default";
        public double HudScale { get; set; } = 1.0;
    }

    public static class AppSettingsManager
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenDual");

        public static readonly string SettingsFilePath = Path.Combine(SettingsDir, "settings.json");

        public static AppSettings Current { get; private set; } = new();

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        Current = loaded;
                        return Current;
                    }
                }
            }
            catch
            {
                // Fallback to default settings if file is missing or malformed
            }

            Current = new AppSettings();
            return Current;
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Ignore disk write errors to prevent interrupting user session
            }
        }
    }
}
