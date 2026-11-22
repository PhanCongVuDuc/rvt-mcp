# Architecture

## Two processes, one pipe

```
MCP client (Claude Code / Cursor / …)
        │  stdio (NDJSON)
        ▼
RvtMcp.Server  (.NET 8 console app, global tool)
        │  TCP (2022–2024)  OR  Named Pipe (2025–2027) — NDJSON + token auth
        ▼
RvtMcp.Plugin  (Revit add-in DLL, one per year)
        │  ExternalEvent.Raise()
        ▼
Revit API  (UIApplication / Document)
```

**Server** is an MCP server. It talks stdio to the client, translates each tool call into a JSON envelope, and forwards it over a local transport to whichever Revit plugin is running. Server is a plain `.NET tool` — no GUI, no Revit reference.

**Plugin** is an `IExternalApplication` loaded by Revit. It runs a TCP listener (Revit 2022–2024) or a Named Pipe server (Revit 2025–2027) on a background thread, enqueues requests, and marshals them onto the Revit UI thread via `ExternalEvent.Raise()`. Each request opens its own `Transaction` when it needs one; `batch_execute` wraps many into one `TransactionGroup`.

## Discovery

Every plugin instance writes a per-instance discovery file to `%LOCALAPPDATA%\Bimwright\rvt-mcp\` on startup, named `revit-<year>-<pid>.json`, and additionally publishes the legacy per-year file `revit-<year>.json` for older servers:

| Revit | Per-instance file | Legacy file | Transport |
|-------|-------------------|-------------|-----------|
| 2022  | `revit-2022-<pid>.json` | `revit-2022.json` | TCP |
| 2023  | `revit-2023-<pid>.json` | `revit-2023.json` | TCP |
| 2024  | `revit-2024-<pid>.json` | `revit-2024.json` | TCP |
| 2025  | `revit-2025-<pid>.json` | `revit-2025.json` | Named Pipe |
| 2026  | `revit-2026-<pid>.json` | `revit-2026.json` | Named Pipe |
| 2027  | `revit-2027-<pid>.json` | `revit-2027.json` | Named Pipe |

Each per-instance file is a self-describing JSON object (schema 3; the legacy file keeps the same payload so old servers can read it):

```json
{
  "schema_version": 3,
  "host_app": "revit",
  "host_year": 2024,
  "revit_year": 2024,
  "target_id": "revit-2024-12345",
  "pid": 12345,
  "process_start_utc": "2026-10-06T08:00:00.000Z",
  "transport": "tcp",
  "port": 49152,
  "pipe_name": null,
  "auth_token": "base64...",
  "capabilities": ["tool_catalog"]
}
```

`window_title` is not in the descriptor — the server reads it live from the process. On shutdown the publisher deletes its own per-instance file and, if it owns the legacy file, either deletes it or hands it over to the newest remaining live instance of that year.

The server scans `revit-*.json` on connect, normalizes per-instance and legacy files into candidates (deduplicated by pid), verifies each PID is alive, and auto-deletes only files whose owner is proven dead. It binds to exactly one instance — implicitly on the first call, or explicitly through `--target` / `BIMWRIGHT_TARGET` / `revit_switch_target` with selectors `auto`, a 4-digit year, `pid:<n>` or `id:revit-<year>-<pid>`. Each tool call pins the binding generation; a mid-call switch can never slide a command onto a different instance. MCP clients register one auto-detect `rvt-mcp` entry (the installer wires clients selected by `-Client`; agents must select the requested clients explicitly, or use `-Client none` for MCPB; see [AGENTS.md](AGENTS.md) Steps 2–3); with several instances — including several of the same year — the agent calls `revit_list_available_targets`, picks by `pid`/`window_title`, and pins with `revit_switch_target("pid:<n>")` rather than guessing. **Versions are 4-digit calendar years (`2024`), not R-codes (`R24`) — v0.5+ rejects R-codes with an educational error pointing back to `revit_list_available_targets`.**

## Multi-version strategy

Six plugin shells share one source tree. Each csproj glob-includes `src/shared/**/*.cs`:

```
src/
├── server/                # RvtMcp.Server (.NET 8)
├── shared/                # Handlers, CommandDispatcher, Transport, Logging, Security
│   ├── Handlers/          # per-command tool handlers
│   ├── Infrastructure/    # CommandDispatcher, McpEventHandler, SchemaValidator
│   ├── Transport/         # ITransportServer, TcpTransportServer, PipeTransportServer
│   ├── Security/          # AuthToken, ErrorSanitizer, SecretMasker
│   ├── Config/            # RvtMcpConfig — 3-layer precedence
│   └── Views/             # HistoryWindow
├── plugin-r22/            # net48, TCP
├── plugin-r23/            # net48, TCP
├── plugin-r24/            # net48, TCP
├── plugin-r25/            # net8.0-windows7.0, Named Pipe
├── plugin-r26/            # net8.0-windows7.0, Named Pipe
└── plugin-r27/            # net10.0-windows7.0, Named Pipe
```

Year-specific API differences live behind `#if REVIT2024_OR_GREATER` / `#if REVIT2027_OR_GREATER` and the `RevitCompat` helper (e.g. `ElementId.IntegerValue` vs `.Value` across the R2026 cut). Each shell defines its own `DefineConstants` in csproj.

The **server** is version-agnostic — it just forwards JSON envelopes. All Revit-API coupling lives in the plugin.

## Request lifecycle

1. MCP client sends `tools/call` over stdio.
2. `ToolGateway` (server) picks the right plugin connection by `--target` or auto-detect.
3. Server writes an NDJSON envelope `{ id, command, params }` over TCP/Pipe.
4. Plugin's listener thread enqueues the request and raises the ExternalEvent.
5. Revit invokes `McpEventHandler.Execute()` on the UI thread:
   - Schema validation (reject malformed early with `error / suggestion / hint`).
   - Dispatch to the handler via `CommandDispatcher`.
   - Handler opens its own `Transaction` if it mutates state.
   - Sanitize any leaking paths in error messages.
6. Response envelope travels back up the same pipe.
7. `ToolGateway` resolves the `TaskCompletionSource`; MCP tool method returns the DTO.

Timeout: 60s by default. Long-run tools accept `timeout_seconds` from 1–900; IFC export defaults to 600s. The same per-call budget reaches the plugin and gateway, with a short transport grace on the gateway. On expiry the plugin completes the request TCS so a late UI-thread result is skipped; the error tells agents not to retry clash/export until Revit finishes. Listener cancels pending requests on plugin shutdown.

Wire JSON over 1 MiB is rejected (100 KB logs an S4 warning). Narrow filters / `max_results` instead of retrying the same dump.

## Tool registration

All 14 tools are registered on every start (`Program.ResolveRegisteredToolTypes`); `--disable-send-code` is the only switch that hides one. The server↔plugin handshake (`handshake`, authenticated, 5 s budget) tells a busy Revit from an unreachable one before any command is sent.

## Notifications

Tool-call activity is shown as a Windows notification raised by the server (`ServerToast`, `Windows.UI.Notifications`). One notification per MCP session is updated in place; the plugin draws nothing on Revit's UI thread.

## Config precedence (A9)

```
RvtMcpConfig.Load(args, configFilePath)
   │
   ├── Layer 1: JSON file at %LOCALAPPDATA%\Bimwright\rvt-mcp\rvtmcp.config.json
   ├── Layer 2: env vars (BIMWRIGHT_TARGET, BIMWRIGHT_ENABLE_SEND_CODE, …)
   └── Layer 3: CLI args (--target, --disable-send-code, …)
                    (later layers overwrite earlier; nulls don't override)
```

Hand-rolled parser, no `System.CommandLine` dep. Each field is nullable so "not set" is distinguishable from "set to default".

## Threading

- Transport listener: background thread, never touches Revit API.
- Request queue: `ConcurrentQueue<PendingRequest>` + per-request `TaskCompletionSource`.
- ExternalEvent: Revit calls back on the UI thread; handler drains the queue there.
- Try/catch per command in the queue drain (no starvation).
- Stale-command guard: skip if the TCS is already completed (timeout/cancel).
- Shutdown: cancel all pending TCS, stop socket/pipe, dispose ExternalEvent, delete own discovery file.

## Why this shape

- **Full C#, one stack.** No TypeScript bridge, no Python helper, no IPC format hop. One language across server, plugin and handlers, tests.
- **Process split, not thread split.** The server lives outside Revit so it can start before Revit and outlive a plugin crash. Users update the server via `dotnet tool update -g` without touching their add-in.
- **Source glob, not shared DLL.** Each plugin shell compiles `src/shared/**` directly so version-specific `#if` branches produce distinct binaries — no runtime version sniffing, no reflection fallback.
