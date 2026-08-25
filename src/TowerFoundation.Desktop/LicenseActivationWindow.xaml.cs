using System.IO;
using System.Windows;
using Microsoft.Win32;
using TowerFoundation.Licensing;

namespace TowerFoundation.Desktop;

public partial class LicenseActivationWindow : Window
{
    private readonly ClientLicenseManager _manager;

    public LicenseActivationWindow(
        ClientLicenseManager manager,
        ClientLicenseAssessment assessment,
        bool allowPreview = true)
    {
        _manager = manager;
        InitializeComponent();
        MachineCodeBox.Text = assessment.MachineCode;
        AssessmentText.Text = assessment.Message;
        PreviewButton.Visibility = allowPreview ? Visibility.Visible : Visibility.Collapsed;
    }

    public ClientLicenseAssessment? ActivatedAssessment { get; private set; }

    private void CopyMachineCode_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(MachineCodeBox.Text);
        SetResult("机器码已复制，请发送给授权签发员。", false);
    }

    private void OpenLicenseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开塔基智设客户授权文件",
            Filter = "塔基智设客户授权 (*.tjzlic)|*.tjzlic|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            LicenseTokenBox.Text = File.ReadAllText(dialog.FileName);
            SetResult($"已读取授权文件：{Path.GetFileName(dialog.FileName)}。", false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetResult($"无法读取授权文件：{exception.Message}", true);
        }
    }

    private void ClearToken_Click(object sender, RoutedEventArgs e) => LicenseTokenBox.Clear();

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ActivatedAssessment = _manager.Activate(LicenseTokenBox.Text);
            SetResult("授权验证通过，正式计算、AI、保存和导出功能已启用。", false);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            SetResult($"授权未通过：{exception.Message}", true);
        }
    }

    private void ContinuePreview_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SetResult(string message, bool error)
    {
        ResultText.Text = message;
        ResultText.Foreground = (System.Windows.Media.Brush)FindResource(
            error ? "DangerBrush" : "SuccessBrush");
    }
}
