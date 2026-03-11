# Implementation Plan

## Phase 1: Project Setup & CLI Skeleton

### 1.1 Initialize Node.js project
- `package.json` with bin entry
- TypeScript config
- Build script

### 1.2 CLI framework
- Argument parser (use `commander` or lightweight custom parser)
- Subcommand routing
- Help text generation
- Output formatting (JSON / human-readable)

### 1.3 HTTP client module
- Port discovery from `~/.unity-cli/instances.json`
- Request/response handling
- Timeout and error handling
- `--port` override flag

**Commit**: `feat: CLI skeleton with argument parsing and HTTP client`

---

## Phase 2: Core Commands

### 2.1 Editor commands
- `editor play [--wait]`
- `editor stop`
- `editor pause`
- `editor refresh [--compile]`

### 2.2 Console command
- `console [--lines N] [--filter error|warn|log]`

### 2.3 Exec command
- `exec "<c# code>" [--usings System.Linq,...]`

**Commit**: `feat: editor control, console read, and C# execution commands`

---

## Phase 3: ECS Query Commands

### 3.1 Entity queries
- `query entities --world <server|client> --component <name> [--max N]`
- `query inspect --world <server|client> --index <N> [--version <N>] [--components <list>]`
- `query singleton --world <server|client> --component <name>`
- `query component-values --world <server|client> --component <name> --fields <list>`

### 3.2 System queries
- `query systems --world <server|client> [--filter <name>] [--show-disabled]`

**Commit**: `feat: ECS entity and system query commands`

---

## Phase 4: Game Flow Commands

### 4.1 Connection
- `game connect` (local server connection, blocking until complete)

### 4.2 Mini-game
- `game load --index <N>`
- `game phase <Starting|Playing|Ending>`
- `game overview`

### 4.3 Bots
- `game bots spawn <count>`
- `game bots despawn`

### 4.4 Thin clients
- `game thin-clients connect <count>`
- `game thin-clients disconnect`

**Commit**: `feat: game flow control commands`

---

## Phase 5: Custom Tool Support

### 5.1 Tool discovery
- `tool list [--json]` — fetch available tools from Unity
- `tool call <name> [--param value ...]` — invoke any registered tool

### 5.2 Dynamic help
- `tool help <name>` — show tool description and parameters

**Commit**: `feat: custom tool listing and invocation`

---

## Phase 6: Unity Connector Package

### 6.1 HTTP Server
- `HttpListener` on configurable port (default 8090)
- JSON request parsing
- Command routing to `CommandRegistry`

### 6.2 Instance registration
- Write to `~/.unity-cli/instances.json` on Editor startup
- Remove on Editor quit
- Handle multiple Unity instances

### 6.3 Domain reload handling
- Persist server state across C# domain reloads
- Re-register instance after reload

### 6.4 Tool discovery
- Reuse `[McpTool]` attribute pattern (renamed to `[UnityCliTool]`)
- Auto-discover tools in all loaded assemblies

**Commit**: `feat: Unity Connector package with HTTP server`

---

## Phase 7: Diagnostic Commands

### 7.1 Entity diagnostics
- `diag hierarchy --world <server|client> --index <N>`
- `diag diff --ghost-id <N> --components <list>`
- `diag players [--world <server|client>]`

### 7.2 Vehicle inspection
- `diag vehicle --world <server|client> --index <N>`

**Commit**: `feat: diagnostic commands for entity hierarchy and server/client diff`

---

## Phase 8: Profiler Commands

### 8.1 Profiler
- `profiler hierarchy [--depth N]`
- `profiler capture [--frames N]`

**Commit**: `feat: profiler data access commands`

---

## Phase 9: Polish & Distribution

### 9.1 Documentation
- README with install instructions
- Command reference (auto-generated from help text)
- Unity Connector setup guide

### 9.2 npm publish
- Package naming (`@anthropic/unity-cli` or `unity-editor-cli`)
- CI/CD for automated publish

### 9.3 Testing
- Unit tests for argument parsing
- Integration tests with mock HTTP server

**Commit**: `docs: README, command reference, and publish config`

---

## File Structure (Final)

```
unity-cli/
├── src/
│   ├── index.ts                 # Entry point
│   ├── cli.ts                   # Argument parsing & routing
│   ├── client.ts                # HTTP client + port discovery
│   ├── format.ts                # Output formatting
│   ├── commands/
│   │   ├── editor.ts            # play, stop, pause, refresh
│   │   ├── console.ts           # console log reading
│   │   ├── exec.ts              # C# code execution
│   │   ├── query.ts             # ECS entity/system queries
│   │   ├── game.ts              # connect, load, phase, bots
│   │   ├── tool.ts              # custom tool list/call
│   │   ├── diag.ts              # diagnostic commands
│   │   └── profiler.ts          # profiler commands
│   └── types.ts                 # Shared types
├── unity-connector/             # Unity C# package
│   ├── package.json             # Unity package manifest
│   ├── Editor/
│   │   ├── HttpServer.cs        # HTTP listener
│   │   ├── CommandRouter.cs     # Command dispatch
│   │   ├── InstanceRegistry.cs  # Port/project registration
│   │   ├── ToolDiscovery.cs     # [UnityCliTool] discovery
│   │   └── Attributes/
│   │       ├── UnityCliToolAttribute.cs
│   │       └── ToolParameterAttribute.cs
│   └── Runtime/
│       └── AssemblyInfo.cs
├── docs/
│   ├── 01-overview.md
│   ├── 02-architecture.md
│   └── 03-implementation-plan.md
├── package.json
├── tsconfig.json
└── .gitignore
```
