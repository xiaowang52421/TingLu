[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ExecutablePath,
    [int]$Width=1280,
    [int]$Height=720,
    [int]$AlternateWidth=960,
    [int]$AlternateHeight=640,
    [int]$TimeoutSeconds=120,
    [switch]$Visible
)
$ErrorActionPreference='Stop'
if($Width -lt 320 -or $Height -lt 240 -or $AlternateWidth -lt 320 -or $AlternateHeight -lt 240 -or $TimeoutSeconds -le 0){throw 'Invalid verification dimensions or timeout.'}
if($Width -eq $AlternateWidth -and $Height -eq $AlternateHeight){throw 'The primary and alternate Player resolutions must be different.'}
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$evidence=Join-Path $root ('output/street-verification/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
$null=New-Item -ItemType Directory -Path $evidence -Force
$exe=$null
$log=Join-Path $evidence 'player.log'
$report=[ordered]@{status='NOT_RUN';executable=$ExecutablePath;width=$Width;height=$Height;alternateWidth=$AlternateWidth;alternateHeight=$AlternateHeight;log=$log;scope='Rendered development Player; deterministic production probe, not human input or listening';humanListening='NOT_RUN';humanAccessibility='NOT_RUN'}
$report.visibleWindow=[bool]$Visible
Add-Type -AssemblyName System.Drawing
function Assert-ProbeEvidence($probe,[int]$expectedWidth,[int]$expectedHeight,[string]$label){
    if($probe.status -ne 'PASS' -or $probe.scene -ne 'StreetCrossroads' -or -not $probe.running){throw "$label Street Player probe did not pass."}
    if(-not $probe.pauseEvidence.menuPlayingBeforePause -or -not $probe.pauseEvidence.menuPaused -or -not $probe.pauseEvidence.menuPlayingAfterResume -or -not $probe.pauseEvidence.pauseFrozen -or -not $probe.pauseEvidence.resumeSnapshotPreserved -or -not $probe.pauseEvidence.resumeAdvanced){throw "$label pause/freeze/resume evidence did not pass."}
    if(-not $probe.routeEvidence.routePassed -or [int]$probe.routeEvidence.signalCount -le 0 -or -not $probe.routeEvidence.nsSignalLive){throw "$label production route did not reach both checkpoints and observe the live NS signal."}
    if([string]::IsNullOrWhiteSpace([string]$probe.routeEvidence.signalPhase) -or [string]::IsNullOrWhiteSpace([string]$probe.routeEvidence.signalObjectId) -or $probe.routeEvidence.signalObjectId -ne 'street.signal.ns' -or $null -eq $probe.routeEvidence.PSObject.Properties['signalIsGreen']){throw "$label signal phase/state metadata is missing."}
    $probeTime=[double]$probe.routeEvidence.signalProbeTime
    $observedAt=[double]$probe.routeEvidence.signalObservedAt
    $expiresAt=[double]$probe.routeEvidence.signalExpiresAt
    if($probeTime -lt 0 -or $observedAt -gt $probeTime -or $expiresAt -le $probeTime){throw "$label NS signal observation timestamps are invalid or expired."}
    $paths=@($probe.menuScreenshot,$probe.exploredScreenshot,$probe.pausedScreenshot,$probe.resumedScreenshot,$probe.signalScreenshot,$probe.eastLandingScreenshot)
    if($paths.Count -ne 6){throw "$label screenshot manifest does not contain six images."}
    $hashes=@()
    $images=@()
    foreach($path in $paths){
        if(-not $path -or -not (Test-Path -LiteralPath $path -PathType Leaf)){throw "$label screenshot is missing: $path"}
        $bytes=[IO.File]::ReadAllBytes($path)
        if($bytes.Length -lt 24 -or [Convert]::ToHexString($bytes[0..7]) -ne '89504E470D0A1A0A'){throw "$label screenshot is not a complete PNG: $path"}
        $hashes += [ordered]@{path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
        $bitmap=[Drawing.Bitmap]::new($path)
        try {
            if($bitmap.Width -ne $expectedWidth -or $bitmap.Height -ne $expectedHeight){throw "$label screenshot dimensions differ from the requested ${expectedWidth}x${expectedHeight}: $path"}
            $nonBlack=0
            for($taskY=0;$taskY -lt $bitmap.Height;$taskY+=8){for($taskX=0;$taskX -lt $bitmap.Width;$taskX+=8){$pixel=$bitmap.GetPixel($taskX,$taskY);if($pixel.R -gt 0 -or $pixel.G -gt 0 -or $pixel.B -gt 0){$nonBlack++}}}
            if($nonBlack -eq 0){throw "$label screenshot is blank black: $path"}
            $images += [ordered]@{path=$path;width=$bitmap.Width;height=$bitmap.Height;nonBlackSamples=$nonBlack}
        } finally {$bitmap.Dispose()}
    }
    return @{hashes=$hashes;imageContentChecks=$images}
}
try {
    $exe=(Resolve-Path -LiteralPath $ExecutablePath).Path
    $report.executable=$exe
    $report.executableSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    $report.verifierSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    $info=[System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName=$exe
    $info.UseShellExecute=$false
    $info.CreateNoWindow=$true
    $info.WindowStyle=if($Visible){[System.Diagnostics.ProcessWindowStyle]::Normal}else{[System.Diagnostics.ProcessWindowStyle]::Hidden}
    $isolated=Join-Path $evidence 'isolated-saves'
    $info.Environment['TINGLU_STREET_SAVE_ROOT']=$isolated
    $info.Environment['TINGLU_AUDIO_DIAGNOSTICS']='1'
    foreach($arg in @('-popupwindow','-screen-width',"$Width",'-screen-height',"$Height",'-logFile',$log,'-tinglu-street-probe',$evidence,'-tinglu-street-save-root',$isolated)){$info.ArgumentList.Add($arg)}
    $process=[System.Diagnostics.Process]::Start($info)
    try {
        if(-not $process.WaitForExit($TimeoutSeconds*1000)){$process.Kill($true);$process.WaitForExit();throw 'Street Player probe timed out.'}
        $report.exitCode=$process.ExitCode
        if($process.ExitCode -ne 0){throw "Street Player exited with code $($process.ExitCode)."}
    } finally {$process.Dispose()}
    $probe=Get-Content -LiteralPath (Join-Path $evidence 'street-probe-results.json') -Raw | ConvertFrom-Json
    $primaryImageEvidence=Assert-ProbeEvidence $probe $Width $Height 'Primary-resolution'
    foreach($metric in @('initialProbeMilliseconds','initialRenderMilliseconds','feedbackRequestedMilliseconds','feedbackStartedMilliseconds')){
        if($null -eq $probe.PSObject.Properties[$metric] -or [double]$probe.$metric -lt 0){throw "Street Player timing is missing or invalid: $metric"}
    }
    if($probe.feedbackStartedMilliseconds -lt $probe.feedbackRequestedMilliseconds -or $probe.feedbackStartedMilliseconds -gt $probe.initialProbeMilliseconds){throw 'Cane feedback did not reach software Started inside the measured initial probe.'}
    $report.probe=$probe
    $report.pauseEvidence=$probe.pauseEvidence
    $report.screenshotHashes=$primaryImageEvidence.hashes
    $report.imageContentChecks=$primaryImageEvidence.imageContentChecks
    if((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $report.executableSha256){throw 'Player executable changed during verification.'}
    $playerLog=Get-Content -LiteralPath $log -Raw
    if($playerLog -match '(?im)shader .* is not supported|Shader error in |NullReferenceException|MissingReferenceException'){
        throw 'Player log contains a shader or runtime error.'
    }
    $report.speechBackend=if($playerLog -match 'TingLu speech started;'){'STARTED'}elseif($playerLog -match 'Street audio failed: speech_'){'FAIL'}else{'NOT_CONFIRMED'}
    $report.visualReview='PENDING_IMAGE_INSPECTION'
    if($report.speechBackend -ne 'STARTED'){throw 'Chinese speech did not reach Unity playback; inspect the Player log.'}

    # Repeat the same production probe at a second native resolution. Each run has
    # its own isolated save root and evidence directory, so the hashes prove both
    # dimensions came from actual Player backbuffer captures.
    $alternateEvidence=Join-Path $evidence ('alternate-'+$AlternateWidth+'x'+$AlternateHeight)
    $null=New-Item -ItemType Directory -Path $alternateEvidence -Force
    $alternateLog=Join-Path $alternateEvidence 'player.log'
    $alternateSave=Join-Path $alternateEvidence 'isolated-saves'
    $alternateInfo=[System.Diagnostics.ProcessStartInfo]::new()
    $alternateInfo.FileName=$exe
    $alternateInfo.UseShellExecute=$false
    $alternateInfo.CreateNoWindow=$true
    $alternateInfo.WindowStyle=if($Visible){[System.Diagnostics.ProcessWindowStyle]::Normal}else{[System.Diagnostics.ProcessWindowStyle]::Hidden}
    $alternateInfo.Environment['TINGLU_STREET_SAVE_ROOT']=$alternateSave
    $alternateInfo.Environment['TINGLU_AUDIO_DIAGNOSTICS']='1'
    foreach($arg in @('-popupwindow','-screen-width',"$AlternateWidth",'-screen-height',"$AlternateHeight",'-logFile',$alternateLog,'-tinglu-street-probe',$alternateEvidence,'-tinglu-street-save-root',$alternateSave)){$alternateInfo.ArgumentList.Add($arg)}
    $alternateProcess=[System.Diagnostics.Process]::Start($alternateInfo)
    try {
        if(-not $alternateProcess.WaitForExit($TimeoutSeconds*1000)){$alternateProcess.Kill($true);$alternateProcess.WaitForExit();throw 'Alternate-resolution Street Player probe timed out.'}
        if($alternateProcess.ExitCode -ne 0){throw "Alternate-resolution Street Player exited with code $($alternateProcess.ExitCode)."}
    } finally {$alternateProcess.Dispose()}
    $alternateProbe=Get-Content -LiteralPath (Join-Path $alternateEvidence 'street-probe-results.json') -Raw | ConvertFrom-Json
    $report.alternateResolution=[ordered]@{width=$AlternateWidth;height=$AlternateHeight;evidence=$alternateEvidence;log=$alternateLog;probe=$alternateProbe;hashes=@();imageContentChecks=@()}
    $alternateImageEvidence=Assert-ProbeEvidence $alternateProbe $AlternateWidth $AlternateHeight 'Alternate-resolution'
    $report.alternateResolution.hashes=$alternateImageEvidence.hashes
    $report.alternateResolution.imageContentChecks=$alternateImageEvidence.imageContentChecks
    $alternateLogText=Get-Content -LiteralPath $alternateLog -Raw
    if($alternateLogText -match '(?im)shader .* is not supported|Shader error in |NullReferenceException|MissingReferenceException'){throw 'Alternate Player log contains a shader or runtime error.'}
    $report.twoResolutionEvidence=$true
    $report.status='PASS'
    Write-Output "PASS Street Player probe. Evidence: $evidence"
} catch {
    $report.status='FAIL';$report.reason=$_.ToString();throw
} finally {
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'results.json') -Encoding utf8
}
