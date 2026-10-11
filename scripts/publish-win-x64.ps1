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

$ArtifactRoot = [IO.Path]::GetFullPath((Join-Path $ProjectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
foreach ($OutputPath in @($PublishDir, $PackageDir, $ZipPath)) {
    if (-not [IO.Path]::GetFullPath($OutputPath).StartsWith($ArtifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Output path must stay inside artifacts: $OutputPath"
    }
}

Write-Host "==> Cleaning previous outputs"
Remove-Item -LiteralPath $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $PackageDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $ZipPath -Force -ErrorAction SilentlyContinue

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
    "CPA Dock",
    "",
    "1. Run CPALauncher.exe.",
    "2. Install CPA, or select an existing executable and config.yaml in Settings.",
    "3. Start CPA and open its management page.",
    "",
    "Settings: %AppData%\CPALauncher\settings.json",
    "Repository: https://github.com/ohhello-jie/cpa-dock",
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
