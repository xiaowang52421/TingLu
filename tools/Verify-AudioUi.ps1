param([string]$ReportPath = 'docs/qa/audio-ui-results.json')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot

# Contracts/Config are the shared public surface; UI needs SpeechPriorityQueue from Audio.
$sources = @(Get-ChildItem 'Assets/TingLu/Contracts/*.cs','Assets/TingLu/Config/Shared/*.cs' | ForEach-Object { $_.FullName })
$sources += (Join-Path $taskRoot 'Assets/TingLu/Audio/SpeechPriorityQueue.cs')
$sources += (Join-Path $taskRoot 'Assets/TingLu/Audio/AcousticModel.cs')
$sources += (Join-Path $taskRoot 'Assets/TingLu/UI/AccessibleMenu.cs')
$sources += (Join-Path $taskRoot 'Tests/Accessibility/MenuChecks.cs')
$sources += (Join-Path $taskRoot 'Tests/Accessibility/AudioChecks.cs')

function Assert-SourcePattern([string]$Path, [string]$Pattern, [string]$Message) {
    $content = Get-Content -LiteralPath $Path -Raw
    if ($content -notmatch $Pattern) { throw "Source audit failed: $Message" }
}

function Invoke-SpeechWaveProbe([string]$AudioSourcePath) {
    $sourceText = Get-Content -LiteralPath $AudioSourcePath -Raw
    $classes = [regex]::Match($sourceText, '(?ms)^    internal sealed class SpeechWave\b.*?^    internal static class SystemSpeechClip\b.*?^    }')
    if (-not $classes.Success) { throw 'Could not extract the production speech renderer for the no-Unity probe.' }
    $probeCode = "using System; using System.Collections; using System.IO; using System.Reflection; using System.Runtime.InteropServices; using System.Diagnostics; namespace TingLuSpeechProbe {`n$($classes.Value)`n}"
    $probeTypes = Add-Type -TypeDefinition $probeCode -PassThru -ErrorAction Stop
    $renderer = $probeTypes | Where-Object Name -eq 'SystemSpeechClip'
    $arguments = @('你好，前方有公交站。', $null, $null)
    $renderMethod = $renderer.GetMethod('TryRender')
    $rendered = $renderMethod.Invoke($null, $arguments)
    if (-not $rendered) {
        return [ordered]@{ status = 'NOT_AVAILABLE'; detail = $arguments[2] }
    }

    $wave = $arguments[1]
    $pcm = [byte[]]::new($wave.Samples.Length * 2)
    for ($i = 0; $i -lt $wave.Samples.Length; $i++) {
        $sample = [int16][Math]::Max(-32768, [Math]::Min(32767, [Math]::Round($wave.Samples[$i] * 32767)))
        [BitConverter]::GetBytes($sample).CopyTo($pcm, $i * 2)
    }
    $path = Join-Path ([IO.Path]::GetTempPath()) ('tinglu_speech_probe_' + [guid]::NewGuid().ToString('N') + '.wav')
    try {
        $stream = [IO.File]::Create($path)
        $writer = [IO.BinaryWriter]::new($stream)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $writer.Write([int](36 + $pcm.Length))
        $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $writer.Write([int]16)
        $writer.Write([int16]1); $writer.Write([int16]$wave.Channels); $writer.Write([int]$wave.SampleRate)
        $writer.Write([int]($wave.SampleRate * $wave.Channels * 2)); $writer.Write([int16]($wave.Channels * 2)); $writer.Write([int16]16)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('data')); $writer.Write([int]$pcm.Length); $writer.Write($pcm)
        $writer.Close(); $stream.Dispose()
        $bytes = [IO.File]::ReadAllBytes($path)
        if ([Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'RIFF' -or
            [Text.Encoding]::ASCII.GetString($bytes, 8, 4) -ne 'WAVE' -or $bytes.Length -ne 44 + $pcm.Length) {
            throw 'Speech probe did not produce a valid PCM WAV.'
        }
        return [ordered]@{ status = 'WAV_SYNTHESIS_ONLY'; detail = "valid PCM WAV; rate=$($wave.SampleRate); channels=$($wave.Channels); frames=$($wave.FrameCount); bytes=$($bytes.Length)" }
    } finally {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}

try {
    Add-Type -Path $sources -ErrorAction Stop
    $passed = @()
    $passed += [MenuChecks]::Run()
    $passed += [AudioChecks]::Run()
    $backend = Join-Path $taskRoot 'Assets/TingLu/Audio/UnityAudioPlayback.cs'
    Assert-SourcePattern $backend 'public void SetMasterVolume\(float value\)[\s\S]*?EffectiveVolume\(voices\[i\]\)' 'master volume recomputes active voices'
    Assert-SourcePattern $backend 'public void SetPaused\(bool value\)[\s\S]*?Priority == FeedbackPriority\.Interface\) continue[\s\S]*?voice\.Source\.UnPause\(\)' 'pause freezes world voices and resumes active voices while interface speech stays live'
    Assert-SourcePattern $backend 'public void Stop\(string eventId\)[\s\S]*?speechQueue\.RemoveEvent\(eventId\)[\s\S]*?RemoveRendered\(work => work\.Request\.EventId == eventId\)[\s\S]*?cancelledSpeech\.Add\(eventId\)[\s\S]*?voice\.Source\.Stop\(\)' 'stop covers queued, rendering, rendered, and playing requests'
    Assert-SourcePattern $backend 'renderLifecycle == lifecycle && request\.RecoveryGeneration == generation' 'late speech render results are rejected after reset'
    Assert-SourcePattern $backend 'public void SetVoiceprintIntensity\(float value\)[\s\S]*?EffectiveVolume\(voices\[i\]\)' 'voiceprint intensity recomputes active voices'
    Assert-SourcePattern $backend 'private void StartNextSpeech\(\)[\s\S]*?Do not start the next utterance until the[\s\S]*?active\.Source\.isPlaying' 'speech queue serializes playback so utterances do not overlap'
    Assert-SourcePattern $backend 'bool speechActive = false;[\s\S]*?IsSpeech && voices\[i\]\.Source != null && voices\[i\]\.Source\.isPlaying' 'all active synthesized speech ducks ambience'
    Assert-SourcePattern $backend 'speechRenderLifecycles\.Contains\(renderLifecycle\) \|\| speechRenderLifecycles\.Count >= 2' 'speech permits one bounded stale recovery render beside current speech'
    Assert-SourcePattern $backend 'paused && request\.Priority != FeedbackPriority\.Interface[\s\S]*?paused_before_playback' 'world speech completing after pause is stopped before playback'
    Assert-SourcePattern $backend 'StopQueuedWorldSpeech\(\)[\s\S]*?paused_world_speech_discarded[\s\S]*?paused_during_world_speech_render' 'pause discards queued and in-flight world speech but preserves interface speech'
    Assert-SourcePattern $backend 'new GameObject\("Audio voice "[\s\S]*?child\.transform\.SetParent\(transform, false\)' 'each audio voice owns an independent child transform'
    Assert-SourcePattern $backend 'voice\.PendingStart = true;[\s\S]*?else StartVoice\(voice\)' 'paused world audio waits for actual resume before Started receipt'
    Assert-SourcePattern $backend 'Type\.GetTypeFromProgID\("SAPI\.SpVoice"\)[\s\S]*?Language=804[\s\S]*?no Chinese Windows SAPI voice installed[\s\S]*?FinalReleaseComObject' 'Windows SAPI fallback selects a Chinese voice and releases COM objects'
    Assert-SourcePattern $backend 'TryRenderWindowsPowerShell\(text,[\s\S]*?EncodedCommand[\s\S]*?WaitForExit\(20000\)' 'Windows PowerShell Chinese speech fallback uses an encoded command and timeout'
    Assert-SourcePattern $backend 'Debug\.Log\("TingLu speech started; backend="[\s\S]*?frames=' 'speech start diagnostic reports provider and frame count without text'
    Assert-SourcePattern $backend 'GetEnvironmentVariable\("TINGLU_AUDIO_DIAGNOSTICS"\)' 'speech diagnostics are opt-in through an environment variable'
    $passed += 'audio_backend_source_lifecycle_audit'
    $speechProbe = Invoke-SpeechWaveProbe $backend
    if ($speechProbe.status -eq 'WAV_SYNTHESIS_ONLY') { $passed += 'speech_renderer_no_unity_wav_probe' }
    else { Write-Output "Speech provider unavailable: $($speechProbe.detail)" }
    $report = [ordered]@{
        status = 'PASS'
        scope = 'Pure C# accessibility menu state and shared acoustic/speech data, plus Unity audio backend source lifecycle audit'
        runtime = $PSVersionTable.PSVersion.ToString()
        passed = @($passed)
        unity = 'NOT_RUN'
        audioDevice = 'NOT_RUN'
        speechProvider = $speechProbe.status
        speechEvidence = $speechProbe.detail
    }
    $parent = Split-Path -Parent $ReportPath
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    $passed | ForEach-Object { Write-Output "PASS $_" }
    Write-Output 'Unity: NOT_RUN'
    Write-Output "Audio device: NOT_RUN; speech provider: $($speechProbe.status)"
} catch {
    Write-Error $_
    exit 1
}
