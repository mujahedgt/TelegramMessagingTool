<#
.SYNOPSIS
Sets Mujahed's local Windows User environment profile for the TelegramMessagingTool development machine.

.DESCRIPTION
This script writes only non-secret User environment variables. It does not set tokens, connection strings,
provider command paths, or other credentials. Restart terminals/apps after running so new User variables load.
#>
[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$EnablePublicAccess,
    [switch]$EnableContentLogging
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
    'ALLOW_PUBLIC_ACCESS' = if ($EnablePublicAccess) { 'true' } else { 'false' }
    'LOG_MESSAGE_CONTENT' = if ($EnableContentLogging) { 'true' } else { 'false' }
    'ENABLE_DOCUMENT_EMBEDDINGS' = 'true'
    'ENABLE_ONLINE_SEARCH' = 'true'
    'SEARCH_ROUTING_MODE' = 'llm'
    'ENABLE_IMAGE_VISION' = 'true'
    'ENABLE_AUDIO_TRANSCRIPTION' = 'true'
    'ENABLE_TEXT_TO_SPEECH' = 'true'
    'ENABLE_TELEGRAM_TYPING_INDICATOR' = 'true'
    'ENABLE_SAFE_COMMAND_TOOLS' = 'true'
    'SAFE_COMMAND_PROJECT_ROOT' = $fullProjectRoot
    'ENABLE_REPO_WRITE_TOOLS' = 'true'
    'ENABLE_PLUGINS' = 'true'
    'PLUGIN_DIRECTORY' = $pluginDirectory
    'ENABLE_GITHUB_TOOLS' = 'true'
    'ENABLE_GITHUB_WRITE_TOOLS' = 'true'
    'GITHUB_DEFAULT_OWNER' = 'mujahedgt'
    'GITHUB_DEFAULT_REPO' = 'TelegramMessagingTool'
    'GITHUB_ALLOWED_REPOS' = 'mujahedgt/TelegramMessagingTool,mujahedgt/IsolationForestServer'
}

foreach ($entry in $settings.GetEnumerator()) {
    Set-UserEnvironmentVariable -Name $entry.Key -Value ([string]$entry.Value)
}

Write-Host ''
Write-Host 'Local development profile applied to Windows User environment.'
Write-Host 'Secrets were intentionally not written. Keep TELEGRAM_BOT_TOKEN, GITHUB_TOKEN, TELEGRAM_DB_CONNECTION, and provider command paths configured separately if needed.'
Write-Host 'Restart the bot process or terminal for User environment changes to take effect.'
Write-Host 'Use scripts/Set-SafeEnvironment.ps1 to revert risky flags.'
