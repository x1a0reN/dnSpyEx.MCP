---
name: dnspy-agent-loop
description: Build and operate the dnSpyEx reverse-engineering workflow for Unity Mono + BepInEx 5.x using scripts/workflow/run.ps1. Use this skill when setting up bootstrap/scaffold/build/deploy/run/verify/report, mapping user requirement text into workflow config, diagnosing workflow failures, and presenting course demos with resume/recovery.
---

# dnspy-agent-loop

执行 `scripts/workflow/run.ps1` 驱动闭环流程。

## 意图契约（内置）
- 将用户自然语言解析为以下字段：
  - 必填：`game_dir`、`requirement`
  - 可选：`game_name`、`game_exe`、`plugin_name`、`plugin_id`
- 当 `game_dir` 和 `requirement` 已存在时，不再追问，直接执行。
- 仅在以下场景最小追问：
  - `game_dir` 缺失或无效
  - `requirement` 缺失
  - `game_exe` 自动探测失败

## 执行契约（内置）
- 固定阶段链路：`bootstrap -> scaffold -> build -> deploy -> run -> verify -> report`
- 默认执行命令：
```powershell
.\scripts\workflow\run.ps1 -GameDir "<game_dir>" -GameExe "<game_exe>" -Requirement "<requirement>" -PluginName "<plugin_name>" -PluginId "<plugin_id>" -Stage full
```
- 失败恢复命令：
```powershell
.\scripts\workflow\run.ps1 -GameDir "<game_dir>" -GameExe "<game_exe>" -Requirement "<requirement>" -PluginName "<plugin_name>" -PluginId "<plugin_id>" -Stage full -Resume
```
- 最终必须返回：闭环是否成功、`.workflow/report.md`、`.workflow/report.json`、失败阶段与重试命令。

## MCP 策略（内置）
- 需要逆向分析时，要求目标 Agentic 已连接 dnSpyEx MCP（`http://127.0.0.1:13337/rpc`）。
- 仅执行构建/部署/启动/验收闭环时，可不依赖 MCP。

## 执行要求
- 优先使用 `full` 阶段完成端到端流程。
- 失败后优先使用 `-Resume` 恢复，不从头重跑。
- 始终检查 `.workflow/report.json`、`.workflow/report.md`、`.workflow/logs/build.log`。
- 不修改 MCP 协议端点与现有工具名。

## 阶段策略
- `bootstrap`: 先校验 `game.dir` 与 Unity Managed 目录，再处理 BepInEx。
- `scaffold`: 只通过模板生成项目并自动写引用，避免手工改引用。
- `build/deploy/run/verify`: 每阶段结束记录证据文件和关键路径。
- `report`: 汇总结果并提供可直接复制的重试命令。

## 参考资料
- 工作流详细说明: `references/workflow.md`
- 常见故障排查: `references/troubleshooting.md`
