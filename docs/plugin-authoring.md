# Plugin authoring guide

This project supports trusted local plugin loading behind `ENABLE_PLUGINS=true`.

Plugins are **not sandboxed**. A plugin DLL is trusted OS-level .NET code with the same process permissions as the bot. Only load DLLs you wrote, built, and trust.

## Current phase

Implemented:

- `ENABLE_PLUGINS=false` by default.
- `PLUGIN_DIRECTORY=<project root>/plugins` by default.
- `/plugins` scans `plugin.json` manifests and reports diagnostics.
- `/plugins` shows manifest path and whether the declared entry assembly file is present.
- When `ENABLE_PLUGINS=true`, enabled manifests load their trusted entry assembly.
- Plugin tools must implement `TelegramMessagingTool.Tools.IAgentTool` from `TelegramMessagingTool.Abstractions`.
- Plugin tool names must be listed in `allowedToolNames`.
- Duplicate tool names are rejected so plugins cannot override built-in tools or each other.
- `/tools` shows each tool source, including `plugin:<plugin-id>` for plugin tools.

Still intentionally deferred:

- Plugin-specific dependency isolation/unloading.
- Plugin risk/approval policy beyond metadata display.
- Sandboxing untrusted code. Do **not** treat plugins as untrusted extensions.

## Directory layout

Recommended layout:

```text
plugins/
└─ SamplePlugin/
   ├─ plugin.json
   ├─ SampleEchoTool.cs
   ├─ TelegramMessagingTool.SamplePlugin.csproj
   └─ bin/Release/net10.0/TelegramMessagingTool.SamplePlugin.dll
```

Use `plugins/SamplePlugin/plugin.json.example` or the checked-in sample plugin as a starting template.

## Manifest schema

```json
{
  "id": "sample-plugin",
  "name": "Sample Plugin",
  "version": "1.0.0",
  "entryAssembly": "bin/Release/net10.0/TelegramMessagingTool.SamplePlugin.dll",
  "enabled": true,
  "riskLevel": "low",
  "isReadOnly": true,
  "safetySummary": "Echoes provided input only; does not write files, call networks, or change system state.",
  "allowedToolNames": ["sample_echo"]
}
```

## Field rules

| Field | Required | Rule |
|---|---:|---|
| `id` | Yes | Stable plugin ID, usually lowercase kebab-case. |
| `name` | Yes | Human-readable plugin name. |
| `version` | Yes | Plugin version string. |
| `entryAssembly` | Yes | DLL path under the same plugin directory. Paths outside `PLUGIN_DIRECTORY` are rejected. |
| `enabled` | No | Boolean. Only enabled manifests are loaded when `ENABLE_PLUGINS=true`. |
| `riskLevel` | No | `low`, `medium`, or `high`. Defaults to `medium` if omitted. |
| `isReadOnly` | No | Boolean metadata for whether tools are expected to avoid state changes. Defaults to `true` for low risk and `false` otherwise. |
| `safetySummary` | No | Short human-readable safety summary shown in tool metadata. Truncated to 240 characters. |
| `allowedToolNames` | Yes | Non-empty array of tool names this plugin may expose. |

Tool names must match:

```text
^[a-z][a-z0-9_]{1,40}$
```

Valid examples:

```text
sample_echo
repo_summary
create_report_1
```

Invalid examples:

```text
SampleTool
sample-tool
1_sample_tool
x
```

## Validation and loading behavior

The scanner/loader:

- Accepts valid manifests.
- Rejects malformed JSON.
- Rejects missing required fields.
- Rejects invalid risk levels.
- Rejects empty or invalid `allowedToolNames`.
- Rejects duplicate tool names inside one manifest.
- Skips later manifests that duplicate tool names already discovered in another manifest.
- Skips plugin tools that duplicate built-in tool names.
- Loads only enabled manifests when `ENABLE_PLUGINS=true`.
- Loads entry assemblies only when the assembly path stays under `PLUGIN_DIRECTORY`.
- Instantiates only concrete `IAgentTool` types with a public parameterless constructor.
- Registers only tool names included in the manifest `allowedToolNames`.
- Carries manifest `riskLevel`, `isReadOnly`, and `safetySummary` into `/tools` metadata.
- Handles a missing plugin directory gracefully.

## How to inspect plugins

1. Put plugin folders under the configured directory:

```text
plugins/<PluginName>/plugin.json
```

2. Keep `ENABLE_PLUGINS=false` while authoring if you only want diagnostics.

3. Use Telegram:

```text
/plugins
```

Expected output includes:

- Plugin discovery mode
- Plugin directory
- Manifest counts
- Manifest path
- Entry assembly presence: `present` or `missing`
- Assembly loading mode
- Diagnostics

4. Use Telegram:

```text
/tools
```

Expected output includes registered tools and their source, for example:

```text
- sample_echo: Sample trusted plugin tool that echoes its input. (safe/no approval; source: plugin:sample-plugin; risk: low; read-only; safety: Echoes provided input only; does not write files, call networks, or change system state.)
```

## Building the sample plugin

From the project root:

```bash
dotnet build plugins/SamplePlugin/TelegramMessagingTool.SamplePlugin.csproj --configuration Release --nologo
```

Then enable plugins through environment variables before starting the bot:

```text
ENABLE_PLUGINS=true
PLUGIN_DIRECTORY=<project root>/plugins
```

## Safety notes

- Plugin DLLs are trusted OS-level code.
- Do not place untrusted DLLs under `PLUGIN_DIRECTORY`.
- Review plugin source before building/loading it.
- Prefer approval-backed execution for medium/high-risk plugin tools.
- Never allow arbitrary shell/file/network execution without explicit allowlists and approval flow.
