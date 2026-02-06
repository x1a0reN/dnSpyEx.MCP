function Read-TextFromOffset {
    param(
        [string]$Path,
        [int64]$Offset
    )
    if (-not (Test-Path -LiteralPath $Path)) {
        return @{
            text = ""
            newOffset = $Offset
        }
    }

    $fileLength = (Get-Item -LiteralPath $Path).Length
    if ($Offset -gt $fileLength) {
        $Offset = 0
    }
    if ($Offset -eq $fileLength) {
        return @{
            text = ""
            newOffset = $fileLength
        }
    }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        [void]$stream.Seek($Offset, [System.IO.SeekOrigin]::Begin)
        $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8, $true, 4096, $true)
        try {
            $text = $reader.ReadToEnd()
            return @{
                text = $text
                newOffset = $stream.Position
            }
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-StageFailureCategory {
    param([string]$StageName)
    switch ($StageName) {
        "bootstrap" { return "environment" }
        "scaffold" { return "project-setup" }
        "agent-handoff" { return "agent" }
        "build" { return "build" }
        "deploy" { return "deploy" }
        "run" { return "runtime-start" }
        "verify" { return "runtime-verify" }
        default { return "general" }
    }
}

function Invoke-WorkflowVerify {
    param([hashtable]$Context)

    $cfg = $Context.Config
    $state = $Context.State
    $logFile = [string]$cfg.verify.logFile
    $timeoutSec = [int]$cfg.verify.timeoutSec
    if ($timeoutSec -le 0) {
        $timeoutSec = 120
    }

    $patterns = @()
    foreach ($item in $cfg.verify.successPatterns) {
        if (-not [string]::IsNullOrWhiteSpace([string]$item)) {
            $patterns += [string]$item
        }
    }
    if (($patterns | Measure-Object).Count -eq 0) {
        $patterns = @([string]$cfg.project.id, "Plugin loaded")
    }

    $offset = 0
    $offsetValue = Get-ContextValue -State $state -StageName "run" -Key "verifyStartOffset"
    if ($null -ne $offsetValue) {
        $offset = [int64]$offsetValue
    }
    $buffer = ""
    $matched = $null
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    while ($stopwatch.Elapsed.TotalSeconds -lt $timeoutSec) {
        $chunk = Read-TextFromOffset -Path $logFile -Offset $offset
        $offset = [int64]$chunk.newOffset
        if (-not [string]::IsNullOrEmpty([string]$chunk.text)) {
            $buffer += [string]$chunk.text
            foreach ($pattern in $patterns) {
                if ($buffer.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    $matched = $pattern
                    break
                }
            }
            if ($matched) {
                break
            }
        }
        Start-Sleep -Seconds 1
    }
    $stopwatch.Stop()

    if (-not $matched) {
        $tail = ""
        if (Test-Path -LiteralPath $logFile) {
            $tail = (Get-Content -LiteralPath $logFile -Tail 80 -ErrorAction SilentlyContinue) -join "`n"
        }
        throw "Verify timeout after $timeoutSec seconds. logFile=$logFile tail=$tail"
    }

    return @{
        logFile = $logFile
        matchedPattern = $matched
        elapsedSec = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
        scannedOffset = $offset
    }
}

function Invoke-WorkflowReport {
    param(
        [hashtable]$Context,
        [string]$FailureStage = $null,
        [string]$FailureMessage = $null
    )
    $state = $Context.State
    $summary = [ordered]@{
        workflowName = $state.workflowName
        profilePath = $state.profilePath
        status = if ($FailureStage) { "failed" } else { $state.status }
        generatedAt = Get-UtcNowIso
        totalDurationSec = 0
        failedStage = $FailureStage
        failureCategory = if ($FailureStage) { Get-StageFailureCategory -StageName $FailureStage } else { $null }
        failureMessage = $FailureMessage
        recommendedRetry = if ($FailureStage) { ".\scripts\workflow\run.ps1 -Profile `"$($Context.ProfilePath)`" -Stage full -Resume" } else { $null }
        stages = @()
    }

    foreach ($stageName in $script:StageSequence) {
        $entry = $state.stages[$stageName]
        $displayStatus = [string]$entry.status
        if ($stageName -eq "report" -and $displayStatus -eq "running") {
            $displayStatus = "success"
        }
        $summary.totalDurationSec += [double]$entry.durationSec
        $summary.stages += [ordered]@{
            name = $stageName
            status = $displayStatus
            startedAt = $entry.startedAt
            completedAt = $entry.completedAt
            durationSec = $entry.durationSec
            error = $entry.error
        }
    }
    $summary.totalDurationSec = [Math]::Round([double]$summary.totalDurationSec, 3)

    Save-JsonFile -Path $Context.ReportPath -Value $summary

    $lines = @()
    $lines += "# Workflow Report"
    $lines += ""
    $lines += "- workflow: $($summary.workflowName)"
    $lines += "- status: $($summary.status)"
    $lines += "- generatedAt: $($summary.generatedAt)"
    $lines += "- totalDurationSec: $($summary.totalDurationSec)"
    if ($FailureStage) {
        $lines += "- failedStage: $FailureStage"
        $lines += "- failureCategory: $($summary.failureCategory)"
        $lines += "- recommendedRetry: $($summary.recommendedRetry)"
    }
    $lines += ""
    $lines += "## Stages"
    foreach ($s in $summary.stages) {
        $line = "- $($s.name): $($s.status), durationSec=$($s.durationSec)"
        if (-not [string]::IsNullOrWhiteSpace([string]$s.error)) {
            $line += ", error=$($s.error)"
        }
        $lines += $line
    }
    Set-Content -LiteralPath $Context.ReportMarkdownPath -Value ($lines -join "`r`n") -Encoding UTF8

    return @{
        reportJson = $Context.ReportPath
        reportMarkdown = $Context.ReportMarkdownPath
        status = $summary.status
    }
}
