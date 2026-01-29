# AGENTS（简体中文）

## 背景
- 目标：为 dnSpyEx 构建 MCP 插件，通过本地 IPC + stdio bridge 让 MCP 客户端与 dnSpyEx UI 交互。
- 仓库：D:\Projects\dnSpyEx.MCP

## 目标架构
- dnSpyEx 插件内置本地 IPC（优先 NamedPipe，可选 HTTP）。
- 独立的 MCP stdio bridge（控制台）负责 stdio JSON-RPC，并转发到 IPC。
- 暴露 MVP 工具：程序集 / 命名空间 / 类型 / 成员 / 反编译 / 选中代码。

## 项目结构（MCP 相关）
- Extensions\dnSpyEx.MCP\dnSpyEx.MCP.csproj：扩展项目，引用 dnSpy 合约与 Newtonsoft.Json。
- Extensions\dnSpyEx.MCP\TheExtension.cs：扩展入口，在 AppLoaded 启动服务、AppExit 停止服务。
- Extensions\dnSpyEx.MCP\McpHost.cs：MEF 导出宿主，将 dnSpy 服务注入 IPC 服务器。
- Extensions\dnSpyEx.MCP\Ipc\McpIpcServer.cs：NamedPipe JSON-RPC 服务器（按行读取）；支持 DNSPYEX_MCP_PIPE 覆盖管道名。
- Extensions\dnSpyEx.MCP\Ipc\McpRequestHandler.cs：RPC 分发与 MVP 工具实现；在 UI 线程执行。
- Tools\dnSpyEx.MCP.Bridge\dnSpyEx.MCP.Bridge.csproj：MCP stdio bridge 控制台项目。
- Tools\dnSpyEx.MCP.Bridge\Program.cs：bridge 入口，连接管道并运行 MCP 循环。
- Tools\dnSpyEx.MCP.Bridge\McpServer.cs：MCP JSON-RPC（initialize/tools/*）；将工具调用转发到管道。
- Tools\dnSpyEx.MCP.Bridge\ToolCatalog.cs：工具定义与输入 schema，映射到 RPC 方法。
- Tools\dnSpyEx.MCP.Bridge\PipeClient.cs：NamedPipe 客户端（按行传 JSON）。
- Tools\dnSpyEx.MCP.Bridge\McpPipeDefaults.cs：管道名常量与环境变量名。
- dnSpy.sln：解决方案已包含 dnSpyEx.MCP 与 dnSpyEx.MCP.Bridge。

## 决策
- 采用混合式：插件侧暴露 NamedPipe（或 HTTP），bridge 负责 stdio MCP。
- 先实现 MVP 工具集，再逐步扩展。

## 当前进度
- 2026-01-29：创建 AGENTS.md。
- 2026-01-29：新增 dnSpyEx.MCP 扩展项目，内置 NamedPipe JSON-RPC 服务与 MVP 处理器。
- 2026-01-29：新增 dnSpyEx.MCP.Bridge 控制台项目（stdio MCP -> NamedPipe）。
- 2026-01-29：更新 dnSpy.sln，纳入上述两个项目。
- 2026-01-29：在本机尝试构建失败，原因是 .NET SDK 9 不支持 net10.0-windows（NETSDK1045）；需安装 .NET 10 SDK。
- 2026-01-29：将 AGENT-sc.md 重命名为 AGENTS-SC.md，以统一命名。
- 2026-01-29：修复 McpRequestHandler 中 UTF8String 序列化与可空 MVID 处理；将 bridge 目标框架改为 net10.0-windows。
- 2026-01-29：build.ps1 -NoMsbuild 在本机超时；已单独构建 dnSpyEx.MCP（net48/net10.0-windows）与 dnSpyEx.MCP.Bridge（net10.0-windows），均 0 错误。
- 2026-01-29：扩展程序集名改为 dnSpyEx.MCP.x；构建 dnSpyEx.MCP：net48（1 个警告）、net10.0-windows（0 警告），均无错误。

## 下一步
- 编译解决方案，确认两个新项目可正常构建。
- 启动 dnSpyEx 并验证 AppLoaded 后 NamedPipe 服务器正常启动。
- 运行 bridge 并测试 MCP 调用：listAssemblies / listNamespaces / listTypes / listMembers / decompile / getSelectedText。

## 构建与使用指南

### 前置条件
- .NET SDK 10.x（仓库目标为 net10.0-windows）。
- 可选：Visual Studio Build Tools（完整 dnSpyEx 构建需要 COM 引用，build.ps1 偏好 MSBuild）。

### 构建
方案 A（仓库推荐，使用脚本）：
```
./build.ps1 -NoMsbuild
```

方案 B（构建解决方案）：
```
dotnet build dnSpy.sln -c Release
```

注意：本机上构建失败，原因是 .NET SDK 9 不能构建 net10.0-windows。安装 .NET 10 SDK 后重试。

### 运行 dnSpyEx + MCP bridge
1) 启动 dnSpyEx（net48 或 net10.0-windows 输出）：
```
dnSpy\dnSpy\bin\Release\net48\dnSpy.exe
```

2) 启动 MCP bridge：
```
dotnet run --project Tools/dnSpyEx.MCP.Bridge -c Release
```

### 管道配置
- 默认管道名：`dnSpyEx.MCP`
- 环境变量覆盖：`DNSPYEX_MCP_PIPE`
- bridge 参数：`--pipe <name>`

### 可用 MCP 工具（MVP）
- dnspy.listAssemblies
- dnspy.listNamespaces
- dnspy.listTypes
- dnspy.listMembers
- dnspy.decompile
- dnspy.getSelectedText

### 接入 AI IDE（支持 MCP）
思路：在 IDE 中把 bridge 配置为 stdio MCP server。通用配置示例：

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

流程：
1) 先启动 dnSpyEx（插件在 AppLoaded 时启动管道）。
2) 启动 IDE 的 MCP server（bridge 连接管道）。
3) 在 IDE 的 MCP 工具列表中调用工具。

## 备注
- 用户要求每次进度更新都同步记录到 AGENTS.md。
