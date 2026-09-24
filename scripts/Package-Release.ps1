param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [switch]$SkipTag
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $projectRoot "VersionInfo.cs"
$changeLog = Join-Path $projectRoot "CHANGELOG.md"
$dist = Join-Path $projectRoot ("dist\v" + $Version)

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "A release version must use x.y.z format, for example 0.1.0."
}
if (-not (Test-Path (Join-Path $projectRoot ".git"))) {
    throw "The project directory is not a Git repository."
}
Push-Location $projectRoot
try {
    $dirty = git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw "Unable to read the Git working tree status." }
    if ($dirty) { throw "The working tree has uncommitted or untracked files." }

    $versionSource = Get-Content -LiteralPath $versionFile -Raw -Encoding UTF8
    if ($versionSource -notmatch ('SemanticVersion\s*=\s*"' + [regex]::Escape($Version) + '"')) {
        throw "VersionInfo.cs does not match release version $Version."
    }
    $changeSource = Get-Content -LiteralPath $changeLog -Raw -Encoding UTF8
    if ($changeSource -notmatch ('(?m)^##\s+' + [regex]::Escape($Version) + '\s+-\s+\d{4}-\d{2}-\d{2}\s*$')) {
        throw "CHANGELOG.md has no dated release section for version $Version."
    }
    if ((git tag --list "v$Version") -eq "v$Version") {
        throw "Git tag v$Version already exists."
    }

    $releaseOutput = "bin\Release-$Version"
    & (Join-Path $projectRoot "build.cmd") $releaseOutput
    if ($LASTEXITCODE -ne 0) { throw "Release build failed." }
    & (Join-Path $projectRoot "test.cmd") "$releaseOutput\Sharkey.exe"
    if ($LASTEXITCODE -ne 0) { throw "Automated tests failed." }

    $exe = Join-Path $projectRoot "$releaseOutput\Sharkey.exe"
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
    if ($actual -ne $Version) {
        throw "EXE ProductVersion is $actual, expected $Version."
    }

    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $stage = Join-Path $env:TEMP ("Sharkey-release-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    try {
        Copy-Item -LiteralPath $exe -Destination $stage
        Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $stage
        Copy-Item -LiteralPath $changeLog -Destination $stage
        Copy-Item -LiteralPath (Join-Path $projectRoot "LICENSE") -Destination $stage
        $notes = Join-Path $dist "RELEASE-NOTES-v$Version.md"
        $section = [regex]::Match(
            $changeSource,
            '(?ms)^##\s+' + [regex]::Escape($Version) + '\s+.*?(?=^##\s+|\z)').Value.Trim()
        [IO.File]::WriteAllText(
            $notes,
            "# Sharkey v$Version`r`n`r`n$section`r`n",
            (New-Object Text.UTF8Encoding($false)))

        $releaseExe = Join-Path $dist "Sharkey-win-x64.exe"
        Copy-Item -LiteralPath $exe -Destination $releaseExe -Force
        $fixedNotes = Join-Path $dist "RELEASE-NOTES.md"
        Copy-Item -LiteralPath $notes -Destination $fixedNotes -Force
        $releaseHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $releaseExe).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText(
            "$releaseExe.sha256",
            "$releaseHash  Sharkey-win-x64.exe`r`n",
            (New-Object Text.UTF8Encoding($false)))

        $package = Join-Path $dist "Sharkey-v$Version-win-x64.zip"
        if (Test-Path $package) { Remove-Item -LiteralPath $package -Force }
        Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $package

        $pdb = Join-Path $projectRoot "$releaseOutput\Sharkey.pdb"
        if (Test-Path $pdb) {
            $symbols = Join-Path $dist "Sharkey-v$Version-symbols.zip"
            if (Test-Path $symbols) { Remove-Item -LiteralPath $symbols -Force }
            Compress-Archive -LiteralPath $pdb -DestinationPath $symbols
        }
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText(
            "$package.sha256",
            "$hash  $(Split-Path -Leaf $package)`r`n",
            (New-Object Text.UTF8Encoding($false)))
    }
    finally {
        if (Test-Path $stage) {
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }

    if (-not $SkipTag) {
        git tag -a "v$Version" -m "Sharkey v$Version"
        if ($LASTEXITCODE -ne 0) { throw "Unable to create the Git tag." }
    }
    Write-Host "GitHub Release assets created: dist\v$Version\Sharkey-win-x64.exe, .sha256, RELEASE-NOTES.md"
    Write-Host "Archive created: dist\v$Version\Sharkey-v$Version-win-x64.zip"
    if (-not $SkipTag) { Write-Host "Created local tag v$Version (not pushed)." }
}
finally {
    Pop-Location
}
