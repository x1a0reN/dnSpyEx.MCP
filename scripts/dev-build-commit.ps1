param(
    [string]$Message,
    [switch]$NoPush
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptDir
Set-Location $root

if ([string]::IsNullOrWhiteSpace($Message)) {
    $stamp = Get-Date -Format "yyyy-MM-dd HHmm"
    $Message = "Auto build: $stamp"
}

dotnet build Extensions\dnSpyEx.MCP\dnSpyEx.MCP.csproj -c Release -f net10.0-windows
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$changes = git status --porcelain
if ([string]::IsNullOrWhiteSpace($changes)) {
    Write-Host "No changes to commit."
    exit 0
}

git add -A
git commit -m $Message
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not $NoPush) {
    git push
}
