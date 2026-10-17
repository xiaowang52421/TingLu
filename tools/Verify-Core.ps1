param([string]$ReportPath = 'docs/qa/core-results.json')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$sources = @(Get-ChildItem 'Assets/TingLu/Contracts/*.cs','Assets/TingLu/Bootstrap/Core/*.cs','Assets/TingLu/Config/Shared/*.cs' | ForEach-Object { $_.FullName })
$sources += Join-Path $taskRoot 'Tests/Core/ContractChecks.cs'
try {
    Add-Type -Path $sources -ErrorAction Stop
    $passed = [ContractChecks]::Run()
    $report = [ordered]@{ status = 'PASS'; scope = 'Pure C# contracts and bootstrap core only'; runtime = $PSVersionTable.PSVersion.ToString(); passed = @($passed); unity = 'NOT_RUN'; devices = 'NOT_RUN' }
    $parent = Split-Path -Parent $ReportPath
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    $passed | ForEach-Object { Write-Output "PASS $_" }
} catch {
    Write-Error $_
    exit 1
}
