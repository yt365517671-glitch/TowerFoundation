using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TowerFoundation.Desktop;

public partial class AppDialogWindow : Window
{
    private readonly MessageBoxButton _buttons;
    private MessageBoxResult _result = MessageBoxResult.None;

    public AppDialogWindow(
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage icon,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        InitializeComponent();

        _buttons = buttons;
        Title = string.IsNullOrWhiteSpace(caption) ? "塔基智设提示" : caption;
        TitleTextBlock.Text = Title;
        MessageTextBlock.Text = message;
        ConfigureAppearance(icon);
        ConfigureButtons(buttons, defaultResult);

        Loaded += (_, _) => FocusDefaultButton();
    }

    public MessageBoxResult Result => _result;

    public static MessageBoxResult Show(
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage icon,
        MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowCore(ResolveOwner(), message, caption, buttons, icon, defaultResult);

    public static MessageBoxResult Show(
        Window owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage icon,
        MessageBoxResult defaultResult = MessageBoxResult.None) =>
        ShowCore(owner, message, caption, buttons, icon, defaultResult);

    private static MessageBoxResult ShowCore(
        Window? owner,
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage icon,
        MessageBoxResult defaultResult)
    {
        var dialog = new AppDialogWindow(message, caption, buttons, icon, defaultResult);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog.Result;
    }

    private static Window? ResolveOwner()
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return null;
        }

        return application.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive) ??
               application.MainWindow;
    }

    private void ConfigureAppearance(MessageBoxImage icon)
    {
        var (glyph, description, accent) = icon switch
        {
            MessageBoxImage.Warning => ("!", "请确认操作影响", "#D98B19"),
            MessageBoxImage.Error => ("×", "操作未能完成", "#D94B5B"),
            MessageBoxImage.Question => ("?", "需要你的确认", "#5A5BEA"),
            _ => ("i", "请查看以下信息", "#299C96")
        };

        IconTextBlock.Text = glyph;
        KindTextBlock.Text = description;
        AccentBar.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent));
    }

    private void ConfigureButtons(MessageBoxButton buttons, MessageBoxResult defaultResult)
    {
        PrimaryActionButton.Visibility = Visibility.Visible;
        SecondaryActionButton.Visibility = Visibility.Visible;
        TertiaryActionButton.Visibility = Visibility.Collapsed;

        switch (buttons)
        {
            case MessageBoxButton.OK:
                PrimaryActionButton.Content = "确定";
                PrimaryActionButton.Tag = MessageBoxResult.OK;
                SecondaryActionButton.Visibility = Visibility.Collapsed;
                KeyboardHintTextBlock.Text = "按 Enter 或 Esc 关闭";
                break;

            case MessageBoxButton.OKCancel:
                SecondaryActionButton.Content = "取消";
                SecondaryActionButton.Tag = MessageBoxResult.Cancel;
                PrimaryActionButton.Content = "确定";
                PrimaryActionButton.Tag = MessageBoxResult.OK;
                break;

            case MessageBoxButton.YesNo:
                SecondaryActionButton.Content = "暂不";
                SecondaryActionButton.Tag = MessageBoxResult.No;
                PrimaryActionButton.Content = "确认";
                PrimaryActionButton.Tag = MessageBoxResult.Yes;
                break;

            case MessageBoxButton.YesNoCancel:
                TertiaryActionButton.Visibility = Visibility.Visible;
                TertiaryActionButton.Content = "取消";
                TertiaryActionButton.Tag = MessageBoxResult.Cancel;
                SecondaryActionButton.Content = "否";
                SecondaryActionButton.Tag = MessageBoxResult.No;
                PrimaryActionButton.Content = "是";
                PrimaryActionButton.Tag = MessageBoxResult.Yes;
                break;
        }

        var safeDefault = defaultResult == MessageBoxResult.None
            ? buttons switch
            {
                MessageBoxButton.YesNo => MessageBoxResult.No,
                MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
                _ => MessageBoxResult.OK
            }
            : defaultResult;

        foreach (var button in ActionButtons())
        {
            var result = button.Tag is MessageBoxResult value ? value : MessageBoxResult.None;
            button.IsDefault = result == safeDefault;
            button.IsCancel = result == CancelResult();
        }
    }

    private IEnumerable<Button> ActionButtons()
    {
        yield return TertiaryActionButton;
        yield return SecondaryActionButton;
        yield return PrimaryActionButton;
    }

    private void FocusDefaultButton()
    {
        var button = ActionButtons().FirstOrDefault(item =>
            item.Visibility == Visibility.Visible && item.IsDefault);
        button?.Focus();
    }

    private MessageBoxResult CancelResult() => _buttons switch
    {
        MessageBoxButton.YesNo => MessageBoxResult.No,
        MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
        _ => MessageBoxResult.OK
    };

    private void Complete(MessageBoxResult result)
    {
        _result = result;
        DialogResult = result is MessageBoxResult.OK or MessageBoxResult.Yes;
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MessageBoxResult result })
        {
            Complete(result);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Complete(CancelResult());

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Complete(CancelResult());
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
