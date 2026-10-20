#Requires -Version 7.0
[CmdletBinding()]
param([string]$UnityPath, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$reportPath = Join-Path $projectRoot 'docs/editor-v2/验证结果.json'
$null = New-Item -ItemType Directory -Path (Split-Path -Parent $reportPath) -Force
$report = [ordered]@{
    recordedAtUtc = [datetime]::UtcNow.ToString('o')
    status = 'RUNNING'
    stages = @()
    manualMouseKeyboard = 'NOT_RUN'
    physicalAudio = 'NOT_RUN'
    humanAccessibility = 'NOT_RUN'
}
try {
    $jobs = @(
        @{ stage = 'EditMode'; filter = 'TingLu.Tests.StreetEditorV2' },
        @{ stage = 'EditMode'; filter = 'TingLu.Tests.Upgrade' },
        @{ stage = 'PlayMode'; filter = 'TingLu.Tests.Upgrade.Acceptance' }
    )
    if (-not $SkipBuild) { $jobs += @{ stage = 'BuildWindows'; filter = $null } }
    foreach ($job in $jobs) {
        $argsForStage = @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Verify-Unity.ps1'),'-Stage',$job.stage)
        if ($UnityPath) { $argsForStage += @('-UnityPath',$UnityPath) }
        if ($job.filter) { $argsForStage += @('-TestFilter',$job.filter) }
        $before = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'output/unity-verification') -Directory -ErrorAction SilentlyContinue | ForEach-Object Name)
        & (Join-Path $PSHOME 'pwsh.exe') @argsForStage
        $stageExit = $LASTEXITCODE
        $new = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'output/unity-verification') -Directory | Where-Object Name -NotIn $before)
        if ($new.Count -ne 1) { throw '无法唯一识别本次 Unity 阶段证据，请保留 output/unity-verification 并核对。' }
        $resultPath = Join-Path $new[0].FullName 'results.json'
        $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        $report.stages += [ordered]@{ stage=$job.stage; filter=$job.filter; evidence=[IO.Path]::GetRelativePath($projectRoot,$resultPath).Replace('\','/'); result=$result }
        $report | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $reportPath -Encoding utf8
        if ($stageExit -ne 0 -or $result.status -ne 'PASS') { throw "阶段失败：$($job.stage)。请查看 $resultPath" }
    }
    $fingerprints = @($report.stages | ForEach-Object { $_.result.sourceFingerprint } | Select-Object -Unique)
    if ($fingerprints.Count -ne 1) { throw '阶段之间源码发生变化，需要在源码冻结后重新验证。' }
    $report.sourceFingerprint = $fingerprints[0]
    $report.status = if ($SkipBuild) { 'TESTS_PASS_BUILD_NOT_RUN' } else { 'PASS' }
} catch {
    $report.status = 'FAIL'
    $report.error = $_.ToString()
    throw
} finally {
    $report | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $reportPath -Encoding utf8
}
Write-Output "Street editor v2 evidence: $reportPath"
