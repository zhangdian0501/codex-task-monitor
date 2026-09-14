using System.Windows;
using Application = System.Windows.Application;

namespace CodexTaskMonitor.App;

public partial class App : Application
{
    private const string MutexName = @"Local\CodexTaskMonitor.App.v1";
    private const string ActivationEventName = @"Local\CodexTaskMonitor.Activate.v1";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationStop;
    private Task? _activationLoop;

    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _singleInstanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            _activationEvent.Set();
            _activationEvent.Dispose();
            _activationEvent = null;
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        _activationStop = new CancellationTokenSource();
        _activationLoop = Task.Run(() => WaitForActivation(_activationStop.Token));

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void WaitForActivation(CancellationToken cancellationToken)
    {
        var handles = new WaitHandle[] { _activationEvent!, cancellationToken.WaitHandle };
        while (!cancellationToken.IsCancellationRequested)
        {
            if (WaitHandle.WaitAny(handles) != 0 || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow window)
                {
                    window.ShowFromTray();
                }
            });
        }
    }

    public void ExitApplication()
    {
        IsExiting = true;
        MainWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationStop?.Cancel();
        _activationEvent?.Set();

        try
        {
            _activationLoop?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _activationStop?.Dispose();
        _activationEvent?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
