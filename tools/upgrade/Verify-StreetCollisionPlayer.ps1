[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ExecutablePath,
    [int]$Width=1280,
    [int]$Height=720,
    [int]$AlternateWidth=960,
    [int]$AlternateHeight=640,
    [int]$ThirdWidth=0,
    [int]$ThirdHeight=0,
    [int]$TimeoutSeconds=180,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if ($Width -lt 320 -or $Height -lt 240 -or $AlternateWidth -lt 320 -or $AlternateHeight -lt 240 -or $TimeoutSeconds -le 0) {
    throw 'Invalid Player verification dimensions or timeout.'
}
if ($Width -eq $AlternateWidth -and $Height -eq $AlternateHeight) {
    throw 'The primary and alternate Player resolutions must be different.'
}
if (($ThirdWidth -eq 0) -xor ($ThirdHeight -eq 0)) { throw 'Specify both third-resolution dimensions or leave both at zero.' }
if ($ThirdWidth -ne 0 -and ($ThirdWidth -lt 320 -or $ThirdHeight -lt 240)) { throw 'Invalid third Player resolution.' }

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $evidence = Join-Path $root ('output/street-collision-verification/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
} else {
    if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { throw 'OutputDirectory must be an absolute path.' }
    $evidence = [IO.Path]::GetFullPath($OutputDirectory)
}
$null = New-Item -ItemType Directory -Path $evidence -Force
$exe = (Resolve-Path -LiteralPath $ExecutablePath).Path
$exeHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$verifierHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
$resolutions = @(
    [ordered]@{ width=$Width; height=$Height; label='primary' },
    [ordered]@{ width=$AlternateWidth; height=$AlternateHeight; label='alternate' }
)
if ($ThirdWidth -gt 0) { $resolutions += [ordered]@{ width=$ThirdWidth; height=$ThirdHeight; label='third' } }
$result = [ordered]@{
    status = 'NOT_RUN'
    executable = $exe
    executableSha256 = $exeHash
    verifierSha256 = $verifierHash
    visiblePlayer = $true
    outputDirectory = $evidence
    timeoutSecondsPerResolution = $TimeoutSeconds
    scope = 'Development Windows Player with an isolated codec-backed StreetEntry fixture and real production Tick contacts.'
    humanInput = 'NOT_RUN'
    humanListening = 'NOT_RUN'
    humanAccessibility = 'NOT_RUN'
    runs = @()
}
Add-Type -AssemblyName System.Drawing

function Assert-Truth($value, [string]$message) {
    if (-not [bool]$value) { throw $message }
}

function Assert-CollisionProbe($probe, [int]$expectedWidth, [int]$expectedHeight, [string]$runDirectory) {
    if ($probe.status -ne 'PASS' -or $probe.scene -ne 'StreetCrossroads') { throw 'Collision Player probe did not report PASS in StreetCrossroads.' }
    if ([int]$probe.screenWidth -ne $expectedWidth -or [int]$probe.screenHeight -ne $expectedHeight) {
        throw "Player backbuffer is $($probe.screenWidth)x$($probe.screenHeight), expected ${expectedWidth}x${expectedHeight}."
    }
    Assert-Truth $probe.fixtureCodecRoundTrip 'Fixture did not pass StreetLevelCodec encode/decode/compile.'
    Assert-Truth $probe.entryIsolation.previewEntryCreated 'A real ConfigurePreview StreetEntry was not created.'
    Assert-Truth $probe.initialUnknownZone.signalWasUnknown 'The distant traffic signal was visible before the player explored that area.'
    Assert-Truth $probe.unknownZone.signalRemainedUnknown 'A cane probe revealed the distant signal outside its range.'
    Assert-Truth $probe.caneProbe.revealedNorthWall 'The real cane probe did not record the nearby north wall.'

    $requiredActors = @(
        @{ id='pedestrian.standing'; kind='Pedestrian'; route=$false },
        @{ id='pedestrian.walking'; kind='Pedestrian'; route=$true },
        @{ id='car.parked'; kind='Car'; route=$false },
        @{ id='bicycle.parked'; kind='Bicycle'; route=$false },
        @{ id='electric-bike.parked'; kind='ElectricBike'; route=$false },
        @{ id='car.moving'; kind='Car'; route=$true },
        @{ id='bike.moving'; kind='Bicycle'; route=$true },
        @{ id='electric-bike.moving'; kind='ElectricBike'; route=$true }
    )
    foreach ($required in $requiredActors) {
        $actor = @($probe.actorInventory | Where-Object { $_.id -eq $required.id }) | Select-Object -First 1
        if (-not $actor -or $actor.actorKind -ne $required.kind -or [bool]$actor.hasRoute -ne $required.route) {
            throw "Actor fixture is missing the expected route/parked state: $($required.id)."
        }
    }
    foreach ($actorId in @('pedestrian.walking','car.moving','bike.moving','electric-bike.moving')) {
        $motion = @($probe.routedActorMotion | Where-Object { $_.actorId -eq $actorId }) | Select-Object -First 1
        if (-not $motion -or [double]$motion.distanceMoved -le 0.05) { throw "Routed actor movement was not verified: $actorId." }
    }

    $spawn = $probe.spawnRecovery
    if ($spawn.actorId -ne 'car.moving' -or [int]$spawn.collisionDelta -lt 1 -or [int]$spawn.recoveryDelta -ne 1 -or
        [int]$spawn.triggerDelta -lt 1 -or $spawn.checkpointId -ne 'spawn' -or -not $spawn.safeSpawnFallback -or
        [Math]::Abs([double]$spawn.restoredPosition.x) -gt 0.08 -or [Math]::Abs([double]$spawn.restoredPosition.y) -gt 0.08 -or
        -not $spawn.memoryRetained) { throw 'No-explicit-checkpoint vehicle recovery evidence is incomplete.' }

    $pause = $probe.pauseFreeze
    if (-not $pause.menuPaused -or -not $pause.frozen -or $pause.triggerCountBefore -ne $pause.triggerCountAfter) {
        throw 'Pause did not freeze feedback time and trigger count.'
    }
    if (-not $probe.pulseFadedOut -or [double]$probe.pulseVariation -lt 0.025 -or @($probe.pulseSamples).Count -lt 5) {
        throw 'Halo samples do not show a changing pulse and full fade-out.'
    }
    foreach ($sample in $probe.pulseSamples) {
        if ([double]$sample.actualAge + 0.000001 -lt [double]$sample.requestedAge -or $sample.triggerCount -lt 1) { throw 'A halo pulse sample is missing its real feedback state.' }
    }

    $matrixPath = [string]$probe.collisionMatrixJsonPath
    if (-not $matrixPath -or -not (Test-Path -LiteralPath $matrixPath -PathType Leaf)) { throw 'The independent collision matrix JSON is missing.' }
    $matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json
    if ($matrix.status -ne 'PASS') { throw 'The independent collision matrix did not report PASS.' }
    $requiredCases = @(
        @{ id='person-standing'; kind='Pedestrian'; object='pedestrian.standing'; moving=$false; recovery=0; blocking=$true },
        @{ id='person-walking'; kind='Pedestrian'; object='pedestrian.walking'; moving=$true; recovery=0; blocking=$false },
        @{ id='car-parked'; kind='Car'; object='car.parked'; moving=$false; recovery=0; blocking=$true },
        @{ id='bicycle-parked'; kind='Bicycle'; object='bicycle.parked'; moving=$false; recovery=0; blocking=$true },
        @{ id='electric-bike-parked'; kind='ElectricBike'; object='electric-bike.parked'; moving=$false; recovery=0; blocking=$true },
        @{ id='car-moving'; kind='Car'; object='car.moving'; moving=$true; recovery=1; blocking=$false },
        @{ id='bicycle-moving'; kind='Bicycle'; object='bicycle.moving'; moving=$true; recovery=1; blocking=$false },
        @{ id='electric-bike-moving'; kind='ElectricBike'; object='electric-bike.moving'; moving=$true; recovery=1; blocking=$false },
        @{ id='wall'; kind='Wall'; object='matrix.wall'; moving=$false; recovery=0; blocking=$true },
        @{ id='building'; kind='Building'; object='matrix.building'; moving=$false; recovery=0; blocking=$true },
        @{ id='barrier'; kind='Barrier'; object='matrix.barrier'; moving=$false; recovery=0; blocking=$true },
        @{ id='vehicle-red-light-stop'; kind='Car'; object='car.red-light'; moving=$false; recovery=0; blocking=$true },
        @{ id='vehicle-route-end'; kind='Car'; object='car.route-end'; moving=$false; recovery=0; blocking=$true }
    )
    if (@($matrix.cases).Count -ne $requiredCases.Count) { throw 'Collision matrix case count does not cover the required actor and structure set.' }
    $expectedAges = @(0,0.02,0.04,0.06,0.08,0.10,0.14,0.18,0.30,0.50,0.74)
    foreach ($required in $requiredCases) {
        $case = @($matrix.cases | Where-Object { $_.id -eq $required.id }) | Select-Object -First 1
        if (-not $case -or $case.kind -ne $required.kind -or $case.expectedObjectId -ne $required.object -or
            [bool]$case.moving -ne $required.moving -or [int]$case.contactCount -ne 1 -or
            [int]$case.recoveryDelta -ne $required.recovery -or [int]$case.triggerCount -ne 1 -or
            -not $case.glowObserved -or -not $case.couragePreserved -or
            [Math]::Abs([double]$case.courageBefore - [double]$case.courageAfter) -gt 0.001 -or
            -not $case.movingClassificationVerified -or -not $case.fadeOutVerified -or -not $case.passed) {
            throw "Collision case failed real-contact/recovery/courage/feedback assertions: $($required.id)."
        }
        if ([bool]$case.blockingExpected -ne $required.blocking -or ($required.blocking -and -not $case.blocked)) {
            throw "Collision case did not verify the expected physical blocking: $($required.id)."
        }
        if ($required.recovery -eq 1 -and ($case.checkpointIdAfterContact -ne 'spawn' -or
            -not $case.recoveryPositionVerified -or [Math]::Abs([double]$case.positions.playerAfterContact.x) -gt 0.02 -or
            [Math]::Abs([double]$case.positions.playerAfterContact.y) -gt 0.02)) {
            throw "Dangerous vehicle did not recover to the recorded spawn checkpoint: $($required.id)."
        }
        if ($case.contactObjectId -ne $required.object -or -not $case.beforeScreenshot -or
            -not $case.contactScreenshot -or -not $case.fadeScreenshot) {
            throw "Collision case is missing its actual contact or before/contact/fade screenshots: $($required.id)."
        }
        $frames = @($case.frames)
        if ($frames.Count -ne ($expectedAges.Count + 1) -or $frames[0].phase -ne 'before' -or
            $frames[0].screenshot -ne $case.beforeScreenshot) { throw "Collision frame sequence is incomplete: $($required.id)." }
        for ($i=0; $i -lt $expectedAges.Count; $i++) {
            $frame = $frames[$i + 1]
            if ([Math]::Abs([double]$frame.requestedAge - [double]$expectedAges[$i]) -gt 0.00001 -or
                [double]$frame.actualAge + 0.000001 -lt [double]$expectedAges[$i] -or
                [int]$frame.triggerCount -ne 1 -or [string]::IsNullOrWhiteSpace([string]$frame.screenshot) -or
                -not (Test-Path -LiteralPath ([string]$frame.screenshot) -PathType Leaf)) {
                throw "Collision feedback age sample or screenshot is missing: $($required.id), age $($expectedAges[$i])."
            }
        }
        if ($frames[1].phase -ne 'contact' -or [double]$frames[1].alpha -le 0 -or
            [double]$frames[$frames.Count - 1].alpha -gt 0.0001) { throw "Collision flash does not show a contact pulse followed by fade-out: $($required.id)." }
        if ($required.id -eq 'vehicle-red-light-stop' -and
            ($case.signalPhaseAtContactSetup -ne 'ns' -or [double]$case.stoppedForSeconds -lt 0.1)) {
            throw 'Red-light vehicle was not held at its stop line in the red phase before the real contact.'
        }
        if ($required.id -eq 'vehicle-route-end' -and [double]$case.stoppedForSeconds -lt 0.1) {
            throw 'Route-end vehicle was not held stationary before the real contact.'
        }
    }

    $load = $probe.saveReload
    if (-not $load.loadedRunning -or $load.saveStatus -ne 'Success' -or
        $load.savedCollisionCount -ne $load.loadedCollisionCount -or $load.savedCheckpointId -ne $load.loadedCheckpointId -or
        -not $load.feedbackCleared) { throw 'Save/reload evidence did not preserve progress and clear the transient halo.' }

    $wall = $probe.wallFirstHit
    $held = $probe.wallHeldContact
    $separated = $probe.wallAfterSeparation
    if ($wall.objectId -ne 'wall.north' -or $wall.collisionDelta -ne 1 -or $wall.triggerDelta -ne 1 -or $wall.recoveryDelta -ne 0 -or
        $held.collisionDelta -ne 1 -or $held.triggerDelta -ne 1 -or $held.heldCollisionDelta -ne 0 -or $held.heldTriggerDelta -ne 0 -or
        -not $separated.separatedBeforeRetest -or $separated.collisionDelta -ne 1 -or $separated.triggerDelta -ne 1 -or
        $separated.recoveryDelta -ne 0) { throw 'Wall collision de-duplication/separation evidence is incomplete.' }

    $checkpoint = $probe.checkpointRecovery
    if ($checkpoint.actorId -ne 'bike.moving' -or $checkpoint.checkpointId -ne 'checkpoint.north' -or
        $checkpoint.collisionDelta -lt 1 -or $checkpoint.recoveryDelta -ne 1 -or $checkpoint.triggerDelta -lt 1 -or
        [Math]::Abs([double]$checkpoint.restoredPosition.x) -gt 0.08 -or [Math]::Abs([double]$checkpoint.restoredPosition.y - 8) -gt 0.08 -or
        -not $checkpoint.memoryRetained -or -not $checkpoint.memoryMarkedStale) { throw 'Nearest-checkpoint recovery did not preserve stale memory and restore position.' }

    $red = $probe.redLight
    if ($red.phase -ne 'ew' -or $red.signalObjectId -ne 'street.signal.ns' -or $red.signalIsGreen -or
        -not $red.freshSignalObserved -or [Math]::Abs([double]$red.trafficStoppedPositionAfter.x - [double]$red.stopLineX) -gt 0.03 -or
        [Math]::Abs([double]$red.trafficStoppedPositionAfter.x - [double]$red.trafficStoppedPositionBefore.x) -gt 0.01 -or
        [double]$red.stableForSeconds -lt 0.5) { throw 'Red-light stop and fresh player signal observation evidence is incomplete.' }

    $images = @()
    $matrixScreenshots = @()
    foreach ($case in $matrix.cases) { foreach ($frame in $case.frames) { $matrixScreenshots += [string]$frame.screenshot } }
    if (@($probe.screenshots).Count -lt 18 -or $matrixScreenshots.Count -lt ($requiredCases.Count * ($expectedAges.Count + 1))) { throw 'Collision probe screenshot sequence is too short.' }
    foreach ($path in @($probe.screenshots) + $matrixScreenshots) {
        if (-not $path -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Player screenshot is missing: $path" }
        $bytes = [IO.File]::ReadAllBytes($path)
        if ($bytes.Length -lt 24 -or [Convert]::ToHexString($bytes[0..7]) -ne '89504E470D0A1A0A') { throw "Screenshot is not a complete PNG: $path" }
        $bitmap = [Drawing.Bitmap]::new($path)
        try {
            if ($bitmap.Width -ne $expectedWidth -or $bitmap.Height -ne $expectedHeight) { throw "Screenshot dimensions are wrong: $path" }
            $nonBlack = 0
            for ($py=0; $py -lt $bitmap.Height; $py+=8) {
                for ($px=0; $px -lt $bitmap.Width; $px+=8) {
                    $pixel = $bitmap.GetPixel($px,$py)
                    if ($pixel.R -gt 0 -or $pixel.G -gt 0 -or $pixel.B -gt 0) { $nonBlack++ }
                }
            }
            if ($nonBlack -eq 0) { throw "Screenshot is blank black: $path" }
            $images += [ordered]@{ path=$path; width=$bitmap.Width; height=$bitmap.Height; nonBlackSamples=$nonBlack; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        } finally { $bitmap.Dispose() }
    }

    $log = Join-Path $runDirectory 'player.log'
    $logText = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
    if ($logText -match '(?im)shader .* is not supported|Shader error in |NullReferenceException|MissingReferenceException|Street collision probe failed:|Exception:') {
        throw 'Player log contains a shader or runtime error.'
    }
    return @{ screenshots=$images; log=$log; screenshotCount=$images.Count }
}

try {
    foreach ($resolution in $resolutions) {
        $runDirectory = Join-Path $evidence ($resolution.label + '-' + $resolution.width + 'x' + $resolution.height)
        $null = New-Item -ItemType Directory -Path $runDirectory -Force
        $playerEvidence = Join-Path $runDirectory 'player-evidence'
        $isolatedSaveRoot = Join-Path $runDirectory 'isolated-saves'
        $null = New-Item -ItemType Directory -Path $playerEvidence -Force
        $log = Join-Path $runDirectory 'player.log'
        $info = [System.Diagnostics.ProcessStartInfo]::new()
        $info.FileName = $exe
        $info.WorkingDirectory = Split-Path -Parent $exe
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $false
        $info.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Normal
        $info.Environment['TINGLU_STREET_SAVE_ROOT'] = $isolatedSaveRoot
        foreach ($argument in @(
            '-popupwindow','-screen-width',"$($resolution.width)",'-screen-height',"$($resolution.height)",
            '-logFile',$log,'-tinglu-collision-probe',$playerEvidence,'-tinglu-street-save-root',$isolatedSaveRoot
        )) { $info.ArgumentList.Add($argument) }

        $process = [System.Diagnostics.Process]::Start($info)
        try {
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
                $process.Kill($true)
                $process.WaitForExit()
                throw "Visible Player timed out at $($resolution.width)x$($resolution.height)."
            }
            $exitCode = $process.ExitCode
        } finally { $process.Dispose() }
        if ($exitCode -ne 0) { throw "Player exited with code $exitCode at $($resolution.width)x$($resolution.height)." }
        if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $exeHash) { throw 'Player executable changed during evidence capture.' }

        $probePath = Join-Path $playerEvidence 'street-collision-probe-results.json'
        if (-not (Test-Path -LiteralPath $probePath -PathType Leaf)) { throw "Player collision JSON is missing: $probePath" }
        $probe = Get-Content -LiteralPath $probePath -Raw | ConvertFrom-Json
        $imageEvidence = Assert-CollisionProbe $probe $resolution.width $resolution.height $runDirectory
        $result.runs += [ordered]@{
            label = $resolution.label
            width = $resolution.width
            height = $resolution.height
            runDirectory = $runDirectory
            isolatedSaveRoot = $isolatedSaveRoot
            log = $imageEvidence.log
            probe = $probe
            screenshots = $imageEvidence.screenshots
            screenshotCount = $imageEvidence.screenshotCount
        }
    }
    $result.status = 'PASS'
    $result.twoOrThreeResolutionEvidence = $result.runs.Count
    Write-Output "PASS Street collision Player probe. Evidence: $evidence"
} catch {
    $result.status = 'FAIL'
    $result.reason = $_.ToString()
    throw
} finally {
    $result | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $evidence 'results.json') -Encoding utf8
}
