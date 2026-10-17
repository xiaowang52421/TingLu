param([string]$ReportPath='docs/qa/level-editor-results.json')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$sources=@(Get-ChildItem Assets/TingLu -Recurse -Filter '*.cs' -File | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw) -notmatch '\bUnityEngine\b|\bUnityEditor\b|\bNUnit\.Framework\b'
} | ForEach-Object {$_.FullName})
$sources+=Join-Path $taskRoot 'Tests/LevelEditor/LevelEditorChecks.cs'
$runRoot=Join-Path $taskRoot ('tmp/level-editor-runs/'+[guid]::NewGuid().ToString('N'))
try {
    $syntaxFiles=@(Get-ChildItem Assets/TingLu/Levels/Editor -Filter '*.cs')+(Get-Item Assets/TingLu/Bootstrap/Runtime/DemoEntry.cs)
    foreach($file in $syntaxFiles){$tree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText((Get-Content -LiteralPath $file.FullName -Raw));$errors=@($tree.GetDiagnostics()|Where-Object {$_.Severity.ToString() -eq 'Error'});if($errors.Count){throw ($errors -join "`n")}}
    Add-Type -Path $sources -ErrorAction Stop
    $passed=[LevelEditorChecks]::Run($runRoot)
    $report=[ordered]@{status='PASS';scope='Pure C# JSON storage/validation/copy and real DemoSession assembly with recording audio backend';runtime=$PSVersionTable.PSVersion.ToString();passed=@($passed);csharpSyntaxParse='PASS syntax only, not Unity compilation';evidenceDirectory=$runRoot;unityCompile='NOT_RUN';editorWindow='NOT_RUN';dragDrop='NOT_RUN';undoGUI='NOT_RUN';previewScene='NOT_RUN';audioDevice='NOT_RUN';humanAudioOnly='NOT_RUN'}
    $passed|ForEach-Object {Write-Output "PASS $_"}
} catch {
    $report=[ordered]@{status='FAIL';error=$_.ToString();unityCompile='NOT_RUN';editorWindow='NOT_RUN'}
    $report|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $ReportPath -Encoding utf8
    throw
}
$report|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $ReportPath -Encoding utf8
