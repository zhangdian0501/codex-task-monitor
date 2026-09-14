[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$targetPath = Join-Path $projectRoot "src\CodexTaskMonitor.App\bin\$Configuration\net8.0-windows\CodexTaskMonitor.exe"
$shortcutPath = Join-Path $projectRoot 'Codex任务监视器.lnk'

if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
    throw "未找到程序：$targetPath。请先执行 Release 构建。"
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $targetPath
$shortcut.WorkingDirectory = $projectRoot
$shortcut.Description = '打开 Codex 任务监视器'
$shortcut.IconLocation = "$targetPath,0"
$shortcut.Save()

Write-Host "快捷方式已生成：$shortcutPath"
