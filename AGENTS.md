# AGENTS

## Background
- Goal: build a dnSpyEx MCP plugin that exposes an HTTP JSON-RPC server so AI clients can talk to dnSpyEx UI directly.
- Repo: D:\Projects\dnSpyEx.MCP

## Target Architecture
- dnSpyEx plugin hosts a local HTTP JSON-RPC server.
- Expose MVP tools: assembly / namespace / type / member / decompile / selected code.

## Project Structure (MCP-related)
- Extensions\dnSpyEx.MCP\dnSpyEx.MCP.csproj: Extension project; references dnSpy contracts and Newtonsoft.Json.
- Extensions\dnSpyEx.MCP\TheExtension.cs: Extension entrypoint; starts server on AppLoaded and stops on AppExit.
- Extensions\dnSpyEx.MCP\McpHost.cs: MEF-exported host; wires dnSpy services into the HTTP server.
- Extensions\dnSpyEx.MCP\Http\McpHttpServer.cs: HTTP JSON-RPC server (POST /rpc).
- Extensions\dnSpyEx.MCP\Ipc\McpRequestHandler.cs: RPC dispatch + tool handlers; runs on UI dispatcher.
- dnSpy.sln: Solution updated to include dnSpyEx.MCP.

## Decisions
- Use direct HTTP JSON-RPC between the AI client and the dnSpyEx plugin (no bridge).
- Focus on MVP toolset first, then expand.

## Current Progress
- 2026-01-29: AGENTS.md created.
- 2026-01-29: Added dnSpyEx.MCP extension project with NamedPipe JSON-RPC server and MVP handlers.
- 2026-01-29: Added dnSpyEx.MCP.Bridge console project (stdio MCP -> NamedPipe).
- 2026-01-29: Updated dnSpy.sln to include both projects.
- 2026-01-29: Build attempt failed on this machine because .NET SDK 9 does not support net10.0-windows (NETSDK1045). Install .NET 10 SDK to build.
- 2026-01-29: Renamed AGENT-sc.md to AGENTS-SC.md for consistent naming.
- 2026-01-29: Fixed UTF8String JSON serialization and nullable MVID handling in McpRequestHandler; set bridge target to net10.0-windows.
- 2026-01-29: Build script (build.ps1 -NoMsbuild) timed out on this machine; targeted builds succeeded for dnSpyEx.MCP (net10.0-windows) and dnSpyEx.MCP.Bridge (net10.0-windows) with 0 errors.
- 2026-01-29: Changed extension assembly name to dnSpyEx.MCP.x; built dnSpyEx.MCP for net10.0-windows with 0 warnings and no errors.
- 2026-01-29: Added Output window logging for MCP server/requests; built dnSpyEx.MCP net10.0-windows with 0 warnings and no errors.
- 2026-01-29: Added optional secondary target for the plugin with external references via DnSpyExBin; bridge now targets multiple Windows TFMs; builds succeeded.
- 2026-01-29: Added output logging and a targeted suppression for BamlTabSaver NullReferenceException; added a null-guard in BamlTabSaver.
- 2026-01-29: Secondary target build of dnSpy.BamlDecompiler cannot be produced from this repo due to API mismatch with installed binaries; plugin suppresses the crash instead.
- 2026-01-29: Bridge now connects to the pipe on first tool call (lazy connect) and resets the pipe on failures to avoid early "Pipe hasn't been connected yet" exits.
- 2026-01-29: Added pipe read/write error logging on the plugin side and a one-time reconnect retry in the bridge to mitigate transient broken-pipe errors.
- 2026-01-29: Plugin build now auto-copies dnSpyEx.MCP.x.dll into D:\逆向\工具-逆向\dnspyEx\bin\Extensions by default (disable with DisableDnSpyExInstallCopy=true or override DnSpyExInstallDir).
- 2026-01-29: Added explicit NamedPipe security (current user) and server-side creation error handling; removed mandatory label to avoid privilege errors and fixed a shutdown crash from TimeSpan.FromSeconds(long).
- 2026-01-29: Server now accepts multiple concurrent NamedPipe clients (max instances) and handles connections in parallel to avoid timeouts when a stale client holds the only slot.
- 2026-01-29: Added detailed pipe I/O logging (per-client request/EOF/errors) to diagnose early disconnects causing "Pipe closed" in the bridge.
- 2026-01-29: Added opt-in bridge file logging (DNSPYEX_MCP_BRIDGE_LOG) to trace stdio and pipe operations without polluting MCP stdout.
- 2026-01-29: Added handler lifecycle logging and exception capture around per-client pipe tasks to surface immediate disconnect causes.
- 2026-01-29: Replaced JToken.ToString(Formatting) usage with JsonConvert.SerializeObject to avoid Newtonsoft.Json version mismatch crashes in dnSpyEx runtime.
- 2026-01-29: Added PipeAccessRights.CreateNewInstance to pipe security so additional server instances can be created without access denied spam.
- 2026-01-29: Allowed empty namespace parameter for listTypes/decompile namespace, and added a dnspy.help tool with usage tips exposed via tools/list.
- 2026-01-29: Added dnspy.exampleFlow tool with full usage examples and updated tool descriptions to prompt calling it first.
- 2026-01-29: Expanded dnspy.exampleFlow to include dnspy.help and documentation tool guidance.
- 2026-01-29: Added dnspy.exampleFlow coverage for all tools, new method/field/type info tools, and dnspy.search with full dnSpyEx search settings.
- 2026-01-29: Reworked dnspy.search to use a custom dnlib-based search (metadata + IL/body text) instead of internal dnSpy search APIs; updated module keying and UTF8String handling for search results.
- 2026-02-05: Replaced NamedPipe server with HTTP JSON-RPC (HttpListener) and added /health endpoint.
- 2026-02-05: Removed dnSpyEx.MCP.Bridge project from the repo and solution.
- 2026-02-05: Added new RPC tools for type/method inspection, references, call analysis, and search utilities.
- 2026-02-05: Added scripts/dev-build-commit.ps1 for build + auto-copy + commit/push.
- 2026-02-05: Fixed UTF8String JSON output conversions and FnPtrSig MethodSig handling in McpRequestHandler; build passes.

## Next Steps
- Build the extension and confirm it compiles.
- Launch dnSpyEx with the extension and verify HTTP server starts on AppLoaded.
- Test HTTP JSON-RPC calls: listAssemblies / listNamespaces / listTypes / listMembers / decompile / getSelectedText.

## Build & Usage Guide

### Prerequisites
- .NET SDK 10.x (required because the repo targets net10.0-windows).
- Optional: Visual Studio Build Tools for full dnSpyEx build (COM refs). The build script prefers MSBuild.

### Build
Option A (recommended by repo, uses build script):
```
./build.ps1 -NoMsbuild
```

Option B (solution build):
```
dotnet build dnSpy.sln -c Release
```

Note: On this machine, build failed with NETSDK1045 because .NET SDK 9 cannot build net10.0-windows. Install .NET 10 SDK and retry.

### Run dnSpyEx + HTTP JSON-RPC
1) Start dnSpyEx (net10.0-windows output):
```
dnSpy\dnSpy\bin\Release\net10.0-windows\dnSpy.exe
```

2) Send HTTP JSON-RPC to:
```
http://127.0.0.1:13337/rpc
```

Example:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "listAssemblies",
  "params": {}
}
```

### HTTP configuration
- Env var: `DNSPYEX_MCP_HTTP_PREFIX` (e.g. `http://127.0.0.1:13337/`)
- Env var: `DNSPYEX_MCP_HTTP_PORT` (default `13337`)

### Available MCP tools
- listAssemblies
- getAssemblyInfo
- listNamespaces
- listTypes
- listMembers
- getTypeInfo
- getTypeFields
- getTypeProperty
- getMethodSignature
- decompileMethod
- decompileField
- decompileProperty
- decompileEvent
- decompileType
- getFieldInfo
- getEnumInfo
- getStructInfo
- getInterfaceInfo
- getTypeDependencies
- getInheritanceTree
- findPathToType
- findReferences
- getCallers
- getCallees
- search
- searchTypes
- searchMembers
- searchStrings
- getSelectedText
- getSelectedMember
- openInDnSpy
- exampleFlow

### Connect to an AI IDE
This server is HTTP JSON-RPC (not stdio MCP). Configure your client to POST to:
```
http://127.0.0.1:13337/rpc
```
Use `DNSPYEX_MCP_HTTP_PREFIX` or `DNSPYEX_MCP_HTTP_PORT` to override.

## Notes
- User wants progress tracked in AGENTS.md on each update.
- User confirms they use .NET 10 builds only (net48 is not used) and wants build commands without DisableDnSpyExInstallCopy=true so the plugin auto-copies.

## Rules
- After each change, confirm build succeeds with no errors, then git commit and push to the repo.
- After each code change, update project progress in AGENTS.md.
- Whenever AGENTS.md is changed, mirror the corresponding Chinese updates into AGENTS-SC.md (rule block itself excluded).
- Each time a new MCP tool is added, update dnspy.exampleFlow with that tool's usage and example.
