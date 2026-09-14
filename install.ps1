[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$solutionPath = Join-Path $projectRoot 'CodexTaskMonitor.sln'
$installHooksScript = Join-Path $projectRoot 'scripts\install-hooks.ps1'
$createShortcutScript = Join-Path $projectRoot 'scripts\create-shortcut.ps1'
$generatedHooksPath = Join-Path $projectRoot 'artifacts\hooks\hooks.json'
$dotnetPath = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw '未找到 dotnet。请先安装 .NET 8 SDK。'
    }

    $dotnetPath = $dotnetCommand.Source
}

Write-Host 'Codex 任务监视器自动安装'
Write-Host "项目目录：$projectRoot"
Write-Host ''

Write-Host '1/4 正在构建程序...'
& $dotnetPath build $solutionPath --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw '构建失败。'
}

Write-Host ''
Write-Host '2/4 正在生成 HookBridge 和 hooks.json...'
& $installHooksScript -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw 'Hooks 生成失败。'
}

Write-Host ''
Write-Host '3/4 正在生成根目录快捷方式...'
& $createShortcutScript -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw '快捷方式生成失败。'
}

Write-Host ''
Write-Host '4/4 Codex 用户级 Hooks 配置'
$userCodexDir = Join-Path $env:USERPROFILE '.codex'
$targetHooksPath = Join-Path $userCodexDir 'hooks.json'
Write-Host "生成的 hooks.json：$generatedHooksPath"
Write-Host "目标配置文件：$targetHooksPath"
Write-Host '如果选择写入，将覆盖目标 hooks.json；若目标文件已存在，会先创建 .bak 备份。'
$answer = Read-Host '是否写入 Codex 用户级 Hooks 配置？输入 Y 确认，其他内容跳过'

if ($answer -match '^(y|yes|Y|YES|是)$') {
    if (-not (Test-Path -LiteralPath $generatedHooksPath -PathType Leaf)) {
        throw "未找到生成的 hooks.json：$generatedHooksPath"
    }

    New-Item -ItemType Directory -Force -Path $userCodexDir | Out-Null

    if (Test-Path -LiteralPath $targetHooksPath -PathType Leaf) {
        $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $backupPath = "$targetHooksPath.bak.$timestamp"
        Copy-Item -LiteralPath $targetHooksPath -Destination $backupPath -Force
        Write-Host "已备份原配置：$backupPath"
    }

    Copy-Item -LiteralPath $generatedHooksPath -Destination $targetHooksPath -Force
    Write-Host "已写入 Codex 用户级 Hooks 配置：$targetHooksPath"
}
else {
    Write-Host '已跳过 Codex 用户级 Hooks 配置写入。'
}

Write-Host ''
Write-Host '安装流程完成。'
Write-Host '下一步：'
Write-Host '1. 双击项目根目录的 Codex任务监视器.lnk 启动监视器。'
Write-Host '2. 完全重启 Codex App 或 VS Code。'
Write-Host '3. 在 Codex CLI 输入 /hooks，审查并信任 HookBridge。'
