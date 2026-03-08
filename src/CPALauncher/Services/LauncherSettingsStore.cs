using System.Text.Json;
using CPALauncher.Models;

namespace CPALauncher.Services;

public sealed class LauncherSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public string SettingsDirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CPALauncher");

    public string SettingsFilePath => Path.Combine(SettingsDirectoryPath, "settings.json");

    public LauncherSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new LauncherSettings();
        }

        var json = File.ReadAllText(SettingsFilePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LauncherSettings();
        }

        return JsonSerializer.Deserialize<LauncherSettings>(json, SerializerOptions) ?? new LauncherSettings();
    }

    public void Save(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(SettingsDirectoryPath);
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(SettingsFilePath, json);
    }
}
