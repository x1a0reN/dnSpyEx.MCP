param(
    [string]$BaseUrl = "",
    [string]$ApiKey = "",
    [string]$Model = "",
    [string]$SystemPrompt = "You are a helpful assistant.",
    [double]$Temperature = 0.2,
    [int]$MaxTokens = 0,
    [int]$HistoryLimit = 20,
    [string]$Prompt = "",
    [switch]$Once
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-Option {
    param(
        [string]$Primary,
        [string[]]$EnvKeys,
        [string]$Fallback
    )
    if (-not [string]::IsNullOrWhiteSpace($Primary)) {
        return $Primary
    }
    foreach ($key in $EnvKeys) {
        $value = [Environment]::GetEnvironmentVariable($key)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }
    }
    return $Fallback
}

function Normalize-BaseUrl {
    param([string]$Url)
    $v = $Url.Trim()
    if ([string]::IsNullOrWhiteSpace($v)) {
        throw "Base URL is required. Set -BaseUrl or environment OPENAI_BASE_URL."
    }
    if ($v.EndsWith("/")) {
        $v = $v.Substring(0, $v.Length - 1)
    }
    if ($v.EndsWith("/v1", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $v
    }
    return "$v/v1"
}

function Convert-ContentToText {
    param($Content)
    if ($null -eq $Content) {
        return ""
    }
    if ($Content -is [string]) {
        return $Content
    }
    if ($Content -is [System.Collections.IEnumerable]) {
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($item in $Content) {
            if ($item -is [string]) {
                [void]$parts.Add($item)
                continue
            }
            if ($item.PSObject.Properties["text"]) {
                [void]$parts.Add([string]$item.text)
                continue
            }
            [void]$parts.Add([string]$item)
        }
        return ($parts -join "")
    }
    return [string]$Content
}

function Invoke-ChatCompletion {
    param(
        [string]$ApiEndpoint,
        [string]$ApiKeyValue,
        [string]$ModelName,
        [double]$Temp,
        [int]$MaxTok,
        [System.Collections.IList]$Messages
    )
    $headers = @{
        "Authorization" = "Bearer $ApiKeyValue"
        "Content-Type" = "application/json"
    }

    $body = [ordered]@{
        model = $ModelName
        messages = $Messages
        temperature = $Temp
        stream = $false
    }
    if ($MaxTok -gt 0) {
        $body.max_tokens = $MaxTok
    }

    $json = $body | ConvertTo-Json -Depth 64
    try {
        $response = Invoke-RestMethod -Uri $ApiEndpoint -Method Post -Headers $headers -Body $json
    }
    catch {
        $message = $_.Exception.Message
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
            $message = "$message`n$($_.ErrorDetails.Message)"
        }
        throw "Request failed: $message"
    }

    if ($null -eq $response -or $null -eq $response.choices -or $response.choices.Count -eq 0) {
        throw "Empty response: missing choices."
    }

    $answer = Convert-ContentToText -Content $response.choices[0].message.content
    return @{
        text = $answer
        raw = $response
    }
}

function Trim-History {
    param(
        [System.Collections.IList]$Messages,
        [int]$Limit
    )
    if ($Limit -le 0) {
        return
    }
    $maxCount = ($Limit * 2) + 1
    while ($Messages.Count -gt $maxCount) {
        $Messages.RemoveAt(1)
    }
}

function Show-Help {
    Write-Host "Commands:"
    Write-Host "  /help              show help"
    Write-Host "  /exit              quit"
    Write-Host "  /clear             clear message history (keep system prompt)"
    Write-Host "  /save <path>       save conversation to text file"
    Write-Host "  /system <prompt>   replace system prompt and reset history"
}

$resolvedBaseUrl = Resolve-Option -Primary $BaseUrl -EnvKeys @("OPENAI_BASE_URL", "AI_BASE_URL") -Fallback ""
$resolvedApiKey = Resolve-Option -Primary $ApiKey -EnvKeys @("OPENAI_API_KEY", "AI_API_KEY") -Fallback ""
$resolvedModel = Resolve-Option -Primary $Model -EnvKeys @("OPENAI_MODEL", "AI_MODEL") -Fallback "gpt-4o-mini"

if ([string]::IsNullOrWhiteSpace($resolvedApiKey)) {
    throw "API key is required. Set -ApiKey or environment OPENAI_API_KEY."
}

$normalizedBaseUrl = Normalize-BaseUrl -Url $resolvedBaseUrl
$chatEndpoint = "$normalizedBaseUrl/chat/completions"

$messages = New-Object System.Collections.ArrayList
[void]$messages.Add([ordered]@{
        role = "system"
        content = $SystemPrompt
    })

Write-Host "AI Chat CLI"
Write-Host "Endpoint: $chatEndpoint"
Write-Host "Model: $resolvedModel"
Write-Host "Type /help for commands."

if ($Once -and [string]::IsNullOrWhiteSpace($Prompt)) {
    throw "When -Once is used, -Prompt must not be empty."
}

while ($true) {
    $userInput = $Prompt
    if (-not $Once) {
        $userInput = Read-Host "You"
    }
    if ([string]::IsNullOrWhiteSpace($userInput)) {
        if ($Once) {
            break
        }
        continue
    }

    if ($userInput.StartsWith("/")) {
        if ($userInput -eq "/exit") {
            break
        }
        if ($userInput -eq "/help") {
            Show-Help
            if ($Once) {
                break
            }
            continue
        }
        if ($userInput -eq "/clear") {
            while ($messages.Count -gt 1) {
                $messages.RemoveAt(1)
            }
            Write-Host "History cleared."
            if ($Once) {
                break
            }
            continue
        }
        if ($userInput.StartsWith("/save ")) {
            $path = $userInput.Substring(6).Trim()
            if ([string]::IsNullOrWhiteSpace($path)) {
                Write-Host "Usage: /save <path>"
                if ($Once) {
                    break
                }
                continue
            }
            $lines = New-Object System.Collections.Generic.List[string]
            foreach ($m in $messages) {
                [void]$lines.Add("[$($m.role)]")
                [void]$lines.Add([string]$m.content)
                [void]$lines.Add("")
            }
            Set-Content -Path $path -Value $lines -Encoding UTF8
            Write-Host "Saved: $path"
            if ($Once) {
                break
            }
            continue
        }
        if ($userInput.StartsWith("/system ")) {
            $newPrompt = $userInput.Substring(8).Trim()
            if ([string]::IsNullOrWhiteSpace($newPrompt)) {
                Write-Host "Usage: /system <prompt>"
                if ($Once) {
                    break
                }
                continue
            }
            $messages.Clear()
            [void]$messages.Add([ordered]@{
                    role = "system"
                    content = $newPrompt
                })
            Write-Host "System prompt updated. History reset."
            if ($Once) {
                break
            }
            continue
        }

        Write-Host "Unknown command. Type /help"
        if ($Once) {
            break
        }
        continue
    }

    [void]$messages.Add([ordered]@{
            role = "user"
            content = $userInput
        })

    try {
        $result = Invoke-ChatCompletion -ApiEndpoint $chatEndpoint -ApiKeyValue $resolvedApiKey -ModelName $resolvedModel -Temp $Temperature -MaxTok $MaxTokens -Messages $messages
        $answer = [string]$result.text
        Write-Host ""
        Write-Host "AI:"
        Write-Host $answer
        Write-Host ""
        [void]$messages.Add([ordered]@{
                role = "assistant"
                content = $answer
            })
        Trim-History -Messages $messages -Limit $HistoryLimit
    }
    catch {
        Write-Host ""
        Write-Host $_.Exception.Message
        Write-Host ""
    }

    if ($Once) {
        break
    }
}
