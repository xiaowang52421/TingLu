$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path
Set-Location -LiteralPath $projectRoot

$sources = @(
    Get-ChildItem 'Assets/TingLu/Contracts/*.cs', 'Assets/TingLu/Config/Shared/*.cs',
        'Assets/TingLu/Perception/*.cs', 'Assets/TingLu/Memory/*.cs' |
        ForEach-Object { $_.FullName }
)
$sources += (Resolve-Path 'Assets/TingLu/Presentation/PresentationFrameComposer.cs').Path
$sources += (Resolve-Path 'Assets/TingLu/Tests/EditMode/PerceptionMemory/PerceptionPresentationPureChecks.cs').Path

Add-Type -Path $sources -ErrorAction Stop
$checks = [PerceptionPresentationPureChecks]::Run()
$checks | ForEach-Object { Write-Output "PASS $_" }
