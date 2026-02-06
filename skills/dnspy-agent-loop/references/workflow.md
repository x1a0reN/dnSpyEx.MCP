# Workflow Reference

## 入口命令
```powershell
.\scripts\workflow\run.ps1 -Profile .\profiles\demo.unity-mono.yaml -Stage full
```

或直接传入路径与需求（最简）：
```powershell
.\scripts\workflow\run.ps1 -GameDir "D:\Games\YourGame" -GameExe "YourGame.exe" -Requirement "主角无敌" -Stage full
```

## 阶段定义
- `bootstrap`: 检测游戏目录、识别架构、安装 BepInEx（如缺失）。
- `scaffold`: 生成插件项目，自动扫描并写入引用，产出 `references.lock.json`。
- `build`: 执行 `dotnet build` 并写入 `.workflow/logs/build.log`。
- `deploy`: 复制到 `BepInEx\plugins` 并保留备份。
- `run`: 启动或复用游戏进程，记录 PID。
- `verify`: 轮询日志关键字，命中即通过。
- `report`: 输出 `.workflow/report.json` 与 `.workflow/report.md`。

## Resume
```powershell
.\scripts\workflow\run.ps1 -Profile .\profiles\demo.unity-mono.yaml -Stage full -Resume
```

## 关键产物
- `.workflow/state.json`: 阶段状态与上下文数据。
- `.workflow/bootstrap/install-manifest.json`: BepInEx 安装信息。
- `.workflow/workspace/<ProjectName>/references.lock.json`: 自动引用清单。
- `.workflow/deploy.manifest.json`: 部署结果。
- `.workflow/report.json`: 结构化总结。
