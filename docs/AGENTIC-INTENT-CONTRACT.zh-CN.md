# Agentic 意图协议（Skill Pack）

## 目标
让任意 Agentic 在接收用户自然语言后，自动理解并执行 dnSpy 工作流闭环。

## 输入契约
用户可能以自由文本描述需求。Agent 必须解析为：
- `game_name`（可选）
- `game_dir`（必填）
- `requirement`（必填）
- `game_exe`（可选，缺失时自动探测或最小追问）
- `plugin_name`（可选，默认 `AutoPlugin`）
- `plugin_id`（可选，默认 `com.autogen.<plugin_name_lower>`）

## 执行契约
1. 当 `game_dir` 与 `requirement` 已确定时，不再追问，直接执行工作流。
2. 固定阶段链路：
   - `bootstrap -> scaffold -> build -> deploy -> run -> verify -> report`
3. 执行命令（示例）：
```powershell
.\scripts\workflow\run.ps1 -GameDir "<game_dir>" -GameExe "<game_exe>" -Requirement "<requirement>" -PluginName "<plugin_name>" -PluginId "<plugin_id>" -Stage full
```
4. 失败恢复命令：
```powershell
.\scripts\workflow\run.ps1 -GameDir "<game_dir>" -GameExe "<game_exe>" -Requirement "<requirement>" -PluginName "<plugin_name>" -PluginId "<plugin_id>" -Stage full -Resume
```

## 输出契约
Agent 最终必须返回：
- 是否闭环成功（成功/失败）。
- 报告路径：`.workflow/report.md`、`.workflow/report.json`。
- 如失败：明确失败阶段 + 下一条重试命令。

## 追问规则（最小化）
只有在以下情况允许追问：
1. `game_dir` 缺失或路径无效。
2. `requirement` 缺失。
3. `game_exe` 自动探测失败。

其余信息允许使用默认值直接执行。
