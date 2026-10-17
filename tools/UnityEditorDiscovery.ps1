function Get-TingLuProjectUnityVersion([string]$ProjectRoot) {
    $versionFile = Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $versionFile)) { throw "Unity version file not found: $versionFile" }
    $match = [regex]::Match((Get-Content -LiteralPath $versionFile -Raw), '(?m)^m_EditorVersion:\s*(\S+)')
    if (-not $match.Success) { throw "Unity editor version is missing from $versionFile" }
    return $match.Groups[1].Value
}

function Test-TingLuUnityEditorPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $item = Get-Item -LiteralPath $Path
    if ($item.Name -ine 'Unity.exe') { return $false }
    # Unity Hub also ships a Unity.exe launcher. An editor binary lives under an Editor directory.
    return [IO.DirectoryInfo]::new($item.DirectoryName).Name -ieq 'Editor'
}

function Get-TingLuUnityEditorProductVersion([string]$Path) {
    return (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion
}

function Test-TingLuUnityEditorVersionPath([string]$Path, [string]$Version) {
    if (-not (Test-TingLuUnityEditorPath $Path)) { return $false }
    # Read the executable's version first: installations may use arbitrary directory names.
    $productVersion = Get-TingLuUnityEditorProductVersion $Path
    if (-not [string]::IsNullOrWhiteSpace($productVersion)) {
        $match = [regex]::Match($productVersion, '^(\d+\.\d+\.\d+[abfp]\d+(?:c\d+)?)(?:_|\s|$)')
        if (-not $match.Success) { return $false }
        $actual = $match.Groups[1].Value
        return $actual -eq $Version -or ($actual -replace 'c\d+$', '') -eq ($Version -replace 'c\d+$', '')
    }
    return $false
}

function Add-TingLuHubEditorPaths($Value, [System.Collections.Generic.List[string]]$Paths) {
    if ($null -eq $Value) { return }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            $child = $Value[$key]
            if ($child -is [string] -and ($key -match '(?i)^(location|path|editorpath|installdir)$' -or $child -match '(?i)[\\/]Editor[\\/]Unity\.exe$')) { $Paths.Add($child) }
            Add-TingLuHubEditorPaths $child $Paths
        }
        return
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        foreach ($child in $Value) { Add-TingLuHubEditorPaths $child $Paths }
        return
    }
    if ($Value -is [pscustomobject]) {
        foreach ($property in $Value.PSObject.Properties) {
            $child = $property.Value
            if ($child -is [string] -and ($property.Name -match '(?i)^(location|path|editorpath|installdir)$' -or $child -match '(?i)[\\/]Editor[\\/]Unity\.exe$')) { $Paths.Add($child) }
            Add-TingLuHubEditorPaths $child $Paths
        }
    }
}

function Get-TingLuHubEditorPaths([string[]]$HubFiles) {
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $HubFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
        try {
            $content = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
            Add-TingLuHubEditorPaths $content $paths
        } catch { Write-Verbose "Could not read Unity Hub editor list '$file': $($_.Exception.Message)" }
    }
    return $paths.ToArray()
}

function Find-TingLuUnityEditor([string]$ProjectRoot, [string[]]$RunningEditorPaths = @(), [string[]]$HubFiles = @()) {
    $version = Get-TingLuProjectUnityVersion $ProjectRoot
    $versionNames = @($version, ($version -replace 'c\d+$', '')) | Select-Object -Unique
    $candidates = [System.Collections.Generic.List[string]]::new()
    foreach ($path in $RunningEditorPaths) { if ($path) { $candidates.Add($path) } }
    foreach ($path in @($env:UNITY_EDITOR_PATH, $env:UNITY_EDITOR)) { if ($path) { $candidates.Add($path) } }
    # Standalone editors need not be registered with Hub or installed on a particular drive.
    $uninstallKeys = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')
    foreach ($entry in @(Get-ItemProperty -Path $uninstallKeys -ErrorAction SilentlyContinue)) {
        if ($entry.DisplayName -notmatch '^Unity\s+\d') { continue }
        if ($entry.InstallLocation) { $candidates.Add($entry.InstallLocation) }
        if ($entry.DisplayIcon) {
            $icon = $entry.DisplayIcon -replace ',\s*-?\d+$', ''
            $candidates.Add($icon.Trim('"'))
        }
    }
    foreach ($commandName in @('Unity.exe', 'Unity')) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command -and $command.Source) { $candidates.Add($command.Source) }
    }
    $hubRoots = @($env:APPDATA, $env:LOCALAPPDATA) | Where-Object { $_ }
    $hubJson = @($hubRoots | ForEach-Object { Join-Path $_ 'UnityHub/editors.json' }) + $HubFiles
    foreach ($path in (Get-TingLuHubEditorPaths $hubJson)) { $candidates.Add($path) }
    $installRoots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramW6432) | Where-Object { $_ } | Select-Object -Unique
    foreach ($root in $installRoots) {
        foreach ($versionName in $versionNames) {
            $candidates.Add((Join-Path $root "Unity/Hub/Editor/$versionName/Editor/Unity.exe"))
            $candidates.Add((Join-Path $root "Unity $versionName/Editor/Unity.exe"))
        }
    }
    $seen = @{}
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            if ((Split-Path -Leaf $candidate) -ieq 'Editor') { $candidate = Join-Path $candidate 'Unity.exe' }
            elseif (Test-Path -LiteralPath (Join-Path $candidate 'Editor/Unity.exe') -PathType Leaf) { $candidate = Join-Path $candidate 'Editor/Unity.exe' }
            else { $candidate = Join-Path $candidate 'Unity.exe' }
        }
        if (-not (Test-TingLuUnityEditorPath $candidate)) { continue }
        $resolved = (Resolve-Path -LiteralPath $candidate).Path
        if (-not (Test-TingLuUnityEditorVersionPath $resolved $version)) { continue }
        $key = $resolved.ToLowerInvariant()
        if (-not $seen.ContainsKey($key)) { $seen[$key] = $true; return $resolved }
    }
    return $null
}
