using System.Windows;
using System.Windows.Input;
using CodexTaskMonitor.App.Services;

namespace CodexTaskMonitor.App;

public partial class ApprovalWindow : Window
{
    public ApprovalWindow(string summary, string context, string details)
    {
        InitializeComponent();
        DataContext = new { Summary = summary, Context = context, Details = details };
    }

    public bool IsApproved { get; private set; }

    private void Allow_Click(object sender, RoutedEventArgs e)
    {
        IsApproved = true;
        DialogResult = true;
    }

    private void Deny_Click(object sender, RoutedEventArgs e)
    {
        IsApproved = false;
        DialogResult = false;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        WindowPlacement.PlaceBottomRight(this);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
