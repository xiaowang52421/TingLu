[CmdletBinding()]
param(
    [string]$UnityPath,
    [ValidateSet('Import', 'EditMode', 'PlayMode', 'BuildWindows')]
    [string]$Stage = 'Import',
    [string]$TestFilter,
    [int]$TimeoutSeconds = 1200
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }
$evidenceRoot = Join-Path $taskRoot ('output/unity-verification/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$reportPath = Join-Path $evidenceRoot 'results.json'
$verifierSha256 = $null
$verifierHashError = $null
try { $verifierSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash }
catch { $verifierHashError = $_.Exception.Message }
$report = [ordered]@{ stage=$Stage; status='NOT_RUN'; unityPath=$null; log=$null; reason=$null; deviceAudio='NOT_RUN'; humanAudioOnlyPlaythrough='NOT_RUN'; verifierSha256=$verifierSha256 }
if ($verifierHashError) { $report.verifierHashError = $verifierHashError }
function Save-Report { $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8 }
try {
    $null = New-Item -ItemType Directory -Path $evidenceRoot -Force
    # Establish an auditable NOT_RUN record before editor discovery or any other setup can fail.
    Save-Report
} catch {
    # A missing/unwritable evidence root cannot produce a durable report; preserve the
    # original error rather than claiming that a run was attempted.
    throw "Could not initialize Unity evidence directory '$evidenceRoot': $($_.Exception.Message)"
}
function Get-SourceManifest {
    $byPath = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($directory in @('Assets','Packages','ProjectSettings')) {
        Get-ChildItem -LiteralPath (Join-Path $taskRoot $directory) -Recurse -File -Force | ForEach-Object {
            $path = [IO.Path]::GetRelativePath($taskRoot,$_.FullName).Replace('\','/')
            $byPath.Add($path, [ordered]@{path=$path;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash})
        }
    }
    $paths = [string[]]$byPath.Keys
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    foreach ($path in $paths) { $byPath[$path] }
}
try {
. (Join-Path $PSScriptRoot 'UnityEditorDiscovery.ps1')
$version = Get-TingLuProjectUnityVersion $taskRoot
$report.projectUnityVersion = $version
if (-not $UnityPath) {
    $runningEditorPaths = @()
    try { $runningEditorPaths = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop | ForEach-Object { $_.ExecutablePath }) } catch { }
    $UnityPath = Find-TingLuUnityEditor -ProjectRoot $taskRoot -RunningEditorPaths $runningEditorPaths
    if ($UnityPath) { $report.discovery = 'project-version-matched editor from environment, registry, PATH, running process, Unity Hub editor list, or ProgramFiles' }
}
if (-not $UnityPath) {
    $report.reason = 'Unity editor not found. Supply -UnityPath with the installed editor executable.'
    Save-Report
    Write-Output "NOT_RUN $($report.reason) Evidence: $reportPath"
    exit 2
}
try {
    $UnityPath = (Resolve-Path -LiteralPath $UnityPath).Path
    if (-not (Test-TingLuUnityEditorVersionPath $UnityPath $version)) { throw "UnityPath must point to the project's Unity $version Editor/Unity.exe (checked by executable version where available), not the Unity Hub launcher or another editor version." }
    $report.unityPath = $UnityPath
    $editorProcesses = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue)
    foreach ($editorProcess in $editorProcesses) {
        $match = [regex]::Match($editorProcess.CommandLine, '(?i)-projectPath\s+(?:"([^"]+)"|(\S+))')
        if (-not $match.Success) { continue }
        $openProject = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
        if ([System.IO.Path]::GetFullPath($openProject).TrimEnd('\','/') -eq $taskRoot.TrimEnd('\','/')) {
            $report.reason = "Project is already open in Unity process $($editorProcess.ProcessId). Close that instance before batch verification."
            Save-Report
            Write-Output "NOT_RUN $($report.reason) Evidence: $reportPath"
            exit 2
        }
    }
    $logPath = Join-Path $evidenceRoot 'unity.log'
    $report.log = $logPath
    $manifest = @(Get-SourceManifest)
    $manifestJson = ConvertTo-Json -InputObject $manifest -Depth 4 -Compress
    $manifestPath = Join-Path $evidenceRoot 'source-manifest.json'
    $manifestJson | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $sourceHasher = [Security.Cryptography.SHA256]::Create()
    try { $report.sourceFingerprint = [Convert]::ToHexString($sourceHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($manifestJson))) }
    finally { $sourceHasher.Dispose() }
    $report.sourceManifest = $manifestPath
    # ProcessStartInfo.ArgumentList preserves Unicode paths and spaces without shell quoting.
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $UnityPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $arguments = @('-batchmode', '-projectPath', $taskRoot, '-logFile', $logPath)
    if ($Stage -in @('EditMode', 'PlayMode')) {
        $testPath = Join-Path $evidenceRoot ($Stage.ToLowerInvariant() + '.xml')
        $report.testResults = $testPath
        $arguments += @('-runTests', '-testPlatform', $Stage, '-testResults', $testPath)
        if ($TestFilter) { $arguments += @('-testFilter', $TestFilter); $report.testFilter = $TestFilter }
        $performanceEvidence = Join-Path $evidenceRoot 'street-acceptance'
        $null = New-Item -ItemType Directory -Path $performanceEvidence -Force
        $startInfo.Environment['TINGLU_UPGRADE_EVIDENCE_ROOT'] = $performanceEvidence
        if ($Stage -eq 'PlayMode') {
            $startInfo.Environment['TINGLU_STREET_SAVE_ROOT'] = Join-Path $evidenceRoot 'isolated-saves'
            $startInfo.Environment['TINGLU_UPGRADE_EVIDENCE_ROOT'] = Join-Path $evidenceRoot 'street-acceptance'
            $startInfo.Environment['TINGLU_AUDIO_DIAGNOSTICS'] = '1'
            $screenshotPath = Join-Path $evidenceRoot 'first-step-menu.png'
            $startInfo.Environment['TINGLU_SCREENSHOT_PATH'] = $screenshotPath
        }
    } else {
        $arguments += '-quit'
        if ($Stage -eq 'BuildWindows') {
            $buildPath = Join-Path $evidenceRoot 'Windows/TingLu.exe'
            $startInfo.Environment['TINGLU_BUILD_PATH'] = $buildPath
            $report.build = $buildPath
            $arguments += @('-buildTarget', 'Win64', '-executeMethod', 'TingLu.Diagnostics.Editor.WindowsDemoBuild.Build')
        }
    }
    foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
        $completedAt = $null
        $forcedAfterCompletion = $false
        while (-not $process.WaitForExit(1000)) {
            if (Test-Path -LiteralPath $logPath) {
                $currentLog = Get-Content -LiteralPath $logPath -Raw
                $hasShutdown = $currentLog -match 'Input System module state changed to: Shutdown\.|Batchmode quit successfully invoked'
                $hasResult = if ($Stage -in @('EditMode', 'PlayMode')) { Test-Path -LiteralPath $testPath } elseif ($Stage -eq 'BuildWindows') { (Test-Path -LiteralPath $buildPath) -and $currentLog -match 'TingLu Windows build succeeded:' } else { $currentLog -match 'Batchmode quit successfully invoked' }
                if ($hasShutdown -and $hasResult) {
                    if (-not $completedAt) { $completedAt = [datetime]::UtcNow }
                    if (([datetime]::UtcNow - $completedAt).TotalSeconds -ge 15) {
                        # Some editor installations hang during shutdown after writing complete results.
                        # Only stop this script's child; all normal evidence checks still apply below.
                        $process.Kill($true)
                        $process.WaitForExit()
                        $forcedAfterCompletion = $true
                        $report.editorShutdown = 'FORCED_AFTER_COMPLETED_STAGE'
                        break
                    }
                }
            }
            if ([datetime]::UtcNow -ge $deadline) { $process.Kill($true); $process.WaitForExit(); throw "Unity timed out after $TimeoutSeconds seconds." }
        }
        $report.exitCode = $process.ExitCode
        if ($process.ExitCode -ne 0 -and -not $forcedAfterCompletion) { throw "Unity exited with code $($process.ExitCode). See $logPath" }
    } finally { $process.Dispose() }
    if (-not (Test-Path -LiteralPath $logPath)) { throw 'Unity did not write a log; verification cannot be confirmed.' }
    $log = Get-Content -LiteralPath $logPath -Raw
    if ($log -match '(?m)\berror CS\d+\b|Shader error in |Scripts have compiler errors|Aborting batchmode due to|BuildFailedException') { throw "Unity log contains compilation, shader or batch errors. See $logPath" }
    if ($Stage -in @('EditMode', 'PlayMode')) {
        if (-not (Test-Path -LiteralPath $testPath)) { throw 'Unity did not write test results.' }
        [xml]$tests = Get-Content -LiteralPath $testPath -Raw
        $testRun = $tests.SelectSingleNode('/test-run')
        if (-not $testRun -or [int]$testRun.total -le 0 -or $testRun.result -ne 'Passed' -or [int]$testRun.failed -ne 0 -or [int]$testRun.passed -ne [int]$testRun.total -or [int]$testRun.skipped -ne 0 -or [int]$testRun.inconclusive -ne 0) { throw "$Stage results did not confirm a non-empty fully passing test run without skips." }
        $report.tests = [ordered]@{ total=[int]$testRun.total; passed=[int]$testRun.passed; failed=[int]$testRun.failed; skipped=[int]$testRun.skipped; inconclusive=[int]$testRun.inconclusive }
    }
    if ($Stage -eq 'BuildWindows' -and (-not (Test-Path -LiteralPath $buildPath) -or $log -notmatch 'TingLu Windows build succeeded:')) { throw 'Windows build did not confirm a successful build and executable.' }
    if ($Stage -eq 'PlayMode') {
        # The legacy screenshot hook is optional; only advertise files actually produced.
        if (Test-Path -LiteralPath $screenshotPath -PathType Leaf) { $report.screenshot = $screenshotPath }
        $report.screenshots = @(Get-ChildItem -LiteralPath $evidenceRoot -Recurse -File -Filter '*.png' | ForEach-Object {
            [ordered]@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
        })
        $uniqueScreenshotHashes = @($report.screenshots | ForEach-Object { $_.sha256 } | Select-Object -Unique)
        $report.uniqueScreenshotCount = $uniqueScreenshotHashes.Count
    }
    $afterManifest = @(Get-SourceManifest)
    if ($env:TINGLU_ALLOW_RELOCATION_GENERATED_FILES -eq '1') {
        # The isolated relocation test deliberately lets Unity import the moved
        # assets. Unity may create/update these two metadata files during import;
        # they are temporary-project products, not edits to the tested source.
        $beforeComparable = @($manifest | Where-Object {
            $_.path -notmatch '^Assets/LevelEditorUnityTest_[0-9a-f]+\.meta$' -and
            $_.path -ne 'Assets/迁移后的关卡 模块/Editor/PreviewTemporary.meta'
        })
        $afterComparable = @($afterManifest | Where-Object {
            $_.path -notmatch '^Assets/LevelEditorUnityTest_[0-9a-f]+\.meta$' -and
            $_.path -ne 'Assets/迁移后的关卡 模块/Editor/PreviewTemporary.meta'
        })
        $manifestJson = ConvertTo-Json -InputObject $beforeComparable -Depth 4 -Compress
        $afterManifestJson = ConvertTo-Json -InputObject $afterComparable -Depth 4 -Compress
    } else {
        $afterManifestJson = ConvertTo-Json -InputObject $afterManifest -Depth 4 -Compress
    }
    $report.sourceUnchangedDuringStage = $manifestJson -ceq $afterManifestJson
    if (-not $report.sourceUnchangedDuringStage) { throw 'Project source/assets/settings changed during Unity verification; evidence cannot certify a frozen version.' }
    $report.status = 'PASS'
    Save-Report
    Write-Output "PASS $Stage Evidence: $reportPath"
} catch {
    $report.status = 'FAIL'
    $report.reason = $_.ToString()
    Save-Report
    throw
}
} catch {
    $report.status = 'FAIL'
    $report.reason = $_.ToString()
    Save-Report
    throw
}
