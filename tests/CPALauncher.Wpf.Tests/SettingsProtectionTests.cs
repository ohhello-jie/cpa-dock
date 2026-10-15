using CPALauncher.Models;
using CPALauncher.Services;
using Xunit;

namespace CPALauncher.Wpf.Tests;

public sealed class SettingsProtectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "CPA 配置保护 " + Guid.NewGuid().ToString("N"));
    private LauncherSettingsStore Store => new(root);
    private static LauncherSettings Settings(string version) => new()
    {
        ExecutablePath = @"C:\测试目录\cli-proxy-api.exe", ConfigPath = @"C:\测试目录\config.yaml",
        LastInstalledCpaVersion = version, ManagementSecretKey = "fixture-secret", AutoStartService = true,
    };

    [Fact]
    public void Save_KeepsPreviousValidSettings_AndLeavesNoTemporaryFiles()
    {
        var store = Store;
        store.Save(Settings("old"));
        var old = File.ReadAllText(store.SettingsFilePath);
        store.Save(Settings("new"));
        Assert.Equal(old, File.ReadAllText(store.BackupFilePath));
        Assert.Equal("new", Store.Load().LastInstalledCpaVersion);
        Assert.Equal("fixture-secret", Store.Load().ManagementSecretKey);
        Assert.Equal(2, Directory.GetFiles(root).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Load_RecoversCorruptOrMissingMain_FromValidBackup(bool missing)
    {
        var store = Store;
        store.Save(Settings("old"));
        if (missing) File.Delete(store.SettingsFilePath);
        else File.WriteAllText(store.SettingsFilePath, "{broken");
        var recovered = store.Load();
        Assert.Equal("old", recovered.LastInstalledCpaVersion);
        Assert.Equal("fixture-secret", recovered.ManagementSecretKey);
        Assert.NotNull(store.RecoveryNotice);
        Assert.True(store.CanSave);
        Assert.Equal("old", Store.Load().LastInstalledCpaVersion);
        if (!missing) Assert.Equal("{broken", File.ReadAllText(Assert.Single(Directory.GetFiles(root, "*.corrupt-*"))));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("")]
    public void Load_WhenBothCopiesInvalid_BlocksDefaultOverwrite(string damaged)
    {
        Directory.CreateDirectory(root);
        var store = Store;
        File.WriteAllText(store.SettingsFilePath, damaged);
        File.WriteAllText(store.BackupFilePath, damaged);
        store.Load();
        Assert.False(store.CanSave);
        Assert.Throws<InvalidOperationException>(() => store.Save(Settings("new")));
        Assert.Equal(damaged, File.ReadAllText(store.SettingsFilePath));
        Assert.Equal(damaged, File.ReadAllText(store.BackupFilePath));
    }

    [Fact]
    public void Save_LockedMain_PreservesBothCopies_ThenRetryWorks()
    {
        var store = Store;
        store.Save(Settings("old"));
        var old = File.ReadAllText(store.SettingsFilePath);
        using (File.Open(store.SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => store.Save(Settings("new")));
        Assert.Equal(old, File.ReadAllText(store.SettingsFilePath));
        Assert.Equal(old, File.ReadAllText(store.BackupFilePath));
        Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        store.Save(Settings("new"));
        Assert.Equal("new", store.Load().LastInstalledCpaVersion);
    }

    [Fact]
    public void Save_DoesNotOverwriteExternallyDamagedSettings()
    {
        var store = Store;
        store.Save(Settings("old"));
        store.Load();
        File.WriteAllText(store.SettingsFilePath, "damaged after load");
        Assert.Throws<InvalidDataException>(() => store.Save(Settings("new")));
        Assert.Equal("damaged after load", File.ReadAllText(store.SettingsFilePath));
    }

    [Fact]
    public void Save_ConcurrentWriterLock_LeavesOriginalUntouched()
    {
        var store = Store;
        store.Save(Settings("old"));
        using (File.Open(store.SettingsFilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsAny<IOException>(() => Store.Save(Settings("new")));
        Assert.Equal("old", Store.Load().LastInstalledCpaVersion);
    }

    [Fact]
    public void ConfigGeneration_BacksUpExistingConfig()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.yaml");
        File.WriteAllText(path, "original config with credentials");
        CpaConfigGenerator.WriteDefaultConfig(path, "127.0.0.1", 8317, null, "test-key");
        Assert.Equal("original config with credentials", File.ReadAllText(path + ".bak"));
        Assert.Contains("test-key", File.ReadAllText(path));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
