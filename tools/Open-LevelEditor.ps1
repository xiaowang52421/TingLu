[CmdletBinding()]
param([string]$UnityPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'UnityEditorDiscovery.ps1')
if (-not $UnityPath) { $UnityPath = Find-TingLuUnityEditor -ProjectRoot $projectRoot }
if (-not $UnityPath) { throw '没有找到本工程所需的 Unity 2022.3.62f1c1。请先用 Unity Hub 安装对应版本，再双击此入口。' }
$version = Get-TingLuProjectUnityVersion $projectRoot
if (-not (Test-TingLuUnityEditorVersionPath $UnityPath $version)) { throw "请选择工程所需的 Unity $version 编辑器。" }
foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue)) {
    $match = [regex]::Match($process.CommandLine, '(?i)-projectPath\s+(?:"([^"]+)"|(\S+))')
    if (-not $match.Success) { continue }
    $openPath = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
    if ([IO.Path]::GetFullPath($openPath).TrimEnd('\','/') -ieq $projectRoot.TrimEnd('\','/')) {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show('工程已经打开。请在 Unity 顶部选择 TingLu → 关卡编辑器，进入新版街区工作台。','听路关卡编辑器') | Out-Null
        return
    }
}
$arguments = '-projectPath "' + $projectRoot + '" -executeMethod TingLu.Levels.Editor.LevelEditorV2Window.ShowWindow'
Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden | Out-Null
