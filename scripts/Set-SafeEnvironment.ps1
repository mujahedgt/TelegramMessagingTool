<#
.SYNOPSIS
Reverts risky TelegramMessagingTool Windows User environment flags to safer local defaults.

.DESCRIPTION
This script writes only Windows User environment variables. It does not remove or print secrets.
Restart terminals/apps after running so new User variables load.
#>
[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Set-UserEnvironmentVariable {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    [Environment]::SetEnvironmentVariable($Name, $Value, 'User')
    Write-Host "Set User env $Name=$Value"
}

$fullProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
$pluginDirectory = [System.IO.Path]::GetFullPath((Join-Path $fullProjectRoot 'plugins'))

$settings = [ordered]@{
    'ALLOW_PUBLIC_ACCESS' = 'false'
    'LOG_MESSAGE_CONTENT' = 'false'
    'ENABLE_REPO_WRITE_TOOLS' = 'false'
    'ENABLE_GITHUB_WRITE_TOOLS' = 'false'
    'ENABLE_SAFE_COMMAND_TOOLS' = 'false'
    'ENABLE_PLUGINS' = 'false'
    'ENABLE_ONLINE_SEARCH' = 'false'
    'SEARCH_ROUTING_MODE' = 'heuristic'
    'ENABLE_DOCUMENT_EMBEDDINGS' = 'false'
    'ENABLE_IMAGE_VISION' = 'false'
    'ENABLE_AUDIO_TRANSCRIPTION' = 'false'
    'ENABLE_TEXT_TO_SPEECH' = 'false'
    'ENABLE_TELEGRAM_TYPING_INDICATOR' = 'false'
    'ENABLE_STREAMING_RESPONSES' = 'false'
    'SAFE_COMMAND_PROJECT_ROOT' = $fullProjectRoot
    'PLUGIN_DIRECTORY' = $pluginDirectory
    'ENABLE_GITHUB_TOOLS' = 'false'
}

foreach ($entry in $settings.GetEnumerator()) {
    Set-UserEnvironmentVariable -Name $entry.Key -Value ([string]$entry.Value)
}

Write-Host ''
Write-Host 'Safer local profile applied to Windows User environment.'
Write-Host 'Secrets were intentionally not removed or printed.'
Write-Host 'Restart the bot process or terminal for User environment changes to take effect.'
