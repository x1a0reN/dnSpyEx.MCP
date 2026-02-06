# dnSpyEx.MCP

This repo is a fork of dnSpyEx with an MCP integration layer:
- The dnSpyEx extension hosts a local HTTP JSON-RPC server.

The goal is to let AI tools talk to the dnSpyEx UI without requiring a separate bridge process.

## Architecture

```
AI client (HTTP JSON-RPC)
            |
            v
dnSpyEx.MCP extension (inside dnSpyEx)
```

## Features

- HTTP JSON-RPC server (POST `/rpc`) with `/health` endpoint.
- MCP standard endpoints over HTTP JSON-RPC: `initialize`, `tools/list`, `tools/call`.
- MCP resource endpoints (empty catalog): `resources/list`, `resources/templates/list`.
- Assembly discovery and metadata: `listAssemblies`, `getAssemblyInfo`.
- Namespace/type/member listing: `listNamespaces`, `listTypes`, `listMembers`.
- Type inspection: `getTypeInfo`, `getTypeFields`, `getTypeProperty`, `getMethodSignature`.
- Decompilation: `decompileMethod`, `decompileField`, `decompileProperty`, `decompileEvent`, `decompileType`.
- IL output and body stats: `decompileMethodIL`, `getMethodBodyInfo`.
- Search utilities: `search`, `searchTypes`, `searchMembers`, `searchStrings`.
- Reference analysis: `findReferences`.
- Call and usage analysis: `getCallers`, `getCallees`, `findMethodUsages`, `findFieldUsages`, `findTypeUsages`.
- Type relations and implementations: `getTypeDependencies`, `getInheritanceTree`, `findPathToType`, `findDerivedTypes`, `findImplementations`.
- Overrides and attributes: `getOverridesChain`, `findAttributes`.
- Assembly graph and symbol resolution: `getAssemblyGraph`, `symbolResolve`.
- Export helpers: `exportSelectedDecompile`.
- UI helpers: `getSelectedText`, `getSelectedMember`, `openInDnSpy`.
- Usage examples: `exampleFlow`.

## Components (MCP-related)

- `Extensions/dnSpyEx.MCP/` - dnSpyEx extension that starts an HTTP JSON-RPC server.
  - `TheExtension.cs` starts/stops the server on AppLoaded/AppExit.
  - `Http/McpHttpServer.cs` HTTP JSON-RPC server (POST /rpc).
  - `Ipc/McpRequestHandler.cs` MVP tool handlers (runs on UI dispatcher).

## Agentic Workflow (Unity Mono + BepInEx 5.x)

This repo now includes an automation workflow layer for game-side loop closure:

- Entry script: `scripts/workflow/run.ps1`
- Stages: `bootstrap -> scaffold -> build -> deploy -> run -> verify -> report`
- Resume support: `-Resume` continues from `.workflow/state.json`
- Output reports: `.workflow/report.json` and `.workflow/report.md`

### Quick Start

1) Copy and edit profile:

```PS
Copy-Item .\profiles\demo.unity-mono.yaml .\profiles\mygame.yaml
```

2) Run full workflow:

```PS
.\scripts\workflow\run.ps1 -Profile .\profiles\mygame.yaml -Stage full
```

Or run with direct arguments (no profile required):

```PS
.\scripts\workflow\run.ps1 -GameDir "D:\Games\YourGame" -GameExe "YourGame.exe" -Requirement "主角无敌" -Stage full
```

3) Resume after fixing errors:

```PS
.\scripts\workflow\run.ps1 -Profile .\profiles\mygame.yaml -Stage full -Resume
```

### Profile Keys

Top-level keys in profile:

- `workflow`
- `game`
- `bepinex`
- `project`
- `build`
- `deploy`
- `run`
- `verify`

Default behavior:

- `bepinex.major` is fixed to `5` in V1.
- `project.framework` defaults to `net472`.
- `deploy.pluginsDir` defaults to `${game.dir}\BepInEx\plugins\${project.name}`.
- `verify.logFile` defaults to `${game.dir}\BepInEx\LogOutput.log`.
- `verify.successPatterns` defaults to plugin id + BepInEx marker + `Plugin loaded`.

### Generated Artifacts

- `.workflow/state.json`: stage status and resumable context.
- `.workflow/bootstrap/install-manifest.json`: BepInEx install metadata.
- `.workflow/workspace/<ProjectName>/references.lock.json`: auto reference snapshot.
- `.workflow/deploy.manifest.json`: deployed file manifest.
- `.workflow/logs/build.log`: build output.

### Skill Pack

A companion skill pack is included:

- `skills/dnspy-agent-loop/SKILL.md`
- `skills/dnspy-agent-loop/references/workflow.md`
- `skills/dnspy-agent-loop/references/troubleshooting.md`

### Package For Other Agentic

Build distributable skill pack:

```PS
.\scripts\package-skill-pack.ps1
```

Output:

- staging folder: `.artifacts/dnspy-agent-workflow-skillpack-<timestamp>/`
- zip file: `.artifacts/dnspy-agent-workflow-skillpack-<timestamp>.zip`

Detailed guide:

- `docs/AGENTIC-SKILL-PACK-USAGE.zh-CN.md`
- `docs/AGENTIC-INTENT-CONTRACT.zh-CN.md`

## AI Chat CLI Wrapper

A minimal OpenAI-compatible CLI wrapper is available:

- Script: `scripts/ai-chat-cli.ps1`
- API endpoint: `<baseurl>/v1/chat/completions`
- Supports multi-turn chat and command controls.

### Quick Start

Set env vars:

```PS
$env:OPENAI_BASE_URL = "https://your-api-host"
$env:OPENAI_API_KEY = "your_api_key"
$env:OPENAI_MODEL = "gpt-4o-mini"
```

Run interactive chat:

```PS
.\scripts\ai-chat-cli.ps1
```

Run single prompt:

```PS
.\scripts\ai-chat-cli.ps1 -Once -Prompt "你好，给我一个 C# 控制台示例"
```

Built-in commands:

- `/help`
- `/exit`
- `/clear`
- `/save <path>`
- `/system <prompt>`

## Build

This repo still builds dnSpyEx. The upstream build script is kept.

```PS
./build.ps1 -NoMsbuild
```

Or build the full solution:

```PS
dotnet build dnSpy.sln -c Release
```

## Run

1) Launch dnSpyEx from the build output (net48 or net10.0-windows):
```
dnSpy\dnSpy\bin\Release\net48\dnSpy.exe
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

MCP standard example (Codex):
```json
{ "jsonrpc": "2.0", "id": 1, "method": "initialize", "params": { "protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": { "name": "codex", "version": "1.0" } } }
{ "jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {} }
{ "jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": { "name": "listAssemblies", "arguments": {} } }
```

## MCP Tools

- `listAssemblies`
- `getAssemblyInfo`
- `listNamespaces`
- `listTypes`
- `listMembers`
- `getTypeInfo`
- `getTypeFields`
- `getTypeProperty`
- `getMethodSignature`
- `decompileMethod` / `decompileField` / `decompileProperty` / `decompileEvent` / `decompileType`
- `decompileMethodIL`
- `getMethodBodyInfo`
- `searchTypes` / `searchMembers` / `searchStrings` / `search`
- `findReferences`
- `findMethodUsages` / `findFieldUsages` / `findTypeUsages`
- `findImplementations`
- `findDerivedTypes`
- `getCallers` / `getCallees`
- `getTypeDependencies`
- `getInheritanceTree`
- `getOverridesChain`
- `findPathToType`
- `findAttributes`
- `getAssemblyGraph`
- `symbolResolve`
- `exportSelectedDecompile`
- `getSelectedText`
- `getSelectedMember`
- `openInDnSpy`
- `exampleFlow`

## Configuration

HTTP server configuration:
- Env var: `DNSPYEX_MCP_HTTP_PREFIX` (e.g. `http://127.0.0.1:13337/`)
- Env var: `DNSPYEX_MCP_HTTP_PORT` (default `13337`)

## Upstream

This repo is based on dnSpyEx: https://github.com/dnSpyEx/dnSpy

## License

dnSpy is licensed under GPLv3. See `dnSpy/dnSpy/LicenseInfo/GPLv3.txt`.
