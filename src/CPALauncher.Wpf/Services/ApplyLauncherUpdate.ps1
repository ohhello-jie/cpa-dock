param(
    [int]$ProcessId = 0,
    [string]$PackageDirectory,
    [Parameter(Mandatory = $true)][string]$InstallDirectory,
    [Parameter(Mandatory = $true)][string]$LauncherPath,
    [ValidateRange(1, 90)][int]$WaitSeconds = 90,
    [string]$RecoverDirectory
)

$ErrorActionPreference = 'Stop'
$LogPath = Join-Path $PSScriptRoot 'apply-launcher-update.log'
$transaction = $null
$journal = $null
$canRestart = $false
$rollbackComplete = $true
$lock = $null
$newProcess = $null

function Write-UpdateLog([string]$Message) {
    try { Add-Content -LiteralPath $LogPath -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message" -Encoding UTF8 } catch { }
}
function Flush-File([string]$Path) {
    $stream = [IO.File]::Open($Path, 'Open', 'Write', 'Read')
    try { $stream.Flush($true) } finally { $stream.Dispose() }
}
function Get-Checksum([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function Save-Journal {
    $path = Join-Path $transaction 'state.json'
    $temp = $path + '.tmp'
    [IO.File]::WriteAllText($temp, ($journal | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    Flush-File $temp
    if ([IO.File]::Exists($path)) { [IO.File]::Replace($temp, $path, [NullString]::Value) }
    else { [IO.File]::Move($temp, $path) }
}
function Validate-Transaction([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ([IO.Path]::GetDirectoryName($resolved) -ne $InstallDirectory -or
        [IO.Path]::GetFileName($resolved) -notmatch '^\.cpa-update-[a-f0-9]{32}$') {
        throw 'Invalid recovery directory.'
    }
    return $resolved
}
function Validate-Entries {
    foreach ($entry in $journal.Entries) {
        if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($entry.Destination)) -ne $InstallDirectory -or
            [IO.Path]::GetFileName($entry.Destination) -notin @('CPA Dock.exe','CPALauncher.exe','README.txt','LICENSE.txt','THIRD-PARTY-LICENSES.txt') -or
            [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($entry.Backup)) -ne $transaction -or
            [IO.Path]::GetFileName($entry.Backup) -notmatch '^old-\d+$') { throw 'Invalid recovery entry.' }
    }
}
function Stop-NewProcess {
    if (-not $journal.ProcessId) { return }
    $process = Get-Process -Id $journal.ProcessId -ErrorAction SilentlyContinue
    if ($process -and $process.StartTime.ToUniversalTime().Ticks.ToString() -eq $journal.ProcessStarted -and
        $process.Path -eq $LauncherPath) {
        $process.Kill()
        if (-not $process.WaitForExit(10000)) { throw 'Updated launcher did not exit.' }
    }
}
function Restore-Files {
    Validate-Entries
    Stop-NewProcess
    foreach ($entry in $journal.Entries) {
        try {
            if ($entry.Existing) {
                if ([IO.File]::Exists($entry.Destination) -and (Get-Checksum $entry.Destination) -eq $entry.OldHash) { continue }
                if ((Get-Checksum $entry.Backup) -ne $entry.OldHash) { throw 'Backup checksum mismatch.' }
                # Retain the immutable backup until every file has been restored.
                $restore = Join-Path $transaction 'restore.tmp'
                [IO.File]::Copy($entry.Backup, $restore, $true)
                Flush-File $restore
                if ([IO.File]::Exists($entry.Destination)) { [IO.File]::Replace($restore, $entry.Destination, [NullString]::Value) }
                else { [IO.File]::Move($restore, $entry.Destination) }
            } elseif ([IO.File]::Exists($entry.Destination)) {
                if ((Get-Checksum $entry.Destination) -ne $entry.NewHash) { throw 'Installed file changed; retained for inspection.' }
                [IO.File]::Delete($entry.Destination)
            }
        } catch {
            $script:rollbackComplete = $false
            Write-UpdateLog "Restore failed for $($entry.Destination): $($_.Exception.Message). Backup: $($entry.Backup)"
        }
    }
    if ($script:rollbackComplete) { $journal.Phase = 'restored'; Save-Journal }
}
function Start-Launcher([switch]$Recovered) {
    $options = @{ FilePath = $LauncherPath; WorkingDirectory = $InstallDirectory; PassThru = $true; WindowStyle = 'Hidden' }
    $options.ArgumentList = if ($Recovered) { '--update-failed' } else { '--update-apply "' + $transaction + '"' }
    $process = Start-Process @options
    if (-not $Recovered) {
        $script:newProcess = $process
        $journal.ProcessId = $process.Id
        $journal.ProcessStarted = $process.StartTime.ToUniversalTime().Ticks.ToString()
        Save-Journal
    }
    if ($process.WaitForExit(3000)) { throw "Launcher exited immediately (code $($process.ExitCode))." }
    if (-not $Recovered) {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        $ready = Join-Path $transaction 'ready'
        while (-not [IO.File]::Exists($ready) -and [DateTime]::UtcNow -lt $deadline -and -not $process.HasExited) { Start-Sleep -Milliseconds 100 }
        if ($process.HasExited -or -not [IO.File]::Exists($ready) -or [IO.File]::ReadAllText($ready) -ne $process.Id.ToString()) {
            throw 'Launcher did not acknowledge startup.'
        }
    }
    Write-UpdateLog "Launcher started (PID $($process.Id))."
}

try {
    $InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
    $LauncherPath = [IO.Path]::GetFullPath($LauncherPath)
    if ([IO.Path]::GetDirectoryName($LauncherPath) -ne $InstallDirectory -or
        [IO.Path]::GetFileName($LauncherPath) -notin @('CPA Dock.exe', 'CPALauncher.exe')) { throw 'Launcher path is outside the installation directory.' }
    $lockPath = Join-Path $InstallDirectory '.cpa-update.lock'
    $lock = [IO.FileStream]::new($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None, 1, [IO.FileOptions]::DeleteOnClose)
    if ($ProcessId -gt 0) {
        $oldProcess = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($oldProcess -and -not $oldProcess.WaitForExit($WaitSeconds * 1000)) { throw 'Launcher is still running; no files were changed.' }
    }
    $canRestart = $true
    if ($RecoverDirectory) {
        $transaction = Validate-Transaction $RecoverDirectory
        $journal = Get-Content -LiteralPath (Join-Path $transaction 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($journal.Phase -notin @('pending','restored','committed')) { throw 'Invalid recovery state.' }
        if ($journal.Phase -ne 'committed') { Restore-Files }
        if (-not $rollbackComplete) { throw 'Recovery is incomplete.' }
        Start-Launcher -Recovered
        Write-UpdateLog 'Recovery completed.'
    } else {
        # Never overwrite or discard an unfinished earlier transaction.
        if (@(Get-ChildItem -LiteralPath $InstallDirectory -Directory -Filter '.cpa-update-*').Count -gt 0) { $canRestart = $false; throw 'An earlier update requires recovery.' }
        $PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\')
        $packageExe = @('CPA Dock.exe','CPALauncher.exe') | ForEach-Object { Join-Path $PackageDirectory $_ } | Where-Object { [IO.File]::Exists($_) } | Select-Object -First 1
        if (-not $packageExe) { throw 'Launcher executable was not found in update package.' }
        $transaction = Join-Path $InstallDirectory ('.cpa-update-' + [Guid]::NewGuid().ToString('N'))
        [IO.Directory]::CreateDirectory($transaction) | Out-Null
        # Recovery stays beside the installation even if the download cache disappears.
        Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $transaction 'recover.ps1')
        $command = '@echo off' + "`r`n" + 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0recover.ps1" -RecoverDirectory "%~dp0." -InstallDirectory "%~dp0.." -LauncherPath "%~dp0..\' + [IO.Path]::GetFileName($LauncherPath) + '"' + "`r`npause`r`n"
        [IO.File]::WriteAllText((Join-Path $transaction '恢复旧版.cmd'), $command, [Text.Encoding]::ASCII)
        $entries = [System.Collections.Generic.List[object]]::new()
        # Only shipped application files can be replaced; configuration and credentials are never package content.
        $files = @($packageExe) + @('README.txt','LICENSE.txt','THIRD-PARTY-LICENSES.txt' | ForEach-Object { Join-Path $PackageDirectory $_ } | Where-Object { [IO.File]::Exists($_) })
        foreach ($file in $files) {
            $destination = if ($file -eq $packageExe) { $LauncherPath } else { Join-Path $InstallDirectory ([IO.Path]::GetFileName($file)) }
            $stage = Join-Path $transaction ('new-' + $entries.Count)
            $backup = Join-Path $transaction ('old-' + $entries.Count)
            [IO.File]::Copy($file, $stage)
            Flush-File $stage
            $existing = [IO.File]::Exists($destination)
            $oldHash = $null
            if ($existing) { [IO.File]::Copy($destination, $backup); Flush-File $backup; $oldHash = (Get-Checksum $backup) }
            $entries.Add([pscustomobject]@{ Destination=$destination; Stage=$stage; Backup=$backup; Existing=$existing; OldHash=$oldHash; NewHash=(Get-Checksum $stage) })
        }
        $journal = [pscustomobject]@{ Phase='pending'; Entries=@($entries.ToArray()); ProcessId=0; ProcessStarted='' }
        Save-Journal
        foreach ($entry in ($entries | Sort-Object { $_.Destination -eq $LauncherPath })) {
            if ($entry.Existing) { [IO.File]::Replace($entry.Stage, $entry.Destination, [NullString]::Value) }
            else { [IO.File]::Move($entry.Stage, $entry.Destination) }
            Write-UpdateLog "Replaced $($entry.Destination)."
        }
        Start-Launcher
        $journal.Phase = 'committed'
        try { Save-Journal } catch { $journal.Phase = 'pending'; throw }
        Write-UpdateLog 'Update completed.'
    }
}
catch {
    Write-UpdateLog "Update failed: $($_.Exception.Message)"
    if ($journal -and $journal.Phase -ne 'committed') {
        try { Restore-Files } catch { $rollbackComplete = $false; Write-UpdateLog "Restore failed: $($_.Exception.Message)" }
    } elseif ($RecoverDirectory) { $rollbackComplete = $false }
    if ($rollbackComplete -and $canRestart) {
        Write-UpdateLog 'Original files restored; restarting the previous launcher.'
        try { Start-Launcher -Recovered } catch { Write-UpdateLog "Previous launcher could not start: $($_.Exception.Message)" }
    }
    if (-not $rollbackComplete) { Write-UpdateLog "Recovery files retained at $transaction. Run recover.ps1 or the recovery command after closing the launcher." }
    exit 1
}
finally {
    if ($transaction -and $rollbackComplete -and [IO.Directory]::Exists($transaction)) {
        $resolved = Validate-Transaction $transaction
        Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($lock) { $lock.Dispose() }
}
