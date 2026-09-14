[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$bridgeProject = Join-Path $projectRoot 'src\CodexTaskMonitor.HookBridge\CodexTaskMonitor.HookBridge.csproj'
$dotnetPath = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw '未找到 dotnet。请先安装 .NET 8 SDK。'
    }

    $dotnetPath = $dotnetCommand.Source
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'artifacts\hooks'
}

$publishDirectory = Join-Path $OutputDirectory 'bridge'
$hooksPath = Join-Path $OutputDirectory 'hooks.json'

Write-Host '正在发布 HookBridge（不会修改 Codex 配置）...'
& $dotnetPath publish $bridgeProject --configuration $Configuration --output $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'HookBridge 发布失败。'
}

$bridgeExe = Join-Path $publishDirectory 'CodexTaskMonitor.HookBridge.exe'
$command = '& "' + $bridgeExe + '"'
$shortHook = @{
    type = 'command'
    command = $command
    commandWindows = $command
    timeout = 5
}
$permissionHook = @{
    type = 'command'
    command = $command
    commandWindows = $command
    timeout = 600
    statusMessage = '等待 Codex 任务监视器审批'
}
$stopHook = @{
    type = 'command'
    command = $command
    commandWindows = $command
    timeout = 600
    statusMessage = '等待 Codex 任务监视器输入'
}
$sessionEndHook = @{
    type = 'command'
    command = $command
    commandWindows = $command
    timeout = 3
}
$interruptHook = @{
    type = 'command'
    command = $command
    commandWindows = $command
    timeout = 3
}

$hooks = [ordered]@{
    description = 'Codex Task Monitor lifecycle bridge for Windows.'
    hooks = [ordered]@{
        SessionStart = @(@{ hooks = @($shortHook) })
        UserPromptSubmit = @(@{ hooks = @($shortHook) })
        PreToolUse = @(@{ hooks = @($shortHook) })
        PermissionRequest = @(@{ hooks = @($permissionHook) })
        PostToolUse = @(@{ hooks = @($shortHook) })
        PreCompact = @(@{ hooks = @($shortHook) })
        PostCompact = @(@{ hooks = @($shortHook) })
        SubagentStart = @(@{ hooks = @($shortHook) })
        SubagentStop = @(@{ hooks = @($shortHook) })
        Stop = @(@{ hooks = @($stopHook) })
        Interrupt = @(@{ hooks = @($interruptHook) })
        SessionEnd = @(@{ hooks = @($sessionEndHook) })
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$json = $hooks | ConvertTo-Json -Depth 8
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($hooksPath, $json, $utf8NoBom)

Write-Host ''
Write-Host 'HookBridge 和 hooks.json 已生成：'
Write-Host "  $hooksPath"
Write-Host ''
Write-Host '脚本没有修改 ~/.codex/config.toml 或 ~/.codex/hooks.json。'
Write-Host '请检查生成文件后，手动复制或合并到你信任的 Codex 配置层，并在 Codex 中使用 /hooks 审查和信任。'
