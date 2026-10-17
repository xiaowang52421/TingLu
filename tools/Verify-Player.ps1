[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ExecutablePath,
    [int]$TimeoutSeconds = 180,
    [switch]$Visible
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'PlayerEvidenceRules.ps1')
if($TimeoutSeconds -le 0){throw 'TimeoutSeconds must be positive.'}
$taskRoot=Split-Path -Parent $PSScriptRoot
$ExecutablePath=(Resolve-Path -LiteralPath $ExecutablePath).Path
$evidenceRoot=Join-Path $taskRoot ('output/player-verification/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
$null=New-Item -ItemType Directory -Path $evidenceRoot -Force
$logPath=Join-Path $evidenceRoot 'player.log'
$report=[ordered]@{status='NOT_RUN';executable=$ExecutablePath;scope='Rendered Windows development Player startup and screenshot; no human input or listening validation';log=$logPath;timeoutSeconds=$TimeoutSeconds;window=if($Visible){'VISIBLE'}else{'HIDDEN'};visualLayout='NOT_VERIFIED';screenshot='NOT_CAPTURED';speechVerified=$false;speechVerification='NOT_CONFIRMED';deviceListening='NOT_RUN';humanAudioOnlyPlaythrough='NOT_RUN'}
try {
    $startInfo=[System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName=$ExecutablePath
    $startInfo.UseShellExecute=$false
    $startInfo.CreateNoWindow=$true
    $startInfo.WindowStyle=if($Visible){[System.Diagnostics.ProcessWindowStyle]::Normal}else{[System.Diagnostics.ProcessWindowStyle]::Hidden}
    $startInfo.Environment['TINGLU_AUDIO_DIAGNOSTICS']='1'
    $startInfo.Environment['TINGLU_STREET_SAVE_ROOT']=Join-Path $evidenceRoot 'isolated-saves'
    foreach($argument in @('-popupwindow','-screen-width','960','-screen-height','640','-logFile',$logPath,'-tinglu-startup-probe',$evidenceRoot)){$startInfo.ArgumentList.Add($argument)}
    $process=[System.Diagnostics.Process]::Start($startInfo)
    try {
        $deadline=[datetime]::UtcNow.AddSeconds($TimeoutSeconds)
        while(-not $process.WaitForExit(1000)) {
            if([datetime]::UtcNow -ge $deadline){$process.Kill($true);$process.WaitForExit();throw 'Player startup probe timed out.'}
        }
        $report.exitCode=$process.ExitCode
        if($process.ExitCode -ne 0){throw "Player exited with code $($process.ExitCode)."}
    } finally {$process.Dispose()}
    $startupPath=Join-Path $evidenceRoot 'startup-results.json'
    if(-not (Test-Path -LiteralPath $startupPath)){throw 'Player did not write startup verification results.'}
    $startup=Get-Content -LiteralPath $startupPath -Raw | ConvertFrom-Json
    if($startup.status -ne 'PASS'){throw 'Player startup verification failed.'}
    $report.startup=$startup
    $screenshotPath=Join-Path $evidenceRoot 'first-step-menu.png'
    if($startup.screenshotCreated -and (Test-Path -LiteralPath $screenshotPath -PathType Leaf)){$report.screenshot=$screenshotPath}
    elseif($startup.screenshotCreated){$report.screenshot='REPORTED_CREATED_BUT_FILE_MISSING'}
    else{$report.screenshot='NOT_CREATED'}
    $report.visualLayout=Get-TingLuVisualLayoutStatus ([bool]$Visible)
    $log=Get-Content -LiteralPath $logPath -Raw
    $audioFailures=@([regex]::Matches($log,'(?m)^Audio .*speech_(?:unavailable|render_failed).*$') | ForEach-Object Value)
    $report.audioFailures=$audioFailures
    if($audioFailures.Count){throw 'Player could not synthesize Chinese speech. See player.log.'}
    $speechEvidence=Get-TingLuSpeechStartEvidence $log
    $report.speechStartCount=$speechEvidence.Count
    if($speechEvidence.Verified){
        $report.speechVerified=$true
        $report.speechVerification='VERIFIED_STARTED_SUCCESS'
        $report.speechBackends=$speechEvidence.Backends
    }else{
        $report.speechVerified=$false
        $report.speechVerification='NOT_CONFIRMED_NO_STARTED_SUCCESS_LOG'
    }
    $report.status='PASS'
    $report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'results.json') -Encoding utf8
    Write-Output "PASS Rendered Player startup Evidence: $evidenceRoot"
} catch {
    $report.status='FAIL';$report.reason=$_.ToString()
    $report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'results.json') -Encoding utf8
    throw
}
