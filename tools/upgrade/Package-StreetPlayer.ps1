#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SourceBuildDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DestinationDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:PathComparer = [StringComparer]::OrdinalIgnoreCase
$script:PathComparison = [StringComparison]::OrdinalIgnoreCase
$script:Separator = [IO.Path]::DirectorySeparatorChar

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $expanded = [Environment]::ExpandEnvironmentVariables($Path)
    $full = [IO.Path]::GetFullPath($expanded)
    $root = [IO.Path]::GetPathRoot($full)
    if ($full.Length -gt $root.Length) {
        $full = $full.TrimEnd([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
    }
    return $full
}

function Test-PathIsWithin {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $prefix = $Parent.TrimEnd([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)) + [IO.Path]::DirectorySeparatorChar
    return $Child.StartsWith($prefix, $script:PathComparison)
}

function Get-ExistingAncestors {
    param([Parameter(Mandatory = $true)][string]$Path)

    $chain = [Collections.Generic.List[string]]::new()
    $current = Get-NormalizedPath -Path $Path
    while ($null -ne $current) {
        if ([IO.Directory]::Exists($current) -or [IO.File]::Exists($current)) {
            $chain.Add($current)
        }

        $parent = [IO.Directory]::GetParent($current)
        if ($null -eq $parent) { break }
        if ($script:PathComparer.Equals($parent.FullName, $current)) { break }
        $current = $parent.FullName
    }
    $chain.Reverse()
    return $chain.ToArray()
}

function Assert-NoReparseInPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    foreach ($ancestor in (Get-ExistingAncestors -Path $Path)) {
        $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "拒绝经过重解析点（符号链接/联接）：$ancestor"
        }
    }
}

function Test-EntryExists {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([IO.Directory]::Exists($Path) -or [IO.File]::Exists($Path)) { return $true }

    $parent = [IO.Directory]::GetParent($Path)
    if ($null -eq $parent -or -not [IO.Directory]::Exists($parent.FullName)) { return $false }

    $leaf = [IO.Path]::GetFileName($Path)
    foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($parent.FullName)) {
        if ($script:PathComparer.Equals([IO.Path]::GetFileName($entry), $leaf)) { return $true }
    }
    return $false
}

function Get-RelativePathForManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $relative = [IO.Path]::GetRelativePath($Root, $Path)
    if ($relative -eq '.' -or [IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", $script:PathComparison)) {
        throw "扫描结果越出了目录边界：$Path"
    }
    return $relative.Replace([IO.Path]::DirectorySeparatorChar, '/')
}

function Get-DirectoryInventory {
    param([Parameter(Mandatory = $true)][string]$Root)

    $files = [Collections.Generic.List[object]]::new()
    $directories = [Collections.Generic.List[string]]::new()
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($Root)

    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
            $item = Get-Item -LiteralPath $entry -Force -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "输入目录含重解析点，已拒绝打包：$entry"
            }

            $relative = Get-RelativePathForManifest -Root $Root -Path $item.FullName
            if (($item.Attributes -band [IO.FileAttributes]::Directory) -ne 0) {
                $directories.Add($relative)
                $pending.Push($item.FullName)
            }
            elseif (($item.Attributes -band [IO.FileAttributes]::Device) -ne 0) {
                throw "输入目录含不支持的设备项：$entry"
            }
            else {
                $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                $files.Add([pscustomobject]@{
                    relativePath = $relative
                    length = [long]$item.Length
                    sha256 = $hash
                })
            }
        }
    }

    $fileArray = $files.ToArray()
    [Array]::Sort($fileArray, [Comparison[object]]{
        param($left, $right)
        return [StringComparer]::OrdinalIgnoreCase.Compare([string]$left.relativePath, [string]$right.relativePath)
    })
    $directoryArray = $directories.ToArray()
    [Array]::Sort($directoryArray, [StringComparer]::OrdinalIgnoreCase)

    return [pscustomobject]@{
        files = $fileArray
        directories = $directoryArray
    }
}

function Compare-BuildInventories {
    param(
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)][string]$ActualRoot
    )

    if ($Expected.files.Count -ne $Actual.files.Count) {
        throw "文件数量不一致：源目录 $($Expected.files.Count) 个，复制目录 $($Actual.files.Count) 个。"
    }
    if ($Expected.directories.Count -ne $Actual.directories.Count) {
        throw "目录数量不一致：源目录 $($Expected.directories.Count) 个，复制目录 $($Actual.directories.Count) 个。"
    }

    $actualFiles = [Collections.Generic.Dictionary[string, object]]::new($script:PathComparer)
    foreach ($file in $Actual.files) { $actualFiles.Add([string]$file.relativePath, $file) }
    foreach ($expectedFile in $Expected.files) {
        $relative = [string]$expectedFile.relativePath
        if (-not $actualFiles.ContainsKey($relative)) {
            throw "复制后缺少文件：$relative（目录：$ActualRoot）"
        }
        $actualFile = $actualFiles[$relative]
        if (-not [StringComparer]::Ordinal.Equals($relative, [string]$actualFile.relativePath) -or
            [long]$expectedFile.length -ne [long]$actualFile.length -or
            -not [StringComparer]::Ordinal.Equals([string]$expectedFile.sha256, [string]$actualFile.sha256)) {
            throw "SHA-256/长度/路径核对失败：$relative"
        }
    }

    $expectedDirectories = [Collections.Generic.HashSet[string]]::new($script:PathComparer)
    foreach ($directory in $Expected.directories) { [void]$expectedDirectories.Add([string]$directory) }
    foreach ($directory in $Actual.directories) {
        if (-not $expectedDirectories.Contains([string]$directory)) {
            throw "复制目录出现源目录中不存在的文件夹：$directory"
        }
        [void]$expectedDirectories.Remove([string]$directory)
    }
    if ($expectedDirectories.Count -gt 0) {
        throw "复制后缺少文件夹：$(@($expectedDirectories) -join ', ')"
    }
}

function Assert-CompleteWindowsPlayer {
    param([Parameter(Mandatory = $true)]$Inventory)

    $filesByName = [Collections.Generic.HashSet[string]]::new($script:PathComparer)
    foreach ($file in $Inventory.files) { [void]$filesByName.Add([string]$file.relativePath) }
    $directoriesByName = [Collections.Generic.HashSet[string]]::new($script:PathComparer)
    foreach ($directory in $Inventory.directories) { [void]$directoriesByName.Add([string]$directory) }

    foreach ($required in @('TingLu.exe', 'UnityPlayer.dll')) {
        if (-not $filesByName.Contains($required)) { throw "Windows Player 缺少必要文件：$required" }
    }
    if (-not $directoriesByName.Contains('TingLu_Data')) {
        throw 'Windows Player 缺少 TingLu_Data 目录。'
    }
    if (-not @($Inventory.files | Where-Object { ([string]$_.relativePath).StartsWith('TingLu_Data/', $script:PathComparison) }).Count) {
        throw 'TingLu_Data 目录为空，拒绝把不完整构建打包。'
    }
    if (-not $filesByName.Contains('TingLu_Data/globalgamemanagers')) {
        throw 'TingLu_Data 缺少 Unity Player 核心文件 globalgamemanagers。'
    }

    $optionalComponents = @(
        [pscustomobject]@{ path = 'UnityCrashHandler64.exe'; kind = 'file' },
        [pscustomobject]@{ path = 'MonoBleedingEdge'; kind = 'directory' },
        [pscustomobject]@{ path = 'GameAssembly.dll'; kind = 'file' },
        [pscustomobject]@{ path = 'TingLu_Data/il2cpp_data'; kind = 'directory' },
        [pscustomobject]@{ path = 'TingLu_Data/Managed'; kind = 'directory' }
    )
    $componentChecks = [Collections.Generic.List[object]]::new()
    $componentChecks.Add([pscustomobject]@{ path = 'TingLu.exe'; required = $true; status = 'present' })
    $componentChecks.Add([pscustomobject]@{ path = 'UnityPlayer.dll'; required = $true; status = 'present' })
    $componentChecks.Add([pscustomobject]@{ path = 'TingLu_Data'; required = $true; status = 'present' })
    foreach ($component in $optionalComponents) {
        $present = if ($component.kind -eq 'file') { $filesByName.Contains($component.path) } else { $directoriesByName.Contains($component.path) }
        if ($present -and $component.kind -eq 'directory') {
            $prefix = $component.path + '/'
            $hasContents = @($Inventory.files | Where-Object { ([string]$_.relativePath).StartsWith($prefix, $script:PathComparison) }).Count -gt 0
            if (-not $hasContents) { throw "构建包含空的 $($component.path) 目录。" }
        }
        $componentChecks.Add([pscustomobject]@{
            path = $component.path
            required = $false
            status = if ($present) { 'present' } else { 'not-in-this-build' }
        })
    }

    $hasMono = $directoriesByName.Contains('MonoBleedingEdge') -and
        @($Inventory.files | Where-Object { ([string]$_.relativePath).StartsWith('MonoBleedingEdge/', $script:PathComparison) }).Count -gt 0 -and
        $directoriesByName.Contains('TingLu_Data/Managed') -and
        @($Inventory.files | Where-Object { ([string]$_.relativePath).StartsWith('TingLu_Data/Managed/', $script:PathComparison) }).Count -gt 0
    $hasIl2Cpp = $filesByName.Contains('GameAssembly.dll') -and
        $directoriesByName.Contains('TingLu_Data/il2cpp_data') -and
        @($Inventory.files | Where-Object { ([string]$_.relativePath).StartsWith('TingLu_Data/il2cpp_data/', $script:PathComparison) }).Count -gt 0
    if (-not ($hasMono -or $hasIl2Cpp)) {
        throw 'Windows Player 缺少完整的 Mono 或 IL2CPP 运行时内容。'
    }
    $componentChecks.Add([pscustomobject]@{
        path = if ($hasMono) { 'MonoBleedingEdge + TingLu_Data/Managed' } else { 'GameAssembly.dll + TingLu_Data/il2cpp_data' }
        required = $true
        status = 'present'
    })

    return $componentChecks.ToArray()
}

function Ensure-DirectoryPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$OutputRoot
    )

    if (-not (Test-PathIsWithin -Parent $OutputRoot -Child $Path) -and -not $script:PathComparer.Equals($Path, $OutputRoot)) {
        throw "只允许在项目 output 内创建目录：$Path"
    }
    $relative = [IO.Path]::GetRelativePath($OutputRoot, $Path)
    $current = $OutputRoot
    if ($relative -eq '.') { return }

    foreach ($part in $relative.Split([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar), [StringSplitOptions]::RemoveEmptyEntries)) {
        if ($part -eq '.' -or $part -eq '..') { throw "路径包含越界片段：$Path" }
        $current = [IO.Path]::Combine($current, $part)
        if (Test-EntryExists -Path $current) {
            if (-not [IO.Directory]::Exists($current)) { throw "目标父路径不是目录：$current" }
            Assert-NoReparseInPath -Path $current
        }
        else {
            [void][IO.Directory]::CreateDirectory($current)
            Assert-NoReparseInPath -Path $current
        }
    }
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

$projectRoot = Get-NormalizedPath -Path (Join-Path $PSScriptRoot '../..')
$outputRoot = Get-NormalizedPath -Path (Join-Path $projectRoot 'output')
if (-not [IO.Directory]::Exists($outputRoot)) { throw "项目 output 目录不存在：$outputRoot" }

$sourceRoot = Get-NormalizedPath -Path $SourceBuildDirectory
$destinationRoot = Get-NormalizedPath -Path $DestinationDirectory
if (-not [IO.Directory]::Exists($sourceRoot)) { throw "构建源目录不存在或不是目录：$sourceRoot" }
if (-not (Test-PathIsWithin -Parent $outputRoot -Child $destinationRoot)) {
    throw "试玩包目标必须是项目 output 下的新目录：$outputRoot"
}
if ($script:PathComparer.Equals($sourceRoot, $destinationRoot) -or
    (Test-PathIsWithin -Parent $sourceRoot -Child $destinationRoot) -or
    (Test-PathIsWithin -Parent $destinationRoot -Child $sourceRoot)) {
    throw '构建源目录与试玩包目标目录不能相同或互相包含。'
}

Assert-NoReparseInPath -Path $sourceRoot
Assert-NoReparseInPath -Path $destinationRoot
Assert-NoReparseInPath -Path $outputRoot
if (Test-EntryExists -Path $destinationRoot) { throw "试玩包目标已存在，拒绝覆盖：$destinationRoot" }

$destinationParent = [IO.Directory]::GetParent($destinationRoot).FullName
Ensure-DirectoryPath -Path $destinationParent -OutputRoot $outputRoot
if (Test-EntryExists -Path $destinationRoot) { throw "试玩包目标已存在，拒绝覆盖：$destinationRoot" }

$sourceInventory = Get-DirectoryInventory -Root $sourceRoot
if ($sourceInventory.files.Count -eq 0) { throw '构建源目录没有文件。' }
foreach ($reservedName in @('开始体验.txt', 'package-manifest.json')) {
    if ($sourceInventory.files | Where-Object { [StringComparer]::OrdinalIgnoreCase.Equals([string]$_.relativePath, $reservedName) }) {
        throw "构建源目录已包含保留文件名 $reservedName，避免覆盖而停止。"
    }
}
$componentChecks = Assert-CompleteWindowsPlayer -Inventory $sourceInventory

$totalBytes = [long]0
foreach ($file in $sourceInventory.files) { $totalBytes += [long]$file.length }

$destinationLeaf = [IO.Path]::GetFileName($destinationRoot)
$stageRoot = Join-Path $destinationParent ('.' + $destinationLeaf + '.package-stage-' + [guid]::NewGuid().ToString('N'))
if (Test-EntryExists -Path $stageRoot) { throw "临时打包目录意外存在：$stageRoot" }
[void][IO.Directory]::CreateDirectory($stageRoot)
Assert-NoReparseInPath -Path $stageRoot

try {
    foreach ($relativeDirectory in $sourceInventory.directories) {
        $relativeNative = $relativeDirectory.Replace('/', [IO.Path]::DirectorySeparatorChar)
        $targetDirectory = [IO.Path]::Combine($stageRoot, $relativeNative)
        [void][IO.Directory]::CreateDirectory($targetDirectory)
    }
    foreach ($file in $sourceInventory.files) {
        $relativeNative = ([string]$file.relativePath).Replace('/', [IO.Path]::DirectorySeparatorChar)
        $sourceFile = [IO.Path]::Combine($sourceRoot, $relativeNative)
        $targetFile = [IO.Path]::Combine($stageRoot, $relativeNative)
        [IO.File]::Copy($sourceFile, $targetFile, $false)
    }

    $stageBuildInventory = Get-DirectoryInventory -Root $stageRoot
    Compare-BuildInventories -Expected $sourceInventory -Actual $stageBuildInventory -ActualRoot $stageRoot
    Assert-CompleteWindowsPlayer -Inventory $stageBuildInventory | Out-Null

    $sourceAfterCopy = Get-DirectoryInventory -Root $sourceRoot
    Compare-BuildInventories -Expected $sourceInventory -Actual $sourceAfterCopy -ActualRoot $sourceRoot

    $guide = @'
听路街区试玩｜开始体验

进入游戏后，使用 WASD 按画面方向移动。移动鼠标可以转身；游戏中鼠标指针会隐藏。
按一下鼠标右键敲杖，角色会探查前方附近的环境。聆听会一直进行，不需要按键。
还没有探明的地方会保持黑暗；敲杖可以帮助你了解周围环境。

按 Esc 打开或返回暂停菜单。菜单里用方向键选择，按 Enter 确认，按 Esc 或 Backspace 返回。
按 G 可以重新听取当前目标。

本试玩的进度保存在独立位置，不会覆盖旧版《听路》的存档。
部分环境声音仍为占位音效；声音方向、距离和真人听辨体验尚未完成完整验证。
'@
    $guidePath = Join-Path $stageRoot '开始体验.txt'
    Write-Utf8NoBom -Path $guidePath -Content $guide
    $guideHash = (Get-FileHash -LiteralPath $guidePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{
        schemaVersion = 1
        contractVersion = '1.0.0'
        packageName = 'TingLu Street Player'
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        sourceBuildDirectory = $sourceRoot
        destinationDirectory = $destinationRoot
        hashAlgorithm = 'SHA-256'
        buildFileCount = $sourceInventory.files.Count
        buildTotalBytes = $totalBytes
        sourceAndCopiedBuildFilesMatched = $true
        componentChecks = $componentChecks
        buildFiles = $sourceInventory.files
        packageGuide = [ordered]@{
            path = '开始体验.txt'
            length = [long](Get-Item -LiteralPath $guidePath -Force).Length
            sha256 = $guideHash
        }
        manifestSelfHash = 'omitted (self-referential)'
        limitations = @(
            '本清单记录目录复制与 SHA-256 比对，不代表 Unity 构建、运行画面或玩法验收。',
            '环境声音仍含占位内容，真人听辨体验尚未完成完整验证。'
        )
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 8
    $manifestPath = Join-Path $stageRoot 'package-manifest.json'
    Write-Utf8NoBom -Path $manifestPath -Content ($manifestJson + [Environment]::NewLine)

    $finalInventory = Get-DirectoryInventory -Root $stageRoot
    $buildFilesOnly = @($finalInventory.files | Where-Object {
        -not [StringComparer]::OrdinalIgnoreCase.Equals([string]$_.relativePath, '开始体验.txt') -and
        -not [StringComparer]::OrdinalIgnoreCase.Equals([string]$_.relativePath, 'package-manifest.json')
    })
    $finalBuildInventory = [pscustomobject]@{ files = $buildFilesOnly; directories = $finalInventory.directories }
    Compare-BuildInventories -Expected $sourceInventory -Actual $finalBuildInventory -ActualRoot $stageRoot

    if (Test-EntryExists -Path $destinationRoot) { throw "试玩包目标在复制期间已出现，拒绝覆盖：$destinationRoot" }
    Assert-NoReparseInPath -Path $destinationParent
    [IO.Directory]::Move($stageRoot, $destinationRoot)

    Write-Output "打包完成：$destinationRoot"
    Write-Output "构建文件：$($sourceInventory.files.Count) 个；合计 $totalBytes 字节。"
    Write-Output '所有构建文件已逐个完成 SHA-256 比对。'
    Write-Output "体验说明：$(Join-Path $destinationRoot '开始体验.txt')"
    Write-Output "文件清单：$(Join-Path $destinationRoot 'package-manifest.json')"
}
catch {
    throw "打包未完成。为避免删除或覆盖任何内容，临时目录已保留供检查：$stageRoot`n$($_.Exception.Message)"
}
