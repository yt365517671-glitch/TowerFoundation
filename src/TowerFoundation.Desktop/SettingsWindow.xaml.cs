using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using TowerFoundation.Application;
using TowerFoundation.Infrastructure;
using TowerFoundation.Licensing;

namespace TowerFoundation.Desktop;

public partial class SettingsWindow : Window
{
    private static readonly string[] SupportedDeepSeekModels =
    [
        "deepseek-v4-pro",
        "deepseek-v4-flash"
    ];

    private readonly IApplicationSettingsService _settingsService;
    private readonly IDeepSeekService _deepSeekService;
    private readonly IVisualGeotechnicalAiService _visualService;
    private readonly ClientLicenseManager? _licenseManager;
    private readonly Action<ClientLicenseAssessment>? _licenseChanged;

    public SettingsWindow(
        IApplicationSettingsService settingsService,
        IDeepSeekService deepSeekService,
        IVisualGeotechnicalAiService visualService,
        ClientLicenseManager? licenseManager = null,
        Action<ClientLicenseAssessment>? licenseChanged = null)
    {
        _settingsService = settingsService;
        _deepSeekService = deepSeekService;
        _visualService = visualService;
        _licenseManager = licenseManager;
        _licenseChanged = licenseChanged;
        InitializeComponent();
        ModelBox.ItemsSource = SupportedDeepSeekModels;
        VisionModelBox.ItemsSource = VisualAiModelCatalog.SupportedModels;
        LoadSettings();
        LoadLicenseStatus();
    }

    public bool SettingsChanged { get; private set; }

    private void LoadLicenseStatus()
    {
        if (_licenseManager is null)
        {
            LicenseStateText.Text = "开发测试版：保留现有本机配置，不执行授权限制。";
            LicenseMachineCodeText.Text = "正式发布版使用独立的 production 配置和授权目录。";
            ManageLicenseButton.Visibility = Visibility.Collapsed;
            return;
        }

        var assessment = _licenseManager.Assess();
        LicenseStateText.Text = assessment.Message;
        LicenseMachineCodeText.Text = $"机器码：{assessment.MachineCode}";
        ManageLicenseButton.Content = assessment.IsUsable ? "查看 / 更新授权" : "输入授权码";
    }

    private void ManageLicense_Click(object sender, RoutedEventArgs e)
    {
        if (_licenseManager is null)
        {
            return;
        }

        var dialog = new LicenseActivationWindow(
            _licenseManager,
            _licenseManager.Assess(),
            allowPreview: false)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true && dialog.ActivatedAssessment is not null)
        {
            _licenseChanged?.Invoke(dialog.ActivatedAssessment);
            LoadLicenseStatus();
        }
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        OnlinePreferredRadio.IsChecked = settings.AiMode == AiOperatingMode.OnlinePreferred;
        OfflineOnlyRadio.IsChecked = settings.AiMode == AiOperatingMode.OfflineOnly;
        BaseUrlBox.Text = settings.DeepSeekBaseUrl;
        ModelBox.SelectedItem = SupportedDeepSeekModels.Contains(settings.DeepSeekModel)
            ? settings.DeepSeekModel
            : SupportedDeepSeekModels[0];
        VisionBaseUrlBox.Text = settings.VisionBaseUrl;
        VisionModelBox.SelectedItem = VisualAiModelCatalog.SupportedModels.Contains(settings.VisionModel)
            ? settings.VisionModel
            : VisualAiModelCatalog.DefaultModel;
        VisionBatchSizeBox.Text = settings.VisionPagesPerBatch.ToString();
        TimeoutBox.Text = settings.RequestTimeoutSeconds.ToString();
        ProjectDirectoryBox.Text = settings.DefaultProjectDirectory;
        ExportDirectoryBox.Text = settings.DefaultExportDirectory;
        GeotechnicalHistoryDirectoryBox.Text =
            settings.DefaultGeotechnicalHistoryDirectory;
        MonitoringDrawingHistoryDirectoryBox.Text =
            settings.DefaultMonitoringDrawingHistoryDirectory;
        OcrStartPageBox.Text = settings.OcrStartPage.ToString();
        OcrEndPageBox.Text = settings.OcrEndPage.ToString();
        StoredKeyText.Text = settings.HasApiKey
            ? $"本机已加密保存 DeepSeek 密钥：••••{settings.ApiKeyLastFour}；留空可继续使用。"
            : "本机尚未保存 DeepSeek 密钥。";
        StoredVisionKeyText.Text = settings.HasVisionApiKey
            ? $"本机已加密保存视觉 API 密钥：••••{settings.VisionApiKeyLastFour}；留空可继续使用。"
            : "本机尚未保存视觉 API 密钥。";
    }

    private void ClearApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureFormalUseAuthorized())
        {
            return;
        }

        RunSettingsAction(
            () =>
            {
                _settingsService.Save(_settingsService.Load(), clearApiKey: true);
                ApiKeyBox.Clear();
                SettingsChanged = true;
                LoadSettings();
                SetResult("已清除本机加密保存的 DeepSeek API 密钥。", true);
            });
    }

    private void ClearVisionApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureFormalUseAuthorized())
        {
            return;
        }

        RunSettingsAction(
            () =>
            {
                _settingsService.Save(_settingsService.Load(), clearVisionApiKey: true);
                VisionApiKeyBox.Clear();
                SettingsChanged = true;
                LoadSettings();
                SetResult("已清除本机加密保存的视觉 API 密钥。", true);
            });
    }

    private void ImportVisionCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureFormalUseAuthorized())
        {
            return;
        }

        RunSettingsAction(
            () =>
            {
                if (_settingsService is not LocalApplicationSettingsService localSettings)
                {
                    throw new InvalidOperationException("当前设置存储不支持从业务空间 CSV 导入。请直接填写视觉 API 密钥。");
                }

                var dialog = new OpenFileDialog
                {
                    Title = "选择阿里云百炼业务空间 API CSV",
                    Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                    CheckFileExists = true,
                    InitialDirectory = AppContext.BaseDirectory
                };

                if (dialog.ShowDialog(this) != true)
                {
                    return;
                }

                var result = localSettings.ImportVisualApiFromCsv(dialog.FileName);
                if (!result.Imported)
                {
                    throw new InvalidOperationException(result.Message);
                }

                VisionApiKeyBox.Clear();
                SettingsChanged = true;
                LoadSettings();
                SetResult(result.Message, true);
            });
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureFormalUseAuthorized())
        {
            return;
        }

        await TestSavedConnectionsAsync(
            testDeepSeek: true,
            testVision: true,
            "正在测试本机已保存的文字 AI 与视觉 AI 配置…");
    }

    private async void TestVisionConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureFormalUseAuthorized())
        {
            return;
        }

        try
        {
            SaveCurrentSettings();
            LoadSettings();
            await TestSavedConnectionsAsync(
                testDeepSeek: false,
                testVision: true,
                "正在测试所选视觉理解模型是否能实际识图…");
        }
        catch (Exception exception)
        {
            ShowOperationError(exception);
        }
    }

    private async void SaveAndTest_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureFormalUseAuthorized())
        {
            return;
        }

        try
        {
            SaveCurrentSettings();
            LoadSettings();
            await TestSavedConnectionsAsync(
                testDeepSeek: true,
                testVision: true,
                "设置已保存，正在测试文字 AI 与视觉 AI…");
        }
        catch (Exception exception)
        {
            ShowOperationError(exception);
        }
    }

    private void BrowseProjectDirectory_Click(object sender, RoutedEventArgs e) =>
        BrowseForDirectory(ProjectDirectoryBox, "选择项目默认保存位置");

    private void BrowseExportDirectory_Click(object sender, RoutedEventArgs e) =>
        BrowseForDirectory(ExportDirectoryBox, "选择资料默认导出位置");

    private void BrowseGeotechnicalHistoryDirectory_Click(
        object sender,
        RoutedEventArgs e) =>
        BrowseForDirectory(
            GeotechnicalHistoryDirectoryBox,
            "选择地勘分析记录默认保存位置");

    private void BrowseMonitoringDrawingHistoryDirectory_Click(
        object sender,
        RoutedEventArgs e) =>
        BrowseForDirectory(
            MonitoringDrawingHistoryDirectoryBox,
            "选择监控杆图纸识别记录默认保存位置");

    private void BrowseForDirectory(System.Windows.Controls.TextBox target, string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };
        if (Directory.Exists(target.Text))
        {
            dialog.InitialDirectory = target.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            target.Text = dialog.FolderName;
        }
    }

    private void OpenOfficialLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !IsApprovedOfficialHost(uri.Host))
        {
            SetResult("官网链接无效或不在允许的官方域名中。", false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            SetResult("已使用系统默认浏览器打开官方页面。", true);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException)
        {
            SetResult($"无法打开浏览器：{exception.Message}", false);
        }
    }

    private static bool IsApprovedOfficialHost(string host) =>
        host.Equals("platform.deepseek.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("api-docs.deepseek.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("bailian.console.aliyun.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("help.aliyun.com", StringComparison.OrdinalIgnoreCase);

    private async Task TestSavedConnectionsAsync(
        bool testDeepSeek,
        bool testVision,
        string pendingMessage)
    {
        SetActionButtonsEnabled(false);
        AiProgressText.Text = pendingMessage;
        AiProgressPanel.Visibility = Visibility.Visible;
        SetResult(pendingMessage, null);
        try
        {
            var results = new List<AiConnectionResult>();
            if (testDeepSeek)
            {
                results.Add(await _deepSeekService.TestConnectionAsync());
            }
            if (testVision)
            {
                results.Add(await _visualService.TestConnectionAsync());
            }

            SetResult(
                string.Join(Environment.NewLine, results.Select(result => result.Message)),
                results.All(result => result.Success));
        }
        catch (Exception exception)
        {
            ShowOperationError(exception);
        }
        finally
        {
            AiProgressPanel.Visibility = Visibility.Collapsed;
            SetActionButtonsEnabled(true);
        }
    }

    private void SaveCurrentSettings()
    {
        if (!int.TryParse(TimeoutBox.Text, out var timeoutSeconds) ||
            timeoutSeconds is < 10 or > 180)
        {
            throw new InvalidOperationException("请求超时必须是 10 到 180 秒之间的整数。");
        }
        if (!int.TryParse(VisionBatchSizeBox.Text, out var visionBatchSize) ||
            visionBatchSize is < 1 or > 6)
        {
            throw new InvalidOperationException("视觉分析每批页数必须是 1 到 6 之间的整数。");
        }
        if (!int.TryParse(OcrStartPageBox.Text, out var ocrStartPage) || ocrStartPage < 1)
        {
            throw new InvalidOperationException("PDF 识别开始页必须是大于等于 1 的整数。");
        }
        if (!int.TryParse(OcrEndPageBox.Text, out var ocrEndPage) ||
            ocrEndPage < 0 ||
            ocrEndPage > 0 && ocrEndPage < ocrStartPage)
        {
            throw new InvalidOperationException("PDF 识别结束页必须为 0，或不小于开始页。");
        }

        var settings = new ApplicationSettings
        {
            AiMode = OfflineOnlyRadio.IsChecked == true
                ? AiOperatingMode.OfflineOnly
                : AiOperatingMode.OnlinePreferred,
            DeepSeekBaseUrl = BaseUrlBox.Text.Trim(),
            DeepSeekModel = ModelBox.SelectedItem as string ?? string.Empty,
            VisionBaseUrl = VisionBaseUrlBox.Text.Trim(),
            VisionModel = VisionModelBox.SelectedItem as string ?? string.Empty,
            VisionPagesPerBatch = visionBatchSize,
            RequestTimeoutSeconds = timeoutSeconds,
            DefaultProjectDirectory = ValidateDirectoryPath(
                ProjectDirectoryBox.Text,
                "项目默认保存位置"),
            DefaultExportDirectory = ValidateDirectoryPath(
                ExportDirectoryBox.Text,
                "资料默认导出位置"),
            DefaultGeotechnicalHistoryDirectory = ValidateDirectoryPath(
                GeotechnicalHistoryDirectoryBox.Text,
                "地勘分析记录默认保存位置"),
            DefaultMonitoringDrawingHistoryDirectory = ValidateDirectoryPath(
                MonitoringDrawingHistoryDirectoryBox.Text,
                "监控杆图纸识别记录默认保存位置"),
            OcrStartPage = ocrStartPage,
            OcrEndPage = ocrEndPage
        };
        _settingsService.Save(
            settings,
            string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? null : ApiKeyBox.Password,
            clearApiKey: false,
            string.IsNullOrWhiteSpace(VisionApiKeyBox.Password) ? null : VisionApiKeyBox.Password,
            clearVisionApiKey: false);
        ApiKeyBox.Clear();
        VisionApiKeyBox.Clear();
        SettingsChanged = true;
    }

    private static string ValidateDirectoryPath(string path, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"{fieldName}不能为空。");
        }

        try
        {
            var normalizedPath = Path.GetFullPath(path.Trim());
            Directory.CreateDirectory(normalizedPath);
            return normalizedPath;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or
                PathTooLongException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"{fieldName}无效或无法创建：{exception.Message}",
                exception);
        }
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        ClearButton.IsEnabled = enabled;
        ClearVisionButton.IsEnabled = enabled;
        ImportVisionCsvButton.IsEnabled = enabled;
        TestVisionButton.IsEnabled = enabled;
        TestButton.IsEnabled = enabled;
        SaveAndTestButton.IsEnabled = enabled;
        BrowseProjectDirectoryButton.IsEnabled = enabled;
        BrowseExportDirectoryButton.IsEnabled = enabled;
        BrowseGeotechnicalHistoryDirectoryButton.IsEnabled = enabled;
        BrowseMonitoringDrawingHistoryDirectoryButton.IsEnabled = enabled;
    }

    private void RunSettingsAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            ShowOperationError(exception);
        }
    }

    private void SetResult(string text, bool? success)
    {
        TestResultText.Text = text;
        TestResultText.Foreground = (System.Windows.Media.Brush)FindResource(
            success switch
            {
                true => "SuccessBrush",
                false => "WarningBrush",
                _ => "MutedTextBrush"
            });
    }

    private void ShowOperationError(Exception exception) => SetResult(exception.Message, false);

    private bool EnsureFormalUseAuthorized()
    {
        if (_licenseManager is null || _licenseManager.Assess().IsUsable)
        {
            return true;
        }

        SetResult("当前为未授权预览模式；授权后才能保存或测试 API 配置。", false);
        ManageLicense_Click(this, new RoutedEventArgs());
        return _licenseManager.Assess().IsUsable;
    }
}
