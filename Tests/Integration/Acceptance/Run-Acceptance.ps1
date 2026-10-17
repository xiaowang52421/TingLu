[CmdletBinding()]
param(
    [string]$WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$workspace = (Resolve-Path $WorkspaceRoot).Path
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$runId = $stamp + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $workspace (Join-Path 'tmp/acceptance-runs' $runId)
$null = New-Item -ItemType Directory -Path $runRoot -Force

$candidates = @(
    Get-ChildItem (Join-Path $workspace 'Assets/TingLu') -Filter '*.cs' -File -Recurse
    Get-ChildItem $PSScriptRoot -Filter '*.cs' -File -Recurse
) | Sort-Object -Property FullName -Unique

# Selection is by source text, not folder or assembly name. This keeps genuine pure C#
# production modules while excluding files that need UnityEngine or NUnit types.
$included = [System.Collections.Generic.List[string]]::new()
$excluded = [System.Collections.Generic.List[object]]::new()
foreach ($candidate in $candidates) {
    $sourceText = Get-Content -LiteralPath $candidate.FullName -Raw
    if ($sourceText -match '(?s)\bUnityEngine\b|\bNUnit\.Framework\b') {
        $excluded.Add([pscustomobject]@{ path = [System.IO.Path]::GetRelativePath($workspace, $candidate.FullName); reason = 'source references UnityEngine or NUnit.Framework' })
    } else {
        $included.Add($candidate.FullName)
    }
}

if (-not ($included | Where-Object { $_ -eq (Join-Path $PSScriptRoot 'AcceptanceChecks.cs') })) {
    throw 'AcceptanceChecks.cs did not pass the source-text filter.'
}

$compileStarted = Get-Date
Add-Type -Path $included.ToArray() -ErrorAction Stop
$compileSeconds = [math]::Round(((Get-Date) - $compileStarted).TotalSeconds, 3)
$results = [TingLu.Diagnostics.AcceptanceChecks]::Run($runRoot)
$failed = @($results | Where-Object Status -eq 'FAIL')

$evidence = [ordered]@{
    runId = $runId
    startedLocal = $stamp
    command = 'pwsh -NoProfile -File Tests/Integration/Acceptance/Run-Acceptance.ps1'
    compiler = 'PowerShell Add-Type (C#), no Unity or NUnit references'
    sourceFilter = 'Recursively read candidate .cs files under Assets/TingLu and Tests/Integration/Acceptance; exclude each file whose contents reference UnityEngine or NUnit.Framework.'
    compileSeconds = $compileSeconds
    includedSources = @($included | ForEach-Object { [System.IO.Path]::GetRelativePath($workspace, $_) })
    excludedSources = @($excluded)
    results = @($results)
    totals = [ordered]@{ pass = @($results | Where-Object Status -eq 'PASS').Count; fail = $failed.Count; notRun = 0 }
    manualVerification = [ordered]@{
        unityImportOrPlayMode = 'NOT RUN'
        audioHardwareOrHumanListening = 'NOT RUN'
        realBlindPlayerOrAccessibilityStudy = 'NOT RUN'
        crossModuleDemoSession = 'COMPILED IN THE PURE C# SOURCE SET; Unity scene and PlayMode behavior NOT RUN'
    }
    saveWorkspace = $runRoot
}

$evidencePath = Join-Path $runRoot 'evidence.json'
$evidence | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $evidencePath -Encoding utf8
$qaPath = Join-Path $workspace 'docs/qa/acceptance-results.json'
$evidence | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $qaPath -Encoding utf8

Write-Output ("Evidence: " + [System.IO.Path]::GetRelativePath($workspace, $evidencePath))
Write-Output ("QA result: " + [System.IO.Path]::GetRelativePath($workspace, $qaPath))
Write-Output ("Pure C# checks: {0} passed, {1} failed" -f $evidence.totals.pass, $evidence.totals.fail)
if ($failed.Count -gt 0) { exit 1 }
