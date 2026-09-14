# Codex 任务监视器

Windows .NET 8 WPF 任务监视器，通过 Codex Hooks + 本机 Named Pipe 监听会话生命周期。程序显示 12 类 Hook 的独立中文状态，将支持的权限请求转到自定义审批窗，并可在一轮任务结束时从右下角输入下一步要求。

## 当前能力

- 系统托盘单实例常驻：关闭主窗口后继续监听，重复启动只会拉起已有窗口。
- 会话列表显示：独立 Hook 状态、会话名称、项目、模型、权限模式、最近事件、更新时间。
- 会话名称来自首次 `UserPromptSubmit.prompt`，没有提示词时回退为短会话 ID。
- 项目名来自 `cwd` 最后一段，例如 `G:\work\hr-moka` 显示为 `hr-moka`。
- 模型列来自 Hook 公共字段 `model`，优先使用 `SessionStart` / `UserPromptSubmit` 的模型值。
- HookBridge 显式使用 UTF-8 读写标准输入、标准输出和本地管道，中文会话名称及参数不会按 Windows 本地代码页错误解码。
- 权限审批窗支持允许或拒绝，并显示工具名、会话、项目和完整参数。
- `Stop` 事件会检查 `last_assistant_message`；仅当最后一条助手消息明确要求回复、确认、选择或提供信息时，才弹出等待输入窗口。提交内容后，Codex 使用该内容继续当前会话，选择“稍后处理”或关闭窗口则保持停止。
- 权限、信息、警告、错误、确认和文本输入均使用本程序的深色自定义窗口，并定位在主屏工作区右下角。
- HookBridge 无法连接监视器或超时时不输出审批决策，Codex 会继续使用原生审批流程。
- 不使用第三方 NuGet 包，不自动修改 Codex 配置，不安装依赖，不启动服务。

## Hook 与状态

| Hook | 状态 |
| --- | --- |
| `SessionStart` | 会话已开始 |
| `UserPromptSubmit` | 正在处理提示 |
| `PreToolUse` | 准备调用工具 |
| `PermissionRequest` | 等待权限审批 |
| `PostToolUse` | 工具调用完成 |
| `PreCompact` | 正在压缩上下文 |
| `PostCompact` | 上下文压缩完成 |
| `SubagentStart` | 子代理已启动 |
| `SubagentStop` | 子代理已停止 |
| `Stop` | 等待输入 |
| `Interrupt` | 本轮已中断 |
| `SessionEnd` | 会话已结束 |

## 能拦截什么

根据 OpenAI 官方 Hooks 文档，`PermissionRequest` 会在 Codex 准备请求工具审批时触发，例如 Shell 提权、托管网络访问、`apply_patch` 和 MCP 工具审批。当前生成的 Hook 没有 matcher，因此会接收 Codex 通过该事件公开的全部请求。

可以接管：

- `Bash` / `exec_command` 权限提升。
- `apply_patch`、`Edit`、`Write` 审批。
- MCP 工具审批。
- Codex 通过 `PermissionRequest` 公开的托管网络访问审批。

不能接管：

- Browser use 的网站访问、浏览历史等授权。
- 托管网页搜索和部分专用工具路径的内置确认。
- Codex App 自身的登录、更新、设置和 Hooks 信任管理界面。

这些功能使用各自的客户端或服务端权限控制，不会向 HookBridge 发送 `PermissionRequest`，因此仍显示 Codex 原生弹窗。这不是监视器配置或 Hook 信任失败。

`Stop` 等待输入不是权限审批。由于 Hook 没有“正在提问”的专用字段，监视器会根据 `last_assistant_message` 的问号及“请回复、请确认、请选择、请提供”等文字判断是否需要输入。提交时，监视器返回 `decision: "block"` 和用户输入的 `reason`，Codex 随后自动创建继续提示并恢复当前会话。

参考：[OpenAI 官方 Hooks 文档](https://learn.chatgpt.com/zh-Hans/docs/hooks)、[OpenAI 权限文档](https://learn.chatgpt.com/zh-Hans/docs/permissions)。

## 环境要求

- Windows 10/11
- .NET 8 SDK：用于构建和发布 HookBridge
- .NET 8 Desktop Runtime：用于运行 WPF 程序
- Codex App、Codex CLI 或 VS Code Codex 扩展中至少一种能读取 `hooks.json`

如果 PowerShell 提示找不到 `dotnet`，请先安装 .NET 8 SDK，并重新打开终端。

## 快速安装

推荐直接运行项目根目录的一键安装工具：

```powershell
cd G:\codex-task-monitor
.\install.ps1
```

脚本会自动完成：

1. 构建 Release 程序。
2. 发布 HookBridge 并生成 `artifacts\hooks\hooks.json`。
3. 生成项目根目录快捷方式 `Codex任务监视器.lnk`。
4. 询问是否写入 Codex 用户级 Hooks 配置 `~\.codex\hooks.json`。

当脚本询问是否写入 Codex 配置时：

- 输入 `Y`：复制生成的 `hooks.json` 到 `~\.codex\hooks.json`。如果原文件存在，会先生成 `.bak` 备份。
- 输入其他内容：跳过配置写入，只保留本项目内生成的 Hooks 文件。

脚本不会自动启动监视器，也不会自动信任 Hooks。写入配置后，仍需要完全重启 Codex App 或 VS Code，并在 `/hooks` 中审查和信任 HookBridge。`Stop` Hook 的超时时间为 600 秒，用于等待右下角输入窗口。

## 1. 构建程序

在 PowerShell 中执行：

```powershell
cd G:\codex-task-monitor
dotnet build .\CodexTaskMonitor.sln --configuration Release
```

成功时应看到 `0 个错误`。解决方案内的 `NuGet.Config` 清空了远程包源，构建不会下载第三方依赖。

主程序路径：

```text
G:\codex-task-monitor\src\CodexTaskMonitor.App\bin\Release\net8.0-windows\CodexTaskMonitor.exe
```

## 2. 生成桌面式快捷入口

在项目根目录生成快速启动快捷方式：

```powershell
.\scripts\create-shortcut.ps1
```

生成文件：

```text
G:\codex-task-monitor\Codex任务监视器.lnk
```

以后双击这个快捷方式即可打开监视器。项目路径或构建配置变化后，请重新执行脚本。

## 3. 生成 Hooks 文件

执行：

```powershell
.\scripts\install-hooks.ps1
```

这个脚本只做两件事：

1. 发布 HookBridge 到 `G:\codex-task-monitor\artifacts\hooks\bridge`。
2. 生成 `G:\codex-task-monitor\artifacts\hooks\hooks.json`。

脚本不会写入 `~\.codex\config.toml`、`~\.codex\hooks.json`，也不会写入项目 `.codex` 目录。

生成后的桥接程序路径通常是：

```text
G:\codex-task-monitor\artifacts\hooks\bridge\CodexTaskMonitor.HookBridge.exe
```

## 4. 接入 Codex App

用户级接入方式：

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\.codex" | Out-Null
Copy-Item `
  'G:\codex-task-monitor\artifacts\hooks\hooks.json' `
  "$env:USERPROFILE\.codex\hooks.json" `
  -Force
```

然后完全退出并重新打开 Codex App。

打开 Hooks 管理界面：

```powershell
$codexCli = Get-ChildItem "$env:USERPROFILE\.vscode\extensions\openai.chatgpt-*\bin\windows-x86_64\codex.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

& $codexCli.FullName --no-alt-screen
```

在 CLI 中输入：

```text
/hooks
```

确认这些事件显示为 Installed 和 Active，并进入每个 Hook 审查，信任指向 `CodexTaskMonitor.HookBridge.exe` 的命令：

- `SessionStart`
- `UserPromptSubmit`
- `PreToolUse`
- `PermissionRequest`
- `PostToolUse`
- `PreCompact`
- `PostCompact`
- `SubagentStart`
- `SubagentStop`
- `Stop`
- `Interrupt`
- `SessionEnd`

Hook 文件内容变化后，信任哈希也会变化，需要重新进入 `/hooks` 审查并信任。

## 5. 接入 VS Code Codex

先确认 VS Code Codex 在 Windows 环境运行，而不是 WSL。

VS Code 设置 JSON 中应包含：

```json
"chatgpt.runCodexInWindowsSubsystemForLinux": false
```

打开方式：

1. VS Code 中按 `Ctrl+Shift+P`。
2. 输入并打开 `Preferences: Open User Settings (JSON)`。
3. 添加或确认上面的设置。
4. 重启 VS Code。

然后使用第 4 步同一份用户级 `~\.codex\hooks.json`。重启 VS Code 后，在 VS Code Codex 里发起新任务，监视器应收到 `SessionStart` 或 `UserPromptSubmit`。

## 6. 推荐启动顺序

1. 双击 `G:\codex-task-monitor\Codex任务监视器.lnk`。
2. 确认右上角状态显示“正在监听”。
3. 打开 Codex App 或 VS Code Codex。
4. 发起一个新任务。
5. 触发需要权限的操作时，在“Codex 权限审批”窗口选择允许或拒绝。
6. 当助手最后一条消息明确要求回复时，在“Codex 正在等待输入”窗口提交下一步要求，或选择“稍后处理”。普通回答结束只更新状态，不弹窗。

监视器必须先运行，才能接管权限弹窗和等待输入。若监视器未运行，Codex 会回到原生权限流程，`Stop` 也不会显示输入窗口。

## 7. 手动联通测试

项目提供了弹窗和生命周期联调脚本。先启动任务监视器，再执行：

```powershell
.\scripts\test-popups.ps1
```

脚本依次验证：

- 深色信息、警告、错误和确认窗口。
- 深色文本输入窗口及中文输入。
- 真实 `PermissionRequest` 允许或拒绝响应。
- 12 类 Hook 的状态变化。
- `Stop` 等待输入及继续响应。
- 普通回答结束不弹窗，明确提问时弹出输入窗。

执行期间需要逐个操作右下角窗口。测试结束后，列表会保留一个已结束的 `popup-test-*` 会话，可使用“清空已结束”删除。

## 8. 常见问题

### `dotnet` 不是内部或外部命令

说明 .NET SDK 没装好，或终端还没刷新 PATH。安装 .NET 8 SDK 后重新打开 PowerShell。

### `/hooks` 里显示 Installed 但监视器没反应

按顺序检查：

- 监视器是否已经启动，右上角是否显示“正在监听”。
- `/hooks` 中对应事件是否 Active。
- Hook 是否已经 Trusted。
- `hooks.json` 里的路径是否仍指向当前 `G:\codex-task-monitor`。
- VS Code 是否关闭了 WSL 模式。

### 权限弹窗还是 Codex 原生弹窗

通常是监视器未运行、HookBridge 无法连接 Named Pipe、Hook 未信任，或当前弹窗不是 `PermissionRequest` Hook 支持的工具审批。

### Browser use 或浏览历史确认没有被接管

这是预期行为。Browser use、浏览历史、托管网页搜索及部分专用工具使用独立权限控制，不经过本地工具 Hook 链路，监视器不会收到 `PermissionRequest`。目前没有官方支持的替换方式。

### 任务结束时没有弹出等待输入窗口

按顺序检查：

- 用户级 `hooks.json` 是否包含 `Stop`。
- `Stop` Hook 是否为 Active 和 Trusted。
- 是否已复制本项目最新生成的 `artifacts\hooks\hooks.json`。
- 修改 Hook 配置后是否完全重启了 Codex App 或 VS Code。
- 监视器是否在任务结束前已启动。

### 列表里没有项目名

项目名来自 Hook 输入的 `cwd`。如果 Codex 当前环境没有传入 `cwd`，监视器只能显示空值或已有值。

### 会话名称不是 Codex 左侧任务标题

Hooks 当前没有提供 Codex UI 里的任务标题。监视器用首次用户提示词生成本地会话名称。

## 本地管道协议

HookBridge 每次连接发送一行 UTF-8 JSON：

```json
{
  "messageId": "...",
  "sessionId": "thr_...",
  "eventName": "PermissionRequest",
  "cwd": "G:\\repo",
  "model": "...",
  "permissionMode": "default",
  "payload": {}
}
```

WPF 程序返回一行 JSON。权限审批示例：

```json
{ "success": true, "decision": "allow", "message": null }
```

等待输入提交示例：

```json
{ "success": true, "decision": "block", "message": "继续检查测试失败原因" }
```

HookBridge 会将它转换为 `Stop` Hook 输出：

```json
{ "decision": "block", "reason": "继续检查测试失败原因" }
```

管道名：

```text
CodexTaskMonitor.HookBridge.v1
```

管道使用 `CurrentUserOnly`，仅允许当前 Windows 用户连接。

## MVP 边界

- 会话状态仅保存在内存中，退出程序后清空。
- 暂不包含开机自启、安装包、日志持久化和审批规则记忆。
- `SessionEnd` 的触发时机由 Codex 生命周期决定，不等同于切换离开某个会话。
- 本程序只是把 Codex 的审批请求转到本地窗口，不替代 Codex 的沙箱和权限策略。
- 本程序无法替换未通过 Hooks 暴露的 Codex/ChatGPT 客户端授权窗口。
