# AGENTS

## Background
- Goal: build a dnSpyEx MCP plugin with a local IPC server and a stdio bridge so MCP clients can talk to dnSpyEx UI.
- Repo: D:\Projects\dnSpyEx.MCP

## Target Architecture
- dnSpyEx plugin hosts a local IPC server (NamedPipe preferred; HTTP optional).
- Separate MCP stdio bridge (console app) speaks MCP JSON-RPC over stdio and forwards to the IPC server.
- Expose MVP tools: assembly / namespace / type / member / decompile / selected code.

## Project Structure (MCP-related)
- Extensions\dnSpyEx.MCP\dnSpyEx.MCP.csproj: Extension project; references dnSpy contracts and Newtonsoft.Json.
- Extensions\dnSpyEx.MCP\TheExtension.cs: Extension entrypoint; starts server on AppLoaded and stops on AppExit.
- Extensions\dnSpyEx.MCP\McpHost.cs: MEF-exported host; wires dnSpy services into the IPC server.
- Extensions\dnSpyEx.MCP\Ipc\McpIpcServer.cs: NamedPipe JSON-RPC server (line-delimited); supports DNSPYEX_MCP_PIPE override.
- Extensions\dnSpyEx.MCP\Ipc\McpRequestHandler.cs: RPC dispatch + MVP tools; runs on UI dispatcher.
- Tools\dnSpyEx.MCP.Bridge\dnSpyEx.MCP.Bridge.csproj: MCP stdio bridge console app.
- Tools\dnSpyEx.MCP.Bridge\Program.cs: Bridge entrypoint; connects to pipe and runs MCP loop.
- Tools\dnSpyEx.MCP.Bridge\McpServer.cs: MCP JSON-RPC (initialize/tools/*); forwards tool calls to pipe.
- Tools\dnSpyEx.MCP.Bridge\ToolCatalog.cs: Tool definitions and input schemas mapped to RPC methods.
- Tools\dnSpyEx.MCP.Bridge\PipeClient.cs: NamedPipe client (line-delimited JSON).
- Tools\dnSpyEx.MCP.Bridge\McpPipeDefaults.cs: Pipe name constants and env var name.
- dnSpy.sln: Solution updated to include dnSpyEx.MCP and dnSpyEx.MCP.Bridge.

## Decisions
- Use hybrid model: plugin exposes NamedPipe (or HTTP) and bridge handles stdio MCP.
- Focus on MVP toolset first, then expand.

## Current Progress
- 2026-01-29: AGENTS.md created.
- 2026-01-29: Added dnSpyEx.MCP extension project with NamedPipe JSON-RPC server and MVP handlers.
- 2026-01-29: Added dnSpyEx.MCP.Bridge console project (stdio MCP -> NamedPipe).
- 2026-01-29: Updated dnSpy.sln to include both projects.
- 2026-01-29: Build attempt failed on this machine because .NET SDK 9 does not support net10.0-windows (NETSDK1045). Install .NET 10 SDK to build.
- 2026-01-29: Renamed AGENT-sc.md to AGENTS-SC.md for consistent naming.
- 2026-01-29: Fixed UTF8String JSON serialization and nullable MVID handling in McpRequestHandler; set bridge target to net10.0-windows.
- 2026-01-29: Build script (build.ps1 -NoMsbuild) timed out on this machine; targeted builds succeeded for dnSpyEx.MCP (net48/net10.0-windows) and dnSpyEx.MCP.Bridge (net10.0-windows) with 0 errors.
- 2026-01-29: Changed extension assembly name to dnSpyEx.MCP.x; built dnSpyEx.MCP for net48 (1 warning) and net10.0-windows (0 warnings), no errors.
- 2026-01-29: Added Output window logging for MCP server/requests; built dnSpyEx.MCP net48 (1 warning) and net10.0-windows (0 warnings), no errors.

## Next Steps
- Build the solution and confirm both projects compile.
- Launch dnSpyEx with the extension and verify NamedPipe server starts on AppLoaded.
- Run the bridge and test MCP calls: listAssemblies / listNamespaces / listTypes / listMembers / decompile / getSelectedText.

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

### Run dnSpyEx + MCP bridge
1) Start dnSpyEx (net48 or net10.0-windows output):
```
dnSpy\dnSpy\bin\Release\net48\dnSpy.exe
```

2) Start the MCP bridge:
```
dotnet run --project Tools/dnSpyEx.MCP.Bridge -c Release
```

### Pipe configuration
- Default pipe name: `dnSpyEx.MCP`
- Override via env var: `DNSPYEX_MCP_PIPE`
- Or bridge arg: `--pipe <name>`

### Available MCP tools (MVP)
- dnspy.listAssemblies
- dnspy.listNamespaces
- dnspy.listTypes
- dnspy.listMembers
- dnspy.decompile
- dnspy.getSelectedText

### Connect to an AI IDE (MCP-capable)
General idea: configure the IDE to launch the bridge as a stdio MCP server. Example generic config:

```json
{
  "mcpServers": {
    "dnspyex": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "Tools/dnSpyEx.MCP.Bridge",
        "-c",
        "Release"
      ],
      "env": {
        "DNSPYEX_MCP_PIPE": "dnSpyEx.MCP"
      }
    }
  }
}
```

Workflow:
1) Launch dnSpyEx first (plugin starts pipe on AppLoaded).
2) Start the IDE MCP server (bridge connects to the pipe).
3) Use tools from the IDE's MCP tool list.

## Notes
- User wants progress tracked in AGENTS.md on each update.

## Rules
- After each change, confirm build succeeds with no errors, then git commit and push to the repo.
- After each code change, update project progress in AGENTS.md.
- Whenever AGENTS.md is changed, mirror the corresponding Chinese updates into AGENTS-SC.md (rule block itself excluded).
