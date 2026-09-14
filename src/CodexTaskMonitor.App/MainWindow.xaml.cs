using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using Application = System.Windows.Application;
using CodexTaskMonitor.App.Models;
using CodexTaskMonitor.App.Services;

namespace CodexTaskMonitor.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly NamedPipeMonitorService _pipeService = new();
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly Dictionary<string, SessionItem> _sessionsById = new(StringComparer.Ordinal);
    private readonly List<Window> _dialogWindows = [];
    private readonly TrayIconService _trayIcon;
    private string _pipeStatus = "正在启动";
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _trayIcon = new TrayIconService(ShowFromTray, ExitApplication);
        _pipeService.MessageReceived = ReceiveMessageAsync;
        _pipeService.StatusChanged += status => Dispatcher.BeginInvoke(() => PipeStatus = status);
        _pipeService.Start();
    }

    public ObservableCollection<SessionItem> Sessions { get; } = [];

    public string PipeStatus
    {
        get => _pipeStatus;
        private set
        {
            if (_pipeStatus == value)
            {
                return;
            }

            _pipeStatus = value;
            OnPropertyChanged();
        }
    }

    public Visibility EmptyStateVisibility => Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task<PipeResponse> ReceiveMessageAsync(HookEnvelope message)
    {
        if (string.Equals(message.EventName, "DialogRequest", StringComparison.Ordinal))
        {
            return await ShowInteractiveDialogAsync(() => RequestPrompt(message));
        }

        if (string.Equals(message.EventName, "PermissionRequest", StringComparison.Ordinal))
        {
            return await ShowInteractiveDialogAsync(() => RequestApproval(message));
        }

        if (string.Equals(message.EventName, "Stop", StringComparison.Ordinal))
        {
            return await ShowInteractiveDialogAsync(() => RequestNextInput(message));
        }

        await Dispatcher.InvokeAsync(() => UpdateSession(message));
        return new PipeResponse();
    }

    private async Task<PipeResponse> ShowInteractiveDialogAsync(Func<PipeResponse> showDialog)
    {
        await _dialogGate.WaitAsync();
        try
        {
            return await Dispatcher.InvokeAsync(showDialog);
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    private PipeResponse RequestApproval(HookEnvelope message)
    {
        var session = UpdateSession(message);
        session.Status = "等待审批";

        var toolName = GetString(message.Payload, "tool_name") ?? "未知工具";
        var description = GetNestedString(message.Payload, "tool_input", "description");
        var details = GetToolDetails(message.Payload);
        var context = $"工具：{toolName}    会话：{session.SessionName}    项目：{session.ProjectName}";

        var dialog = new ApprovalWindow(description ?? $"请求使用 {toolName}", context, details);
        _dialogWindows.Add(dialog);
        try
        {
            if (IsVisible)
            {
                dialog.Owner = this;
            }

            dialog.ShowDialog();
        }
        finally
        {
            _dialogWindows.Remove(dialog);
        }

        session.UpdatedAt = DateTimeOffset.Now;

        if (dialog.IsApproved)
        {
            session.Status = "权限审批通过";
            session.LastEvent = "审批已允许";
            return new PipeResponse { Decision = "allow" };
        }

        session.Status = "权限审批拒绝";
        session.LastEvent = "审批已拒绝";
        return new PipeResponse { Decision = "deny", Message = "用户在 Codex 任务监视器中拒绝了该请求。" };
    }

    private PipeResponse RequestNextInput(HookEnvelope message)
    {
        var session = UpdateSession(message);
        if (!ShouldRequestInput(message.Payload))
        {
            session.Status = "本轮已完成";
            session.LastEvent = "本轮完成";
            return new PipeResponse();
        }

        session.Status = "等待输入";
        session.LastEvent = "等待用户输入";
        var assistantMessage = GetString(message.Payload, "last_assistant_message")?.Trim() ?? string.Empty;
        var prompt = new PromptWindow(
            "Codex 正在等待输入",
            $"会话：{session.SessionName}\n项目：{session.ProjectName}\n\n输入下一步要求，提交后将继续当前会话。",
            PromptKind.Input,
            contextText: assistantMessage);

        if (IsVisible)
        {
            prompt.Owner = this;
        }

        _dialogWindows.Add(prompt);
        try
        {
            prompt.ShowDialog();
        }
        finally
        {
            _dialogWindows.Remove(prompt);
        }
        session.UpdatedAt = DateTimeOffset.Now;

        if (!string.Equals(prompt.Decision, "submit", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(prompt.ResponseText))
        {
            session.Status = "等待输入";
            session.LastEvent = "等待用户输入";
            return new PipeResponse();
        }

        session.Status = "输入已提交";
        session.LastEvent = "继续请求已提交";
        return new PipeResponse { Decision = "block", Message = prompt.ResponseText };
    }

    private PipeResponse RequestPrompt(HookEnvelope message)
    {
        var kind = GetString(message.Payload, "dialog_type") switch
        {
            "warning" => PromptKind.Warning,
            "error" => PromptKind.Error,
            "confirmation" => PromptKind.Confirmation,
            "input" => PromptKind.Input,
            _ => PromptKind.Information
        };
        var heading = GetString(message.Payload, "title") ?? "Codex 任务监视器";
        var text = GetString(message.Payload, "message") ?? string.Empty;
        var defaultValue = GetString(message.Payload, "default_value") ?? string.Empty;
        var prompt = new PromptWindow(heading, text, kind, defaultValue);

        if (IsVisible)
        {
            prompt.Owner = this;
        }

        _dialogWindows.Add(prompt);
        try
        {
            prompt.ShowDialog();
        }
        finally
        {
            _dialogWindows.Remove(prompt);
        }
        return new PipeResponse { Decision = prompt.Decision, Message = prompt.ResponseText };
    }

    private SessionItem UpdateSession(HookEnvelope message)
    {
        if (!_sessionsById.TryGetValue(message.SessionId, out var session))
        {
            session = new SessionItem { SessionId = message.SessionId };
            _sessionsById.Add(message.SessionId, session);
            Sessions.Insert(0, session);
            OnPropertyChanged(nameof(EmptyStateVisibility));
        }

        if (!string.IsNullOrWhiteSpace(message.Cwd))
        {
            session.Cwd = message.Cwd;
            session.ProjectName = GetProjectName(message.Cwd);
        }

        if (!string.IsNullOrWhiteSpace(message.Model) &&
            (string.IsNullOrWhiteSpace(session.Model) || IsPrimaryModelEvent(message.EventName)))
        {
            session.Model = message.Model;
        }

        if (string.Equals(message.EventName, "UserPromptSubmit", StringComparison.Ordinal) &&
            string.Equals(session.SessionName, session.ShortId, StringComparison.Ordinal))
        {
            var prompt = GetString(message.Payload, "prompt");
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                session.SessionName = CreateSessionName(prompt);
            }
        }

        if (!string.IsNullOrWhiteSpace(message.PermissionMode))
        {
            session.PermissionMode = message.PermissionMode;
        }

        session.LastEvent = TranslateEvent(message.EventName);
        session.Status = StatusForEvent(message.EventName);
        session.UpdatedAt = DateTimeOffset.Now;
        return session;
    }

    private static string StatusForEvent(string eventName) => eventName switch
    {
        "SessionStart" => "会话已开始",
        "UserPromptSubmit" => "正在处理提示",
        "PreToolUse" => "准备调用工具",
        "PermissionRequest" => "等待权限审批",
        "PostToolUse" => "工具调用完成",
        "PreCompact" => "正在压缩上下文",
        "PostCompact" => "上下文压缩完成",
        "SubagentStart" => "子代理已启动",
        "SubagentStop" => "子代理已停止",
        "Stop" => "本轮已完成",
        "Interrupt" => "本轮已中断",
        "SessionEnd" => "会话已结束",
        _ => "未知事件"
    };

    private static string TranslateEvent(string eventName) => eventName switch
    {
        "SessionStart" => "会话开始",
        "SessionEnd" => "会话结束",
        "UserPromptSubmit" => "收到提示",
        "PreToolUse" => "工具调用前",
        "PostToolUse" => "工具调用后",
        "PermissionRequest" => "权限审批",
        "PreCompact" => "压缩前",
        "PostCompact" => "压缩后",
        "SubagentStart" => "子代理启动",
        "SubagentStop" => "子代理停止",
        "Stop" => "本轮完成",
        "Interrupt" => "本轮中断",
        _ => eventName
    };

    private static bool IsPrimaryModelEvent(string eventName) =>
        eventName is "SessionStart" or "UserPromptSubmit";

    private static string GetProjectName(string cwd)
    {
        var trimmed = cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return cwd;
        }

        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? cwd : name;
    }

    private static string CreateSessionName(string prompt)
    {
        var normalized = string.Join(
            " ",
            prompt.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length > 40 ? normalized[..40] + "…" : normalized;
    }

    private static string? GetString(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? GetNestedString(JsonElement payload, string parent, string name)
    {
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(parent, out var parentValue)
            ? GetString(parentValue, name)
            : null;
    }

    private static bool ShouldRequestInput(JsonElement payload)
    {
        var message = GetString(payload, "last_assistant_message")?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (message.EndsWith('?') || message.EndsWith('？'))
        {
            return true;
        }

        var tail = message.Length > 200 ? message[^200..] : message;
        string[] requestMarkers =
        [
            "请回复",
            "请确认",
            "请选择",
            "请提供",
            "请填写",
            "请告诉",
            "需要你",
            "需要您",
            "是否",
            "能否",
            "要不要",
            "你希望",
            "您希望"
        ];

        return requestMarkers.Any(marker => tail.Contains(marker, StringComparison.Ordinal));
    }

    private static string GetToolDetails(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("tool_input", out var toolInput))
        {
            if (toolInput.ValueKind == JsonValueKind.Object &&
                toolInput.TryGetProperty("command", out var command) &&
                command.ValueKind == JsonValueKind.String)
            {
                return command.GetString() ?? string.Empty;
            }

            return JsonSerializer.Serialize(toolInput, new JsonSerializerOptions { WriteIndented = true });
        }

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private void ClearEnded_Click(object sender, RoutedEventArgs e)
    {
        var ended = Sessions.Where(item => item.Status is "会话已结束" or "权限审批拒绝").ToArray();
        foreach (var item in ended)
        {
            Sessions.Remove(item);
            _sessionsById.Remove(item.SessionId);
        }

        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    internal void ShowFromTray()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke((Action)ShowFromTray);
            return;
        }

        if (_disposed || ((App)Application.Current).IsExiting)
        {
            return;
        }

        Show();
        WindowState = WindowState.Normal;
        EnsureWindowWithinVirtualScreen();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private async void ExitApplication()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke((Action)ExitApplication);
            return;
        }

        if (_disposed)
        {
            return;
        }

        CloseDialogWindows();
        await DisposeResourcesAsync();
        ((App)Application.Current).ExitApplication();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!((App)Application.Current).IsExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        CloseDialogWindows();
        DisposeResourcesAsync().AsTask().GetAwaiter().GetResult();
        base.OnClosing(e);
    }

    private void CloseDialogWindows()
    {
        foreach (var window in _dialogWindows.ToArray())
        {
            window.Close();
        }
    }

    private void EnsureWindowWithinVirtualScreen()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var left = double.IsNaN(Left) ? SystemParameters.VirtualScreenLeft : Left;
        var top = double.IsNaN(Top) ? SystemParameters.VirtualScreenTop : Top;
        var right = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        var bottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        var isOutside = left + width < SystemParameters.VirtualScreenLeft ||
                        left > right ||
                        top + height < SystemParameters.VirtualScreenTop ||
                        top > bottom;

        if (!isOutside)
        {
            return;
        }

        Left = SystemParameters.VirtualScreenLeft + Math.Max(0, (SystemParameters.VirtualScreenWidth - width) / 2);
        Top = SystemParameters.VirtualScreenTop + Math.Max(0, (SystemParameters.VirtualScreenHeight - height) / 2);
    }

    private async ValueTask DisposeResourcesAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayIcon.Dispose();
        await _pipeService.DisposeAsync();
        _dialogGate.Dispose();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

