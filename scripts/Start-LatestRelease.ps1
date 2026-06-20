$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$LatestReleaseFile = Join-Path $ProjectRoot '.latest-release'

if (-not (Test-Path -LiteralPath $LatestReleaseFile)) {
    throw "Missing .latest-release file at $LatestReleaseFile"
}

$RelativeReleasePath = (Get-Content -LiteralPath $LatestReleaseFile -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($RelativeReleasePath)) {
    throw ".latest-release is empty."
}

$ReleaseExe = Join-Path $ProjectRoot (Join-Path $RelativeReleasePath 'TelegramMessagingTool.exe')
$ReleaseExe = [System.IO.Path]::GetFullPath($ReleaseExe)

if (-not (Test-Path -LiteralPath $ReleaseExe)) {
    throw "Latest release executable not found: $ReleaseExe"
}

Set-Location $ProjectRoot
& $ReleaseExe
