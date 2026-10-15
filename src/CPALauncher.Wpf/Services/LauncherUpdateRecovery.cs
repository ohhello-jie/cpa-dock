using System.Diagnostics;
using System.Text.Json;

namespace CPALauncher.Services;

public static class LauncherUpdateRecovery
{
    public static bool StartPendingRecovery(string executable)
    {
        var install = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        foreach (var directory in Directory.GetDirectories(install, ".cpa-update-*"))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(directory), @"^\.cpa-update-[a-f0-9]{32}$")) continue;
            var journal = Path.Combine(directory, "state.json");
            if (!File.Exists(journal))
            {
                // v0.4.1's helper has no journal and is still completing the upgrade that launched us.
                if (!File.Exists(Path.Combine(directory, "recover.ps1"))) continue;
                if (Directory.GetDirectories(directory).Length != 0 || Directory.GetFiles(directory).Any(p =>
                    !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p), @"^(recover\.ps1|恢复旧版\.cmd|state\.json\.tmp|new-\d+|old-\d+)$")))
                    throw new IOException($"更新准备被中断，请检查恢复目录：{directory}");
                Directory.Delete(directory, true); // No durable intent: installed files have not changed.
                continue;
            }
            using var state = JsonDocument.Parse(File.ReadAllText(journal));
            var phase = state.RootElement.GetProperty("Phase").GetString();
            if (phase == "committed")
            {
                Directory.Delete(directory, true);
                continue;
            }
            if (phase is not ("pending" or "restored")) throw new IOException("无法识别启动器更新记录。");
            var script = Path.Combine(directory, "recover.ps1");
            if (!File.Exists(script)) throw new IOException($"恢复脚本缺失，备份保留在：{directory}");
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = install,
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                "-ProcessId", Environment.ProcessId.ToString(), "-RecoverDirectory", directory,
                "-InstallDirectory", install, "-LauncherPath", executable }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("无法启动恢复程序。");
            return true;
        }
        return false;
    }

    public static void AcknowledgeStartup(string executable, string transaction)
    {
        var install = Path.GetDirectoryName(Path.GetFullPath(executable));
        var directory = Path.GetFullPath(transaction);
        if (!string.Equals(Path.GetDirectoryName(directory), install, StringComparison.OrdinalIgnoreCase) ||
            !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(directory), @"^\.cpa-update-[a-f0-9]{32}$") ||
            !File.Exists(Path.Combine(directory, "state.json")))
            throw new IOException("启动确认目录无效。");
        AtomicFile.WriteText(Path.Combine(directory, "ready"), Environment.ProcessId.ToString());
    }
}
