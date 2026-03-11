# Unity CLI — Project Overview

## What is this?

A command-line tool that lets AI coding assistants (Claude Code, Cursor, etc.) control the Unity Editor directly via shell commands — no MCP protocol, no Python relay, no complex setup.

## Problem

Current Unity + AI integration relies on MCP (Model Context Protocol):

```
AI Assistant → MCP Protocol → Python Relay Server → WebSocket → Unity Editor
```

This requires:
- Python runtime + virtual environment
- FastMCP framework + uvicorn
- MCP configuration JSON
- WebSocket relay process running in background
- Complex reconnection logic for Unity domain reloads

**The Python relay does almost nothing** — it just forwards JSON between the AI and Unity. All actual tool logic lives in Unity C#.

## Solution

Replace the entire MCP + Python stack with a single CLI binary:

```
AI Assistant → Bash("unity-cli ...") → HTTP → Unity Editor
```

- **One command install**: `npm i -g @anthropic/unity-cli`
- **Zero dependencies**: Node.js only (already present in AI coding environments)
- **Works with any AI**: Any tool that can run shell commands
- **Stateless**: Each CLI invocation is independent — no connection management, no reconnection logic

## Architecture

```
┌──────────────┐     ┌──────────────┐     ┌──────────────────────┐
│  AI Assistant│     │  unity-cli   │     │  Unity Editor        │
│  (Claude,    │────→│  (Node.js)   │────→│  (HTTP Listener)     │
│   Cursor..)  │     │              │     │                      │
│              │←────│  stdout/JSON │←────│  CommandRegistry     │
│  Bash tool   │     │              │     │  [McpTool] handlers  │
└──────────────┘     └──────────────┘     └──────────────────────┘
```

### Three components:

1. **CLI Binary** (this repo) — TypeScript/Node.js. Parses commands, sends HTTP requests to Unity, prints results to stdout.

2. **Unity Connector Package** — Lightweight C# Editor plugin. Opens HTTP endpoint on localhost, dispatches commands to registered handlers. Reuses existing `[McpTool]` attribute system.

3. **Project Custom Tools** — Game-specific tools using `[McpTool]` attribute. These stay in each Unity project, not in the CLI or connector package.

## Scope

### In scope
- CLI binary with subcommands for all standard Unity operations
- Unity Connector package (HTTP listener + command dispatch)
- npm distribution
- Documentation for tool authors

### Out of scope (for now)
- GUI / TUI interface
- Non-localhost connections (remote Unity instances)
- Authentication / multi-user
