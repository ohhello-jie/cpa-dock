param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][string]$InstallDirectory,
    [Parameter(Mandatory = $true)][string]$LauncherPath,
    [ValidateRange(1, 90)][int]$WaitSeconds = 90
)

$ErrorActionPreference = 'Stop'
$LogPath = Join-Path $PSScriptRoot 'apply-launcher-update.log'
$transaction = $null
$applied = [System.Collections.Generic.List[object]]::new()
$createdDirectories = [System.Collections.Generic.List[string]]::new()
$canRestart = $false
$rollbackComplete = $true

function Write-UpdateLog([string]$Message) {
    try { Add-Content -LiteralPath $LogPath -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message" -Encoding UTF8 } catch { }
}

function Ensure-Directory([string]$Path) {
    if (-not [IO.Directory]::Exists($Path)) {
        Ensure-Directory ([IO.Path]::GetDirectoryName($Path))
        [IO.Directory]::CreateDirectory($Path) | Out-Null
        $createdDirectories.Add($Path)
    }
}

function Start-Launcher([switch]$Recovered) {
    $options = @{ FilePath = $LauncherPath; WorkingDirectory = $InstallDirectory; PassThru = $true }
    if ($Recovered) { $options.ArgumentList = '--update-failed' }
    $process = Start-Process @options
    if ($process.WaitForExit(3000)) { throw "Launcher exited immediately (code $($process.ExitCode))." }
    Write-UpdateLog "Launcher started (PID $($process.Id))."
}

try {
    $InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
    $PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\')
    $LauncherPath = [IO.Path]::GetFullPath($LauncherPath)
    if ([IO.Path]::GetDirectoryName($LauncherPath) -ne $InstallDirectory -or
        [IO.Path]::GetFileName($LauncherPath) -notin @('CPA Dock.exe', 'CPALauncher.exe')) {
        throw 'Launcher path is outside the installation directory.'
    }
    $oldProcess = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($oldProcess) {
        Write-UpdateLog "Waiting for launcher process $ProcessId to exit."
        if (-not $oldProcess.WaitForExit($WaitSeconds * 1000)) {
            throw 'Launcher is still running; no files were changed.'
        }
    }
    $canRestart = $true
    $packageExe = @('CPA Dock.exe', 'CPALauncher.exe') |
        ForEach-Object { Join-Path $PackageDirectory $_ } |
        Where-Object { [IO.File]::Exists($_) } | Select-Object -First 1
    if (-not $packageExe) { throw 'Launcher executable was not found in update package.' }

    # Stage on the destination volume. Failure here must not change installed files.
    $transaction = Join-Path $InstallDirectory ('.cpa-update-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($transaction) | Out-Null
    $entries = [System.Collections.Generic.List[object]]::new()
    $files = @(Get-ChildItem -LiteralPath $PackageDirectory -File -Recurse | Sort-Object FullName)
    foreach ($file in $files) {
        if ($file.FullName -ne $packageExe -and $file.Name -in @('CPA Dock.exe', 'CPALauncher.exe')) { continue }
        # Portable settings and CPA configuration belong to the user.
        if ($file.Name -in @('settings.json', 'config.yaml')) { continue }
        $relative = $file.FullName.Substring($PackageDirectory.Length + 1)
        $destination = if ($file.FullName -eq $packageExe) { $LauncherPath } else { Join-Path $InstallDirectory $relative }
        $stage = Join-Path $transaction ('new-' + $entries.Count)
        $backup = Join-Path $transaction ('old-' + $entries.Count)
        Copy-Item -LiteralPath $file.FullName -Destination $stage
        $entries.Add([pscustomobject]@{ Destination = $destination; Stage = $stage; Backup = $backup; Existing = [IO.File]::Exists($destination) })
    }

    # Commit the executable last; a locked executable also exercises rollback of its sidecars.
    foreach ($entry in ($entries | Sort-Object { $_.Destination -eq $LauncherPath })) {
        Ensure-Directory ([IO.Path]::GetDirectoryName($entry.Destination))
        if ($entry.Existing) {
            # ReplaceFile can fail after renaming the original to its backup (Win32 error 1177).
            $applied.Add($entry)
            [IO.File]::Replace($entry.Stage, $entry.Destination, $entry.Backup)
        } else {
            [IO.File]::Move($entry.Stage, $entry.Destination)
            $applied.Add($entry)
        }
        Write-UpdateLog "Replaced $($entry.Destination)."
    }
    Start-Launcher
    Write-UpdateLog 'Update completed.'
}
catch {
    Write-UpdateLog "Update failed: $($_.Exception.Message)"
    for ($i = $applied.Count - 1; $i -ge 0; $i--) {
        $entry = $applied[$i]
        try {
            if ($entry.Existing) {
                if (-not [IO.File]::Exists($entry.Backup)) {
                    if (-not [IO.File]::Exists($entry.Destination)) { throw 'Neither original nor backup exists.' }
                    continue
                }
                if ([IO.File]::Exists($entry.Destination)) { [IO.File]::Replace($entry.Backup, $entry.Destination, $entry.Stage) }
                else { [IO.File]::Move($entry.Backup, $entry.Destination) }
            } else { [IO.File]::Delete($entry.Destination) }
        } catch {
            $rollbackComplete = $false
            Write-UpdateLog "Restore failed for $($entry.Destination): $($_.Exception.Message). Backup: $($entry.Backup)"
        }
    }
    for ($i = $createdDirectories.Count - 1; $i -ge 0; $i--) {
        try { [IO.Directory]::Delete($createdDirectories[$i], $false) } catch { }
    }
    if ($rollbackComplete -and $canRestart) {
        Write-UpdateLog 'Original files restored; restarting the previous launcher.'
        try { Start-Launcher -Recovered } catch { Write-UpdateLog "Previous launcher could not start: $($_.Exception.Message)" }
    }
    if (-not $rollbackComplete) { Write-UpdateLog "Recovery files retained at $transaction. Do not remove them before restoring the installation." }
    exit 1
}
finally {
    # Only delete the exact transaction directory created by this invocation.
    if ($transaction -and $rollbackComplete -and [IO.Directory]::Exists($transaction)) {
        $resolved = [IO.Path]::GetFullPath($transaction)
        if ([IO.Path]::GetDirectoryName($resolved) -eq $InstallDirectory -and
            [IO.Path]::GetFileName($resolved).StartsWith('.cpa-update-')) {
            Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    if ($canRestart -and $rollbackComplete) {
        $packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'package'))
        $packagePrefix = $packageRoot + [IO.Path]::DirectorySeparatorChar
        if (($PackageDirectory -eq $packageRoot -or $PackageDirectory.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) -and
            $InstallDirectory -ne $packageRoot -and -not $InstallDirectory.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath (Join-Path $PSScriptRoot 'package.zip') -Force -ErrorAction SilentlyContinue
        }
    }
}
