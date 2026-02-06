param(
    [string]$OutputRoot = ".artifacts",
    [string]$Version = "",
    [switch]$NoZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-Date -Format "yyyyMMdd-HHmmss"
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
Set-Location $repoRoot

$outputRootPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
New-Item -ItemType Directory -Path $outputRootPath -Force | Out-Null

$packageName = "dnspy-agent-workflow-skillpack-$Version"
$stagingRoot = Join-Path $outputRootPath $packageName
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

function Copy-PackageItem {
    param(
        [string]$SourceRelative,
        [string]$TargetRelative
    )
    $source = Join-Path $repoRoot $SourceRelative
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Missing required source for packaging: $SourceRelative"
    }
    $target = Join-Path $stagingRoot $TargetRelative
    $targetParent = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $targetParent)) {
        New-Item -ItemType Directory -Path $targetParent -Force | Out-Null
    }
    Copy-Item -LiteralPath $source -Destination $target -Recurse -Force
}

Copy-PackageItem -SourceRelative "skills\dnspy-agent-loop" -TargetRelative "skill\dnspy-agent-loop"
Copy-PackageItem -SourceRelative "scripts\workflow" -TargetRelative "workflow\scripts\workflow"
Copy-PackageItem -SourceRelative "templates\BepInExPlugin" -TargetRelative "workflow\templates\BepInExPlugin"
Copy-PackageItem -SourceRelative "profiles\demo.unity-mono.yaml" -TargetRelative "workflow\profiles\demo.unity-mono.yaml"
Copy-PackageItem -SourceRelative "docs\AGENTIC-SKILL-PACK-USAGE.zh-CN.md" -TargetRelative "docs\AGENTIC-SKILL-PACK-USAGE.zh-CN.md"
Copy-PackageItem -SourceRelative "docs\AGENTIC-INTENT-CONTRACT.zh-CN.md" -TargetRelative "docs\AGENTIC-INTENT-CONTRACT.zh-CN.md"

$manifest = [ordered]@{
    name = $packageName
    createdAt = (Get-Date).ToUniversalTime().ToString("o")
    sourceRepo = $repoRoot
    contents = @(
        "skill/dnspy-agent-loop",
        "workflow/scripts/workflow",
        "workflow/templates/BepInExPlugin",
        "workflow/profiles/demo.unity-mono.yaml",
        "docs/AGENTIC-SKILL-PACK-USAGE.zh-CN.md",
        "docs/AGENTIC-INTENT-CONTRACT.zh-CN.md"
    )
}

$manifestPath = Join-Path $stagingRoot "package-manifest.json"
$manifest | ConvertTo-Json -Depth 32 | Set-Content -Path $manifestPath -Encoding UTF8

$zipPath = Join-Path $outputRootPath ($packageName + ".zip")
if (-not $NoZip) {
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $stagingRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal
}

Write-Host "Skill pack staged at: $stagingRoot"
if (-not $NoZip) {
    Write-Host "Skill pack zip: $zipPath"
}
