$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$assets = @(Get-ChildItem Assets -Recurse -Force | Where-Object { $_.Extension -ne '.meta' })
$missing = @($assets | Where-Object { -not (Test-Path -LiteralPath ($_.FullName + '.meta')) })
if($missing.Count) { throw ('Missing meta: ' + ($missing.FullName -join ', ')) }
$guids = @{}
foreach($meta in Get-ChildItem Assets -Recurse -Filter '*.meta') {
    $match = [regex]::Match((Get-Content -LiteralPath $meta.FullName -Raw), '(?m)^guid: ([0-9a-f]{32})\s*$')
    if(-not $match.Success) { throw "Invalid GUID in $($meta.FullName)" }
    $guid = $match.Groups[1].Value
    if($guids.ContainsKey($guid)) { throw "Duplicate GUID: $guid" }
    $guids[$guid]=$meta.FullName
}
$assemblies = @{}
foreach($file in Get-ChildItem Assets -Recurse -Filter '*.asmdef') {
    $definition = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if($assemblies.ContainsKey($definition.name)) { throw "Duplicate assembly: $($definition.name)" }
    $assemblies[$definition.name]=$definition
}
function Visit-Assembly([string]$assemblyName,[string[]]$stack) {
    if($stack -contains $assemblyName) { throw ('Assembly cycle: '+ (($stack + $assemblyName) -join ' -> ')) }
    foreach($reference in $assemblies[$assemblyName].references) {
        if($reference -like 'Unity.*') { continue }
        if(-not $assemblies.ContainsKey($reference)) { throw "Missing assembly reference: $reference" }
        Visit-Assembly $reference ($stack + $assemblyName)
    }
}
foreach($assemblyName in $assemblies.Keys) { Visit-Assembly $assemblyName @() }
foreach($assetFile in Get-ChildItem Assets -Recurse -File | Where-Object { $_.Extension -in '.unity','.mat' }) {
    foreach($reference in [regex]::Matches((Get-Content -LiteralPath $assetFile.FullName -Raw),'guid: ([0-9a-f]{32})')) {
        if(-not $guids.ContainsKey($reference.Groups[1].Value)) { throw "Unresolved asset GUID in $($assetFile.FullName)" }
    }
}
$packages = Get-Content Packages/manifest.json -Raw | ConvertFrom-Json
if(-not $packages.dependencies.'com.unity.modules.imgui') { throw 'Runtime menu requires IMGUI module' }
$report = [ordered]@{status='PASS'; assets=$assets.Count; uniqueGuids=$guids.Count; assemblies=$assemblies.Count; checks=@('Every asset and directory has meta','All GUIDs valid and unique','Assembly names and references valid and acyclic','Scene and material GUID references resolve','IMGUI dependency declared'); unityImport='NOT_RUN'}
$report | ConvertTo-Json -Depth 5 | Set-Content docs/qa/project-results.json -Encoding utf8
$report.checks | ForEach-Object { Write-Output "PASS $_" }
