using CPALauncher.Services;
using Xunit;

namespace CPALauncher.Wpf.Tests;

public sealed class CpaTransactionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "CPA 事务 " + Guid.NewGuid().ToString("N"));
    private string Target => Path.Combine(root, "cli-proxy-api.exe");
    private CpaUpdateTransaction Prepare(bool running = false)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Target, "old");
        var transaction = CpaUpdateTransaction.Create(Target, "v1", "v2", running);
        File.WriteAllText(transaction.StagedPath, "new");
        transaction.Prepare();
        return transaction;
    }

    [Fact]
    public void InterruptedReplacement_ReopensAndRestores_WithoutLosingJournalBeforeMetadata()
    {
        var original = Prepare(true);
        original.Apply();
        var recovered = CpaUpdateTransaction.OpenPending(Target)!;
        Assert.True(recovered.WasRunning);
        Assert.Equal("v1", recovered.PreviousVersion);
        recovered.Rollback();
        Assert.Equal("old", File.ReadAllText(Target));
        Assert.NotNull(CpaUpdateTransaction.OpenPending(Target));
        recovered.Rollback(); // Retrying recovery is safe.
        recovered.FinishRollback();
        Assert.Null(CpaUpdateTransaction.OpenPending(Target));
    }

    [Fact]
    public void PreparationDoesNotChangeExecutable_AndCanBeDiscardedAfterInterruption()
    {
        Prepare();
        Assert.Equal("old", File.ReadAllText(Target));
        Assert.Null(CpaUpdateTransaction.OpenPending(Target));
        Assert.False(Directory.Exists(Target + ".update"));
    }

    [Fact]
    public void DamagedBackup_IsRetainedAndNeverInstalled()
    {
        var transaction = Prepare(); transaction.Apply();
        File.WriteAllText(Path.Combine(transaction.DirectoryPath, "old.exe"), "corrupt");
        Assert.Throws<InvalidDataException>(() => transaction.Rollback());
        Assert.Equal("new", File.ReadAllText(Target));
        Assert.True(File.Exists(Path.Combine(transaction.DirectoryPath, "state.json")));
    }

    [Fact]
    public void LockedRollback_RetainsBackup_AndCanRetry()
    {
        var transaction = Prepare(); transaction.Apply();
        using (File.Open(Target, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => transaction.Rollback());
        var recovered = CpaUpdateTransaction.OpenPending(Target)!;
        recovered.Rollback(); recovered.FinishRollback();
        Assert.Equal("old", File.ReadAllText(Target));
    }

    [Fact]
    public void FailedCommitJournalWrite_RemainsRecoverable()
    {
        var transaction = Prepare(); transaction.Apply();
        using (File.Open(Path.Combine(transaction.DirectoryPath, "state.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => transaction.Commit());
        transaction.Rollback(); transaction.FinishRollback();
        Assert.Equal("old", File.ReadAllText(Target));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task HealthCheck_DeterminesCommitOrRollback_AndPreservesRunningState(bool wasRunning, bool healthy)
    {
        var transaction = Prepare(wasRunning);
        var running = wasRunning;
        var version = "v1";
        var starts = 0;
        var task = CpaUpdateCoordinator.ApplyAsync(transaction,
            () => { running = false; return Task.CompletedTask; },
            isNew => { running = true; starts++; Assert.Equal(isNew ? "new" : "old", File.ReadAllText(Target)); return Task.CompletedTask; },
            () => Task.FromResult(File.ReadAllText(Target) == "old" || healthy),
            v => version = v);
        if (healthy) await task;
        else await Assert.ThrowsAsync<IOException>(() => task);
        Assert.Equal(healthy ? "new" : "old", File.ReadAllText(Target));
        Assert.Equal(healthy ? "v2" : "v1", version);
        Assert.Equal(wasRunning, running);
        Assert.Equal(!healthy && wasRunning ? 2 : 1, starts);
        Assert.False(Directory.Exists(transaction.DirectoryPath));
    }

    [Fact]
    public async Task SettingsWriteFailure_RollsBackBinaryAndVersion()
    {
        var transaction = Prepare();
        var version = "v1";
        await Assert.ThrowsAsync<IOException>(() => CpaUpdateCoordinator.ApplyAsync(transaction,
            () => Task.CompletedTask, _ => Task.CompletedTask, () => Task.FromResult(true),
            v => { if (v == "v2") throw new IOException("disk full"); version = v; }));
        Assert.Equal("old", File.ReadAllText(Target));
        Assert.Equal("v1", version);
    }

    [Fact]
    public void InterruptedBeforeReplacement_RestoresPreviousRunningIntent()
    {
        var transaction = Prepare(true);
        transaction.BeginActivation();
        var pending = CpaUpdateTransaction.OpenPending(Target)!;
        Assert.True(pending.WasRunning);
        pending.Rollback(); pending.FinishRollback();
        Assert.Equal("old", File.ReadAllText(Target));
    }

    [Fact]
    public void LegacyLauncherUpdaterDirectory_IsLeftForItsRunningHelper()
    {
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, ".cpa-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "old-0"), "legacy backup");
        Assert.False(LauncherUpdateRecovery.StartPendingRecovery(Path.Combine(root, "CPA Dock.exe")));
        Assert.Equal("legacy backup", File.ReadAllText(Path.Combine(directory, "old-0")));
    }

    [Fact]
    public async Task FailedOldServiceRestart_KeepsRecoveryIntent()
    {
        var transaction = Prepare(true);
        await Assert.ThrowsAsync<IOException>(() => CpaUpdateCoordinator.ApplyAsync(transaction,
            () => Task.CompletedTask,
            isNew => { if (!isNew) throw new IOException("Old service temporarily unavailable"); return Task.CompletedTask; },
            () => Task.FromResult(false), _ => { }));
        Assert.Equal("old", File.ReadAllText(Target));
        Assert.True(CpaUpdateTransaction.OpenPending(Target)!.WasRunning);
    }

    [Fact]
    public void UnknownRecoveryData_IsPreserved()
    {
        Directory.CreateDirectory(Target + ".update");
        var unknown = Path.Combine(Target + ".update", "user-file");
        File.WriteAllText(unknown, "keep");
        Assert.Throws<IOException>(() => CpaUpdateTransaction.OpenPending(Target));
        Assert.Equal("keep", File.ReadAllText(unknown));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
