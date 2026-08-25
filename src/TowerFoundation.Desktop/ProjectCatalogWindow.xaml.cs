using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TowerFoundation.Application;

namespace TowerFoundation.Desktop;

public partial class ProjectCatalogWindow : Window
{
    public ProjectCatalogWindow(
        IReadOnlyList<ProjectCatalogEntry> entries,
        string projectDirectory)
    {
        InitializeComponent();
        ProjectCatalogList.ItemsSource = entries;
        ProjectDirectoryText.Text = projectDirectory;
        ProjectCountText.Text = $"{entries.Count(entry => entry.IsReadable)} 个项目";
        EmptyStatePanel.Visibility = entries.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public string? SelectedProjectPath { get; private set; }

    private ProjectCatalogEntry? SelectedEntry =>
        ProjectCatalogList.SelectedItem as ProjectCatalogEntry;

    private void ProjectCatalogList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        OpenSelectedButton.IsEnabled = SelectedEntry?.IsReadable == true;
    }

    private void ProjectCatalogList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null)
        {
            OpenSelected();
        }
    }

    private void OpenSelected_Click(object sender, RoutedEventArgs e) =>
        OpenSelected();

    private void OpenSelected()
    {
        if (SelectedEntry?.IsReadable != true)
        {
            return;
        }

        SelectedProjectPath = SelectedEntry.FilePath;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedEntry?.IsReadable == true)
        {
            OpenSelected();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void Header_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
