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

$RuntimeEnvironmentVariables = @(
    'TELEGRAM_BOT_TOKEN',
    'ADMIN_CHAT_ID',
    'ALLOWED_CHAT_IDS',
    'ALLOW_PUBLIC_ACCESS',
    'OLLAMA_URL',
    'OLLAMA_MODEL',
    'OLLAMA_MODEL_CHAT',
    'OLLAMA_MODEL_PLAN',
    'OLLAMA_MODEL_DOC_QA',
    'OLLAMA_MODEL_SUMMARY',
    'OLLAMA_MODEL_TOOL_FINAL',
    'OLLAMA_MODEL_IMAGE',
    'OLLAMA_MODEL_VOICE',
    'OLLAMA_EMBEDDING_URL',
    'OLLAMA_EMBEDDING_MODEL',
    'ENABLE_DOCUMENT_EMBEDDINGS',
    'ENABLE_ONLINE_SEARCH',
    'SEARCH_ROUTING_MODE',
    'ENABLE_SAFE_COMMAND_TOOLS',
    'SAFE_COMMAND_PROJECT_ROOT',
    'ENABLE_PLUGINS',
    'PLUGIN_DIRECTORY',
    'TELEGRAM_DB_CONNECTION',
    'APPLY_MIGRATIONS',
    'LOG_MESSAGE_CONTENT',
    'CONVERSATION_MAX_HISTORY'
)

foreach ($Name in $RuntimeEnvironmentVariables) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($Name, 'Process'))) {
        $UserValue = [Environment]::GetEnvironmentVariable($Name, 'User')
        if (-not [string]::IsNullOrWhiteSpace($UserValue)) {
            [Environment]::SetEnvironmentVariable($Name, $UserValue, 'Process')
        }
    }
}

Set-Location $ProjectRoot
& $ReleaseExe
