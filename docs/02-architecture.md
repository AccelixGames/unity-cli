# Architecture

## Communication Protocol

### Request

```
POST http://localhost:{port}/command
Content-Type: application/json

{
  "command": "manage_editor",
  "params": {
    "action": "play",
    "wait_for_completion": true
  }
}
```

### Response

```json
{
  "success": true,
  "message": "Editor entered play mode",
  "data": { ... }
}
```

### Error

```json
{
  "success": false,
  "message": "Editor is already in play mode"
}
```

### Long-running operations

Some commands (connect-local, compile) take time. Two strategies:

**Strategy A: Blocking (default)**
CLI sends request, Unity holds the connection open until completion, CLI prints result.

**Strategy B: Async + Poll**
```bash
# Start async operation
unity-cli connect-local --async
# → { "taskId": "abc123", "status": "pending" }

# Poll status
unity-cli status abc123
# → { "status": "completed", "result": { ... } }
```

Default is blocking. `--async` flag for non-blocking when needed.

## Port Discovery

Unity Connector writes its port to a known file:

```
~/.unity-cli/instances.json
```

```json
[
  {
    "projectPath": "/home/user/MyGame",
    "port": 8090,
    "pid": 12345,
    "unityVersion": "6000.3.2f1"
  }
]
```

CLI reads this file to find the right Unity instance. If multiple instances:
- `--project` flag to specify
- Default: use the most recently registered instance

## CLI Command Structure

```
unity-cli <category> <action> [options]
```

### Categories

| Category | Commands | Description |
|----------|----------|-------------|
| `editor` | `play`, `stop`, `pause`, `refresh` | Editor lifecycle |
| `query` | `entities`, `inspect`, `singleton`, `component-values` | ECS runtime queries |
| `game` | `connect`, `load`, `phase`, `bots` | Game flow control |
| `exec` | (direct) | Execute C# code |
| `console` | (direct) | Read console logs |
| `tool` | `list`, `call` | Custom tool management |
| `profiler` | `capture`, `hierarchy` | Profiler data |

### Examples

```bash
# Editor
unity-cli editor play --wait
unity-cli editor stop
unity-cli editor refresh --compile

# ECS Queries
unity-cli query entities --world server --component Health --max 10
unity-cli query inspect --world server --index 42
unity-cli query singleton --world server --component GamePhaseState

# Game
unity-cli game connect
unity-cli game load --index 0
unity-cli game phase Playing
unity-cli game bots spawn 10
unity-cli game bots despawn

# Execute C#
unity-cli exec "Debug.Log(Time.time)"

# Console
unity-cli console --lines 50 --filter error

# Custom tools
unity-cli tool list
unity-cli tool call player_status --world server

# Profiler
unity-cli profiler hierarchy --depth 3
```

## Unity Connector Package

### Responsibilities

1. **HTTP Server** — `HttpListener` on localhost, configurable port
2. **Command Dispatch** — Routes incoming commands to C# handlers
3. **Tool Discovery** — Finds `[McpTool]`-attributed classes via reflection
4. **Instance Registration** — Writes port/project info to `~/.unity-cli/instances.json`
5. **Domain Reload Survival** — Re-opens HTTP server after C# reload

### Reuse from existing MCP package

| Component | Action |
|-----------|--------|
| `[McpTool]` attribute | Keep as-is (rename to `[UnityCliTool]`) |
| `[ToolParameter]` attribute | Keep as-is |
| `CommandRegistry` | Keep dispatch logic, remove WebSocket transport |
| `ToolDiscoveryService` | Keep reflection-based discovery |
| `Response` helpers | Keep `SuccessResponse`/`ErrorResponse` |
| `StdioBridgeHost` | Remove entirely |
| `connection.py` | Remove entirely |
| `main.py` | Remove entirely |
| All Python code | Remove entirely |

## Data Flow

```
1. User types: unity-cli query entities --world server --component Health

2. CLI parses args:
   command = "query_entities"
   params  = { world: "server", component: "Health" }

3. CLI reads ~/.unity-cli/instances.json → finds port 8090

4. CLI sends:
   POST http://localhost:8090/command
   { "command": "query_entities", "params": { "world": "server", "component": "Health" } }

5. Unity Connector receives → CommandRegistry.Dispatch("query_entities", params)

6. CommandRegistry finds McpQueryEntities.HandleCommand → executes

7. Unity returns:
   { "success": true, "data": { "entities": [...], "count": 42 } }

8. CLI prints result to stdout (JSON or formatted text)
```
