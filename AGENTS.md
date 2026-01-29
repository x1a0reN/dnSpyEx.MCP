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

## Next Steps
- Build the solution and confirm both projects compile.
- Launch dnSpyEx with the extension and verify NamedPipe server starts on AppLoaded.
- Run the bridge and test MCP calls: listAssemblies / listNamespaces / listTypes / listMembers / decompile / getSelectedText.

## Notes
- User wants progress tracked in AGENTS.md on each update.
