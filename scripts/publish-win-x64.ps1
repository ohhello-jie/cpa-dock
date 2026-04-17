param(
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "src\CPALauncher.Wpf\CPALauncher.Wpf.csproj"
$PublishProfile = "SingleFile"

$ModeName = if ($FrameworkDependent) { "framework-dependent" } else { "self-contained" }
$PublishDir = Join-Path $ProjectRoot "artifacts\publish\$Runtime\$ModeName"
$PackageDir = Join-Path $ProjectRoot "artifacts\release\CPALauncher-$Runtime-$ModeName"
$ZipPath = Join-Path $ProjectRoot "artifacts\release\CPALauncher-$Runtime-$ModeName.zip"

Write-Host "==> Cleaning previous outputs"
Remove-Item -Recurse -Force $PublishDir -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $PackageDir -ErrorAction SilentlyContinue
Remove-Item -Force $ZipPath -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null

$PublishArgs = @(
    "publish",
    $ProjectFile,
    "-c", "Release",
    "-r", $Runtime,
    "-o", $PublishDir,
    "-p:PublishProfile=$PublishProfile"
)

if ($FrameworkDependent) {
    $PublishArgs += "-p:SelfContained=false"
}
else {
    $PublishArgs += "-p:SelfContained=true"
}

Write-Host "==> Running dotnet publish"
& dotnet @PublishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$PublishedExe = Join-Path $PublishDir "CPALauncher.exe"
if (-not (Test-Path $PublishedExe)) {
    throw "Published CPALauncher.exe was not found: $PublishedExe"
}

$PublishedFiles = @(Get-ChildItem -Path $PublishDir -Recurse -File)
$UnexpectedFiles = @($PublishedFiles | Where-Object {
    $_.FullName -ne $PublishedExe
})

if ($UnexpectedFiles.Count -gt 0) {
    $UnexpectedList = $UnexpectedFiles |
        Sort-Object FullName |
        ForEach-Object { $_.FullName.Replace($PublishDir, '.').TrimStart('\\') }
    throw "Publish output is not a true single-file bundle. Unexpected files:`n - $($UnexpectedList -join "`n - ")"
}

Write-Host "==> Assembling deliverable folder"
Copy-Item $PublishedExe -Destination (Join-Path $PackageDir "CPALauncher.exe") -Force

$ReadmePath = Join-Path $PackageDir "README.txt"
@(
    "CPA Launcher package",
    "",
    "1. Run CPALauncher.exe.",
    "2. On first launch, let the launcher download CPA, then finish the setup wizard.",
    "3. If you already have CPA, you can also point the launcher to an existing cli-proxy-api.exe and config.yaml.",
    "4. Launcher settings are stored at %AppData%\CPALauncher\settings.json.",
    "5. If the CPA path becomes invalid, the launcher will prompt you to install or reconfigure it again.",
    "6. This launcher does not replace CPA itself or rewrite your config.yaml without your confirmation.",
    "",
    "Publish mode: $ModeName",
    "Runtime: $Runtime",
    "Generated at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
) | Set-Content -Path $ReadmePath -Encoding UTF8

Write-Host "==> Creating zip package"
Compress-Archive -Path (Join-Path $PackageDir "*") -DestinationPath $ZipPath -Force

Write-Host ""
Write-Host "Publish completed:"
Write-Host "  PublishDir : $PublishDir"
Write-Host "  PackageDir : $PackageDir"
Write-Host "  ZipPath    : $ZipPath"
