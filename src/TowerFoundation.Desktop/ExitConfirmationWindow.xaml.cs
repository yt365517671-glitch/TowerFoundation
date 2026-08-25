using System.Windows;

namespace TowerFoundation.Desktop;

public partial class ExitConfirmationWindow : Window
{
    public ExitConfirmationWindow(string fileDisplay, string progressDisplay)
    {
        InitializeComponent();
        DataContext = new
        {
            FileDisplay = string.IsNullOrWhiteSpace(fileDisplay)
                ? "当前项目尚未保存"
                : fileDisplay,
            ProgressDisplay = progressDisplay
        };
    }

    public static bool Confirm(
        Window owner,
        string fileDisplay,
        string progressDisplay)
    {
        var dialog = new ExitConfirmationWindow(fileDisplay, progressDisplay)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
