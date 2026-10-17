$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $taskRoot 'tools/UnityEditorDiscovery.ps1')
. (Join-Path $taskRoot 'tools/PlayerEvidenceRules.ps1')

function Assert-Portability([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
$sandbox=Join-Path ([IO.Path]::GetTempPath()) ('tinglu-unity-discovery-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $sandbox -Force
try {
    $project=Join-Path $sandbox 'project'
    $null=New-Item -ItemType Directory -Path (Join-Path $project 'ProjectSettings') -Force
    Set-Content -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Value "m_EditorVersion: 2022.3.62f1c1`n" -Encoding utf8
    $editor=Join-Path $sandbox 'Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe'
    $hubLauncher=Join-Path $sandbox 'UnityHub/Unity.exe'
    $null=New-Item -ItemType Directory -Path (Split-Path -Parent $editor) -Force
    $null=New-Item -ItemType Directory -Path (Split-Path -Parent $hubLauncher) -Force
    Set-Content -LiteralPath $editor -Value 'test editor executable'
    Set-Content -LiteralPath $hubLauncher -Value 'test hub launcher'
    # Stub only the PE metadata reader; these text fixtures test discovery, not real binaries.
    function Get-TingLuUnityEditorProductVersion([string]$Path) {
        if ($Path -eq $editor) { return '2022.3.62f1_b0109b07edb8' }
        if ($Path -eq $wrongVersion) { return '2021.3.0f1_test' }
        return $null
    }
    Assert-Portability ((Get-TingLuProjectUnityVersion $project) -eq '2022.3.62f1c1') 'Project version was not read.'
    Assert-Portability (-not (Test-TingLuUnityEditorPath $hubLauncher)) 'Unity Hub Unity.exe launcher was accepted as the Editor.'
    Assert-Portability (Test-TingLuUnityEditorPath $editor) 'Editor/Unity.exe was rejected.'
    Assert-Portability (Test-TingLuUnityEditorVersionPath $editor '2022.3.62f1c1') 'Project-matching Editor version was rejected.'
    $hubList=Join-Path $sandbox 'editors.json'
    @{ editors=@(@{version='2022.3.62f1';location=(Split-Path -Parent (Split-Path -Parent $editor))}) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $hubList
    $hubPaths=Get-TingLuHubEditorPaths @($hubList)
    Assert-Portability ($hubPaths.Count -eq 1) 'Unity Hub editors.json location was not found.'
    $found=Find-TingLuUnityEditor -ProjectRoot $project -RunningEditorPaths @($editor) -HubFiles @($hubList)
    Assert-Portability ($found -eq (Resolve-Path -LiteralPath $editor).Path) 'Version-matched editor discovery failed.'
    $wrongVersion=Join-Path $sandbox 'Unity/Hub/Editor/2021.3.0f1/Editor/Unity.exe'
    $null=New-Item -ItemType Directory -Path (Split-Path -Parent $wrongVersion) -Force
    Set-Content -LiteralPath $wrongVersion -Value 'wrong version editor'
    Assert-Portability (-not (Test-TingLuUnityEditorPath $wrongVersion) -or (Find-TingLuUnityEditor -ProjectRoot $project -RunningEditorPaths @($wrongVersion) -HubFiles @()) -ne (Resolve-Path -LiteralPath $wrongVersion).Path) 'A different editor version was selected.'
    Assert-Portability (-not (Test-TingLuUnityEditorVersionPath $wrongVersion '2022.3.62f1c1')) 'A different explicit editor version was accepted.'
    Assert-Portability (-not (Get-TingLuSpeechStartEvidence 'TingLu speech diagnostic queued').Verified) 'Non-start diagnostic was mistaken for speech success.'
    $speech=Get-TingLuSpeechStartEvidence 'TingLu speech started; backend=Windows SAPI; frames=19200'
    Assert-Portability ($speech.Verified -and $speech.Count -eq 1 -and $speech.Backends[0] -eq 'Windows SAPI') 'Successful started-speech diagnostic was not verified.'
    Assert-Portability ((Get-TingLuVisualLayoutStatus $false) -eq 'NOT_VERIFIED_HIDDEN_WINDOW' -and (Get-TingLuVisualLayoutStatus $true) -eq 'NOT_REVIEWED') 'Hidden or visible Player layout was overstated.'
    $unityTool=Get-Content -LiteralPath (Join-Path $taskRoot 'tools/Verify-Unity.ps1') -Raw
    $playerTool=Get-Content -LiteralPath (Join-Path $taskRoot 'tools/Verify-Player.ps1') -Raw
    $playerRules=Get-Content -LiteralPath (Join-Path $taskRoot 'tools/PlayerEvidenceRules.ps1') -Raw
    Assert-Portability ($unityTool -match 'Find-TingLuUnityEditor' -and $unityTool -match 'Test-TingLuUnityEditorVersionPath') 'Verify-Unity is not using shared project-version discovery/validation.'
    Assert-Portability ($playerTool -match 'TimeoutSeconds = 180' -and $playerTool -match 'speechVerified=\$false' -and $playerRules -match 'NOT_VERIFIED_HIDDEN_WINDOW') 'Verify-Player defaults or evidence labels are missing.'
    Assert-Portability ($unityTool -notmatch '[A-Z]:\\Program Files' -and $playerTool -notmatch '[A-Z]:\\Users\\') 'A machine-specific absolute path was added to a verification tool.'
    'PASS project_version_and_editor_path_validation'
    'PASS unityhub_editors_json_discovery'
    'PASS version_matched_candidate_selection'
    'PASS started_success_speech_evidence_only'
    'PASS hidden_window_layout_remains_unverified'
    'PASS verification_tools_use_portable_project_paths'
} finally {
    $sandboxFull=[IO.Path]::GetFullPath($sandbox)
    $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if(-not $sandboxFull.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $sandboxFull) -notlike 'tinglu-unity-discovery-*'){throw 'Refusing to remove a portability test directory outside the temporary workspace.'}
    if(Test-Path -LiteralPath $sandboxFull){Remove-Item -LiteralPath $sandboxFull -Recurse -Force}
}
