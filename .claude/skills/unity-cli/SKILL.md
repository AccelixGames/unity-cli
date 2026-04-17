---
name: unity-cli
description: Use when calling a running Unity Editor from the shell (console, screenshots, exec C#, custom tools). Skip code grep — tools are discovered live via `unity-cli list`.
---

# unity-cli

CLI forwards commands over localhost HTTP to a running Unity Editor. All logic lives in the Editor (`[UnityCliTool]` static classes).

## Golden Rule

**Never guess tool names/params.** Run `unity-cli list` — it's the authoritative schema for the Editor currently loaded, including project-specific custom tools.

## Workflow

```bash
unity-cli status                            # live instances + ports
unity-cli list                              # JSON array of all tools
unity-cli <tool> --<snake_key> <value> ...  # call
```

No `status` output ⇒ Unity not running or connector package not installed.

## Syntax rules

- Tool + param names are snake_case (from `list`)
- Bool params: bare flag or `--flag true`
- Stdin: `unity-cli exec - <<'EOF' ... EOF`

## Multi-instance selection

1. `--port N` (from `status`)
2. `--project <substring>` matches `projectPath`
3. **CWD inside projectPath** → that instance (auto)
4. Most recent timestamp

## Global flags

| Flag | Purpose |
|---|---|
| `--port N` | Pin instance |
| `--project S` | Match by path substring |
| `--timeout MS` | Default 120000 |
| `--help` / `-h` | Any subcommand |

## Built-in subcommands (non-forwarding)

`editor`, `test`, `exec`, `status`, `update`, `version`, `help`. Everything else is forwarded as-is.

## Output contract

CLI unwraps Unity's `{success, message, data}` envelope:

- **Success** → stdout = `data` (pretty JSON, or raw string), exit 0
- **Failure** → stderr = `Error: <message>` (+ `Details: <data>`), exit 1
- `data == null` → silent success (e.g. `refresh_unity`)
- Early-closed connections (play mode entry) → `"... sent (connection closed before response)"`

Pipe `list` directly: `unity-cli list | jq '.[].name'`.

## Useful jq one-liners

```bash
unity-cli list | jq '.[] | select(.name | contains("prefab"))'
unity-cli list | jq '.[] | select(.name == "manage_editor")'
unity-cli list | jq -r '.[].name'
unity-cli list | jq 'group_by(.group) | map({group: .[0].group, tools: map(.name)})'
```

## Pitfalls

- **Compile errors** → commands fail/timeout. Check `unity-cli console --type error` first.
- **Domain reload** → transient timeout. Retry.
- **Browser Origin header** / CORS → 403. CLI path is fine.
- **Concurrent callers** → serialized by `SemaphoreSlim` (queued, not parallel).

### Console filter toggle gotcha

`unity-cli console --type warning|log` may return **empty arrays even when entries exist** — the tool calls `UnityEditor.LogEntries.GetEntryCount()`, which respects the **Console window's filter toggles**. If the user has Warning or Info toggled off, the API hides them.

Pre-enable flags before reading:

```bash
unity-cli exec - <<'EOF'
var t = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
var m = t.GetMethod("SetConsoleFlag",
    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
m.Invoke(null, new object[] { 256, true });   // LogLevelWarning
m.Invoke(null, new object[] { 512, true });   // LogLevelLog
// 128 = LogLevelError (usually on by default)
EOF
```

## Authoring custom tools (Unity-side)

Any public static class loaded in an Editor assembly:

```csharp
[UnityCliTool(Name = "my_tool", Description = "...")]
public static class MyTool
{
    public class Parameters
    {
        [ToolParameter("what this flag does", Required = true)]
        public string TargetPath { get; set; }
    }
    public static object HandleCommand(JObject @params) { ... }
}
```

- Name auto-derives to snake_case from class if omitted.
- Any asmdef works (Editor-loaded).
- Appears in `list` after next compile. Zero registration.
