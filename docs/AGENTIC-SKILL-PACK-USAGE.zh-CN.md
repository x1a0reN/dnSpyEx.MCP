# dnSpy AgentWorkflow Skill Pack 使用教程

## 1. 这份包是什么
- 这是给其他 Agentic 直接复用的闭环工作流包。
- 目标行为：用户只说 `游戏名 + 路径 + 需求`，Agent 自动执行：
  - `bootstrap -> scaffold -> build -> deploy -> run -> verify -> report`

## 2. 包内结构
- `skill/dnspy-agent-loop/`
  - Skill 本体（`SKILL.md` + `references`）。
- `workflow/scripts/workflow/`
  - 工作流脚本（入口 `run.ps1` + `lib/*.ps1`）。
- `workflow/templates/BepInExPlugin/`
  - 插件模板。
- `workflow/profiles/demo.unity-mono.yaml`
  - 示例配置。
- `docs/`
  - 使用指南与意图协议文档。

## 3. 给其他 Agentic 的两种接入方式

### 方式 A：支持 Skill 目录（推荐）
1. 把 `skill/dnspy-agent-loop` 拷贝到目标 Agent 的 Skill 目录。
2. 在目标 Agent 会话中启用该 Skill。
3. 告诉用户按自然语言输入：
   - `帮我逆向<游戏名>，路径是 <game_dir>，需求是 <requirement>`
4. Agent 应自动解析意图并执行工作流。

### 方式 B：不支持 Skill 目录（降级）
1. 把 `docs/AGENTIC-INTENT-CONTRACT.zh-CN.md` 作为系统提示词/团队规范。
2. 把 `workflow/` 目录放到本地工作目录。
3. Agent 仍按同样自然语言触发工作流。

## 4. AI 执行规范（必须遵守）
- 必须优先从用户自然语言中提取：
  - `game_name`
  - `game_dir`
  - `requirement`
- 可选提取：
  - `game_exe`
  - `plugin_name`
  - `plugin_id`
- 只有缺关键字段时才最小追问。
- 拿到关键字段后，直接执行：
```powershell
.\scripts\workflow\run.ps1 -GameDir "<game_dir>" -GameExe "<game_exe>" -Requirement "<requirement>" -PluginName "<plugin_name>" -PluginId "<plugin_id>" -Stage full
```
- 如果失败，执行：
```powershell
.\scripts\workflow\run.ps1 -GameDir "<game_dir>" -GameExe "<game_exe>" -Requirement "<requirement>" -PluginName "<plugin_name>" -PluginId "<plugin_id>" -Stage full -Resume
```

## 5. 典型对话示例
- 用户：
  - `帮我逆向逃离鸭科夫，游戏路径是 D:\Games\Duckov，需求是主角无敌并支持 F6 开关`
- Agent：
  - 自动解析并执行工作流。
  - 完成后返回 `.workflow/report.md` 结论。

## 6. 验收清单
- `.workflow/report.json` 存在并可读。
- `.workflow/report.md` 存在并包含阶段状态。
- `.workflow/workspace/<PluginName>/Plugin.cs` 存在并包含需求日志。
- 游戏插件目录已生成 DLL。
- `verify` 阶段通过或给出明确失败原因与重试命令。

## 7. 常见问题
- Q: 需要 MCP 源码吗？
  - A: 这个 Skill 包不包含 MCP 源码，只包含工作流与 Skill。
- Q: GitHub API 限流导致 bootstrap 失败怎么办？
  - A: 重试，或先手动安装 BepInEx 后再跑 `-Resume`。
