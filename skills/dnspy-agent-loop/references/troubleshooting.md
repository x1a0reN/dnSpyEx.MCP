# Troubleshooting

## bootstrap 失败
- 症状: `Cannot locate Unity Managed directory`
  - 处理: 在 profile 指定 `game.unityManagedDir`。
- 症状: `Cannot find BepInEx asset`
  - 处理: 检查 `bepinex.source.repo`、网络、`bepinex.major` 是否为 5。

## scaffold 失败
- 症状: `No DLL references found`
  - 处理: 先确认 `BepInEx/core` 与 `*_Data/Managed` 是否存在 DLL。

## build 失败
- 症状: `CS0246`
  - 处理: 缺引用，重跑 `scaffold` 或检查 `references.lock.json`。
- 症状: `CS0103`
  - 处理: 变量或成员名错误，回看 Agent 生成代码。
- 证据: `.workflow/logs/build.log`

## deploy/run/verify 失败
- deploy: 检查 `deploy.pluginsDir` 写权限。
- run: 检查 `game.exe` 是否匹配实际文件名。
- verify: 检查 `verify.logFile` 路径、`successPatterns` 是否包含插件日志标记。

## 快速恢复
```powershell
.\scripts\workflow\run.ps1 -Profile <your-profile> -Stage full -Resume
```
