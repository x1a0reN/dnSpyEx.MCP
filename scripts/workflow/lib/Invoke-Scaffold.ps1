function Convert-ToIdentifier {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "Plugin"
    }
    $normalized = ($Value -replace "[^A-Za-z0-9_]", "_")
    if ($normalized -match "^[0-9]") {
        return "_$normalized"
    }
    return $normalized
}

function Convert-ToCSharpStringLiteral {
    param([string]$Value)
    if ($null -eq $Value) {
        return ""
    }
    $escaped = $Value.Replace("\", "\\").Replace('"', '\"')
    $escaped = $escaped.Replace("`r", "\r").Replace("`n", "\n")
    return $escaped
}

function Get-RelativePathSafe {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )
    $baseUri = [Uri](([System.IO.Path]::GetFullPath($BasePath)).TrimEnd("\") + "\")
    $targetUri = [Uri]([System.IO.Path]::GetFullPath($TargetPath))
    $relative = $baseUri.MakeRelativeUri($targetUri).ToString().Replace("/", "\")
    return [Uri]::UnescapeDataString($relative)
}

function ConvertTo-ReferenceXml {
    param(
        [string]$ProjectDir,
        [System.Collections.IEnumerable]$DllFiles
    )
    $byName = [ordered]@{}
    foreach ($dll in $DllFiles) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($dll.FullName)
        if (-not $byName.Contains($name)) {
            $byName[$name] = $dll.FullName
        }
    }
    $builder = New-Object System.Text.StringBuilder
    foreach ($name in $byName.Keys) {
        $fullPath = [string]$byName[$name]
        $relative = Get-RelativePathSafe -BasePath $ProjectDir -TargetPath $fullPath
        [void]$builder.AppendLine("    <Reference Include=`"$name`">")
        [void]$builder.AppendLine("      <HintPath>$relative</HintPath>")
        [void]$builder.AppendLine("      <Private>false</Private>")
        [void]$builder.AppendLine("    </Reference>")
    }
    return $builder.ToString().TrimEnd()
}

function Invoke-WorkflowScaffold {
    param([System.Collections.IDictionary]$Context)

    $cfg = $Context.Config
    $state = $Context.State
    $gameDir = [string]$cfg.game.dir
    $managedDir = [string](Get-ContextValue -State $state -StageName "bootstrap" -Key "unityManagedDir")
    if ([string]::IsNullOrWhiteSpace($managedDir)) {
        $managedDir = Get-WorkflowManagedDir -Context $Context
    }

    $projectName = [string]$cfg.project.name
    $projectRoot = Join-Path $Context.WorkflowRoot ("workspace\" + $projectName)
    $templateRoot = Join-Path $Context.RepoRoot "templates\BepInExPlugin"
    $templateCsproj = Join-Path $templateRoot "BepInExPlugin.csproj"
    $templatePlugin = Join-Path $templateRoot "Plugin.cs"

    if (-not (Test-Path -LiteralPath $templateCsproj) -or -not (Test-Path -LiteralPath $templatePlugin)) {
        throw "Template files are missing in templates\BepInExPlugin."
    }

    New-Item -ItemType Directory -Path $projectRoot -Force | Out-Null

    $csprojPath = Join-Path $projectRoot ($projectName + ".csproj")
    if (-not (Test-Path -LiteralPath $csprojPath)) {
        Copy-Item -LiteralPath $templateCsproj -Destination $csprojPath -Force
    }

    $pluginSourcePath = Join-Path $projectRoot "Plugin.cs"
    if (-not (Test-Path -LiteralPath $pluginSourcePath)) {
        Copy-Item -LiteralPath $templatePlugin -Destination $pluginSourcePath -Force
    }

    $dllCandidates = @()
    $bepCoreDir = Join-Path $gameDir "BepInEx\core"
    if (Test-Path -LiteralPath $bepCoreDir) {
        $dllCandidates += Get-ChildItem -LiteralPath $bepCoreDir -File -Filter *.dll
    }
    if (Test-Path -LiteralPath $managedDir) {
        $dllCandidates += Get-ChildItem -LiteralPath $managedDir -File -Filter *.dll
    }
    if (($dllCandidates | Measure-Object).Count -eq 0) {
        throw "No DLL references found in BepInEx/core or Managed directory."
    }

    $referenceXml = ConvertTo-ReferenceXml -ProjectDir $projectRoot -DllFiles $dllCandidates
    $pluginNamespace = Convert-ToIdentifier -Value ($projectName -replace "\.", "_")
    $pluginClass = "Plugin"

    $csprojContent = Get-Content -LiteralPath $csprojPath -Raw -Encoding UTF8
    $csprojContent = $csprojContent.Replace("__TARGET_FRAMEWORK__", [string]$cfg.project.framework)
    $csprojContent = $csprojContent.Replace("__REFERENCE_ITEMS__", $referenceXml)
    Set-Content -LiteralPath $csprojPath -Value $csprojContent -Encoding UTF8

    $pluginContent = Get-Content -LiteralPath $pluginSourcePath -Raw -Encoding UTF8
    $requirementText = Convert-ToCSharpStringLiteral -Value ([string]$cfg.workflow.requirement)
    $pluginContent = $pluginContent.Replace("__PLUGIN_NAMESPACE__", $pluginNamespace)
    $pluginContent = $pluginContent.Replace("__PLUGIN_CLASS__", $pluginClass)
    $pluginContent = $pluginContent.Replace("__PLUGIN_ID__", [string]$cfg.project.id)
    $pluginContent = $pluginContent.Replace("__PLUGIN_NAME__", $projectName)
    $pluginContent = $pluginContent.Replace("__PLUGIN_VERSION__", [string]$cfg.project.version)
    $pluginContent = $pluginContent.Replace("__WORKFLOW_REQUIREMENT__", $requirementText)
    Set-Content -LiteralPath $pluginSourcePath -Value $pluginContent -Encoding UTF8

    $referencesLock = Join-Path $projectRoot "references.lock.json"
    $lockData = [ordered]@{
        generatedAt = Get-UtcNowIso
        projectName = $projectName
        gameDir = $gameDir
        unityManagedDir = $managedDir
        bepinexCoreDir = $bepCoreDir
        references = @()
    }
    foreach ($dll in $dllCandidates) {
        $source = if ($dll.FullName.StartsWith($bepCoreDir, [System.StringComparison]::OrdinalIgnoreCase)) { "bepinex-core" } else { "unity-managed" }
        $lockData.references += [ordered]@{
            name = [System.IO.Path]::GetFileNameWithoutExtension($dll.Name)
            file = $dll.FullName
            source = $source
        }
    }
    Save-JsonFile -Path $referencesLock -Value $lockData

    return @{
        projectDir = $projectRoot
        csprojPath = $csprojPath
        pluginSourcePath = $pluginSourcePath
        referencesLockPath = $referencesLock
        totalReferenceCount = $lockData.references.Count
    }
}
