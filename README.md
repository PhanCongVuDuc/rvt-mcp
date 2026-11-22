<!-- mcp-name: io.github.bimwright/rvt-mcp -->

<h1 align="center">rvt-mcp</h1>

<p align="center">
  MCP gateway for Autodesk Revit 2022–2027 — a trimmed fork with the 14 tools in daily use
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#supported-revit-versions"><img src="https://img.shields.io/badge/Revit-2022--2027-186BFF" alt="Revit 2022-2027" /></a>
  <a href="#tools"><img src="https://img.shields.io/badge/MCP-14%20tools-6C47FF" alt="MCP tools" /></a>
</p>

---

## What it is

A fork of [bimwright/rvt-mcp](https://github.com/bimwright/rvt-mcp) cut down to what this machine actually uses. A .NET 8 server talks MCP over stdio; a thin add-in per Revit year (2022–2027) runs inside Revit and is reached over localhost TCP (≤2024) or a named pipe (≥2025). Nothing leaves the machine. Details: [ARCHITECTURE.md](ARCHITECTURE.md).

Removed from upstream: the other 224 tools (typed query, create, modify, MEP, structural, schedules, families, annotation, graphics, rooms, links, parameters, organization, workflows, lint), ToolBaker and adaptive bake, per-model change history and the change-impact survey, the send_code body journal, the in-Revit WPF toast, HTTP/SSE transport, the daily update check, SQLite spill output and five prompts. Anything those tools did is done through `revit_send_code_to_revit` or through an add-in command.

---

## Install

This fork is installed from the Sonny umbrella repo, not from upstream releases:

```powershell
pwsh -NoProfile -File scripts/install-rvt-mcp.ps1   # in Sonny: builds the server, registers it for every project
```

The add-in is built and deployed with `dotnet build src/RvtMcp.sln -c Debug` in this repo (close Revit first — the build copies the plugin into `%APPDATA%\Autodesk\Revit\Addins\<year>\RvtMcp\`). Pass `-p:RvtMcpSkipDeploy=true` to build without copying.

**Check it works:** restart the AI client, open a model in Revit, start MCP from the ribbon (**Add-Ins** → **RvtMcp**) and ask the agent to call `revit_get_current_view_info`.

---

## Tools

| Toolset | Tools |
|---------|-------|
| `meta` | `revit_send_code_to_revit`, `revit_open_model`, `revit_list_recent_models`, `revit_list_available_targets`, `revit_get_current_target`, `revit_switch_target`, `revit_list_addin_commands`, `revit_call_addin_command` |
| `query` | `revit_get_current_view_info` |
| `view` | `revit_capture_view_image`, `revit_activate_view` |
| `export` | `revit_export_pdf`, `revit_export_image` |
| `sheets` | `revit_list_sheets` |

All five toolsets are on by default. `--tools <csv>` narrows the list further.

- **`revit_send_code_to_revit`** compiles and runs a C# body inside Revit; `--read-only` or `--disable-send-code` removes it. See [docs/send-code.md](docs/send-code.md).
- **Add-in commands:** `revit_list_addin_commands` / `revit_call_addin_command` run commands other add-ins register in the same Revit process. See [docs/addin-commands.md](docs/addin-commands.md).
- **Prompt:** `revit_stairs` — guided stair creation through `send_code` ([docs/stairs-workflow.md](docs/stairs-workflow.md)).

### Activity notifications

The server, not Revit, raises a Windows notification for tool calls, so Revit's UI thread draws nothing. Each MCP session has one notification: calls update it in place (latest tool, success/failure counts); after `toastIdleSeconds` of quiet (default 20) the next call raises a new banner. Turn it on or off with the ribbon **Toast** button or `enableToast` in the config file — the server follows the file without a restart. Calls made through the `rvt-mcp addin call` command line do not go through MCP and raise no notification.

---

## Configuration

Precedence, high wins: **CLI → env (`BIMWRIGHT_*`) →** `%LOCALAPPDATA%\Bimwright\rvt-mcp\rvtmcp.config.json`.

| Setting | CLI | Env | JSON |
|---------|-----|-----|------|
| Target selector | `--target 2024` / `--target pid:12345` | `BIMWRIGHT_TARGET` | `target` |
| Toolsets | `--toolsets query,meta` | `BIMWRIGHT_TOOLSETS` | `toolsets` |
| Tool allowlist | `--tools send_code_to_revit,switch_target` | `BIMWRIGHT_TOOLS` | `tools` |
| Read-only | `--read-only` | `BIMWRIGHT_READ_ONLY=1` | `readOnly` |
| send_code | `--enable-send-code` / `--disable-send-code` | `BIMWRIGHT_ENABLE_SEND_CODE` | `enableSendCode` |
| Call log | `--enable-call-log` / `--disable-call-log` | `BIMWRIGHT_ENABLE_CALL_LOG` | `enableCallLog` |
| Keep send_code bodies in History (re-run) | `--cache-send-code-bodies` / `--no-cache-send-code-bodies` | `BIMWRIGHT_CACHE_SEND_CODE_BODIES=1` | `cacheSendCodeBodies` |
| Response guard | `--enable-response-guard` / `--disable-response-guard` | `BIMWRIGHT_ENABLE_RESPONSE_GUARD` | `enableResponseGuard` |
| Warn / strong warn / budget / cap bytes | `--response-warn-bytes` … `--max-response-bytes` | `BIMWRIGHT_RESPONSE_*_BYTES` | `responseWarnBytes` … `maxResponseBytes` |
| Spill retention (default 36 h) | `--spill-retention-hours <n>` | `BIMWRIGHT_SPILL_RETENTION_HOURS` | `spillRetentionHours` |
| LAN bind (plugin) | — | `BIMWRIGHT_ALLOW_LAN_BIND=1` | `allowLanBind` |
| Notifications (default on) | ribbon **Toast** | `BIMWRIGHT_ENABLE_TOAST=0` | `enableToast` |
| Notification idle (default 20 s) | Settings → Toast | — | `toastIdleSeconds` |
| UI language (add-in) | ribbon **Language** | `BIMWRIGHT_UI_LANGUAGE` | `uiLanguage` |

After changing server flags, restart the MCP connection so the client picks up the new tool list.

The response guard defaults **on**: warnings at 65536 bytes, strong warnings above 262144, a 716800-byte budget and a 1048576-byte transport cap. Oversized reads return `RESPONSE_TOO_LARGE`; completed writes return a compact summary. Oversized `send_code` output spills to a local file with `mutation_applied: null` — read the file instead of rerunning the command. A spill file is kept 36 hours by default.

---

## Permissions & auto mode

MCP annotations describe each tool's document/file effects. `send_code` has no annotations: keep it out of automatic permissions and confirm each arbitrary-code call. For Claude Code, copy only the read-only allow list below; do not allow the broad `mcp__rvt-mcp__*` wildcard.

<details>
<summary>Read-only allow list (generated from annotations)</summary>

<!-- BEGIN READ_ONLY_ALLOWLIST -->
```json
{
  "permissions": {
    "allow": [
      "mcp__rvt-mcp__revit_activate_view",
      "mcp__rvt-mcp__revit_get_current_target",
      "mcp__rvt-mcp__revit_get_current_view_info",
      "mcp__rvt-mcp__revit_list_addin_commands",
      "mcp__rvt-mcp__revit_list_available_targets",
      "mcp__rvt-mcp__revit_list_recent_models",
      "mcp__rvt-mcp__revit_list_sheets"
    ]
  }
}
```
<!-- END READ_ONLY_ALLOWLIST -->

</details>

send_code defaults **on**; call-log defaults **off**. Authenticated server settings override plugin settings for that request only. Call-log off suppresses the server journal and the plugin `mcp-calls.jsonl`; in-memory History remains available.

---

## Supported Revit versions

| Revit | Plugin TFM | Transport |
|-------|------------|-----------|
| 2022–2024 | .NET Framework 4.8 | TCP |
| 2025–2026 | .NET 8 (`net8.0-windows7.0`) | Named Pipe |
| 2027 | .NET 10 (`net10.0-windows7.0`) | Named Pipe |

Full Revit desktop only. The server needs Windows 10 1809 or later (it targets `net8.0-windows10.0.19041.0` for notifications).

---

## Multiple Revit instances

The server binds to exactly one Revit instance — including when several instances of the same year run at once. It binds implicitly on the first call or explicitly through `--target` / `BIMWRIGHT_TARGET` / `revit_switch_target`. Selectors: `auto`, a 4-digit year (`2024`), `pid:<n>` or `id:revit-<year>-<pid>`; legacy R-codes like `R24` are rejected.

When more than one instance runs, do not guess the target:

1. Call `revit_list_available_targets`: every live instance is listed with `target_id`, `pid`, `host_year`, `window_title` and `source`.
2. Pick the instance by `pid` and/or `window_title`.
3. Pin it with `revit_switch_target("pid:<n>")`.
4. `revit_get_current_target` shows the active binding, its generation and the proposed `next_target_id`.

Every target error code except `TARGET_INTERRUPTED` means the command was **not** sent:

| Code | Meaning | Agent action |
|---|---|---|
| `NO_TARGET` | No live instance matches the selector | Call `revit_list_available_targets`; start Revit / the add-in or fix the selector |
| `TARGET_UNAVAILABLE` | The bound instance is alive but cannot be reached. The binding is kept | Retry shortly; ask the user to (re)start MCP in that Revit |
| `TARGET_BUSY` | The endpoint did not complete the handshake within 5 s — another gateway holds its connection, or Revit is blocked | Retry later; do not run two gateways on one instance |
| `TARGET_LOST` | The explicitly pinned (`pid:`/`id:`) instance exited | Check `candidates`, confirm with the user, then `revit_switch_target` |
| `TARGET_CHANGED` | The implicitly bound instance exited and exactly one replacement exists | Check the proposed instance, confirm with `revit_switch_target`, then repeat the call |
| `AMBIGUOUS_TARGET` | The implicitly bound instance exited and two or more instances match | List, pick by `pid`/`window_title`, `revit_switch_target("pid:<n>")` |
| `TARGET_INTERRUPTED` | The command was sent but the connection closed before a response (`sent:true`, `outcome:unknown`) | Inspect the model state before repeating any write |
| `INVALID_TARGET_SELECTOR` | Malformed selector or R-code | Use `auto`, a year, `pid:<n>` or `id:<target_id>` |

**After a Revit restart — even with a single Revit open** — the next call returns `TARGET_CHANGED` and the server waits for one `revit_switch_target` call before it sends anything, because the restarted Revit can have a different model open.

---

## Security and privacy

- Local only: loopback TCP or a local named pipe, with a per-session auth token in the discovery files under `%LOCALAPPDATA%\Bimwright\rvt-mcp\`.
- Tool arguments are schema-checked before handlers run; errors returned to the model are sanitized.
- `send_code` runs arbitrary C# in the Revit process. Use `--read-only` or `--disable-send-code` if that is unacceptable.
- send_code bodies are kept in History only when `cacheSendCodeBodies` is on; otherwise logs hold a hash and length.

More: [SECURITY.md](SECURITY.md).

---

## Docs

| Doc | Topic |
|-----|--------|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Processes, transport, DTO rules |
| [docs/send-code.md](docs/send-code.md) | send_code source forms and failure handling |
| [docs/addin-commands.md](docs/addin-commands.md) | Commands other add-ins register |
| [docs/stairs-workflow.md](docs/stairs-workflow.md) | The `revit_stairs` prompt |
| [docs/localization.md](docs/localization.md) | UI languages, overrides, hot reload |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Build, test |

---

## License

Apache-2.0 — [LICENSE](LICENSE). Forked from [bimwright/rvt-mcp](https://github.com/bimwright/rvt-mcp); changed files are marked by this fork's git history.

Revit and Autodesk are trademarks of Autodesk, Inc. This project is not affiliated with, sponsored by, or endorsed by Autodesk, Inc.
