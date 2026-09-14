using System.Drawing;
using System.Windows.Forms;

namespace CodexTaskMonitor.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayIconService(Action showWindow, Action exitApplication)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开监视器", null, (_, _) => showWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exitApplication());

        _notifyIcon = new NotifyIcon
        {
            Text = "Codex 任务监视器",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => showWindow();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
