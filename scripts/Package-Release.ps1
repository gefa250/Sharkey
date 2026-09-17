param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [switch]$SkipTag
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $projectRoot "VersionInfo.cs"
$changeLog = Join-Path $projectRoot "CHANGELOG.md"
$dist = Join-Path $projectRoot "dist"

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "正式发布版本必须是 x.y.z，例如 0.1.0。"
}
if (-not (Test-Path (Join-Path $projectRoot ".git"))) {
    throw "当前目录尚未初始化 Git。"
}
Push-Location $projectRoot
try {
    $dirty = git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw "无法读取 Git 工作区状态。" }
    if ($dirty) { throw "工作区存在未提交或未跟踪的修改，不能发布。" }

    $versionSource = Get-Content -LiteralPath $versionFile -Raw
    if ($versionSource -notmatch ('SemanticVersion\s*=\s*"' + [regex]::Escape($Version) + '"')) {
        throw "VersionInfo.cs 与目标版本 $Version 不一致。"
    }
    $changeSource = Get-Content -LiteralPath $changeLog -Raw
    if ($changeSource -notmatch ('(?m)^##\s+' + [regex]::Escape($Version) + '\s+-\s+\d{4}-\d{2}-\d{2}\s*$')) {
        throw "CHANGELOG.md 缺少版本 $Version 的正式发布日期章节。"
    }
    if ((git tag --list "v$Version") -eq "v$Version") {
        throw "Git 标签 v$Version 已存在。"
    }

    & (Join-Path $projectRoot "build.cmd")
    if ($LASTEXITCODE -ne 0) { throw "Release 构建失败。" }
    & (Join-Path $projectRoot "test.cmd")
    if ($LASTEXITCODE -ne 0) { throw "自动测试失败。" }

    $exe = Join-Path $projectRoot "bin\Release\Sharkey.exe"
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
    if ($actual -ne $Version) {
        throw "EXE ProductVersion 为 $actual，与目标版本 $Version 不一致。"
    }

    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $stage = Join-Path $env:TEMP ("Sharkey-release-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    try {
        Copy-Item -LiteralPath $exe -Destination $stage
        Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $stage
        Copy-Item -LiteralPath $changeLog -Destination $stage
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

        $pdb = Join-Path $projectRoot "bin\Release\Sharkey.pdb"
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
        if ($LASTEXITCODE -ne 0) { throw "创建 Git 标签失败。" }
    }
    Write-Host "GitHub Release 文件已生成：dist\Sharkey-win-x64.exe、.sha256、RELEASE-NOTES.md"
    Write-Host "归档包已生成：dist\Sharkey-v$Version-win-x64.zip"
    if (-not $SkipTag) { Write-Host "已创建本地标签 v$Version（未推送）。" }
}
finally {
    Pop-Location
}
