using System.Windows;

namespace TowerFoundation.Desktop;

public sealed record StepConfirmationRequest(
    string Title,
    string Subtitle,
    string Summary,
    IReadOnlyList<string> ConfirmationItems);

public partial class StepConfirmationWindow : Window
{
    public StepConfirmationWindow(StepConfirmationRequest request)
    {
        InitializeComponent();
        DataContext = request;
    }

    public static bool Confirm(Window owner, StepConfirmationRequest request)
    {
        var dialog = new StepConfirmationWindow(request)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
