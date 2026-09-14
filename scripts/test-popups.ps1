[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$title = 'Codex 任务监视器测试'
$projectRoot = Split-Path -Parent $PSScriptRoot
$bridgePath = Join-Path $projectRoot 'artifacts\hooks\bridge\CodexTaskMonitor.HookBridge.exe'
$sessionId = 'popup-test-' + [DateTimeOffset]::Now.ToUnixTimeSeconds()
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Send-Dialog {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('information', 'warning', 'error', 'confirmation', 'input')]
        [string]$Type,

        [Parameter(Mandatory)]
        [string]$Heading,

        [Parameter(Mandatory)]
        [string]$Message,

        [string]$DefaultValue = ''
    )

    $output = Send-Hook -EventName 'DialogRequest' -Fields @{
        dialog_type = $Type
        title = $Heading
        message = $Message
        default_value = $DefaultValue
    }

    if ([string]::IsNullOrWhiteSpace($output)) {
        throw '没有收到自定义弹窗响应。请确认监视器正在运行。'
    }

    return $output | ConvertFrom-Json
}

function Send-Hook {
    param(
        [Parameter(Mandatory)]
        [string]$EventName,

        [hashtable]$Fields = @{}
    )

    $message = [ordered]@{
        session_id = $sessionId
        cwd = $projectRoot
        hook_event_name = $EventName
        model = 'popup-test'
        permission_mode = 'default'
    }

    foreach ($entry in $Fields.GetEnumerator()) {
        $message[$entry.Key] = $entry.Value
    }

    $json = $message | ConvertTo-Json -Depth 8 -Compress
    $bytes = $utf8.GetBytes($json)
    $processInfo = New-Object System.Diagnostics.ProcessStartInfo
    $processInfo.FileName = $bridgePath
    $processInfo.UseShellExecute = $false
    $processInfo.CreateNoWindow = $true
    $processInfo.RedirectStandardInput = $true
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::Start($processInfo)
    try {
        $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
        $process.StandardInput.BaseStream.Flush()
        $process.StandardInput.Close()
        $output = $process.StandardOutput.ReadToEnd()
        $errorOutput = $process.StandardError.ReadToEnd()

        if (-not $process.WaitForExit(610000)) {
            $process.Kill()
            throw "$EventName 测试超时。"
        }

        if ($process.ExitCode -ne 0) {
            throw "$EventName 测试失败：$errorOutput"
        }

        return $output.Trim()
    }
    finally {
        $process.Dispose()
    }
}

try {
    if (-not (Test-Path -LiteralPath $bridgePath -PathType Leaf)) {
        throw "未找到 HookBridge：$bridgePath"
    }

    [void](Send-Dialog -Type information -Heading $title -Message '这是自定义信息弹窗测试。')
    [void](Send-Dialog -Type warning -Heading $title -Message '这是自定义警告弹窗测试，不代表真实警告。')
    [void](Send-Dialog -Type error -Heading $title -Message '这是自定义错误弹窗测试，不代表程序发生错误。')

    $questionResult = Send-Dialog -Type confirmation -Heading '问题确认测试' -Message '是否继续执行 Hook 和权限弹窗测试？'
    if ($questionResult.decision -ne 'yes') {
        [void](Send-Dialog -Type information -Heading $title -Message '测试已取消。')
        exit 0
    }

    $inputResult = Send-Dialog -Type input -Heading '文本输入测试' `
        -Message '请输入一段中文，用于验证自定义输入弹窗。' `
        -DefaultValue '中文输入正常'
    $answer = $inputResult.message

    [void](Send-Hook -EventName 'SessionStart' -Fields @{ source = 'startup' })
    Start-Sleep -Milliseconds 900
    [void](Send-Hook -EventName 'UserPromptSubmit' -Fields @{ prompt = "弹窗测试：$answer"; turn_id = 'popup-turn' })
    Start-Sleep -Milliseconds 900
    [void](Send-Hook -EventName 'PreToolUse' -Fields @{
        turn_id = 'popup-turn'
        tool_name = 'Bash'
        tool_input = @{ command = 'Write-Output popup-test' }
    })
    Start-Sleep -Milliseconds 900

    $permissionOutput = Send-Hook -EventName 'PermissionRequest' -Fields @{
        turn_id = 'popup-turn'
        tool_name = 'Bash'
        tool_input = @{
            description = '测试权限确认弹窗'
            command = 'Write-Output permission-popup-test'
        }
    }

    if ([string]::IsNullOrWhiteSpace($permissionOutput)) {
        throw '没有收到权限审批结果。请确认监视器正在运行。'
    }

    Start-Sleep -Milliseconds 900
    [void](Send-Hook -EventName 'PostToolUse' -Fields @{
        turn_id = 'popup-turn'
        tool_name = 'Bash'
        tool_input = @{ command = 'Write-Output popup-test' }
        tool_response = @{ success = $true }
    })
    Start-Sleep -Milliseconds 900
    [void](Send-Hook -EventName 'PreCompact' -Fields @{ trigger = 'manual' })
    Start-Sleep -Milliseconds 900
    [void](Send-Hook -EventName 'PostCompact' -Fields @{ trigger = 'manual' })
    Start-Sleep -Milliseconds 900
    [void](Send-Hook -EventName 'SubagentStart' -Fields @{
        turn_id = 'popup-turn'
        agent_id = 'popup-agent'
        agent_type = 'test'
    })
    Start-Sleep -Milliseconds 900
    [void](Send-Hook -EventName 'SubagentStop' -Fields @{
        turn_id = 'popup-turn'
        agent_id = 'popup-agent'
        agent_type = 'test'
    })
    Start-Sleep -Milliseconds 900
    $ordinaryStopOutput = Send-Hook -EventName 'Stop' -Fields @{
        turn_id = 'popup-turn'
        stop_hook_active = $false
        last_assistant_message = '普通回答已经完成。'
    }
    if (-not [string]::IsNullOrWhiteSpace($ordinaryStopOutput)) {
        throw '普通 Stop 不应返回继续响应。'
    }

    Start-Sleep -Milliseconds 900
    $stopOutput = Send-Hook -EventName 'Stop' -Fields @{
        turn_id = 'popup-turn-question'
        stop_hook_active = $false
        last_assistant_message = '这是等待输入测试，请填写下一步要求？'
    }
    Start-Sleep -Milliseconds 900
    [void](Send-Hook -EventName 'Interrupt' -Fields @{ turn_id = 'popup-turn' })
    Start-Sleep -Milliseconds 900
    [void](Send-Hook -EventName 'SessionEnd' -Fields @{ reason = 'other' })

    $decision = if ($permissionOutput -match '"behavior":"allow"') { '允许' } else { '拒绝' }
    $stopDecision = if ($stopOutput -match '"decision":"block"') { '已提交继续内容' } else { '稍后处理' }
    [void](Send-Dialog -Type information -Heading '测试完成' `
        -Message "权限选择：$decision`n等待输入：$stopDecision`n测试会话：$sessionId")
}
catch {
    Write-Error "测试失败：$($_.Exception.Message)"
    exit 1
}
