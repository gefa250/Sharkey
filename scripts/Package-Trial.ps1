param([string]$OutputDirectory = "dist\trial")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $source = Get-Content -LiteralPath "VersionInfo.cs" -Raw
    $version = [regex]::Match($source, 'SemanticVersion\s*=\s*"([^"]+)"').Groups[1].Value
    if ($version -notmatch '^\d+\.\d+\.\d+-dev$') { throw "Expected a development version." }
    $commit = (git rev-parse --short HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Unable to read Git commit." }
    $suffix = if (git diff --name-only HEAD) { "-working" } else { "" }
    & .\build.cmd bin/Trial/
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
    & .\test.cmd (Join-Path $root "bin\Trial\Sharkey.exe")
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
    $exe = Join-Path $root "bin\Trial\Sharkey.exe"
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion -ne $version) { throw "Version mismatch." }
    $out = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $stage = Join-Path $out ("Sharkey-" + $version + "-" + $commit + $suffix)
    if (Test-Path -LiteralPath $stage) { throw "Trial output already exists; choose another output directory." }
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item -LiteralPath $exe -Destination $stage
    foreach ($file in @("README.md", "CHANGELOG.md", "LICENSE")) { Copy-Item -LiteralPath $file -Destination $stage }
    $hash = (Get-FileHash -LiteralPath (Join-Path $stage "Sharkey.exe") -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $stage "Sharkey.exe.sha256"), "$hash  Sharkey.exe`r`n")
    $zip = "$stage-win-x64.zip"
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
    $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$zip.sha256", "$zipHash  $([IO.Path]::GetFileName($zip))`r`n")
    Write-Output "TRIAL_PACKAGE=$zip"
    Write-Output "SHA256=$zipHash"
} finally { Pop-Location }
