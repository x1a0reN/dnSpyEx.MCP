---
name: dnspy-agent-loop
description: Build and operate the dnSpyEx reverse-engineering workflow for Unity Mono + BepInEx 5.x using scripts/workflow/run.ps1. Use this skill when setting up bootstrap/scaffold/build/deploy/run/verify/report, mapping user requirement text into workflow config, diagnosing workflow failures, and presenting course demos with resume/recovery.
---

# dnspy-agent-loop

执行 `scripts/workflow/run.ps1` 驱动闭环流程。

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
