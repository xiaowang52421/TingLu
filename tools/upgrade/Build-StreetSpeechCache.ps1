[CmdletBinding()]
param(
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}

function Get-StreetSpeechCorpus {
    $texts = [System.Collections.Generic.List[string]]::new()

    # Fixed focus labels used by AccessibleMenu.
    @(
        '开始新游戏', '继续上次', '继续上次，现在还没有存档', '设置', '重听目标', '退出游戏',
        '接着走', '从路口重来', '回到首页', '再走一次', '保存并返回'
    ) | ForEach-Object { $texts.Add($_) }

    @('显示方式：标准', '显示方式：隐藏界面', '显示方式：纯音频') | ForEach-Object { $texts.Add($_) }
    @('声音：耳机', '声音：扬声器') | ForEach-Object { $texts.Add($_) }
    for ($percent = 0; $percent -le 100; $percent += 10) { $texts.Add("音量：$percent%") }
    @('字幕：开', '字幕：关', '震动：开', '震动：关', '颜色：标准', '颜色：色觉辅助') |
        ForEach-Object { $texts.Add($_) }
    for ($percent = 75; $percent -le 200; $percent += 5) { $texts.Add("文字大小：$percent%") }
    for ($percent = 0; $percent -le 200; $percent += 10) { $texts.Add("声纹强度：$percent%") }

    # Fixed lines emitted by StreetSession. Free-form/dynamic text still uses the runtime fallback.
    @(
        '去街对面的巷口。WASD走路，移动鼠标转身，右键敲杖。路上的声音一直都能听见。',
        '先沿西侧盲道走到斑马线前。听着提示音过街，再往东北小巷走，到巷口就到了。',
        '这里站稳了。',
        '撞上车了，回到刚才站稳的地方。',
        '缓一缓，回到刚才站稳的地方。',
        '到巷口了。这段路走完了。',
        '已经走到街对面了。',
        '路线说明暂不可用。',
        '出了点问题，请先回到首页，再试一次。'
    ) | ForEach-Object { $texts.Add($_) }

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $unique = [System.Collections.Generic.List[string]]::new()
    foreach ($text in $texts) {
        if ([string]::IsNullOrWhiteSpace($text)) { throw 'Speech corpus contains an empty line.' }
        if ($text.Contains("`r") -or $text.Contains("`n")) { throw 'Speech corpus lines must not contain line breaks.' }
        if ($seen.Add($text)) { $unique.Add($text) }
    }
    return ,$unique.ToArray()
}

function Get-StreetSpeechKey([string]$Text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-StreetSpeechGuid([string]$RelativePath) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $seed = [System.Text.Encoding]::UTF8.GetBytes("tinglu-street-speech-cache-v1:$RelativePath")
        return ([System.BitConverter]::ToString($sha.ComputeHash($seed))).Replace('-', '').ToLowerInvariant().Substring(0, 32)
    }
    finally { $sha.Dispose() }
}

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Assert-PathWithin([string]$Path, [string]$Root, [string]$Label) {
    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $prefix = $fullRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label path escaped its owned root: $fullPath"
    }
}

function Assert-NoReparseTree([string]$Path, [string]$Label) {
    $rootItem = Get-Item -LiteralPath $Path -Force
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label must not be a reparse point: $Path"
    }
    if (-not $rootItem.PSIsContainer) { return }
    $pending = [System.Collections.Generic.Stack[System.IO.DirectoryInfo]]::new()
    $pending.Push([System.IO.DirectoryInfo]$rootItem)
    while ($pending.Count -gt 0) {
        foreach ($item in $pending.Pop().GetFileSystemInfos()) {
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Label contains a reparse point; refusing recursive operation: $($item.FullName)"
            }
            if (($item.Attributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
                $pending.Push([System.IO.DirectoryInfo]$item)
            }
        }
    }
}

function Write-AudioMeta([string]$Path, [string]$RelativePath) {
    $guid = Get-StreetSpeechGuid $RelativePath
    $meta = @"
fileFormatVersion: 2
guid: $guid
AudioImporter:
  externalObjects: {}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 0
    quality: 1
    conversionMode: 0
  platformSettingOverrides: {}
  forceToMono: 0
  normalize: 1
  preloadAudioData: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"@
    Write-Utf8NoBom $Path ($meta.TrimStart("`r", "`n"))
}

function Write-FolderMeta([string]$Path, [string]$RelativePath) {
    $guid = Get-StreetSpeechGuid $RelativePath
    $meta = @"
fileFormatVersion: 2
guid: $guid
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@
    Write-Utf8NoBom $Path ($meta.TrimStart("`r", "`n"))
}

function Test-Pcm16Wave([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 44 -or [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'RIFF' -or
        [System.Text.Encoding]::ASCII.GetString($bytes, 8, 4) -ne 'WAVE') { return $false }
    $format = 0; $channels = 0; $rate = 0; $bits = 0; $dataLength = 0
    for ($offset = 12; $offset + 8 -le $bytes.Length;) {
        $tag = [System.Text.Encoding]::ASCII.GetString($bytes, $offset, 4)
        $length = [int][System.BitConverter]::ToUInt32($bytes, $offset + 4)
        $begin = $offset + 8
        if ($length -lt 0 -or $begin + $length -gt $bytes.Length) { return $false }
        if ($tag -eq 'fmt ' -and $length -ge 16) {
            $format = [System.BitConverter]::ToUInt16($bytes, $begin)
            $channels = [System.BitConverter]::ToUInt16($bytes, $begin + 2)
            $rate = [System.BitConverter]::ToUInt32($bytes, $begin + 4)
            $bits = [System.BitConverter]::ToUInt16($bytes, $begin + 14)
        }
        elseif ($tag -eq 'data') { $dataLength = $length }
        $offset = $begin + $length + ($length % 2)
    }
    return ($format -eq 1 -and $channels -eq 1 -and $rate -eq 22050 -and $bits -eq 16 -and $dataLength -gt 0 -and ($dataLength % 2) -eq 0)
}

function Write-ManifestMeta([string]$Path, [string]$RelativePath) {
    $guid = Get-StreetSpeechGuid $RelativePath
    $meta = @"
fileFormatVersion: 2
guid: $guid
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@
    Write-Utf8NoBom $Path ($meta.TrimStart("`r", "`n"))
}

$projectRootFull = [System.IO.Path]::GetFullPath($ProjectRoot)
$audioAssets = Join-Path $projectRootFull 'Assets/TingLu/AudioAssets'
$resourcesRoot = Join-Path $audioAssets 'Resources'
$destination = Join-Path $resourcesRoot 'TingLuSpeech'
$destinationMeta = "$destination.meta"
$corpus = Get-StreetSpeechCorpus
$stageRoot = Join-Path $projectRootFull (Join-Path 'Temp' ('StreetSpeechCache-' + [guid]::NewGuid().ToString('N')))
$tempRoot = Join-Path $projectRootFull 'Temp'
$stagedLibrary = Join-Path $stageRoot 'TingLuSpeech'
$committed = $false
$createdResourcesRoot = $false
$newInstallMoved = $false
$oldDirectoryBackedUp = $false
$oldMetaBackedUp = $false
$backupDirectory = $null
$backupMeta = $null

if (-not (Test-Path -LiteralPath $audioAssets -PathType Container)) { throw "Expected owned AudioAssets directory is missing: $audioAssets" }
Assert-NoReparseTree $audioAssets 'AudioAssets root'
if (Test-Path -LiteralPath $resourcesRoot) { Assert-NoReparseTree $resourcesRoot 'Resources root' }
Assert-NoReparseTree $tempRoot 'Project staging root'
Assert-PathWithin $stageRoot $tempRoot 'Temporary staging directory'
Assert-PathWithin $destination (Join-Path $audioAssets 'Resources') 'Speech resource directory'
if (Test-Path -LiteralPath $destination -PathType Container) {
    Assert-NoReparseTree $destination 'Existing speech cache'
    $existingManifest = Join-Path $destination 'manifest.json'
    if (-not (Test-Path -LiteralPath $existingManifest -PathType Leaf) -or
        -not ((Get-Content -LiteralPath $existingManifest -Raw) -match '"cache"\s*:\s*"TingLu street speech"')) {
        throw "Refusing to replace an unmanaged Resources directory: $destination"
    }
}

try {
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    Assert-NoReparseTree $stageRoot 'Speech cache staging root'
    $entryMap = [System.Collections.Generic.SortedDictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($text in $corpus) {
        $key = Get-StreetSpeechKey $text
        $entryMap.Add($key, [ordered]@{ key = $key; text = $text })
    }
    $entries = @($entryMap.Values)

    Add-Type -AssemblyName System.Speech
    $probe = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $candidates = @($probe.GetInstalledVoices() | Where-Object {
            $_.Enabled -and $_.VoiceInfo.Culture.Name.StartsWith('zh', [System.StringComparison]::OrdinalIgnoreCase)
        } | Sort-Object { $_.VoiceInfo.Culture.Name }, { $_.VoiceInfo.Name })
    }
    finally { $probe.Dispose() }
    if ($candidates.Count -eq 0) { throw 'No enabled Chinese System.Speech voice is installed.' }

    $format = [System.Speech.AudioFormat.SpeechAudioFormatInfo]::new(
        22050,
        [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
        [System.Speech.AudioFormat.AudioChannel]::Mono)
    $selected = $null
    $candidateErrors = [System.Collections.Generic.List[string]]::new()
    foreach ($candidate in $candidates) {
        $candidateRoot = Join-Path $stageRoot ('candidate-' + (Get-StreetSpeechGuid $candidate.VoiceInfo.Name))
        $candidateLibrary = Join-Path $candidateRoot 'TingLuSpeech'
        New-Item -ItemType Directory -Path $candidateLibrary -Force | Out-Null
        $candidateEntries = @($entries | ForEach-Object { [ordered]@{ key = $_.key; text = $_.text } })
        $synth = $null
        try {
            $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
            $synth.Rate = 0
            $synth.Volume = 100
            $synth.SelectVoice($candidate.VoiceInfo.Name)
            foreach ($entry in $candidateEntries) {
                $wavName = $entry.key + '.wav'
                $wavPath = Join-Path $candidateLibrary $wavName
                try {
                    $synth.SetOutputToWaveFile($wavPath, $format)
                    $synth.Speak($entry.text)
                }
                finally { try { $synth.SetOutputToNull() } catch { } }
                if (-not (Test-Path -LiteralPath $wavPath -PathType Leaf) -or -not (Test-Pcm16Wave $wavPath)) {
                    throw "Voice '$($candidate.VoiceInfo.Name)' produced invalid PCM WAV for key $($entry.key)."
                }
        Write-AudioMeta "$wavPath.meta" "Assets/TingLu/AudioAssets/Resources/TingLuSpeech/$wavName"
                $entry['file'] = $wavName
                $entry['bytes'] = (Get-Item -LiteralPath $wavPath).Length
                $entry['sha256'] = (Get-FileHash -LiteralPath $wavPath -Algorithm SHA256).Hash.ToLowerInvariant()
            }

            $manifest = [ordered]@{
                schema = 1
                cache = 'TingLu street speech'
                resourceRoot = 'TingLuSpeech/'
                key = 'lowercase SHA-256 of UTF-8 text'
                provider = 'System.Speech'
                voice = $candidate.VoiceInfo.Name
                culture = $candidate.VoiceInfo.Culture.Name
                format = 'PCM 16-bit mono, 22050 Hz'
                entries = $candidateEntries
            }
            $json = ConvertTo-Json -InputObject $manifest -Depth 6 -Compress
            $manifestPath = Join-Path $candidateLibrary 'manifest.json'
            Write-Utf8NoBom $manifestPath ($json + "`n")
            Write-ManifestMeta "$manifestPath.meta" 'Assets/TingLu/AudioAssets/Resources/TingLuSpeech/manifest.json'
            $selected = [ordered]@{ root = $candidateRoot; library = $candidateLibrary; voice = $candidate.VoiceInfo.Name; culture = $candidate.VoiceInfo.Culture.Name }
            break
        }
        catch {
            $candidateErrors.Add($candidate.VoiceInfo.Name + ': ' + $_.Exception.Message)
            if ($synth -ne $null) { try { $synth.SetOutputToNull() } catch { }; $synth.Dispose(); $synth = $null }
            Assert-PathWithin $candidateRoot $stageRoot 'Failed voice staging directory'
            Assert-NoReparseTree $candidateRoot 'Failed voice staging directory'
            Remove-Item -LiteralPath $candidateRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        finally { if ($synth -ne $null) { $synth.Dispose() } }
    }
    if ($selected -eq $null) { throw ('No enabled Chinese voice produced the complete cache: ' + ($candidateErrors -join ' | ')) }

    Assert-PathWithin $selected.library $stageRoot 'Selected voice library staging directory'
    Assert-PathWithin $stagedLibrary $stageRoot 'Prepared speech library'
    Assert-NoReparseTree $selected.library 'Selected voice library staging directory'
    Move-Item -LiteralPath $selected.library -Destination $stagedLibrary
    if (Test-Path -LiteralPath $resourcesRoot -PathType Container) {
        # Keep the existing Resources folder and its unrelated assets intact.
    }
    else {
        New-Item -ItemType Directory -Path $resourcesRoot -Force | Out-Null
        $createdResourcesRoot = $true
        Write-FolderMeta "$resourcesRoot.meta" 'Assets/TingLu/AudioAssets/Resources'
    }

    if (Test-Path -LiteralPath $destination -PathType Container) {
        $backupDirectory = Join-Path $audioAssets ('.TingLuSpeech.backup.' + [guid]::NewGuid().ToString('N'))
        Assert-PathWithin $destination (Join-Path $audioAssets 'Resources') 'Existing speech resource directory'
        Assert-PathWithin $backupDirectory $audioAssets 'Speech resource backup'
        Move-Item -LiteralPath $destination -Destination $backupDirectory
        $oldDirectoryBackedUp = $true
        if (Test-Path -LiteralPath $destinationMeta -PathType Leaf) {
            $backupMeta = Join-Path $audioAssets ('.TingLuSpeech.meta.backup.' + [guid]::NewGuid().ToString('N'))
            Assert-PathWithin $backupMeta $audioAssets 'Speech resource meta backup'
            Move-Item -LiteralPath $destinationMeta -Destination $backupMeta
            $oldMetaBackedUp = $true
        }
    }
    Assert-PathWithin $stagedLibrary $stageRoot 'Prepared speech library install source'
    Assert-PathWithin $destination (Join-Path $audioAssets 'Resources') 'Speech library install destination'
    Move-Item -LiteralPath $stagedLibrary -Destination $destination
    $newInstallMoved = $true
    Write-FolderMeta $destinationMeta 'Assets/TingLu/AudioAssets/Resources/TingLuSpeech'
    $committed = $true
    if ($backupDirectory -and (Test-Path -LiteralPath $backupDirectory)) {
        Assert-PathWithin $backupDirectory $audioAssets 'Committed speech resource backup'
        Assert-NoReparseTree $backupDirectory 'Committed speech resource backup'
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $backupDirectory) { Write-Warning "Old cache backup retained outside Resources: $backupDirectory" }
    }
    if ($backupMeta -and (Test-Path -LiteralPath $backupMeta)) { Remove-Item -LiteralPath $backupMeta -Force -ErrorAction SilentlyContinue }
}
catch {
    if (-not $committed) {
        if ($newInstallMoved -and (Test-Path -LiteralPath $destination -PathType Container)) {
            $failedInstall = Join-Path $stageRoot 'failed-install'
            Assert-PathWithin $destination (Join-Path $audioAssets 'Resources') 'Failed speech install'
            Assert-PathWithin $failedInstall $stageRoot 'Failed speech install staging directory'
            Assert-NoReparseTree $destination 'Failed speech install'
            Move-Item -LiteralPath $destination -Destination $failedInstall -ErrorAction SilentlyContinue
            if (Test-Path -LiteralPath $destinationMeta -PathType Leaf) {
                Remove-Item -LiteralPath $destinationMeta -Force -ErrorAction SilentlyContinue
            }
        }
        if ($oldDirectoryBackedUp -and $backupDirectory -and (Test-Path -LiteralPath $backupDirectory -PathType Container)) {
            Assert-PathWithin $backupDirectory $audioAssets 'Speech resource rollback source'
            Assert-PathWithin $destination (Join-Path $audioAssets 'Resources') 'Speech resource rollback destination'
            Move-Item -LiteralPath $backupDirectory -Destination $destination -ErrorAction SilentlyContinue
        }
        if ($oldMetaBackedUp -and $backupMeta -and (Test-Path -LiteralPath $backupMeta -PathType Leaf)) {
            Move-Item -LiteralPath $backupMeta -Destination $destinationMeta -ErrorAction SilentlyContinue
        }
        if ($createdResourcesRoot -and (Test-Path -LiteralPath $resourcesRoot -PathType Container) -and
            -not (Get-ChildItem -LiteralPath $resourcesRoot -Force | Where-Object { $_.Name -ne 'TingLuSpeech.meta' })) {
            Remove-Item -LiteralPath $resourcesRoot -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath "$resourcesRoot.meta" -Force -ErrorAction SilentlyContinue
        }
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $stageRoot -PathType Container) {
        Assert-PathWithin $stageRoot $tempRoot 'Temporary cache cleanup'
        Assert-NoReparseTree $stageRoot 'Temporary cache cleanup'
        Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$manifestJson = [System.IO.File]::ReadAllText((Join-Path $destination 'manifest.json'), [System.Text.UTF8Encoding]::new($false))
$manifestResult = $manifestJson | ConvertFrom-Json
Write-Output ("Speech cache ready: {0} unique WAV entries; voice={1} ({2}); path={3}" -f
    $manifestResult.entries.Count, $manifestResult.voice, $manifestResult.culture,
    'Assets/TingLu/AudioAssets/Resources/TingLuSpeech')
