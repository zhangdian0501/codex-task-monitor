using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodexTaskMonitor.App.Models;

public sealed class SessionItem : INotifyPropertyChanged
{
    private string _sessionName = string.Empty;
    private string _projectName = string.Empty;
    private string _cwd = string.Empty;
    private string _model = string.Empty;
    private string _permissionMode = string.Empty;
    private string _status = "已发现";
    private string _lastEvent = string.Empty;
    private DateTimeOffset _updatedAt = DateTimeOffset.Now;

    public required string SessionId { get; init; }

    public string ShortId => SessionId.Length > 14 ? SessionId[..14] + "…" : SessionId;

    public string SessionName
    {
        get => string.IsNullOrWhiteSpace(_sessionName) ? ShortId : _sessionName;
        set => SetField(ref _sessionName, value);
    }

    public string ProjectName
    {
        get => string.IsNullOrWhiteSpace(_projectName) ? Cwd : _projectName;
        set => SetField(ref _projectName, value);
    }

    public string Cwd
    {
        get => _cwd;
        set
        {
            if (SetField(ref _cwd, value))
            {
                OnPropertyChanged(nameof(ProjectName));
            }
        }
    }

    public string Model
    {
        get => _model;
        set => SetField(ref _model, value);
    }

    public string PermissionMode
    {
        get => _permissionMode;
        set => SetField(ref _permissionMode, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string LastEvent
    {
        get => _lastEvent;
        set => SetField(ref _lastEvent, value);
    }

    public DateTimeOffset UpdatedAt
    {
        get => _updatedAt;
        set
        {
            if (SetField(ref _updatedAt, value))
            {
                OnPropertyChanged(nameof(UpdatedAtText));
            }
        }
    }

    public string UpdatedAtText => UpdatedAt.LocalDateTime.ToString("HH:mm:ss");

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
