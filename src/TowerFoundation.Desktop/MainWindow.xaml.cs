using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TowerFoundation.Application;
using TowerFoundation.Calculation;
using TowerFoundation.Domain;
using TowerFoundation.Infrastructure;
using TowerFoundation.Optimization;
using TowerFoundation.Licensing;

namespace TowerFoundation.Desktop;

public partial class MainWindow : Window
{
    private readonly IApplicationSettingsService _settingsService;
    private readonly IDeepSeekService _deepSeekService;
    private readonly IVisualGeotechnicalAiService _visualGeotechnicalAiService;
    private readonly IMonitoringDrawingVisionAiService _monitoringDrawingVisionAiService;
    private readonly IWordTextExtractor _wordTextExtractor;
    private readonly ILocalPdfOcrService _localPdfOcrService;
    private readonly ClientLicenseManager? _licenseManager;
    private ClientLicenseAssessment? _licenseAssessment;

    public MainWindow(ClientLicenseManager? licenseManager = null)
    {
        InitializeComponent();
        Title = AppBuildProfile.WindowTitle;
        _licenseManager = licenseManager;
        _licenseAssessment = licenseManager?.Assess();

        var foundationCalculator = new RectangularShortColumnFoundationCalculator();
        var optimizer = new ThreeStrategyFoundationOptimizer(foundationCalculator);
        var adjustmentAdvisor = new FoundationAdjustmentAdvisor(foundationCalculator);
        var workflow = new DesignWorkflowService(
            new MonitoringPoleLoadCalculator(),
            foundationCalculator,
            optimizer,
            adjustmentAdvisor);

        _settingsService = new LocalApplicationSettingsService(
            AppDataPaths.ResolveSettingsDirectory());
        _deepSeekService = new DeepSeekService(_settingsService);
        _visualGeotechnicalAiService = new VisualGeotechnicalAiService(_settingsService);
        _monitoringDrawingVisionAiService = new MonitoringDrawingVisionAiService(_settingsService);
        _wordTextExtractor = new DocxTextExtractor();
        _localPdfOcrService = new LocalPdfOcrService();

        var projectRepository = new JsonProjectRepository();
        DataContext = new MainViewModel(
            workflow,
            projectRepository,
            new LocalProjectCatalogService(projectRepository, _settingsService),
            new PrototypeOutputPackageService(),
            _settingsService,
            _deepSeekService,
            _visualGeotechnicalAiService,
            _monitoringDrawingVisionAiService,
            new LocalMonitoringDrawingRecognitionHistoryService(_settingsService),
            _wordTextExtractor,
            _localPdfOcrService,
            new LocalGeotechnicalAnalysisHistoryService(_settingsService),
            new EmbeddedRegionWindCatalog(),
            new EmbeddedTowerLoadCatalog(),
            _licenseAssessment?.IsUsable ?? true);
        UpdateLicenseUi();
    }

    public bool SuppressCloseConfirmationForAutomation { get; set; }

    public bool SuppressStepConfirmationForAutomation { get; set; }

    public bool SuppressSmartCompletionDialogForAutomation { get; set; }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(
            _settingsService,
            _deepSeekService,
            _visualGeotechnicalAiService,
            _licenseManager,
            OnLicenseAssessmentChanged)
        {
            Owner = this
        };
        dialog.ShowDialog();
        if (dialog.SettingsChanged && DataContext is MainViewModel viewModel)
        {
            viewModel.RefreshAiStatus();
            viewModel.RefreshGeotechnicalHistoryFromSettings();
        }
    }

    private void OpenLicense_Click(object sender, RoutedEventArgs e) =>
        ShowLicenseActivation();

    public bool ShowLicenseActivation()
    {
        if (_licenseManager is null)
        {
            return true;
        }

        var dialog = new LicenseActivationWindow(
            _licenseManager,
            _licenseManager.Assess(),
            allowPreview: true)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true && dialog.ActivatedAssessment is not null)
        {
            OnLicenseAssessmentChanged(dialog.ActivatedAssessment);
            return true;
        }

        OnLicenseAssessmentChanged(_licenseManager.Assess());
        return false;
    }

    private void OnLicenseAssessmentChanged(ClientLicenseAssessment assessment)
    {
        _licenseAssessment = assessment;
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SetFormalUseAuthorization(assessment.IsUsable);
        }
        UpdateLicenseUi();
    }

    private bool EnsureFormalUseAuthorized()
    {
        if (_licenseManager is null || _licenseAssessment?.IsUsable == true)
        {
            return true;
        }

        AppDialogWindow.Show(
            this,
            "当前是未授权预览模式。请输入与本机机器码匹配的授权码后，才能执行正式计算、AI识别、保存或导出。",
            "需要授权",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        ShowLicenseActivation();
        return _licenseAssessment?.IsUsable == true;
    }

    private void UpdateLicenseUi()
    {
        if (_licenseManager is null)
        {
            LicenseStatusText.Text = "开发测试版";
            LicenseButton.Visibility = Visibility.Collapsed;
            return;
        }

        var assessment = _licenseAssessment ?? _licenseManager.Assess();
        LicenseStatusText.Text = assessment.IsUsable
            ? assessment.Status == ClientLicenseStatus.Permanent
                ? "永久授权"
                : "授权有效"
            : "预览模式 · 未授权";
        LicenseStatusBadge.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                assessment.IsUsable ? "#2E8B6D" : "#9A5A00"));
        LicenseButton.Content = assessment.IsUsable ? "授权信息" : "输入授权码";
        LicenseButton.Visibility = Visibility.Visible;
    }

    private void TowerCatalogSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (sender is TextBox { IsKeyboardFocusWithin: true })
        {
            OpenTowerCatalogResults();
        }
    }

    private void TowerCatalogSearchBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            OpenTowerCatalogResults();
            if (TowerCatalogResultsList.Items.Count > 0)
            {
                TowerCatalogResultsList.SelectedIndex = 0;
                TowerCatalogResultsList.Focus();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TowerCatalogResultsPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void TowerCatalogDropDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (TowerCatalogResultsPopup.IsOpen)
        {
            TowerCatalogResultsPopup.IsOpen = false;
        }
        else
        {
            OpenTowerCatalogResults();
        }
    }

    private void TowerCatalogResultsList_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(
                TowerCatalogResultsList,
                e.OriginalSource as DependencyObject) is ListBoxItem
            {
                DataContext: TowerLoadCatalogRecord selectedRecord
            })
        {
            CommitTowerCatalogRecord(selectedRecord);
            e.Handled = true;
        }
    }

    private void TowerCatalogResultItem_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem
            {
                DataContext: TowerLoadCatalogRecord selectedRecord
            })
        {
            CommitTowerCatalogRecord(selectedRecord);
            e.Handled = true;
        }
    }

    private void TowerCatalogResultsList_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            TowerCatalogResultsList.SelectedItem is TowerLoadCatalogRecord selectedRecord)
        {
            CommitTowerCatalogRecord(selectedRecord);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TowerCatalogResultsPopup.IsOpen = false;
            TowerCatalogSearchBox.Focus();
            e.Handled = true;
        }
    }

    private void OpenTowerCatalogResults()
    {
        if (DataContext is MainViewModel viewModel &&
            !viewModel.CanOperateWorkflowStep(3))
        {
            TowerCatalogResultsPopup.IsOpen = false;
            return;
        }

        TowerCatalogResultsBorder.Width = Math.Max(
            480,
            TowerCatalogSearchHost.ActualWidth);
        TowerCatalogResultsPopup.IsOpen = true;
    }

    private void CommitTowerCatalogRecord(TowerLoadCatalogRecord selectedRecord)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!viewModel.CanOperateWorkflowStep(3))
        {
            TowerCatalogResultsPopup.IsOpen = false;
            return;
        }

        viewModel.SelectTowerCatalogRecord(selectedRecord);
        TowerCatalogResultsPopup.IsOpen = false;
        TowerCatalogSearchBox.Focus();
        TowerCatalogSearchBox.CaretIndex = TowerCatalogSearchBox.Text.Length;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (SuppressCloseConfirmationForAutomation)
        {
            return;
        }

        var viewModel = DataContext as MainViewModel;
        if (!ExitConfirmationWindow.Confirm(
                this,
                viewModel?.CurrentFileDisplay ?? "当前项目尚未保存",
                viewModel?.ProgressText ?? "0 / 6"))
        {
            e.Cancel = true;
        }
    }

    private void GoToGeotechnicalStep(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            if (!viewModel.CanOperateWorkflowStep(1))
            {
                viewModel.ReturnToCurrentWorkflow();
                return;
            }

            viewModel.NavigateToStep(
                2,
                TowerFoundation.Domain.ProjectStage.SiteReady);
        }
    }

    private void GoToPoleStep(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            if (!viewModel.CanOperateWorkflowStep(2))
            {
                viewModel.ReturnToCurrentWorkflow();
                return;
            }

            var request = BuildGeotechnicalConfirmation(viewModel);
            if (!ConfirmStep(request))
            {
                return;
            }

            viewModel.ConfirmGeotechnicalInputs();
            viewModel.NavigateToStep(
                3,
                TowerFoundation.Domain.ProjectStage.GeotechnicalReady);
        }
    }

    private void GoToSchemesAfterGenerate(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { Schemes.Count: > 0 } viewModel)
        {
            viewModel.NavigateToStep(4);
        }
    }

    private void GoToProjectStep(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { HasProjectType: true } viewModel)
        {
            viewModel.NavigateToStep(1);
        }
    }

    private void GoToTypeStep(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.NavigateToStep(0);
        }
    }

    private void GoToFoundationStep(object sender, RoutedEventArgs e)
    {
        if (!EnsureFormalUseAuthorized())
        {
            return;
        }

        if (DataContext is MainViewModel viewModel)
        {
            if (!viewModel.CanOperateWorkflowStep(3))
            {
                viewModel.ReturnToCurrentWorkflow();
                return;
            }

            StepConfirmationRequest request;
            try
            {
                request = BuildLoadConfirmation(viewModel);
            }
            catch (Exception exception)
            {
                AppDialogWindow.Show(
                    this,
                    exception.Message,
                    "荷载参数尚不完整",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!ConfirmStep(request))
            {
                return;
            }

            viewModel.ConfirmLoadInputs();
            viewModel.PrepareAutomaticDesignInputs();
            viewModel.NavigateToStep(
                4,
                TowerFoundation.Domain.ProjectStage.LoadReady);
        }
    }

    private void OpenSpecialtyCompletion_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureFormalUseAuthorized())
        {
            return;
        }

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!viewModel.CanOperateWorkflowStep(4))
        {
            viewModel.ReturnToCurrentWorkflow();
            return;
        }

        viewModel.ApplySpecialtyCompletionAndRecalculate();
    }

    private void OpenSpecialtyAdvanced_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureFormalUseAuthorized())
        {
            return;
        }

        if (DataContext is MainViewModel viewModel)
        {
            if (!viewModel.CanOperateWorkflowStep(4))
            {
                viewModel.ReturnToCurrentWorkflow();
                return;
            }

            OpenSpecialtyCompletion(viewModel, null);
        }
    }

    private void OpenScopeItem_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureFormalUseAuthorized())
        {
            return;
        }

        if (DataContext is not MainViewModel viewModel ||
            sender is not FrameworkElement { DataContext: FoundationCheckResult item })
        {
            return;
        }

        if (!viewModel.CanOperateWorkflowStep(4))
        {
            viewModel.ReturnToCurrentWorkflow();
            return;
        }

        OpenSpecialtyCompletion(viewModel, item.Code);
    }

    private void OpenSpecialtyCompletion(MainViewModel viewModel, string? focusCode)
    {
        if (SuppressSmartCompletionDialogForAutomation)
        {
            viewModel.ApplySpecialtyCompletionAndRecalculate();
            return;
        }

        var dialog = new SmartSpecialtyCompletionWindow(
            viewModel.Project,
            _settingsService,
            _deepSeekService,
            _wordTextExtractor,
            _localPdfOcrService,
            focusCode)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            viewModel.ApplySpecialtyCompletionAndRecalculate();
        }
    }

    private bool ConfirmStep(StepConfirmationRequest request) =>
        SuppressStepConfirmationForAutomation ||
        StepConfirmationWindow.Confirm(this, request);

    private static StepConfirmationRequest BuildGeotechnicalConfirmation(
        MainViewModel viewModel)
    {
        var project = viewModel.Project;
        var geotechnical = project.Geotechnical;
        var type = project.FoundationSettings.FoundationType;
        if (type is
            TowerFoundation.Domain.FoundationType.RigidShortPile or
            TowerFoundation.Domain.FoundationType.RigidRectangularShortPile)
        {
            var layers = project.FoundationSettings.RigidShortPile.SoilLayers;
            return new StepConfirmationRequest(
                $"确认{viewModel.FoundationTypeDisplay}地勘参数",
                "确认后这些数据将用于刚性判别、水平位移和桩身内力计算",
                $"土重度 {geotechnical.SoilUnitWeightKnPerM3:F2} kN/m³；内摩擦角 {geotechnical.InternalFrictionAngleDegree:F1}°；" +
                $"地下水埋深 {geotechnical.GroundwaterDepthM:F2} m；m值分层 {layers.Count} 层、合计厚度 {layers.Sum(item => item.ThicknessM):F2} m。",
                [
                    "地下水、土重度、内摩擦角及特殊地基风险已与地勘资料核对。",
                    "主要影响深度内的分层厚度和m值来自地勘报告或可靠试验资料。",
                    "允许软件将以上参数用于方案搜索、复算及成果输出。"
                ]);
        }

        if (type == TowerFoundation.Domain.FoundationType.Pile)
        {
            var pile = project.FoundationSettings.Pile;
            return new StepConfirmationRequest(
                "确认独立灌注桩地勘参数",
                "确认后每根独立桩将采用同一组经核对的桩土参数分别验算",
                $"地下水埋深 {geotechnical.GroundwaterDepthM:F2} m；单桩水平承载力 {pile.SinglePileHorizontalCapacityKn:F2} kN；" +
                $"桩土分层 {pile.SoilLayers.Count} 层、合计有效厚度 {pile.SoilLayers.Sum(item => item.ThicknessM):F2} m。",
                [
                    "地下水、土层描述及特殊地基风险已与原始资料核对。",
                    "分层侧阻、端阻、抗拔系数和水平承载力来自地勘或试桩资料。",
                    "允许软件将以上参数用于独立桩方案搜索、复算及成果输出。"
                ]);
        }

        var correction = geotechnical.UseBearingCapacityCorrection
            ? $"；启用宽深修正，fak={geotechnical.CharacteristicBearingCapacityKpa:F2} kPa，ηb={geotechnical.BearingCapacityWidthCorrectionFactor:F2}，ηd={geotechnical.BearingCapacityDepthCorrectionFactor:F2}"
            : "；不启用宽深修正";
        return new StepConfirmationRequest(
            "确认浅基础地勘参数",
            $"当前基础形式：{viewModel.FoundationTypeDisplay}",
            $"fa={geotechnical.BearingCapacityKpa:F2} kPa；土重度 {geotechnical.SoilUnitWeightKnPerM3:F2} kN/m³；" +
            $"基底摩擦系数 {geotechnical.BaseFrictionCoefficient:F3}；地下水埋深 {geotechnical.GroundwaterDepthM:F2} m{correction}。",
            [
                "承载力、重度、摩擦系数和地下水埋深已与地勘报告核对。",
                "如启用宽深修正，fak、ηb、ηd及基底上下土重度已经确认。",
                "允许软件将以上参数用于方案搜索、复算及成果输出。"
            ]);
    }

    private static StepConfirmationRequest BuildLoadConfirmation(
        MainViewModel viewModel)
    {
        var project = viewModel.Project;
        if (project.ProjectType == TowerFoundation.Domain.ProjectType.MonitoringPole)
        {
            var load = new MonitoringPoleLoadCalculator()
                .Calculate(project.MonitoringPole, project.FoundationSettings)
                .FoundationLoad;
            var basic = load.BasicCombination!;
            return new StepConfirmationRequest(
                "确认监控杆基础端荷载",
                "软件已根据杆件几何、设备迎风面积和规范风压形成基础端作用",
                $"采用基本风压 {project.MonitoringPole.BasicWindPressureKpa:F2} kPa；" +
                $"标准组合：N={load.VerticalKn:F2} kN，V={load.ShearXKn:F2} kN，M={load.MomentYKnM:F2} kN·m；" +
                $"基本组合：N={basic.VerticalKn:F2} kN，V={basic.ShearXKn:F2} kN，M={basic.MomentYKnM:F2} kN·m。",
                [
                    "杆高、杆径、壁厚、横臂及设备尺寸与项目条件一致。",
                    "地址风压及0.35 kPa下限的采用情况已经核对。",
                    "允许软件采用上述基础端荷载生成三种基础方案。"
                ]);
        }

        var tower = project.TowerMast;
        if (viewModel.IsMultiLegFoundation &&
            tower.UsesIndividualPileReactions)
        {
            var legBasicDescription = tower.BasicIndividualPileCompressionKn > 0
                ? $"；基本组合每腿压力 {tower.BasicIndividualPileCompressionKn:F2} kN、上拔力 {tower.BasicIndividualPileUpliftKn:F2} kN、水平力 {tower.BasicIndividualPileHorizontalKn:F2} kN"
                : "；未录入基本组合，结构验算将采用标准组合系数推导回退";
            return new StepConfirmationRequest(
                "确认一个塔脚的控制反力",
                $"{viewModel.PileLayoutSummary}；不得用整塔反力平均分配",
                $"每腿最大压力 {tower.IndividualPileCompressionKn:F2} kN；最大上拔力 {tower.IndividualPileUpliftKn:F2} kN；" +
                $"最大水平力 {tower.IndividualPileHorizontalKn:F2} kN{legBasicDescription}；控制工况：{tower.LoadCaseName}。",
                [
                    "标准组合用于每个基础单元的地基、稳定和变形验算，基本组合用于结构与配筋验算。",
                    "正负号、单位、控制工况及企业图集或厂家来源已经核对。",
                    "允许软件按一个塔脚的压力、拔力和水平力包络分别验算，并按基础单元数量汇总材料工程量。"
                ]);
        }

        var horizontal = Math.Sqrt(
            Math.Pow(tower.ShearXKn, 2) +
            Math.Pow(tower.ShearYKn, 2));
        var basicHorizontal = Math.Sqrt(
            Math.Pow(tower.BasicShearXKn, 2) +
            Math.Pow(tower.BasicShearYKn, 2));
        var basicDescription = tower.BasicVerticalKn > 0
            ? $"；基本组合N={tower.BasicVerticalKn:F2} kN，V={basicHorizontal:F2} kN，Mx={tower.BasicMomentXKnM:F2} kN·m，My={tower.BasicMomentYKnM:F2} kN·m"
            : "；未录入基本组合，结构验算将采用标准组合系数推导回退";
        return new StepConfirmationRequest(
            "确认塔桅基础端荷载",
            "塔桅基础端反力已包含风作用，不再按城市重复计算风荷载",
            $"N={tower.VerticalKn:F2} kN，V={horizontal:F2} kN，Mx={tower.MomentXKnM:F2} kN·m，" +
            $"My={tower.MomentYKnM:F2} kN·m{basicDescription}；控制工况：{tower.LoadCaseName}。",
            [
                "该组数据是地基承载力验算所需的基础端标准组合荷载。",
                "基本组合用于基础高度、冲切、受剪、受弯、配筋和材料强度验算。",
                "正负号、单位、控制工况及企业图集或厂家来源已经核对。",
                "允许软件采用上述荷载生成三种基础方案。"
            ]);
    }

    private void ForwardMouseWheelToPage(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject element)
        {
            return;
        }

        var pageScroller = FindAncestor<ScrollViewer>(element);
        if (pageScroller is null)
        {
            return;
        }

        pageScroller.ScrollToVerticalOffset(
            Math.Clamp(
                pageScroller.VerticalOffset - e.Delta,
                0,
                pageScroller.ScrollableHeight));
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
