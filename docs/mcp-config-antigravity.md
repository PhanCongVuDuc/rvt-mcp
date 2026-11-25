# MCP Configuration for Google Antigravity (IDE + `agy` CLI)

> **Audience:** anyone wiring `rvt-mcp.exe` (or any other stdio MCP server) into Google Antigravity.
> **Last verified:** 2026-11-25 against a live Antigravity 2.0 install on Windows 11 (`~/.gemini/config/` with the `.migrated` marker, `agy` CLI 1.5.16).
> **Canonical procedure:** [mcp-client-wiring.md](mcp-client-wiring.md) — this file is the deeper per-vendor reference.

Antigravity is Google's agentic IDE (built by the former Windsurf team). It belongs to the **Gemini CLI family**, so its MCP registry uses the same JSON shape as Gemini CLI / Claude Code: top-level key `mcpServers`, each entry a `command` string plus `args` / `env`. There is no Codex-style TOML and no OpenCode-style `command` array here.

For Anthropic clients see [`mcp-config-claude-clients.md`](./mcp-config-claude-clients.md); for Codex see [`mcp-config-codex.md`](./mcp-config-codex.md).

---

## 1. Config file paths

Antigravity's MCP config path moved between versions — confirm yours in the IDE (see §4).

| Antigravity version | Global MCP config | Notes |
|---|---|---|
| **2.0+** (current) | `~/.gemini/config/mcp_config.json` | Shared by the IDE **and** the `agy` CLI. Presence of `~/.gemini/config/.migrated` indicates 2.0. |
| **pre-2.0** | `~/.gemini/antigravity/mcp_config.json` | On some installs this is a **symlink** to the `config/` file above. |
| any (per-workspace) | `<project>/.agents/mcp_config.json` | Project-scoped overlay; optional. |

On Windows `~` is `%USERPROFILE%` (e.g. `C:\Users\<user>`). The official installer writes **both** `~/.gemini/config/mcp_config.json` and `~/.gemini/antigravity/mcp_config.json` so either path works regardless of version (see §3).

> `~/.gemini/settings.json` is a **sibling** file for Gemini/Antigravity general settings — MCP servers do **not** go there. Keep them in `mcp_config.json`.

---

## 2. JSON format

Standard `mcpServers` shape (same as Claude Code / Gemini CLI).

### Minimal stdio entry for RvtMcp

```json
{
  "mcpServers": {
    "rvt-mcp": {
      "command": "C:\\Users\\<user>\\AppData\\Local\\Bimwright\\rvt-mcp\\server\\current\\rvt-mcp.exe",
      "args": []
    }
  }
}
```

### Full entry with optional knobs

```json
{
  "mcpServers": {
    "rvt-mcp": {
      "command": "C:\\Users\\<user>\\AppData\\Local\\Bimwright\\rvt-mcp\\server\\current\\rvt-mcp.exe",
      "args": ["--target", "2024"],
      "env": {}
    }
  }
}
```

Field reference:

| Field | Required | Meaning |
|---|---|---|
| `command` | yes | Absolute path to `rvt-mcp.exe`. Backslashes must be escaped (`\\`) in JSON. |
| `args` | no | Extra CLI args. `--target <4-digit-year>` pins a Revit instance; `--disable-send-code` hides `send_code`. 4-digit calendar year only, never R-codes. |
| `env` | no | Extra environment variables for the server process. |

There is **no** `type` field here (unlike VS Code, which requires `"type": "stdio"`). Transport is implicitly stdio.

---

## 3. Let the installer wire it

The official installer knows Antigravity as a first-class client:

```powershell
pwsh -NoProfile -File scripts/install.ps1 -Client antigravity
```

It detects `~/.gemini/antigravity` or `~/.gemini/config`, backs up the existing file to `.bak`, and writes the standard `rvt-mcp` entry under `mcpServers` to **both** paths. It never replaces a custom launcher it does not recognise — it reports it instead.

---

## 4. Picking up the change / verifying

1. In the IDE: Agent panel → **MCP Servers → Manage MCP Servers → View raw config** confirms the active path for your version, and **Refresh** reloads servers without restarting.
2. Or restart Antigravity.
3. The `agy` CLI reads the same 2.0 config, so `agy` sessions pick it up too.

> MCP wiring is independent of Antigravity's `AGENTS.md` support (that needs IDE **v1.20.3+**). The two are unrelated.

---

## 5. Sources

- Live Antigravity 2.0 install inspected 2026-11-25 (`~/.gemini/config/mcp_config.json`, `~/.gemini/antigravity/mcp_config.json`, `agy --version`).
- [mcp-client-wiring.md](mcp-client-wiring.md) — canonical per-client registry (Antigravity entry).
- `scripts/install.ps1` — `-Client antigravity` registry entry (`Get-McpClientSpecs`).
