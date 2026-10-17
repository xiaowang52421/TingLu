[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$assetRoot=Join-Path $taskRoot 'Assets'
foreach($item in Get-ChildItem -LiteralPath $assetRoot -Recurse){
    if($item.Extension -eq '.meta' -or (Test-Path -LiteralPath ($item.FullName+'.meta'))){continue}
    $lines=@('fileFormatVersion: 2',('guid: '+[guid]::NewGuid().ToString('N')))
    if($item.PSIsContainer){$lines+='folderAsset: yes'}
    $lines | Set-Content -LiteralPath ($item.FullName+'.meta') -Encoding utf8
}
