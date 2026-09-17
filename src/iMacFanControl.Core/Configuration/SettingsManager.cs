using System;
using System.IO;
using System.Text.Json;

namespace iMacFanControl.Core.Configuration;

public class SettingsManager
{
    private readonly string _settingsFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Settings { get; private set; }

    public SettingsManager(string? customPath = null)
    {
        _settingsFilePath = customPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        Settings = LoadSettings();
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    Settings = loaded;
                    return Settings;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error reading settings from '{_settingsFilePath}': {ex.Message}. Using defaults.");
        }

        Settings = new AppSettings();
        SaveSettings();
        return Settings;
    }

    public void SaveSettings()
    {
        try
        {
            string json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error saving settings: {ex.Message}");
        }
    }
}
