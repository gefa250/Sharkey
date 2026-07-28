param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $projectRoot "VersionInfo.cs"

if ($Version -notmatch '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?<suffix>-(?:0|[0-9A-Za-z-][0-9A-Za-z.-]*))?$') {
    throw "版本号必须符合语义化版本，例如 0.2.0-dev 或 0.2.0。"
}

$numeric = "$($Matches.major).$($Matches.minor).$($Matches.patch).0"
$source = Get-Content -LiteralPath $versionFile -Raw
$source = [regex]::Replace(
    $source,
    'SemanticVersion\s*=\s*"[^"]+"',
    "SemanticVersion = `"$Version`"")
$source = [regex]::Replace(
    $source,
    'AssemblyVersion\s*=\s*"[^"]+"',
    "AssemblyVersion = `"$numeric`"")
$source = [regex]::Replace(
    $source,
    'FileVersion\s*=\s*"[^"]+"',
    "FileVersion = `"$numeric`"")
[IO.File]::WriteAllText(
    $versionFile,
    $source,
    (New-Object Text.UTF8Encoding($false)))

Write-Host "Sharkey 版本已调整为 $Version ($numeric)。"
Write-Host "请更新 CHANGELOG.md、提交变更，再运行 Package-Release.ps1。"
