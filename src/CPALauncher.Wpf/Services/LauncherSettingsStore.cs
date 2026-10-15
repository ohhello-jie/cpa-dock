using System.Text.Json;
using CPALauncher.Models;

namespace CPALauncher.Services;

public sealed class LauncherSettingsStore(string? settingsDirectoryPath = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public string SettingsDirectoryPath => settingsDirectoryPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CPALauncher");

    public string SettingsFilePath => Path.Combine(SettingsDirectoryPath, "settings.json");
    public string BackupFilePath => SettingsFilePath + ".bak";
    public string? RecoveryNotice { get; private set; }
    private bool savingBlocked;
    public bool CanSave => !savingBlocked;

    public LauncherSettings Load()
    {
        RecoveryNotice = null;
        savingBlocked = false;
        if (!File.Exists(SettingsFilePath) && !File.Exists(BackupFilePath)) return new();
        if (TryRead(SettingsFilePath, out var settings)) return settings!;
        if (TryRead(BackupFilePath, out settings))
        {
            try
            {
                using var guard = Lock();
                if (File.Exists(SettingsFilePath))
                    File.Copy(SettingsFilePath, SettingsFilePath + ".corrupt-" + Guid.NewGuid().ToString("N"));
                AtomicFile.WriteText(SettingsFilePath, JsonSerializer.Serialize(settings, SerializerOptions));
                RecoveryNotice = "设置已从备份恢复。";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                savingBlocked = true;
                RecoveryNotice = "已读取设置备份，但无法恢复原文件；暂停保存设置。";
            }
            return settings!;
        }
        savingBlocked = true;
        RecoveryNotice = "设置文件及备份无法读取，已保留原文件并暂停保存。请修复设置文件后重启。";
        return new();
    }

    public void Save(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (savingBlocked) throw new InvalidOperationException(RecoveryNotice);
        Directory.CreateDirectory(SettingsDirectoryPath);
        using var guard = Lock();
        if (File.Exists(SettingsFilePath) && !TryRead(SettingsFilePath, out _))
            throw new InvalidDataException("设置文件无法读取，已保留原文件，未覆盖保存。");
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        AtomicFile.WriteText(SettingsFilePath, json, BackupFilePath);
        if (!File.Exists(BackupFilePath)) AtomicFile.WriteText(BackupFilePath, json);
    }

    private FileStream Lock() => new(SettingsFilePath + ".lock", FileMode.OpenOrCreate,
        FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);

    private static bool TryRead(string path, out LauncherSettings? settings)
    {
        settings = null;
        try { settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), SerializerOptions); return settings is not null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }
}
