[CmdletBinding()]
param([string]$UnityPath, [string]$ReportPath = 'docs/qa/level-editor-unity-results.json')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'UnityEditorDiscovery.ps1')
if (-not $UnityPath) { $UnityPath = Find-TingLuUnityEditor -ProjectRoot $taskRoot }
if (-not $UnityPath) { throw 'Unity editor not found. Supply -UnityPath.' }
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('听路 关卡编辑器迁移验证 ' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $sandbox
foreach ($directory in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $directory) -Destination $sandbox -Recurse
}
# Preserve a source snapshot before any relocation. Never move assets in the working project.
$snapshot = Join-Path $taskRoot ('output/level-editor-source-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip')
Compress-Archive -LiteralPath (Join-Path $sandbox 'Assets'), (Join-Path $sandbox 'Packages'), (Join-Path $sandbox 'ProjectSettings') -DestinationPath $snapshot
function Assert-SandboxPath([string]$Path) {
    $sandboxPrefix = [IO.Path]::GetFullPath($sandbox).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($Path).StartsWith($sandboxPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Relocation target is outside the isolated test project.' }
}
$originalModule = Join-Path $sandbox 'Assets/TingLu/Levels'
$relocatedModule = Join-Path $sandbox 'Assets/迁移后的关卡 模块'
foreach ($path in @($originalModule, $relocatedModule, ($originalModule + '.meta'), ($relocatedModule + '.meta'))) { Assert-SandboxPath $path }
Move-Item -LiteralPath $originalModule -Destination $relocatedModule
Move-Item -LiteralPath ($originalModule + '.meta') -Destination ($relocatedModule + '.meta')
$voiceMeta = Get-ChildItem -LiteralPath (Join-Path $sandbox 'Assets') -Recurse -Filter '*.mat.meta' -File |
    Where-Object { (Get-Content -LiteralPath $_.FullName -Raw) -match '(?m)^guid: 8a6032d7a1144618bbf409fe8749fc72\s*$' } | Select-Object -First 1
if (-not $voiceMeta) { throw 'Voice material metadata was not found in the copied project.' }
$originalMaterial = $voiceMeta.FullName.Substring(0, $voiceMeta.FullName.Length - 5)
$relocatedMaterial = Join-Path $sandbox 'Assets/迁移后的声纹 材质.mat'
foreach ($path in @($originalMaterial, $relocatedMaterial, ($originalMaterial + '.meta'), ($relocatedMaterial + '.meta'))) { Assert-SandboxPath $path }
Move-Item -LiteralPath $originalMaterial -Destination $relocatedMaterial
Move-Item -LiteralPath ($originalMaterial + '.meta') -Destination ($relocatedMaterial + '.meta')
$null = New-Item -ItemType Directory -Path (Join-Path $sandbox 'tools')
foreach ($file in @('Verify-Unity.ps1', 'UnityEditorDiscovery.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $sandbox 'tools')
}
# Keep relocation tests out of the working project and run from an unrelated directory.
$previousDirectory = Get-Location
$previousRelocationFlag = $env:TINGLU_EDITOR_RELOCATION_TESTS
try {
    Set-Location -LiteralPath ([IO.Path]::GetTempPath())
    $env:TINGLU_EDITOR_RELOCATION_TESTS = '1'
    $env:TINGLU_ALLOW_RELOCATION_GENERATED_FILES = '1'
    & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $sandbox 'tools/Verify-Unity.ps1') -UnityPath $UnityPath -Stage EditMode
    $verificationExit = $LASTEXITCODE
} finally {
    Set-Location -LiteralPath $previousDirectory.Path
    $env:TINGLU_EDITOR_RELOCATION_TESTS = $previousRelocationFlag
    Remove-Item Env:TINGLU_ALLOW_RELOCATION_GENERATED_FILES -ErrorAction SilentlyContinue
}
$resultFile = Get-ChildItem -LiteralPath (Join-Path $sandbox 'output/unity-verification') -Recurse -Filter results.json -File | Select-Object -First 1
if (-not $resultFile) { throw "Unity did not produce verification evidence in $sandbox" }
$report = Get-Content -LiteralPath $resultFile.FullName -Raw | ConvertFrom-Json
# Keep logs/XML with the delivery; the isolated TEMP project may be removed later.
$evidenceCopy = Join-Path $taskRoot ('output/level-editor-unity-verification/' + $resultFile.Directory.Name)
$null = New-Item -ItemType Directory -Path $evidenceCopy -Force
Copy-Item -LiteralPath $resultFile.Directory.FullName -Destination (Split-Path -Parent $evidenceCopy) -Recurse -Force
foreach ($field in @('log', 'testResults')) {
    if ($report.PSObject.Properties[$field] -and (Test-Path -LiteralPath $report.$field)) {
        $report | Add-Member -NotePropertyName ('sandbox' + $field) -NotePropertyValue $report.$field
        $report.$field = Join-Path $evidenceCopy (Split-Path -Leaf $report.$field)
    }
}
$report | Add-Member -NotePropertyName relocatedProject -NotePropertyValue $sandbox
$report | Add-Member -NotePropertyName sourceSnapshot -NotePropertyValue $snapshot
$report | Add-Member -NotePropertyName scope -NotePropertyValue 'Actual Unity EditMode APIs in a relocated project with Chinese characters and spaces; manual mouse/keyboard GUI and device audio not verified.'
$destination = if ([IO.Path]::IsPathRooted($ReportPath)) { $ReportPath } else { Join-Path $taskRoot $ReportPath }
$null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $destination -Encoding utf8
Write-Output "Editor evidence: $destination"
if ($verificationExit -ne 0) { throw "Unity EditMode verification failed; see $destination" }
