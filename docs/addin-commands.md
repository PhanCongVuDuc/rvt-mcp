# Add-in commands

Another Revit add-in in the same Revit process can expose its own commands through rvt-mcp, without referencing it:
`revit_list_addin_commands` / `revit_call_addin_command` over MCP, `rvt-mcp addin list|call` on the command line.

## Registering

Under the AppDomain data key `Bimwright.RvtMcp.AddinCommands.v1` the add-in puts (or adds to) a
`ConcurrentDictionary<string, Func<string, string>>`: add-in name → a function that takes a request envelope and returns a response
envelope, both JSON (protocol `"1"`). BCL types only, so it works across Revit 2026+ add-in load contexts; the key is read on every
call, so the load order of the two add-ins does not matter.

Request: `{ "command": "<name>", "args": { … }, "entry": "mcp" | "cli" }`. The reserved command `_catalog` answers
`{ addin, version, protocol: "1", commands: [ { name, description, readOnly, argsSchema } ] }`.

Response: `{ "ok": true, "result": … }` or `{ "ok": false, "error": { "code", "message", "args", "hint" } }`.

## Threads

The function is called on a **thread-pool thread of the plugin, not inside its ExternalEvent** (`AddinCommandRoute`): Revit's thread is
not held while the add-in computes. A command that needs the Revit API must go to Revit's thread itself (its own ExternalEvent, or
Revit.Async) and must not assume it is already there. The call still goes through the plugin's read-only check, call log
(`mcp-calls.jsonl`), History window and toast, like every other tool. In a `--read-only` session a command the add-in declares
`readOnly=false` is refused (`read_only_blocked`).
