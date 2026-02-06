function Get-WorkflowGameExePath {
    param([hashtable]$Context)
    $cfg = $Context.Config
    $gameDir = [string]$cfg.game.dir
    if (-not (Test-Path -LiteralPath $gameDir)) {
        throw "Game directory not found: $gameDir"
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$cfg.game.exe)) {
        $explicitPath = Join-Path $gameDir $cfg.game.exe
        if (-not (Test-Path -LiteralPath $explicitPath)) {
            throw "game.exe not found in game.dir: $explicitPath"
        }
        return (Resolve-Path -LiteralPath $explicitPath).Path
    }

    $exeCandidates = Get-ChildItem -LiteralPath $gameDir -Filter *.exe -File | Where-Object { $_.Name -ne "UnityCrashHandler64.exe" }
    if (($exeCandidates | Measure-Object).Count -eq 0) {
        throw "Cannot detect game exe under: $gameDir"
    }
    $best = $exeCandidates | Sort-Object Length -Descending | Select-Object -First 1
    return $best.FullName
}

function Get-WorkflowGameArch {
    param(
        [hashtable]$Context,
        [string]$ExePath
    )
    $configured = [string]$Context.Config.game.arch
    if ($configured -eq "x64" -or $configured -eq "x86") {
        return $configured
    }

    $dataDirs = Get-ChildItem -LiteralPath $Context.Config.game.dir -Directory -Filter *_Data -ErrorAction SilentlyContinue
    foreach ($dir in $dataDirs) {
        if (Test-Path -LiteralPath (Join-Path $dir.FullName "Plugins\x86_64")) {
            return "x64"
        }
        if (Test-Path -LiteralPath (Join-Path $dir.FullName "Plugins\x86")) {
            return "x86"
        }
    }

    if ($ExePath -match "64") {
        return "x64"
    }
    return "x64"
}

function Get-WorkflowManagedDir {
    param([hashtable]$Context)
    $cfg = $Context.Config
    if (-not [string]::IsNullOrWhiteSpace([string]$cfg.game.unityManagedDir)) {
        $managedPath = Resolve-PathByProfile -PathValue ([string]$cfg.game.unityManagedDir) -ProfileDir $Context.ProfileDir
        if (-not (Test-Path -LiteralPath $managedPath)) {
            throw "Configured game.unityManagedDir does not exist: $managedPath"
        }
        return $managedPath
    }

    $dataDirs = Get-ChildItem -LiteralPath $cfg.game.dir -Directory -Filter *_Data -ErrorAction SilentlyContinue
    foreach ($dataDir in $dataDirs) {
        $candidate = Join-Path $dataDir.FullName "Managed"
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }
    throw "Cannot locate Unity Managed directory under game.dir. Configure game.unityManagedDir explicitly."
}

function Get-BepInExReleaseAsset {
    param(
        [string]$Repo,
        [int]$Major,
        [string]$Arch,
        [string]$Channel,
        [string]$AssetPattern
    )
    $url = "https://api.github.com/repos/$Repo/releases?per_page=30"
    $headers = @{
        "User-Agent" = "dnspyex-mcp-workflow"
        "Accept" = "application/vnd.github+json"
    }
    $releases = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
    if ($null -eq $releases) {
        throw "Failed to load releases from GitHub: $Repo"
    }

    $stableOnly = ($Channel -eq "stable")
    $archToken = if ($Arch -eq "x86") { "win_x86" } else { "win_x64" }
    $versionPattern = "^v?$Major\."
    $assetRegex = if ([string]::IsNullOrWhiteSpace($AssetPattern)) {
        "BepInEx.*$archToken.*\.zip$"
    } else {
        $AssetPattern
    }

    foreach ($release in $releases) {
        if ($stableOnly -and $release.prerelease) {
            continue
        }
        if ($release.tag_name -notmatch $versionPattern) {
            continue
        }
        foreach ($asset in $release.assets) {
            if ($asset.name -notmatch $assetRegex) {
                continue
            }
            return @{
                tag = [string]$release.tag_name
                name = [string]$asset.name
                url = [string]$asset.browser_download_url
                size = [int64]$asset.size
                releaseName = [string]$release.name
            }
        }
    }

    throw "Cannot find BepInEx asset. repo=$Repo major=$Major arch=$Arch channel=$Channel pattern=$assetRegex"
}

function Invoke-WorkflowBootstrap {
    param([hashtable]$Context)

    $cfg = $Context.Config
    $gameDir = [string]$cfg.game.dir
    $exePath = Get-WorkflowGameExePath -Context $Context
    $arch = Get-WorkflowGameArch -Context $Context -ExePath $exePath
    $managedDir = Get-WorkflowManagedDir -Context $Context
    $bepInExCoreDir = Join-Path $gameDir "BepInEx\core"
    $bepInExDllPath = Join-Path $bepInExCoreDir "BepInEx.dll"

    $bootstrapDir = Join-Path $Context.WorkflowRoot "bootstrap"
    $cacheDir = Join-Path $Context.WorkflowRoot "cache"
    New-Item -ItemType Directory -Path $bootstrapDir -Force | Out-Null
    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null

    $installManifestPath = Join-Path $bootstrapDir "install-manifest.json"
    $installInfo = [ordered]@{
        gameDir = $gameDir
        exePath = $exePath
        arch = $arch
        unityManagedDir = $managedDir
        bepinexAlreadyInstalled = (Test-Path -LiteralPath $bepInExDllPath)
        installedFromDownload = $false
        asset = $null
        installedAt = Get-UtcNowIso
    }

    if (-not (Test-Path -LiteralPath $bepInExDllPath)) {
        $asset = Get-BepInExReleaseAsset -Repo ([string]$cfg.bepinex.source.repo) -Major ([int]$cfg.bepinex.major) -Arch $arch -Channel ([string]$cfg.bepinex.channel) -AssetPattern ([string]$cfg.bepinex.assetPattern)
        $zipPath = Join-Path $cacheDir $asset.name
        Invoke-WebRequest -Uri $asset.url -OutFile $zipPath -UseBasicParsing
        if (-not (Test-Path -LiteralPath $zipPath)) {
            throw "BepInEx package download failed: $zipPath"
        }
        $actualSize = (Get-Item -LiteralPath $zipPath).Length
        if ($actualSize -le 0) {
            throw "Downloaded BepInEx package is empty: $zipPath"
        }
        Expand-Archive -LiteralPath $zipPath -DestinationPath $gameDir -Force
        if (-not (Test-Path -LiteralPath $bepInExDllPath)) {
            throw "BepInEx installation failed. Missing file: $bepInExDllPath"
        }
        $installInfo.bepinexAlreadyInstalled = $false
        $installInfo.installedFromDownload = $true
        $installInfo.asset = $asset
        $installInfo.downloadPath = $zipPath
        $installInfo.downloadSize = $actualSize
    }

    Save-JsonFile -Path $installManifestPath -Value $installInfo
    return @{
        gameDir = $gameDir
        gameExePath = $exePath
        gameArch = $arch
        unityManagedDir = $managedDir
        bepinexCoreDir = $bepInExCoreDir
        bepinexInstalled = $true
        installManifestPath = $installManifestPath
    }
}
