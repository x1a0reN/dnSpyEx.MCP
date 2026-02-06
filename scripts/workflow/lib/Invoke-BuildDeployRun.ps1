function Get-WorkflowProjectPath {
    param([hashtable]$Context)
    $state = $Context.State
    $fromState = [string](Get-ContextValue -State $state -StageName "scaffold" -Key "csprojPath")
    if (-not [string]::IsNullOrWhiteSpace($fromState) -and (Test-Path -LiteralPath $fromState)) {
        return $fromState
    }

    $projectName = [string]$Context.Config.project.name
    $projectDir = Join-Path $Context.WorkflowRoot ("workspace\" + $projectName)
    $csproj = Get-ChildItem -LiteralPath $projectDir -Filter *.csproj -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $csproj) {
        throw "Cannot find project file. Run scaffold stage first."
    }
    return $csproj.FullName
}

function Get-BuildSuggestion {
    param([string[]]$BuildOutput)
    $text = ($BuildOutput -join "`n")
    if ($text -match "CS0246") {
        return "CS0246: missing type/reference. Re-run scaffold and verify references.lock.json."
    }
    if ($text -match "CS0103") {
        return "CS0103: unknown symbol. Check naming and using statements in generated code."
    }
    if ($text -match "NU1\d{3}") {
        return "NuGet error. Check network/source and run dotnet restore if needed."
    }
    return "Review .workflow/logs/build.log and retry with -Resume after minimal fixes."
}

function Invoke-WorkflowAgentHandoff {
    param([hashtable]$Context)
    $cfg = $Context.Config
    if ([string]$cfg.agent.mode -ne "external") {
        throw "V1 only supports agent.mode=external."
    }

    $csprojPath = Get-WorkflowProjectPath -Context $Context
    $projectDir = Split-Path -Parent $csprojPath
    $handoffPath = [string]$cfg.agent.handoffFile
    $handoffDir = Split-Path -Parent $handoffPath
    if (-not (Test-Path -LiteralPath $handoffDir)) {
        New-Item -ItemType Directory -Path $handoffDir -Force | Out-Null
    }

    $content = @"
# Agentic Task Handoff

## Objective
- Implement plugin feature in `Plugin.cs` under project: `$($cfg.project.name)`.
- Keep BepInEx plugin metadata stable:
  - id: `$($cfg.project.id)`
  - version: `$($cfg.project.version)`

## Paths
- Project: `$projectDir`
- Project file: `$csprojPath`
- Build output DLL: `$($cfg.project.outputDll)`

## MCP Integration Guidance
- MCP endpoint: `http://127.0.0.1:13337/rpc`
- Suggested calls:
  - `tools/list`
  - `tools/call` with `listAssemblies`, `searchTypes`, `decompileType`, `findReferences`

## Regression Checklist
- Build passes (`dotnet build`).
- Deploy stage copies DLL into game BepInEx plugins directory.
- Verify stage matches one of:
  - `$($cfg.project.id)`
  - `Plugin loaded`

## Notes
- Avoid changing project metadata unless required.
- Keep logs explicit in `Awake()` or `Start()` to simplify verify stage.
"@
    Set-Content -LiteralPath $handoffPath -Value $content -Encoding UTF8

    $command = [string]$cfg.agent.command
    $exitCode = $null
    if (-not [string]::IsNullOrWhiteSpace($command)) {
        Push-Location $projectDir
        try {
            & powershell -NoProfile -ExecutionPolicy Bypass -Command $command
            $exitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
        if ($exitCode -ne 0) {
            throw "agent.command failed with exit code $exitCode."
        }
    }

    return @{
        handoffFile = $handoffPath
        projectDir = $projectDir
        agentCommandExecuted = (-not [string]::IsNullOrWhiteSpace($command))
        agentCommandExitCode = $exitCode
    }
}

function Invoke-WorkflowBuild {
    param([hashtable]$Context)
    $cfg = $Context.Config
    $csprojPath = Get-WorkflowProjectPath -Context $Context
    $projectDir = Split-Path -Parent $csprojPath
    $configuration = [string]$cfg.build.configuration
    $framework = [string]$cfg.project.framework
    $buildLogPath = Join-Path $Context.LogDir "build.log"

    $args = @("build", $csprojPath, "-c", $configuration, "-f", $framework, "-nologo")
    $output = & dotnet @args 2>&1
    $exitCode = $LASTEXITCODE
    Set-Content -LiteralPath $buildLogPath -Value ($output -join "`r`n") -Encoding UTF8

    if ($exitCode -ne 0) {
        $suggestion = Get-BuildSuggestion -BuildOutput $output
        throw "Build failed. suggestion=$suggestion log=$buildLogPath"
    }

    $buildOutputDir = Join-Path $projectDir ("bin\" + $configuration + "\" + $framework)
    $primaryDll = Join-Path $buildOutputDir ([string]$cfg.project.outputDll)
    if (-not (Test-Path -LiteralPath $primaryDll)) {
        $fallback = Get-ChildItem -LiteralPath $buildOutputDir -Filter *.dll -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $fallback) {
            throw "Build succeeded but no DLL found in: $buildOutputDir"
        }
        $primaryDll = $fallback.FullName
    }

    return @{
        csprojPath = $csprojPath
        buildOutputDir = $buildOutputDir
        primaryDll = $primaryDll
        buildLogPath = $buildLogPath
    }
}

function Invoke-WorkflowDeploy {
    param([hashtable]$Context)
    $cfg = $Context.Config
    $state = $Context.State
    $buildOutputDir = [string](Get-ContextValue -State $state -StageName "build" -Key "buildOutputDir")
    if ([string]::IsNullOrWhiteSpace($buildOutputDir) -or -not (Test-Path -LiteralPath $buildOutputDir)) {
        throw "Missing build output directory. Run build stage first."
    }

    $pluginsDir = [string]$cfg.deploy.pluginsDir
    $backupRoot = Join-Path $Context.WorkflowRoot "backups"
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

    $backupPath = $null
    if (Test-Path -LiteralPath $pluginsDir) {
        $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $backupPath = Join-Path $backupRoot ("$($cfg.project.name)_$stamp")
        Copy-Item -LiteralPath $pluginsDir -Destination $backupPath -Recurse -Force
        Remove-Item -LiteralPath $pluginsDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null

    $filesToDeploy = Get-ChildItem -LiteralPath $buildOutputDir -File | Where-Object {
        $_.Extension -in @(".dll", ".pdb")
    }
    foreach ($file in $filesToDeploy) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $pluginsDir $file.Name) -Force
    }

    $manifestPath = Join-Path $Context.WorkflowRoot "deploy.manifest.json"
    $manifest = [ordered]@{
        generatedAt = Get-UtcNowIso
        pluginsDir = $pluginsDir
        backupPath = $backupPath
        files = @()
    }
    foreach ($file in $filesToDeploy) {
        $manifest.files += [ordered]@{
            name = $file.Name
            source = $file.FullName
            destination = Join-Path $pluginsDir $file.Name
        }
    }
    Save-JsonFile -Path $manifestPath -Value $manifest

    return @{
        pluginsDir = $pluginsDir
        deployManifestPath = $manifestPath
        backupPath = $backupPath
        fileCount = $manifest.files.Count
    }
}

function Invoke-WorkflowRun {
    param([hashtable]$Context)
    $cfg = $Context.Config
    $state = $Context.State
    $exePath = [string](Get-ContextValue -State $state -StageName "bootstrap" -Key "gameExePath")
    if ([string]::IsNullOrWhiteSpace($exePath)) {
        $exePath = Get-WorkflowGameExePath -Context $Context
    }
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "Game exe not found: $exePath"
    }

    $existing = @()
    $processName = [System.IO.Path]::GetFileNameWithoutExtension($exePath)
    $all = Get-Process -Name $processName -ErrorAction SilentlyContinue
    foreach ($proc in $all) {
        try {
            if ($proc.Path -and $proc.Path.Equals($exePath, [System.StringComparison]::OrdinalIgnoreCase)) {
                $existing += $proc
            }
        }
        catch {
        }
    }

    $reused = $false
    $pid = $null
    if (($existing | Measure-Object).Count -gt 0) {
        $reused = $true
        $pid = $existing[0].Id
    } else {
        $argValue = $cfg.run.args
        $argList = $null
        if ($argValue -is [System.Collections.IEnumerable] -and -not ($argValue -is [string])) {
            $argList = @($argValue) -join " "
        } else {
            $argList = [string]$argValue
        }
        $process = Start-Process -FilePath $exePath -WorkingDirectory $cfg.game.dir -ArgumentList $argList -PassThru
        $pid = $process.Id
    }

    $verifyLogFile = [string]$cfg.verify.logFile
    $startOffset = 0
    if (Test-Path -LiteralPath $verifyLogFile) {
        $startOffset = (Get-Item -LiteralPath $verifyLogFile).Length
    }

    return @{
        gameExePath = $exePath
        pid = $pid
        reusedProcess = $reused
        startedAt = Get-UtcNowIso
        verifyStartOffset = $startOffset
    }
}
