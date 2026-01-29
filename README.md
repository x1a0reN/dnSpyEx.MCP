# dnSpyEx.MCP

This repo is a fork of dnSpyEx with an MCP integration layer:
- A dnSpyEx extension hosts a local IPC server (NamedPipe).
- A separate stdio bridge exposes MCP JSON-RPC and forwards calls to the pipe.

The goal is to let MCP clients talk to the dnSpyEx UI without requiring dnSpyEx to be launched by the MCP client.

## Architecture

```
MCP client (stdio JSON-RPC)
            |
            v
dnSpyEx.MCP.Bridge (console)
            |
        NamedPipe
            |
            v
dnSpyEx.MCP extension (inside dnSpyEx)
```

## Components (MCP-related)

- `Extensions/dnSpyEx.MCP/` - dnSpyEx extension that starts a NamedPipe JSON-RPC server.
  - `TheExtension.cs` starts/stops the server on AppLoaded/AppExit.
  - `Ipc/McpIpcServer.cs` line-delimited JSON-RPC over NamedPipe.
  - `Ipc/McpRequestHandler.cs` MVP tool handlers (runs on UI dispatcher).
- `Tools/dnSpyEx.MCP.Bridge/` - MCP stdio bridge console app.
  - `Program.cs` entrypoint.
  - `McpServer.cs` MCP JSON-RPC (initialize/tools/*).
  - `ToolCatalog.cs` tool definitions and input schemas.
  - `PipeClient.cs` NamedPipe client (line-delimited JSON).

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

2) Run the MCP bridge:
```
dotnet run --project Tools/dnSpyEx.MCP.Bridge -c Release
```

## MCP Tools (MVP)

- `dnspy.listAssemblies`
- `dnspy.listNamespaces`
- `dnspy.listTypes`
- `dnspy.listMembers`
- `dnspy.decompile`
- `dnspy.getSelectedText`

## Configuration

Pipe name can be overridden with:
- Env var: `DNSPYEX_MCP_PIPE`
- Bridge arg: `--pipe <name>`

Default pipe name: `dnSpyEx.MCP`

## Upstream

This repo is based on dnSpyEx: https://github.com/dnSpyEx/dnSpy

## License

dnSpy is licensed under GPLv3. See `dnSpy/dnSpy/LicenseInfo/GPLv3.txt`.
