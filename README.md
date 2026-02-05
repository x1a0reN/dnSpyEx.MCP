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
- Assembly discovery and metadata: `listAssemblies`, `getAssemblyInfo`.
- Namespace/type/member listing: `listNamespaces`, `listTypes`, `listMembers`.
- Type inspection: `getTypeInfo`, `getTypeFields`, `getTypeProperty`, `getMethodSignature`.
- Decompilation: `decompileMethod`, `decompileField`, `decompileProperty`, `decompileEvent`, `decompileType`.
- Search utilities: `search`, `searchTypes`, `searchMembers`, `searchStrings`.
- Reference analysis: `findReferences`.
- Call analysis: `getCallers`, `getCallees`.
- Type relations: `getTypeDependencies`, `getInheritanceTree`, `findPathToType`.
- UI helpers: `getSelectedText`, `getSelectedMember`, `openInDnSpy`.
- Usage examples: `exampleFlow`.

## Components (MCP-related)

- `Extensions/dnSpyEx.MCP/` - dnSpyEx extension that starts an HTTP JSON-RPC server.
  - `TheExtension.cs` starts/stops the server on AppLoaded/AppExit.
  - `Http/McpHttpServer.cs` HTTP JSON-RPC server (POST /rpc).
  - `Ipc/McpRequestHandler.cs` MVP tool handlers (runs on UI dispatcher).

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
- `searchTypes` / `searchMembers` / `searchStrings` / `search`
- `findReferences`
- `getCallers` / `getCallees`
- `getTypeDependencies`
- `getInheritanceTree`
- `findPathToType`
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
