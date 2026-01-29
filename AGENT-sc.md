# AGENT (简体中文)

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

## 下一步
- 编译解决方案，确认两个新项目可正常构建。
- 启动 dnSpyEx 并验证 AppLoaded 后 NamedPipe 服务器正常启动。
- 运行 bridge 并测试 MCP 调用：listAssemblies / listNamespaces / listTypes / listMembers / decompile / getSelectedText。

## 备注
- 用户要求每次进度更新都同步记录到 AGENTS.md。
