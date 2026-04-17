---
name: unity-cli
description: Use when interacting with a running Unity Editor from the shell - discovering instances, listing available tools, calling Editor commands (console, screenshots, prefab diffs, etc.). Skip grep; tools are discovered at runtime via `unity-cli list`.
---

# unity-cli — How to Use

A CLI that talks to a running Unity Editor over localhost HTTP.
The CLI is a thin forwarder; all commands execute inside Unity.

## Golden Rule

**Tool list is dynamic.** Never guess tool names or parameters from memory.
Always run `unity-cli list` to get the authoritative schema for the currently running Editor. Project-specific custom tools appear there too.

## Three-Step Workflow

```bash
unity-cli status          # 1. Confirm an Editor is running and find its port
unity-cli list            # 2. Get JSON schema of all available tools
unity-cli <tool> [flags]  # 3. Call a tool
```

If `status` shows no instance: Unity isn't running, or the connector package isn't installed in that project.

## Call Syntax

```bash
unity-cli <tool_name> --<param_name> <value> [--<param_name> <value> ...]
```

- Tool name: snake_case (from `list` output's `name` field)
- Parameter name: snake_case (from `list` output's `parameters[].name`)
- Boolean params: bare flag (`--clear`) or `--clear true`
- Multi-word values: quote them

Example:
```bash
unity-cli console --type error --lines 20 --stacktrace user
unity-cli editor_screenshot --path /tmp/shot.png
```

## Multiple Unity Instances

Selection order when multiple Editors are running:

1. `--port <N>` — explicit port (from `status`)
2. `--project <substring>` — matches `projectPath` substring
3. **Auto by CWD**: if the current working directory is inside a project's path, that instance wins
4. Most recent timestamp

Use `status` to see all live instances.

## Global Flags

| Flag | Purpose |
|------|---------|
| `--port <N>` | Pin to a specific Unity instance |
| `--project <substr>` | Pick instance by project path substring |
| `--timeout <ms>` | Request timeout (default 120000) |
| `--help` / `-h` | Help for any subcommand |

## Response Schema

The Unity HTTP server returns a `{success, message, data}` envelope.
**The CLI unwraps it on success and prints only `data`** — so `unity-cli list` outputs a bare JSON array, `unity-cli console` outputs a string or array, etc.

- On success: stdout = the `data` field (array, object, or string), pretty-printed
- On failure: stderr = `Error: <message>` (and `Details: <data>` if present), exit code 1
- `data == null` prints nothing (message-only success — e.g. `unity-cli refresh_unity`)
- Some commands (e.g. entering play mode) close the connection early — CLI reports "... sent (connection closed before response)" on stdout
- `test` command streams / returns structured test results

Pipe directly through `jq` — no unwrapping needed:
```bash
unity-cli list | jq '.[].name'   # works, because stdout is already the array
```

## Built-in Categories (fast path)

These have dedicated CLI subcommands (not generic forwarding):

```bash
unity-cli editor <subcommand>   # Editor lifecycle operations
unity-cli test <subcommand>     # Run/query tests
unity-cli exec <csharp-code>    # Execute C# in Editor (stdin supported: echo ... | unity-cli exec -)
unity-cli status                # Instance discovery
unity-cli update                # Self-update
unity-cli version
```

Everything else is forwarded as-is to the Unity `command` endpoint.

## Common Pitfalls

- **Compile errors in Unity** → commands fail or timeout. Fix scripts first (check `unity-cli console --type error`).
- **Domain reload in progress** → transient timeout. Retry.
- **Stale `status` entry** → CLI auto-cleans dead PIDs on next scan.
- **Browser Origin header** → rejected with 403. CLI path is fine; don't curl from a browser.
- **CORS / OPTIONS** → blocked. This is HTTP-only, not a web API.
- **Concurrent callers** → serialized by Unity (`SemaphoreSlim`). Expect queuing, not parallelism.

## Discovering What Exists (Instead of Grepping)

Don't search the unity-cli repo or the Editor package source to find tool names.
`list` output is the source of truth — it reflects the exact build currently loaded, including project-specific custom `[UnityCliTool]` classes.

```bash
# Find tools by name substring
unity-cli list | jq '.[] | select(.name | contains("prefab"))'

# Inspect one tool's parameters
unity-cli list | jq '.[] | select(.name == "manage_editor")'

# Just the names
unity-cli list | jq -r '.[].name'

# Tools grouped by the `group` field
unity-cli list | jq 'group_by(.group) | map({group: .[0].group, tools: map(.name)})'
```

## Authoring New Tools (Unity-side, quick reference)

In the Unity project, a custom tool is any public static class with:

```csharp
[UnityCliTool(Name = "my_tool", Description = "What it does.")]
public static class MyTool
{
    public class Parameters
    {
        [ToolParameter("What this flag does", Required = true)]
        public string TargetPath { get; set; }
    }

    public static object HandleCommand(JObject @params) { ... }
}
```

- Name auto-derives from class → snake_case if `Name` omitted.
- Class must be in an Editor-loaded assembly (any asmdef works).
- Appears in `list` after next compile. Zero registration code.
