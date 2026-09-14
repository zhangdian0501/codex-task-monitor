using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CodexTaskMonitor.App.Services;

namespace CodexTaskMonitor.App;

public enum PromptKind
{
    Information,
    Warning,
    Error,
    Confirmation,
    Input
}

public partial class PromptWindow : Window
{
    private readonly PromptKind _kind;

    public PromptWindow(
        string heading,
        string message,
        PromptKind kind,
        string defaultValue = "",
        string contextText = "")
    {
        InitializeComponent();
        _kind = kind;
        HeadingText.Text = heading;
        MessageText.Text = message;
        InputTextBox.Text = defaultValue;
        if (!string.IsNullOrWhiteSpace(contextText))
        {
            ContextTextBox.Text = contextText;
            ContextPanel.Visibility = Visibility.Visible;
            Width = 620;
            MaxHeight = 720;
        }
        ConfigureKind();
    }

    public string Decision { get; private set; } = "cancel";

    public string ResponseText => InputTextBox.Text.Trim();

    private void ConfigureKind()
    {
        switch (_kind)
        {
            case PromptKind.Warning:
                SetBadge("警告", "#422F12", "#FCD34D");
                SecondaryButton.Visibility = Visibility.Collapsed;
                PrimaryButton.Content = "知道了";
                break;
            case PromptKind.Error:
                SetBadge("错误", "#451A1A", "#FCA5A5");
                SecondaryButton.Visibility = Visibility.Collapsed;
                PrimaryButton.Content = "知道了";
                break;
            case PromptKind.Confirmation:
                SetBadge("问题", "#263A5F", "#BFDBFE");
                SecondaryButton.Content = "否";
                PrimaryButton.Content = "是";
                break;
            case PromptKind.Input:
                SetBadge("等待输入", "#193B32", "#A7F3D0");
                InputPanel.Visibility = Visibility.Visible;
                SecondaryButton.Content = "稍后处理";
                PrimaryButton.Content = "提交并继续";
                break;
            default:
                SetBadge("提示", "#263A5F", "#BFDBFE");
                SecondaryButton.Visibility = Visibility.Collapsed;
                PrimaryButton.Content = "知道了";
                break;
        }
    }

    private void SetBadge(string text, string background, string foreground)
    {
        KindText.Text = text;
        KindBadge.Background = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(background));
        KindText.Foreground = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(foreground));
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_kind == PromptKind.Input && string.IsNullOrWhiteSpace(InputTextBox.Text))
        {
            ValidationText.Visibility = Visibility.Visible;
            InputTextBox.Focus();
            return;
        }

        Decision = _kind switch
        {
            PromptKind.Confirmation => "yes",
            PromptKind.Input => "submit",
            _ => "acknowledged"
        };
        DialogResult = true;
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        Decision = _kind == PromptKind.Confirmation ? "no" : "cancel";
        DialogResult = false;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Decision = "cancel";
        Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        WindowPlacement.PlaceBottomRight(this);
        if (_kind == PromptKind.Input)
        {
            InputTextBox.Focus();
            InputTextBox.CaretIndex = InputTextBox.Text.Length;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
