# Plugin authoring guide

This project currently supports **plugin manifest discovery only**. The runtime can scan `plugin.json` files and show them through `/plugins`, but it does **not** load plugin assemblies, register plugin tools, or execute plugin code yet.

## Current safe phase

Implemented:

- `ENABLE_PLUGINS=false` by default.
- `PLUGIN_DIRECTORY=<project root>/plugins` by default.
- `/plugins` scans `plugin.json` manifests and reports diagnostics.
- `/plugins` shows manifest path and whether the declared entry assembly file is present.

Not implemented yet:

- Loading `.dll` plugin assemblies.
- Instantiating plugin classes.
- Registering plugin tools in `/tools`.
- Running plugin code from Telegram/model tool calls.

Treat plugin assemblies as trusted OS-level code. Do not place untrusted DLLs in the plugin directory.

## Directory layout

Recommended layout:

```text
plugins/
└─ SamplePlugin/
   ├─ plugin.json
   └─ SamplePlugin.dll
```

Use `plugins/SamplePlugin/plugin.json.example` in this repository as a starting template.

## Manifest schema

```json
{
  "id": "sample-plugin",
  "name": "Sample Plugin",
  "version": "1.0.0",
  "entryAssembly": "SamplePlugin.dll",
  "enabled": false,
  "riskLevel": "low",
  "allowedToolNames": ["sample_tool"]
}
```

## Field rules

| Field | Required | Rule |
|---|---:|---|
| `id` | Yes | Stable plugin ID, usually lowercase kebab-case. |
| `name` | Yes | Human-readable plugin name. |
| `version` | Yes | Plugin version string. |
| `entryAssembly` | Yes | DLL filename under the same plugin folder. Current phase only checks whether it exists. |
| `enabled` | No | Boolean. Discovery reports it, but no plugin code is loaded yet. |
| `riskLevel` | No | `low`, `medium`, or `high`. Defaults to `medium` if omitted. |
| `allowedToolNames` | Yes | Non-empty array of tool names this plugin may expose later. |

Tool names must match:

```text
^[a-z][a-z0-9_]{1,40}$
```

Valid examples:

```text
sample_tool
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

## Validation behavior

The scanner:

- Accepts valid manifests.
- Rejects malformed JSON.
- Rejects missing required fields.
- Rejects invalid risk levels.
- Rejects empty or invalid `allowedToolNames`.
- Rejects duplicate tool names inside one manifest.
- Skips later manifests that duplicate tool names already discovered in another manifest.
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
- Diagnostics

## Safety notes for future phases

When assembly loading is added later:

- Keep it behind `ENABLE_PLUGINS=true`.
- Only load trusted local DLLs.
- Continue showing plugin source/path in `/plugins`.
- Register only manifest-allowlisted tool names.
- Prefer approval-backed execution for medium/high-risk tools.
- Never allow arbitrary shell/file/network execution without explicit allowlists and approval flow.
