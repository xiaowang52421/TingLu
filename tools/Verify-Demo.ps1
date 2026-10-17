param([string]$ReportPath = 'docs/qa/demo-results.json')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$sources = @(Get-ChildItem Assets/TingLu -Recurse -Filter '*.cs' -File | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw) -notmatch '\bUnityEngine\b|\bUnityEditor\b|\bNUnit\.Framework\b'
} | ForEach-Object { $_.FullName })
$sources += (Join-Path $taskRoot 'Tests/Demo/DemoSessionChecks.cs')
$runRoot = Join-Path $taskRoot ('tmp/demo-runs/' + [guid]::NewGuid().ToString('N'))
try {
    Add-Type -Path $sources -ErrorAction Stop
    $levelPath=Join-Path $taskRoot 'Assets/TingLu/Levels/Data/FirstStep.json'
    $levelJson=if(Test-Path -LiteralPath $levelPath){Get-Content -LiteralPath $levelPath -Raw}else{$null}
    $checks = [DemoSessionChecks]::Run($runRoot,$levelJson)
    $report = [ordered]@{ status='PASS'; scope='Pure C# production DemoSession with recording audio test backend and real LocalSaveService IO'; runtime=$PSVersionTable.PSVersion.ToString(); passed=@($checks); evidenceDirectory=$runRoot; unity='NOT_RUN'; audioDevice='NOT_RUN'; speechProvider='NOT_RUN'; humanAudioOnlyPlaythrough='NOT_RUN' }
    $checks | ForEach-Object { Write-Output "PASS $_" }
} catch {
    $report = [ordered]@{status='FAIL'; error=$_.ToString(); unity='NOT_RUN'; audioDevice='NOT_RUN'}
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    throw
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding utf8
