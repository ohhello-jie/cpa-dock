using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Runtime.InteropServices;

namespace CPALauncher.Services;

public sealed class CpaUpdateTransaction
{
    private readonly string target;
    private readonly State state;
    private bool applied;
    public string DirectoryPath { get; }
    public string StagedPath => Path.Combine(DirectoryPath, "new.exe");
    private string BackupPath => Path.Combine(DirectoryPath, "old.exe");
    private string JournalPath => Path.Combine(DirectoryPath, "state.json");
    public string? PreviousVersion => state.PreviousVersion;
    public string NewVersion => state.NewVersion;
    public bool WasRunning => state.WasRunning;

    private CpaUpdateTransaction(string target, State state)
    {
        this.target = Path.GetFullPath(target);
        DirectoryPath = this.target + ".update";
        this.state = state;
    }

    public static CpaUpdateTransaction Create(string target, string? previousVersion, string version, bool wasRunning)
    {
        var transaction = new CpaUpdateTransaction(target, new State { PreviousVersion = previousVersion, NewVersion = version, WasRunning = wasRunning });
        if (Directory.Exists(transaction.DirectoryPath)) throw new IOException("存在未完成的 CPA 更新，请先恢复后再重试。");
        Directory.CreateDirectory(transaction.DirectoryPath);
        return transaction;
    }

    public void Prepare()
    {
        state.OriginalExists = File.Exists(target);
        state.NewHash = Hash(StagedPath);
        if (state.OriginalExists)
        {
            File.Copy(target, BackupPath);
            Flush(BackupPath);
            state.OldHash = Hash(BackupPath);
        }
        Flush(StagedPath);
        Save();
    }

    public void BeginActivation()
    {
        state.Phase = "pending";
        Save();
    }

    public void Apply()
    {
        if (Hash(StagedPath) != state.NewHash) throw new InvalidDataException("更新文件校验失败。");
        if (state.OriginalExists && Hash(target) != state.OldHash) throw new IOException("原程序已发生变化，取消更新。");
        state.Phase = "pending";
        Save(); // Durable intent must precede replacement.
        applied = true;
        if (state.OriginalExists) File.Replace(StagedPath, target, null);
        else File.Move(StagedPath, target);
    }

    public void RecordProcess(int processId)
    {
        using var process = Process.GetProcessById(processId);
        if (!string.Equals(ProcessPath(process), target, StringComparison.OrdinalIgnoreCase))
            throw new IOException("无法确认更新后的 CPA 进程。");
        state.ProcessId = processId;
        state.ProcessStarted = process.StartTime.ToUniversalTime().Ticks;
        Save();
    }

    public void Commit()
    {
        state.Phase = "committed";
        try { Save(); }
        catch { state.Phase = "pending"; throw; }
        TryCleanup();
    }

    public void Rollback()
    {
        if (state.Phase == "committed") return;
        if (state.Phase == "pending" || applied)
        {
            StopRecordedProcess();
            if (state.OriginalExists)
            {
                if (Hash(BackupPath) != state.OldHash) throw new InvalidDataException("原程序备份校验失败，已保留恢复文件。");
                if (File.Exists(target) && Hash(target) == state.OldHash)
                {
                    state.Phase = "rolledBack";
                    Save();
                    return;
                }
                if (File.Exists(target) && Hash(target) != state.NewHash)
                    throw new IOException("程序已发生变化，保留备份供恢复。");
                var restore = Path.Combine(DirectoryPath, "restore.exe");
                File.Copy(BackupPath, restore, true);
                Flush(restore);
                if (File.Exists(target)) File.Replace(restore, target, null);
                else File.Move(restore, target);
            }
            else if (File.Exists(target))
            {
                if (Hash(target) != state.NewHash) throw new IOException("程序已发生变化，未删除文件。");
                File.Delete(target);
            }
        }
        state.Phase = "rolledBack";
        Save();
        // Keep the journal until the caller has durably restored version metadata.
    }

    public void FinishRollback() => TryCleanup();
    public void DiscardPreparation() { if (state.Phase == "prepared") TryCleanup(); }

    public static CpaUpdateTransaction? OpenPending(string target)
    {
        var directory = Path.GetFullPath(target) + ".update";
        if (!Directory.Exists(directory)) return null;
        var journal = Path.Combine(directory, "state.json");
        if (!File.Exists(journal))
        {
            // No journal means replacement has never started. Leave unknown data for inspection.
            if (Directory.GetDirectories(directory).Length == 0 && Directory.GetFiles(directory).All(p => Path.GetFileName(p) is "package.zip" or "new.exe" or "old.exe" || Path.GetFileName(p).StartsWith("state.json.tmp-")))
                Directory.Delete(directory, true);
            else throw new IOException("无法识别未完成的更新目录，已保留文件。");
            return null;
        }
        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(journal)) ?? throw new InvalidDataException("更新恢复记录无法读取。");
        if (state.Phase is not ("prepared" or "pending" or "committed" or "rolledBack")) throw new InvalidDataException("更新恢复状态无效。");
        var transaction = new CpaUpdateTransaction(target, state);
        if (state.Phase is "prepared" or "committed") { transaction.TryCleanup(); return null; }
        return transaction;
    }

    private void StopRecordedProcess()
    {
        if (state.ProcessId is not { } pid) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.StartTime.ToUniversalTime().Ticks != state.ProcessStarted ||
                !string.Equals(ProcessPath(process), target, StringComparison.OrdinalIgnoreCase)) return;
            process.Kill();
            if (!process.WaitForExit(10000)) throw new IOException("CPA 进程尚未退出，已保留备份。");
        }
        catch (ArgumentException) { }
    }

    private void Save() => AtomicFile.WriteText(JournalPath, JsonSerializer.Serialize(state));
    private static string ProcessPath(Process process)
    {
        var path = new StringBuilder(32768);
        var length = path.Capacity;
        if (!QueryFullProcessImageName(process.Handle, 0, path, ref length))
            throw new IOException("无法读取 CPA 进程路径。", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        return path.ToString();
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
    private void TryCleanup() { try { Directory.Delete(DirectoryPath, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void Flush(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read); stream.Flush(true); }
    public sealed class State
    {
        public string Phase { get; set; } = "prepared";
        public bool OriginalExists { get; set; }
        public string? PreviousVersion { get; set; }
        public string NewVersion { get; set; } = "";
        public bool WasRunning { get; set; }
        public string? OldHash { get; set; }
        public string? NewHash { get; set; }
        public int? ProcessId { get; set; }
        public long ProcessStarted { get; set; }
    }
}
