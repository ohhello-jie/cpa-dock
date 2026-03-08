using System.Collections.Concurrent;
using System.Diagnostics;

namespace CPALauncher.Services;

public sealed class CpaProcessManager : IDisposable
{
    private const int MaxBufferedLines = 400;
    private readonly object syncRoot = new();
    private readonly ConcurrentQueue<string> recentOutput = new();
    private Process? managedProcess;

    public event EventHandler<string>? OutputReceived;

    public event EventHandler<int>? ProcessExited;

    public bool IsManagedProcessRunning
    {
        get
        {
            lock (syncRoot)
            {
                return managedProcess is { HasExited: false };
            }
        }
    }

    public int? ManagedProcessId
    {
        get
        {
            lock (syncRoot)
            {
                return managedProcess is { HasExited: false } process ? process.Id : null;
            }
        }
    }

    public IReadOnlyList<string> GetRecentOutput() => recentOutput.ToArray();

    public void ClearRecentOutput()
    {
        while (recentOutput.TryDequeue(out _))
        {
        }
    }

    public Task<ProcessCommandResult> StartAsync(string executablePath, string configPath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return Task.FromResult(ProcessCommandResult.Failed("找不到 cli-proxy-api.exe。"));
        }

        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            return Task.FromResult(ProcessCommandResult.Failed("找不到 config.yaml。"));
        }

        lock (syncRoot)
        {
            if (managedProcess is { HasExited: false } process)
            {
                return Task.FromResult(ProcessCommandResult.Succeeded($"CPA 已在运行，PID={process.Id}"));
            }

            managedProcess?.Dispose();
            managedProcess = null;
        }

        ClearRecentOutput();

        try
        {
            var process = BuildProcess(executablePath, configPath);
            process.OutputDataReceived += (_, args) => HandleOutput("stdout", args.Data);
            process.ErrorDataReceived += (_, args) => HandleOutput("stderr", args.Data);
            process.Exited += (_, _) => HandleProcessExited(process);

            if (!process.Start())
            {
                process.Dispose();
                return Task.FromResult(ProcessCommandResult.Failed("CPA 进程启动失败，系统没有返回有效进程句柄。"));
            }

            lock (syncRoot)
            {
                managedProcess = process;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            HandleOutput("launcher", $"已启动 CPA 进程，PID={process.Id}");

            return Task.FromResult(ProcessCommandResult.Succeeded($"CPA 已启动，PID={process.Id}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ProcessCommandResult.Failed($"启动 CPA 失败：{ex.Message}"));
        }
    }

    public async Task<ProcessCommandResult> StopAsync()
    {
        Process? process;
        lock (syncRoot)
        {
            process = managedProcess;
            if (process is null || process.HasExited)
            {
                managedProcess = null;
                return ProcessCommandResult.Succeeded("当前没有由启动器托管的 CPA 进程。", true);
            }
        }

        try
        {
            HandleOutput("launcher", $"正在停止 CPA 进程，PID={process.Id}");
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            return ProcessCommandResult.Succeeded("CPA 已停止。");
        }
        catch (Exception ex)
        {
            return ProcessCommandResult.Failed($"停止 CPA 失败：{ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            managedProcess?.Dispose();
            managedProcess = null;
        }
    }

    private static Process BuildProcess(string executablePath, string configPath)
    {
        var process = new Process
        {
            StartInfo =
            {
                FileName = Path.GetFullPath(executablePath),
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath)) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        process.StartInfo.ArgumentList.Add("--config");
        process.StartInfo.ArgumentList.Add(Path.GetFullPath(configPath));
        return process;
    }

    private void HandleOutput(string source, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var formatted = $"[{DateTime.Now:HH:mm:ss}] {source} | {line}";
        recentOutput.Enqueue(formatted);
        while (recentOutput.Count > MaxBufferedLines && recentOutput.TryDequeue(out _))
        {
        }

        OutputReceived?.Invoke(this, formatted);
    }

    private void HandleProcessExited(Process process)
    {
        var exitCode = process.ExitCode;

        lock (syncRoot)
        {
            if (ReferenceEquals(managedProcess, process))
            {
                managedProcess = null;
            }
        }

        HandleOutput("launcher", $"CPA 进程已退出，ExitCode={exitCode}");
        ProcessExited?.Invoke(this, exitCode);
        process.Dispose();
    }
}

public sealed record ProcessCommandResult(bool Success, string Message, bool AlreadyInDesiredState = false)
{
    public static ProcessCommandResult Succeeded(string message, bool alreadyInDesiredState = false)
        => new(true, message, alreadyInDesiredState);

    public static ProcessCommandResult Failed(string message)
        => new(false, message);
}
