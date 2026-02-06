param(
    [string]$Profile,
    [string]$GameDir,
    [string]$GameExe,
    [string]$Requirement,
    [string]$PluginName,
    [string]$PluginId,
    [string]$PluginVersion,
    [ValidateSet("bootstrap", "scaffold", "build", "deploy", "run", "verify", "report", "full")]
    [string]$Stage = "full",
    [switch]$Resume
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$script:WorkflowRoot = Join-Path $script:RepoRoot ".workflow"
$script:StatePath = Join-Path $script:WorkflowRoot "state.json"
$script:ReportPath = Join-Path $script:WorkflowRoot "report.json"
$script:ReportMarkdownPath = Join-Path $script:WorkflowRoot "report.md"
$script:LogDir = Join-Path $script:WorkflowRoot "logs"
$script:StageSequence = @("bootstrap", "scaffold", "build", "deploy", "run", "verify", "report")

New-Item -ItemType Directory -Path $script:WorkflowRoot -Force | Out-Null
New-Item -ItemType Directory -Path $script:LogDir -Force | Out-Null

function Write-WorkflowInfo {
    param([string]$Message)
    Write-Host "[workflow] $Message"
}

function Get-UtcNowIso {
    return (Get-Date).ToUniversalTime().ToString("o")
}

function ConvertTo-JsonText {
    param(
        [Parameter(Mandatory = $true)]
        $Value
    )
    return ($Value | ConvertTo-Json -Depth 64)
}

function Save-JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        $Value
    )
    $json = ConvertTo-JsonText -Value $Value
    Set-Content -Path $Path -Value $json -Encoding UTF8
}

function Load-JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )
    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }
    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $null
    }
    try {
        return ConvertFrom-Json -InputObject $raw -AsHashtable -Depth 64
    }
    catch {
        $obj = ConvertFrom-Json -InputObject $raw
        return ConvertTo-Hashtable -InputObject $obj
    }
}

function Test-BlankOrComment {
    param([string]$Line)
    if ($null -eq $Line) {
        return $true
    }
    $trim = $Line.Trim()
    return ($trim.Length -eq 0 -or $trim.StartsWith("#"))
}

function Get-LineIndent {
    param([string]$Line)
    $count = 0
    foreach ($ch in $Line.ToCharArray()) {
        if ($ch -eq " ") {
            $count++
            continue
        }
        break
    }
    return $count
}

function Convert-YamlScalar {
    param([string]$Token)
    $v = $Token.Trim()
    if ($v.StartsWith("'") -and $v.EndsWith("'") -and $v.Length -ge 2) {
        return $v.Substring(1, $v.Length - 2)
    }
    if ($v.StartsWith('"') -and $v.EndsWith('"') -and $v.Length -ge 2) {
        return $v.Substring(1, $v.Length - 2)
    }
    if ($v -eq "null" -or $v -eq "~") {
        return $null
    }
    if ($v -eq "true") {
        return $true
    }
    if ($v -eq "false") {
        return $false
    }
    $intValue = 0
    if ([int]::TryParse($v, [ref]$intValue)) {
        return $intValue
    }
    $doubleValue = 0.0
    if ([double]::TryParse($v, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$doubleValue)) {
        return $doubleValue
    }
    return $v
}

function Get-NextMeaningfulLineInfo {
    param(
        [string[]]$Lines,
        [int]$StartIndex
    )
    for ($cursor = $StartIndex; $cursor -lt $Lines.Length; $cursor++) {
        $candidate = $Lines[$cursor]
        if (Test-BlankOrComment -Line $candidate) {
            continue
        }
        return @{
            Index = $cursor
            Indent = Get-LineIndent -Line $candidate
            Trimmed = $candidate.Trim()
        }
    }
    return $null
}

function Parse-YamlBlock {
    param(
        [string[]]$Lines,
        [ref]$Index,
        [int]$Indent
    )
    $lineInfo = Get-NextMeaningfulLineInfo -Lines $Lines -StartIndex $Index.Value
    if ($null -eq $lineInfo) {
        return @{}
    }
    if ($lineInfo.Indent -lt $Indent) {
        return @{}
    }
    if ($lineInfo.Trimmed.StartsWith("- ")) {
        return Parse-YamlList -Lines $Lines -Index $Index -Indent $Indent
    }
    return Parse-YamlMap -Lines $Lines -Index $Index -Indent $Indent
}

function Parse-YamlMap {
    param(
        [string[]]$Lines,
        [ref]$Index,
        [int]$Indent
    )
    $map = [ordered]@{}
    while ($Index.Value -lt $Lines.Length) {
        $line = $Lines[$Index.Value]
        if (Test-BlankOrComment -Line $line) {
            $Index.Value++
            continue
        }
        $lineIndent = Get-LineIndent -Line $line
        if ($lineIndent -lt $Indent) {
            break
        }
        if ($lineIndent -gt $Indent) {
            throw "Invalid YAML indentation near line $($Index.Value + 1)."
        }
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("- ")) {
            break
        }
        if ($trimmed -notmatch "^([A-Za-z0-9_.-]+)\s*:\s*(.*)$") {
            throw "Invalid YAML key/value near line $($Index.Value + 1): $trimmed"
        }
        $key = $matches[1]
        $rawValue = $matches[2]
        $Index.Value++
        if (-not [string]::IsNullOrWhiteSpace($rawValue)) {
            $map[$key] = Convert-YamlScalar -Token $rawValue
            continue
        }
        $nextInfo = Get-NextMeaningfulLineInfo -Lines $Lines -StartIndex $Index.Value
        if ($null -eq $nextInfo -or $nextInfo.Indent -le $lineIndent) {
            $map[$key] = $null
            continue
        }
        if ($nextInfo.Indent -ne ($lineIndent + 2)) {
            throw "Nested YAML indentation must increase by 2 spaces near line $($nextInfo.Index + 1)."
        }
        $map[$key] = Parse-YamlBlock -Lines $Lines -Index $Index -Indent ($lineIndent + 2)
    }
    return $map
}

function Parse-YamlList {
    param(
        [string[]]$Lines,
        [ref]$Index,
        [int]$Indent
    )
    $list = @()
    while ($Index.Value -lt $Lines.Length) {
        $line = $Lines[$Index.Value]
        if (Test-BlankOrComment -Line $line) {
            $Index.Value++
            continue
        }
        $lineIndent = Get-LineIndent -Line $line
        if ($lineIndent -lt $Indent) {
            break
        }
        if ($lineIndent -gt $Indent) {
            throw "Invalid YAML list indentation near line $($Index.Value + 1)."
        }
        $trimmed = $line.Trim()
        if (-not $trimmed.StartsWith("- ")) {
            break
        }
        $item = $trimmed.Substring(2).Trim()
        $Index.Value++
        if ([string]::IsNullOrWhiteSpace($item)) {
            $nextInfo = Get-NextMeaningfulLineInfo -Lines $Lines -StartIndex $Index.Value
            if ($null -eq $nextInfo -or $nextInfo.Indent -le $lineIndent) {
                $list += $null
                continue
            }
            if ($nextInfo.Indent -ne ($lineIndent + 2)) {
                throw "Nested YAML list indentation must increase by 2 spaces near line $($nextInfo.Index + 1)."
            }
            $list += ,(Parse-YamlBlock -Lines $Lines -Index $Index -Indent ($lineIndent + 2))
            continue
        }
        $list += (Convert-YamlScalar -Token $item)
    }
    return $list
}

function ConvertFrom-SimpleYaml {
    param([string]$Text)
    $lines = $Text -split "(`r`n|`n|`r)"
    $index = 0
    return Parse-YamlBlock -Lines $lines -Index ([ref]$index) -Indent 0
}

function Import-WorkflowProfile {
    param([string]$ProfilePath)
    $raw = Get-Content -LiteralPath $ProfilePath -Raw -Encoding UTF8
    $trim = $raw.Trim()
    if ($trim.StartsWith("{")) {
        try {
            return ConvertFrom-Json -InputObject $raw -AsHashtable -Depth 64
        }
        catch {
            $obj = ConvertFrom-Json -InputObject $raw
            return ConvertTo-Hashtable -InputObject $obj
        }
    }
    $yamlCommand = Get-Command ConvertFrom-Yaml -ErrorAction SilentlyContinue
    if ($null -ne $yamlCommand) {
        $value = ConvertFrom-Yaml -Yaml $raw
        return ConvertTo-Hashtable -InputObject $value
    }
    return ConvertFrom-SimpleYaml -Text $raw
}

function ConvertTo-Hashtable {
    param($InputObject)
    if ($null -eq $InputObject) {
        return $null
    }
    if ($InputObject -is [System.Collections.IDictionary]) {
        $h = [ordered]@{}
        foreach ($k in $InputObject.Keys) {
            $h[$k] = ConvertTo-Hashtable -InputObject $InputObject[$k]
        }
        return $h
    }
    if ($InputObject -is [System.Collections.IEnumerable] -and -not ($InputObject -is [string])) {
        $arr = @()
        foreach ($item in $InputObject) {
            $arr += ,(ConvertTo-Hashtable -InputObject $item)
        }
        return $arr
    }
    if ($InputObject -is [psobject] -and @($InputObject.PSObject.Properties).Count -gt 0) {
        $h = [ordered]@{}
        foreach ($prop in $InputObject.PSObject.Properties) {
            $h[$prop.Name] = ConvertTo-Hashtable -InputObject $prop.Value
        }
        return $h
    }
    return $InputObject
}

function Merge-Hashtable {
    param(
        [System.Collections.IDictionary]$Base,
        [System.Collections.IDictionary]$Overlay
    )
    $result = [ordered]@{}
    foreach ($k in $Base.Keys) {
        $result[$k] = $Base[$k]
    }
    foreach ($k in $Overlay.Keys) {
        $existing = $result[$k]
        $incoming = $Overlay[$k]
        if ($existing -is [System.Collections.IDictionary] -and $incoming -is [System.Collections.IDictionary]) {
            $result[$k] = Merge-Hashtable -Base $existing -Overlay $incoming
            continue
        }
        $result[$k] = $incoming
    }
    return $result
}

function Get-ConfigDefaults {
    return [ordered]@{
        workflow = [ordered]@{
            name = "dnspyex-agent-loop-v1"
            requirement = "No explicit requirement provided."
        }
        game = [ordered]@{
            exe = $null
            arch = "auto"
            unityManagedDir = $null
        }
        bepinex = [ordered]@{
            channel = "stable"
            major = 5
            source = [ordered]@{
                repo = "BepInEx/BepInEx"
            }
            assetPattern = $null
        }
        project = [ordered]@{
            name = "DemoPlugin"
            id = "com.example.demoplugin"
            version = "0.1.0"
            framework = "net472"
            outputDll = $null
        }
        build = [ordered]@{
            configuration = "Release"
        }
        deploy = [ordered]@{
            pluginsDir = $null
        }
        run = [ordered]@{
            args = $null
        }
        verify = [ordered]@{
            logFile = $null
            timeoutSec = 120
            successPatterns = @()
        }
    }
}

function Flatten-ConfigMap {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Map,
        [string]$Prefix = ""
    )
    $flat = @{}
    foreach ($k in $Map.Keys) {
        $path = if ([string]::IsNullOrEmpty($Prefix)) { $k } else { "$Prefix.$k" }
        $value = $Map[$k]
        if ($value -is [System.Collections.IDictionary]) {
            $nested = Flatten-ConfigMap -Map $value -Prefix $path
            foreach ($nk in $nested.Keys) {
                $flat[$nk] = $nested[$nk]
            }
            continue
        }
        if ($value -is [System.Collections.IEnumerable] -and -not ($value -is [string])) {
            continue
        }
        $flat[$path] = $value
    }
    return $flat
}

function Expand-ConfigPlaceholdersInValue {
    param(
        $Value,
        [Parameter(Mandatory = $true)]
        [hashtable]$FlatMap
    )
    if ($Value -is [string]) {
        return ([regex]::Replace($Value, "\$\{([A-Za-z0-9_.-]+)\}", {
                    param($m)
                    $key = $m.Groups[1].Value
                    if ($FlatMap.ContainsKey($key) -and $null -ne $FlatMap[$key]) {
                        return [string]$FlatMap[$key]
                    }
                    return $m.Value
                }))
    }
    if ($Value -is [System.Collections.IDictionary]) {
        $updated = [ordered]@{}
        foreach ($k in $Value.Keys) {
            $updated[$k] = Expand-ConfigPlaceholdersInValue -Value $Value[$k] -FlatMap $FlatMap
        }
        return $updated
    }
    if ($Value -is [System.Collections.IEnumerable] -and -not ($Value -is [string])) {
        $arr = @()
        foreach ($item in $Value) {
            $arr += ,(Expand-ConfigPlaceholdersInValue -Value $item -FlatMap $FlatMap)
        }
        return $arr
    }
    return $Value
}

function Resolve-ConfigPlaceholders {
    param([System.Collections.IDictionary]$Config)
    $resolved = $Config
    for ($pass = 0; $pass -lt 4; $pass++) {
        $flat = Flatten-ConfigMap -Map $resolved
        $resolved = Expand-ConfigPlaceholdersInValue -Value $resolved -FlatMap $flat
    }
    return $resolved
}

function Resolve-PathByProfile {
    param(
        [string]$PathValue,
        [string]$ProfileDir
    )
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $ProfileDir $PathValue))
}

function Normalize-WorkflowConfig {
    param(
        [System.Collections.IDictionary]$RawConfig,
        [string]$ProfileDir
    )
    if ($null -eq $RawConfig.game -or [string]::IsNullOrWhiteSpace([string]$RawConfig.game.dir)) {
        throw "Profile must provide game.dir."
    }
    $cfg = Resolve-ConfigPlaceholders -Config $RawConfig
    $cfg.game.dir = Resolve-PathByProfile -PathValue ([string]$cfg.game.dir) -ProfileDir $ProfileDir
    if ([string]::IsNullOrWhiteSpace([string]$cfg.game.dir)) {
        throw "game.dir cannot be empty."
    }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.project.outputDll)) {
        $cfg.project.outputDll = "$($cfg.project.name).dll"
    }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.deploy.pluginsDir)) {
        $cfg.deploy.pluginsDir = Join-Path $cfg.game.dir "BepInEx\plugins\$($cfg.project.name)"
    } else {
        $cfg.deploy.pluginsDir = Resolve-PathByProfile -PathValue ([string]$cfg.deploy.pluginsDir) -ProfileDir $ProfileDir
    }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.verify.logFile)) {
        $cfg.verify.logFile = Join-Path $cfg.game.dir "BepInEx\LogOutput.log"
    } else {
        $cfg.verify.logFile = Resolve-PathByProfile -PathValue ([string]$cfg.verify.logFile) -ProfileDir $ProfileDir
    }
    if ($cfg.verify.successPatterns -isnot [System.Collections.IEnumerable] -or $cfg.verify.successPatterns -is [string]) {
        $cfg.verify.successPatterns = @()
    }
    if (($cfg.verify.successPatterns | Measure-Object).Count -eq 0) {
        $cfg.verify.successPatterns = @($cfg.project.id, "[Info   : BepInEx]", "Plugin loaded")
    }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.game.arch)) {
        $cfg.game.arch = "auto"
    }
    $arch = [string]$cfg.game.arch
    if (@("auto", "x64", "x86") -notcontains $arch) {
        throw "game.arch must be one of: auto, x64, x86."
    }
    if ([int]$cfg.bepinex.major -ne 5) {
        throw "V1 only supports bepinex.major = 5."
    }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.workflow.requirement)) {
        $cfg.workflow.requirement = "No explicit requirement provided."
    }
    return $cfg
}

function Build-ConfigOverlayFromArgs {
    param(
        [string]$GameDirValue,
        [string]$GameExeValue,
        [string]$RequirementValue,
        [string]$PluginNameValue,
        [string]$PluginIdValue,
        [string]$PluginVersionValue
    )
    $overlay = [ordered]@{}
    if (-not [string]::IsNullOrWhiteSpace($GameDirValue) -or -not [string]::IsNullOrWhiteSpace($GameExeValue)) {
        $overlay.game = [ordered]@{}
        if (-not [string]::IsNullOrWhiteSpace($GameDirValue)) {
            $overlay.game.dir = $GameDirValue
        }
        if (-not [string]::IsNullOrWhiteSpace($GameExeValue)) {
            $overlay.game.exe = $GameExeValue
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($RequirementValue)) {
        if (-not $overlay.Contains("workflow")) {
            $overlay.workflow = [ordered]@{}
        }
        $overlay.workflow.requirement = $RequirementValue
    }
    if (-not [string]::IsNullOrWhiteSpace($PluginNameValue) -or -not [string]::IsNullOrWhiteSpace($PluginIdValue) -or -not [string]::IsNullOrWhiteSpace($PluginVersionValue)) {
        $overlay.project = [ordered]@{}
        if (-not [string]::IsNullOrWhiteSpace($PluginNameValue)) {
            $overlay.project.name = $PluginNameValue
        }
        if (-not [string]::IsNullOrWhiteSpace($PluginIdValue)) {
            $overlay.project.id = $PluginIdValue
        }
        if (-not [string]::IsNullOrWhiteSpace($PluginVersionValue)) {
            $overlay.project.version = $PluginVersionValue
        }
    }
    return $overlay
}

function New-InitialState {
    param(
        [string]$ProfilePath,
        [System.Collections.IDictionary]$Config
    )
    $stages = [ordered]@{}
    foreach ($name in $script:StageSequence) {
        $stages[$name] = [ordered]@{
            status = "pending"
            startedAt = $null
            completedAt = $null
            durationSec = 0
            error = $null
        }
    }
    return [ordered]@{
        version = 1
        workflowName = $Config.workflow.name
        profilePath = $ProfilePath
        status = "running"
        createdAt = Get-UtcNowIso
        updatedAt = Get-UtcNowIso
        stages = $stages
        data = [ordered]@{}
        failures = @()
    }
}

function Save-WorkflowState {
    param([System.Collections.IDictionary]$State)
    $State.updatedAt = Get-UtcNowIso
    Save-JsonFile -Path $script:StatePath -Value $State
}

function Get-RequestedStageList {
    param(
        [string]$RequestedStage,
        [System.Collections.IDictionary]$State,
        [switch]$ResumeMode
    )
    if ($RequestedStage -ne "full") {
        return @($RequestedStage)
    }
    if (-not $ResumeMode) {
        return $script:StageSequence
    }
    $startIndex = 0
    for ($i = 0; $i -lt $script:StageSequence.Count; $i++) {
        $name = $script:StageSequence[$i]
        $status = [string]$State.stages[$name].status
        if ($status -ne "success") {
            $startIndex = $i
            break
        }
        if ($i -eq $script:StageSequence.Count - 1) {
            $startIndex = $script:StageSequence.Count
        }
    }
    if ($startIndex -ge $script:StageSequence.Count) {
        return @()
    }
    return $script:StageSequence[$startIndex..($script:StageSequence.Count - 1)]
}

function Start-Stage {
    param(
        [System.Collections.IDictionary]$State,
        [string]$StageName
    )
    $State.stages[$StageName].status = "running"
    $State.stages[$StageName].error = $null
    $State.stages[$StageName].startedAt = Get-UtcNowIso
    Save-WorkflowState -State $State
}

function Complete-Stage {
    param(
        [System.Collections.IDictionary]$State,
        [string]$StageName,
        [System.Collections.IDictionary]$StageData
    )
    $now = Get-UtcNowIso
    $start = [DateTimeOffset]::Parse([string]$State.stages[$StageName].startedAt)
    $duration = ([DateTimeOffset]::UtcNow - $start.ToUniversalTime()).TotalSeconds
    $State.stages[$StageName].status = "success"
    $State.stages[$StageName].completedAt = $now
    $State.stages[$StageName].durationSec = [Math]::Round($duration, 3)
    $State.stages[$StageName].error = $null
    if ($null -ne $StageData) {
        if ($null -eq $State.data[$StageName]) {
            $State.data[$StageName] = [ordered]@{}
        }
        foreach ($k in $StageData.Keys) {
            $State.data[$StageName][$k] = $StageData[$k]
        }
    }
    Save-WorkflowState -State $State
}

function Fail-Stage {
    param(
        [System.Collections.IDictionary]$State,
        [string]$StageName,
        [System.Exception]$Exception
    )
    $now = Get-UtcNowIso
    $started = [string]$State.stages[$StageName].startedAt
    $duration = 0
    if (-not [string]::IsNullOrWhiteSpace($started)) {
        $start = [DateTimeOffset]::Parse($started)
        $duration = ([DateTimeOffset]::UtcNow - $start.ToUniversalTime()).TotalSeconds
    }
    $message = $Exception.Message
    $State.stages[$StageName].status = "failed"
    $State.stages[$StageName].completedAt = $now
    $State.stages[$StageName].durationSec = [Math]::Round($duration, 3)
    $State.stages[$StageName].error = $message
    $State.status = "failed"
    $State.failures += [ordered]@{
        stage = $StageName
        at = $now
        message = $message
    }
    Save-WorkflowState -State $State
}

function Get-ContextValue {
    param(
        [System.Collections.IDictionary]$State,
        [string]$StageName,
        [string]$Key
    )
    if ($null -eq $State.data[$StageName]) {
        return $null
    }
    if (-not $State.data[$StageName].ContainsKey($Key)) {
        return $null
    }
    return $State.data[$StageName][$Key]
}

. (Join-Path $PSScriptRoot "lib\Invoke-Bootstrap.ps1")
. (Join-Path $PSScriptRoot "lib\Invoke-Scaffold.ps1")
. (Join-Path $PSScriptRoot "lib\Invoke-BuildDeployRun.ps1")
. (Join-Path $PSScriptRoot "lib\Invoke-VerifyReport.ps1")

function Invoke-StageCore {
    param(
        [string]$StageName,
        [System.Collections.IDictionary]$Context
    )
    switch ($StageName) {
        "bootstrap" { return Invoke-WorkflowBootstrap -Context $Context }
        "scaffold" { return Invoke-WorkflowScaffold -Context $Context }
        "build" { return Invoke-WorkflowBuild -Context $Context }
        "deploy" { return Invoke-WorkflowDeploy -Context $Context }
        "run" { return Invoke-WorkflowRun -Context $Context }
        "verify" { return Invoke-WorkflowVerify -Context $Context }
        "report" { return Invoke-WorkflowReport -Context $Context }
        default { throw "Unsupported stage: $StageName" }
    }
}

$profilePath = "<inline>"
$profileDir = $script:RepoRoot
$profileConfig = [ordered]@{}
if (-not [string]::IsNullOrWhiteSpace($Profile)) {
    $profilePath = Resolve-Path -Path $Profile | Select-Object -ExpandProperty Path
    $profileDir = Split-Path -Parent $profilePath
    $profileConfig = Import-WorkflowProfile -ProfilePath $profilePath
    if ($profileConfig -isnot [System.Collections.IDictionary]) {
        $profileConfig = ConvertTo-Hashtable -InputObject $profileConfig
    }
}

$argOverlay = Build-ConfigOverlayFromArgs -GameDirValue $GameDir -GameExeValue $GameExe -RequirementValue $Requirement -PluginNameValue $PluginName -PluginIdValue $PluginId -PluginVersionValue $PluginVersion
$defaults = Get-ConfigDefaults
$merged = Merge-Hashtable -Base $defaults -Overlay $profileConfig
if ($argOverlay.Count -gt 0) {
    $merged = Merge-Hashtable -Base $merged -Overlay $argOverlay
}

if ($null -eq $merged.game -or [string]::IsNullOrWhiteSpace([string]$merged.game.dir)) {
    throw "Missing game path. Provide -Profile with game.dir or pass -GameDir directly."
}

$config = Normalize-WorkflowConfig -RawConfig $merged -ProfileDir $profileDir

$state = $null
if ($Resume -and (Test-Path -LiteralPath $script:StatePath)) {
    $state = Load-JsonFile -Path $script:StatePath
    if ($null -eq $state) {
        throw "Resume requested but state file is empty: $script:StatePath"
    }
    Write-WorkflowInfo "Resuming from state file: $script:StatePath"
} else {
    $state = New-InitialState -ProfilePath $profilePath -Config $config
    Save-WorkflowState -State $state
    Write-WorkflowInfo "Initialized new workflow state: $script:StatePath"
}

$context = [ordered]@{
    RepoRoot = $script:RepoRoot
    WorkflowRoot = $script:WorkflowRoot
    LogDir = $script:LogDir
    StatePath = $script:StatePath
    ReportPath = $script:ReportPath
    ReportMarkdownPath = $script:ReportMarkdownPath
    ProfilePath = $profilePath
    ProfileDir = $profileDir
    Config = $config
    State = $state
}

$stagesToRun = Get-RequestedStageList -RequestedStage $Stage -State $state -ResumeMode:$Resume
if (($stagesToRun | Measure-Object).Count -eq 0) {
    Write-WorkflowInfo "No stages to run."
    exit 0
}

foreach ($stageName in $stagesToRun) {
    Write-WorkflowInfo "Running stage: $stageName"
    Start-Stage -State $state -StageName $stageName
    try {
        $stageData = Invoke-StageCore -StageName $stageName -Context $context
        if ($stageData -isnot [System.Collections.IDictionary]) {
            $stageData = @{}
        }
        Complete-Stage -State $state -StageName $stageName -StageData $stageData
        Write-WorkflowInfo "Stage succeeded: $stageName"
    }
    catch {
        $ex = $_.Exception
        Fail-Stage -State $state -StageName $stageName -Exception $ex
        Write-WorkflowInfo "Stage failed: $stageName -> $($ex.Message)"
        if ($stageName -ne "report") {
            try {
                Start-Stage -State $state -StageName "report"
                $reportData = Invoke-WorkflowReport -Context $context -FailureStage $stageName -FailureMessage $ex.Message
                Complete-Stage -State $state -StageName "report" -StageData $reportData
            }
            catch {
                $reportEx = $_.Exception
                Fail-Stage -State $state -StageName "report" -Exception $reportEx
                Write-WorkflowInfo "Report stage failed: $($reportEx.Message)"
            }
        }
        throw
    }
}

$state.status = "success"
Save-WorkflowState -State $state
Write-WorkflowInfo "Workflow finished."
