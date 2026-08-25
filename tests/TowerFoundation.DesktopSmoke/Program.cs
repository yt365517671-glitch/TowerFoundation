using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TowerFoundation.Application;
using TowerFoundation.Desktop;
using TowerFoundation.Domain;
using TowerFoundation.Infrastructure;
using TowerFoundation.Licensing;

namespace TowerFoundation.DesktopSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var outputPath = args.FirstOrDefault() ??
                         Path.Combine(
                             AppContext.BaseDirectory,
                             "ui-smoke",
                             "main-window.png");

        var application = new App();
        application.InitializeComponent();

        var window = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            SuppressCloseConfirmationForAutomation = true,
            SuppressStepConfirmationForAutomation = true
        };

        window.Show();
        window.UpdateLayout();

        if (!window.IsLoaded || window.ActualWidth < 1180 || window.ActualHeight < 720)
        {
            Console.Error.WriteLine(
                $"FAIL 窗口尺寸或加载状态异常：Loaded={window.IsLoaded}, " +
                $"Size={window.ActualWidth:F0}×{window.ActualHeight:F0}");
            window.Close();
            return 1;
        }

        if (window.DataContext is not MainViewModel viewModel)
        {
            Console.Error.WriteLine("FAIL 主窗口未绑定 MainViewModel。");
            window.Close();
            return 1;
        }
        viewModel.SuppressErrorDialogsForAutomation = true;

        var previewLicenseDirectory = Path.Combine(
            Path.GetTempPath(),
            "TowerFoundation.LicensePreview.Smoke",
            Guid.NewGuid().ToString("N"));
        var previewManager = new ClientLicenseManager(
            new ClientLicenseStore(previewLicenseDirectory),
            "TJSM-FFFFF-FFFFF-FFFFF-FFFFF-FFFFF-F",
            LicenseTrust.RootPublicKeyBase64Url);
        var previewWindow = new MainWindow(previewManager)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            SuppressCloseConfirmationForAutomation = true,
            SuppressStepConfirmationForAutomation = true
        };
        previewWindow.Show();
        previewWindow.UpdateLayout();
        if (previewWindow.DataContext is not MainViewModel previewViewModel ||
            previewViewModel.IsFormalUseAuthorized ||
            previewViewModel.SaveProjectCommand.CanExecute(null) ||
            previewViewModel.GenerateSchemesCommand.CanExecute(null) ||
            previewViewModel.ImportGeotechnicalVisionPdfCommand.CanExecute(null) ||
            previewViewModel.ExportPrototypePackageCommand.CanExecute(null) ||
            previewWindow.FindName("LicenseStatusText") is not TextBlock licenseStatusText ||
            !licenseStatusText.Text.Contains("未授权", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("FAIL 未授权预览模式没有统一拦截计算、AI、保存和导出。" );
            previewWindow.Close();
            window.Close();
            return 1;
        }
        var previewPath = AddSuffix(outputPath, "-license-preview");
        RenderWindow(previewWindow, previewPath);
        var activationWindow = new LicenseActivationWindow(
            previewManager,
            previewManager.Assess(),
            allowPreview: true)
        {
            Owner = previewWindow,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -9000,
            Top = -9000
        };
        activationWindow.Show();
        activationWindow.UpdateLayout();
        if (activationWindow.FindName("MachineCodeBox") is not TextBox machineCodeBox ||
            machineCodeBox.Text != previewManager.MachineCode ||
            activationWindow.FindName("PreviewButton") is not Button { Visibility: Visibility.Visible })
        {
            Console.Error.WriteLine("FAIL 授权窗口未显示机器码或继续预览入口。" );
            activationWindow.Close();
            previewWindow.Close();
            window.Close();
            return 1;
        }
        var activationPath = AddSuffix(outputPath, "-license-activation");
        RenderWindow(activationWindow, activationPath);
        activationWindow.Close();
        previewWindow.Close();
        if (Directory.Exists(previewLicenseDirectory))
        {
            Directory.Delete(previewLicenseDirectory, true);
        }
        Console.WriteLine($"PASS 未授权预览与授权窗口截图 {previewPath} / {activationPath}");

        if (viewModel.Provinces.FirstOrDefault()?.Name != "甘肃省" ||
            viewModel.SelectedProvince?.Name != "甘肃省")
        {
            Console.Error.WriteLine("FAIL 新建项目地址应默认选择甘肃省，且甘肃省应位于省级列表第一位。");
            window.Close();
            return 1;
        }

        if (window.FindName("AppLogoImage") is not Image appLogo ||
            appLogo.Source is null)
        {
            Console.Error.WriteLine("FAIL 主窗口品牌Logo资源未加载。");
            window.Close();
            return 1;
        }

        if (window.FindName("TowerProjectCard") is not Border towerProjectCard ||
            window.FindName("MonitoringProjectCard") is not Border monitoringProjectCard ||
            Grid.GetColumn(towerProjectCard) != 0 ||
            Grid.GetColumn(monitoringProjectCard) != 2)
        {
            Console.Error.WriteLine("FAIL 首页工程入口顺序不正确，应为左塔桅、右监控杆。");
            window.Close();
            return 1;
        }

        if (window.FindName("TowerCatalogAvailabilityText") is not TextBlock catalogAvailability)
        {
            Console.Error.WriteLine("FAIL 首页没有显示企业塔型荷载库状态。");
            window.Close();
            return 1;
        }
        var catalogStatus = new EmbeddedTowerLoadCatalog().Status;
        var catalogDisplayIsValid = catalogStatus.IsCompleteForNewDesign
            ? catalogAvailability.Text.Contains("V2.0", StringComparison.Ordinal) &&
              catalogAvailability.Text.Contains("已就绪", StringComparison.Ordinal) &&
              !catalogAvailability.Text.Contains("废止", StringComparison.Ordinal) &&
              !catalogAvailability.Text.Contains("part1", StringComparison.OrdinalIgnoreCase)
            : catalogAvailability.Text.Contains("暂不可用", StringComparison.Ordinal) &&
              catalogAvailability.Text.Contains("手工荷载", StringComparison.Ordinal);
        if (!catalogDisplayIsValid)
        {
            Console.Error.WriteLine("FAIL 首页企业塔型荷载库状态与实际内置资源不一致。");
            window.Close();
            return 1;
        }

        if (window.FindName("GlobalAiProgressBar") is not ProgressBar globalAiProgressBar ||
            globalAiProgressBar.Maximum != 100)
        {
            Console.Error.WriteLine("FAIL 主流程未加载统一AI进度条。");
            window.Close();
            return 1;
        }

        var typePagePath = AddSuffix(outputPath, "-type");
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        RenderWindow(window, typePagePath);

        var smartProject = new ProjectModel
        {
            ProjectType = ProjectType.CommunicationTower,
            FoundationSettings = new FoundationDesignSettings
            {
                FoundationType = FoundationType.RigidRectangularShortPile
            }
        };
        var smartSettingsDirectory = Path.Combine(
            Path.GetTempPath(),
            "TowerFoundation.SmartCompletion.Smoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(smartSettingsDirectory);
        try
        {
            var smartSettings = new LocalApplicationSettingsService(smartSettingsDirectory);
            using var smartDeepSeek = new DeepSeekService(smartSettings);
            var smartWindow = new SmartSpecialtyCompletionWindow(
                smartProject,
                smartSettings,
                smartDeepSeek,
                new DocxTextExtractor(),
                new LocalPdfOcrService())
            {
                Owner = window,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -9000,
                Top = -9000
            };
            smartWindow.Show();
            smartWindow.UpdateLayout();
            var defaults = smartWindow.ApplyRecommendedDefaultsForAutomation();
            smartWindow.UpdateLayout();
            if (defaults.FilledCategoryCount < 2 ||
                smartProject.FoundationSettings.SpecialtyDesign.Settlement.AllowableSettlementMm != 350 ||
                smartProject.FoundationSettings.SpecialtyDesign.Crack.EnvironmentCategory.Contains("待确认", StringComparison.Ordinal) ||
                smartWindow.FindName("DeformationCard") is not Border { Visibility: Visibility.Visible } ||
                smartWindow.FindName("CrackCard") is not Border { Visibility: Visibility.Visible } ||
                smartWindow.FindName("ProgressPanel") is not Border)
            {
                Console.Error.WriteLine("FAIL 智能补齐窗口未按矩形刚性短柱桩显示适用项目或填入候选值。");
                smartWindow.Close();
                window.Close();
                return 1;
            }

            if (smartWindow.FindName("AnchorConnectionComboBox") is not ComboBox anchorConnectionBox ||
                smartWindow.FindName("AnchorParametersPanel") is not StackPanel anchorParametersPanel)
            {
                Console.Error.WriteLine("FAIL 智能补齐窗口缺少塔脚连接模板或锚栓参数区。");
                smartWindow.Close();
                window.Close();
                return 1;
            }
            anchorConnectionBox.SelectedValue = AnchorConnectionType.AnchorBoltCage;
            smartWindow.UpdateLayout();
            if (anchorParametersPanel.Visibility != Visibility.Visible)
            {
                Console.Error.WriteLine("FAIL 选择锚栓笼后没有显示锚栓参数。" );
                smartWindow.Close();
                window.Close();
                return 1;
            }
            anchorConnectionBox.SelectedValue = AnchorConnectionType.DirectEmbedded;
            smartWindow.UpdateLayout();
            if (anchorParametersPanel.Visibility != Visibility.Collapsed)
            {
                Console.Error.WriteLine("FAIL 选择直埋连接后仍要求填写锚栓参数。" );
                smartWindow.Close();
                window.Close();
                return 1;
            }

            var smartCompletionPath = AddSuffix(outputPath, "-smart-completion");
            RenderWindow(smartWindow, smartCompletionPath);
            smartWindow.Close();
            if (smartProject.FoundationSettings.SpecialtyDesign.Settlement.AllowableSettlementMm != 0)
            {
                Console.Error.WriteLine("FAIL 取消智能补齐窗口后没有回滚候选参数。" );
                window.Close();
                return 1;
            }
            Console.WriteLine($"PASS 智能补齐窗口截图 {smartCompletionPath}");
            Console.WriteLine("STAGE 智能补齐窗口完成，进入主流程地址与地勘页");
        }
        finally
        {
            Directory.Delete(smartSettingsDirectory, recursive: true);
        }

        var evidenceImport = new GeotechnicalDocumentImportResult
        {
            Document = new DocumentTextExtractionResult
            {
                SourceName = "界面测试地勘报告.pdf",
                Content = "第6章 地基土评价：建议持力层承载力特征值fak=150kPa。勘察期间稳定地下水埋深5.0m。"
            },
            AiResult = new GeotechnicalAiExtractionResult
            {
                ProjectName = "界面测试工程",
                BearingCapacityKpa = 150,
                CharacteristicBearingCapacityKpa = 150,
                SoilUnitWeightKnPerM3 = 18,
                GroundwaterDepthM = 5,
                Evidence = "原文第6章及地层参数表",
                Confidence = 0.91
            }
        };
        var evidenceWindow = new GeotechnicalEvidenceReviewWindow(evidenceImport)
        {
            Owner = window,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -9000,
            Top = -9000
        };
        evidenceWindow.Show();
        evidenceWindow.UpdateLayout();
        if (!evidenceWindow.SourceText.Contains("fak=150", StringComparison.Ordinal) ||
            evidenceWindow.CandidateRows.Count < 4 ||
            evidenceWindow.FindName("CandidateEvidenceGrid") is not DataGrid evidenceGrid ||
            evidenceGrid.Items.Count != evidenceWindow.CandidateRows.Count)
        {
            Console.Error.WriteLine("FAIL AI候选复核窗口没有并排展示本机原文与结构化候选。" );
            evidenceWindow.Close();
            window.Close();
            return 1;
        }
        var evidenceReviewPath = AddSuffix(outputPath, "-ai-evidence-review");
        RenderWindow(evidenceWindow, evidenceReviewPath);
        evidenceWindow.Close();
        Console.WriteLine($"PASS AI原文与候选复核窗口截图 {evidenceReviewPath}");

        viewModel.SelectMonitoringProjectCommand.Execute(null);
        if (viewModel.Project.ProjectType != TowerFoundation.Domain.ProjectType.MonitoringPole)
        {
            Console.Error.WriteLine("FAIL 监控杆入口未切换项目类型。");
            window.Close();
            return 1;
        }

        viewModel.SelectedStep = 1;
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var windowWidthBeforeAddress = window.ActualWidth;
        var windowHeightBeforeAddress = window.ActualHeight;
        var projectStepContent = window.FindName("ProjectStepContent") as FrameworkElement;
        var projectWidthBeforeAddress = projectStepContent?.ActualWidth ?? 0;

        viewModel.SelectedProvince = viewModel.Provinces.Single(item => item.Name == "甘肃省");
        viewModel.SelectedCity = viewModel.Cities.Single(item => item.Name == "兰州市");
        viewModel.SelectedCounty = viewModel.Counties.Single(item => item.Name == "城关区");
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        if (projectStepContent is null ||
            Math.Abs(window.ActualWidth - windowWidthBeforeAddress) > 0.5 ||
            Math.Abs(window.ActualHeight - windowHeightBeforeAddress) > 0.5 ||
            Math.Abs(projectStepContent.ActualWidth - projectWidthBeforeAddress) > 0.5)
        {
            Console.Error.WriteLine(
                $"FAIL 省市县选择改变界面尺寸：窗口 {windowWidthBeforeAddress:F0}×{windowHeightBeforeAddress:F0} -> " +
                $"{window.ActualWidth:F0}×{window.ActualHeight:F0}，项目页 {projectWidthBeforeAddress:F0} -> {projectStepContent?.ActualWidth:F0}。");
            window.Close();
            return 1;
        }

        if (window.FindName("CityBox") is not ComboBox cityBox)
        {
            Console.Error.WriteLine("FAIL 未找到城市下拉框。");
            window.Close();
            return 1;
        }
        cityBox.IsDropDownOpen = true;
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        cityBox.IsDropDownOpen = false;
        if (Math.Abs(window.ActualWidth - windowWidthBeforeAddress) > 0.5 ||
            Math.Abs(window.ActualHeight - windowHeightBeforeAddress) > 0.5)
        {
            Console.Error.WriteLine("FAIL 展开城市下拉弹层改变了主窗口尺寸。");
            window.Close();
            return 1;
        }
        if (Math.Abs(viewModel.BasicWindPressureKpa - 0.35) > 1e-9 ||
            !viewModel.WindPressureSourceBadge.Contains("0.35") ||
            !viewModel.WindPressureSummary.Contains("0.30") ||
            !viewModel.WindPressureSummary.Contains("0.35"))
        {
            Console.Error.WriteLine(
                $"FAIL 地址风压联动异常：w0={viewModel.BasicWindPressureKpa:F2}，来源={viewModel.WindPressureSourceBadge}");
            window.Close();
            return 1;
        }

        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var addressPagePath = AddSuffix(outputPath, "-address");
        RenderWindow(window, addressPagePath);

        viewModel.Project.Geotechnical.UseBearingCapacityCorrection = true;
        viewModel.Project.Geotechnical.BearingCapacityKpa = 110;
        viewModel.Project.Geotechnical.CharacteristicBearingCapacityKpa = 110;
        viewModel.Project.Geotechnical.SoilUnitWeightKnPerM3 = 21;
        viewModel.Project.Geotechnical.BearingCapacityWidthCorrectionFactor = 0.3;
        viewModel.Project.Geotechnical.BearingCapacityDepthCorrectionFactor = 1.5;
        window.DataContext = null;
        window.DataContext = viewModel;
        viewModel.NavigateToStep(
            2,
            TowerFoundation.Domain.ProjectStage.SiteReady);
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var geotechnicalPagePath = AddSuffix(outputPath, "-geotechnical-rules");
        RenderWindow(window, geotechnicalPagePath);
        Console.WriteLine("STAGE 地勘页截图完成，开始滚轮与下一步交互");

        if (window.FindName("GeotechnicalHistoryComboBox") is not ComboBox ||
            window.FindName("ReuseGeotechnicalHistoryButton") is not Button ||
            window.FindName("ReanalyzeGeotechnicalHistoryButton") is not Button ||
            window.FindName("DeleteGeotechnicalHistoryButton") is not Button ||
            !viewModel.GeotechnicalHistorySummary.Contains("本机", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("FAIL 地勘页缺少本机分析记录选择、引用、重新分析或主动删除入口。" );
            window.Close();
            return 1;
        }

        viewModel.IsRigidCircularShortPileFoundation = true;
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        if (window.FindName("GeotechnicalStepScrollViewer") is not ScrollViewer geotechnicalScroller ||
            window.FindName("RigidShortPileSoilLayerGrid") is not DataGrid rigidSoilLayerGrid)
        {
            Console.Error.WriteLine("FAIL 未找到步骤3整页滚动容器或刚性桩土层表。");
            window.Close();
            return 1;
        }
        geotechnicalScroller.ScrollToVerticalOffset(geotechnicalScroller.ScrollableHeight / 2);
        var geotechnicalMidpoint = geotechnicalScroller.VerticalOffset;
        rigidSoilLayerGrid.RaiseEvent(new MouseWheelEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            -120)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent
        });
        window.UpdateLayout();
        if (geotechnicalScroller.VerticalOffset <= geotechnicalMidpoint)
        {
            Console.Error.WriteLine("FAIL 步骤3滚到页面中段后，鼠标位于地勘土层表时整页滚轮没有继续移动。");
            window.Close();
            return 1;
        }
        viewModel.IsRectangularFoundation = true;
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);

        if (FindVisualChildren<CheckBox>(window).Any(item =>
                Convert.ToString(item.Content)?.StartsWith("我已", StringComparison.Ordinal) == true))
        {
            Console.Error.WriteLine("FAIL 地勘页仍存在旧式“我已核对”确认勾选框。");
            window.Close();
            return 1;
        }
        if (window.FindName("GeotechnicalConfirmationNotice") is not null)
        {
            Console.Error.WriteLine("FAIL 地勘页仍存在无实际作用的确认说明条。");
            window.Close();
            return 1;
        }
        if (window.FindName("GeotechnicalNextButton") is not Button geotechnicalNextButton)
        {
            Console.Error.WriteLine("FAIL 未找到地勘下一步按钮。");
            window.Close();
            return 1;
        }
        geotechnicalNextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Console.WriteLine("STAGE 地勘下一步点击完成");
        if (!viewModel.Project.Geotechnical.IsConfirmed ||
            viewModel.SelectedStep != 3 ||
            viewModel.Project.AuditTrail.All(item => item.Action != "弹窗确认地勘参数"))
        {
            Console.Error.WriteLine("FAIL 地勘下一步没有通过二级确认流程写入确认状态并继续。");
            window.Close();
            return 1;
        }
        if (window.FindName("MonitoringDrawingVisionButton") is not Button ||
            window.FindName("MonitoringDrawingCandidateGrid") is not DataGrid monitoringCandidateGrid ||
            window.FindName("ApplyMonitoringDrawingCandidateButton") is not Button ||
            window.FindName("ApplyMonitoringMissingInputsButton") is not Button ||
            window.FindName("MonitoringMissingInputPanel") is not Border missingInputPanel ||
            window.FindName("MonitoringParameterGuideExpander") is not Expander parameterGuideExpander ||
            window.FindName("MonitoringParameterGuideImage") is not Image parameterGuideImage ||
            parameterGuideImage.Source is null ||
            window.FindName("MonitoringArmSegmentGrid") is not DataGrid)
        {
            Console.Error.WriteLine("FAIL 监控杆荷载页缺少视觉识图、二次补录、参数位置图或分段明细控件。" );
            window.Close();
            return 1;
        }
        if (monitoringCandidateGrid.Columns.Count != 9 ||
            monitoringCandidateGrid.Columns[0] is not DataGridTemplateColumn ||
            monitoringCandidateGrid.Columns[7] is not DataGridTextColumn ||
            monitoringCandidateGrid.Columns[8] is not DataGridTemplateColumn)
        {
            Console.Error.WriteLine("FAIL 监控杆候选表没有使用单击复选框，或仍把只读冲突状态伪装成复选框。" );
            window.Close();
            return 1;
        }
        if (viewModel.PoleHeightInput.HasValue ||
            viewModel.ArmMountingHeightInput.HasValue ||
            viewModel.AttachmentProjectedAreaInput.HasValue ||
            viewModel.AttachmentWeightInput.HasValue)
        {
            Console.Error.WriteLine("FAIL 新建监控杆项目仍把内部样例数值显示为用户输入。" );
            window.Close();
            return 1;
        }
        var tooltipLabels = new HashSet<string>(StringComparer.Ordinal)
        {
            "立杆高度（m）", "立杆下端尺寸（mm）", "立杆上端尺寸（mm）", "立杆壁厚（mm）",
            "横杆安装高度（m）", "横杆长度（m）", "横杆近端尺寸（mm）", "横杆远端尺寸（mm）",
            "横杆壁厚（mm）", "横杆数量", "设备迎风面积（m²）", "设备重量（kN）"
        };
        if (FindVisualChildren<TextBlock>(window)
            .Where(item => tooltipLabels.Contains(item.Text))
            .Any(item => item.ToolTip is null))
        {
            Console.Error.WriteLine("FAIL 监控杆数值标签没有完整配置鼠标悬停解释。" );
            window.Close();
            return 1;
        }
        var originalAttachmentArea = viewModel.Project.MonitoringPole.AttachmentProjectedAreaM2;
        var originalAttachmentWeight = viewModel.Project.MonitoringPole.AttachmentWeightKn;
        var originalPoleHeight = viewModel.Project.MonitoringPole.PoleHeightM;
        var originalArmMountingHeight = viewModel.Project.MonitoringPole.ArmMountingHeightM;
        var partialDrawingCandidate = CreatePartialMonitoringDrawingSmokeCandidate();
        viewModel.Project.MonitoringDrawingCandidates.Add(partialDrawingCandidate);
        viewModel.MonitoringDrawingCandidates.Add(partialDrawingCandidate);
        viewModel.SelectedMonitoringDrawingCandidate = partialDrawingCandidate;
        monitoringCandidateGrid.BringIntoView();
        monitoringCandidateGrid.ScrollIntoView(partialDrawingCandidate.Fields[0]);
        window.UpdateLayout();
        window.Dispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
        var firstCandidateRow = monitoringCandidateGrid.ItemContainerGenerator
            .ContainerFromItem(partialDrawingCandidate.Fields[0]) as DataGridRow;
        var candidateRowCheckBoxes = firstCandidateRow is null
            ? []
            : FindVisualChildren<CheckBox>(firstCandidateRow).ToArray();
        if (candidateRowCheckBoxes.Length != 2)
        {
            Console.Error.WriteLine("FAIL 监控杆候选行未生成可直接操作的采用与人工确认复选框。" );
            window.Close();
            return 1;
        }
        candidateRowCheckBoxes[0].IsChecked = false;
        candidateRowCheckBoxes[0].GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();
        if (partialDrawingCandidate.Fields[0].IsSelected)
        {
            Console.Error.WriteLine("FAIL 单击式采用复选框未把取消勾选写回候选字段。" );
            window.Close();
            return 1;
        }
        candidateRowCheckBoxes[0].IsChecked = true;
        candidateRowCheckBoxes[0].GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();
        candidateRowCheckBoxes[1].IsChecked = true;
        candidateRowCheckBoxes[1].GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();
        if (!partialDrawingCandidate.Fields[0].IsSelected ||
            !partialDrawingCandidate.Fields[0].IsManuallyConfirmed)
        {
            Console.Error.WriteLine("FAIL 采用与人工确认复选框未双向写回候选字段。" );
            window.Close();
            return 1;
        }
        Console.WriteLine("PASS 监控杆候选采用与人工确认支持单击式双向勾选，冲突状态为只读文字。" );
        viewModel.ApplyMonitoringDrawingCandidateCommand.Execute(null);
        if (Math.Abs(viewModel.Project.MonitoringPole.PoleHeightM - 6.5) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.ArmMountingHeightM - originalArmMountingHeight) > 1e-12 ||
            viewModel.Project.MonitoringPole.ExplicitDrawingInputFields.Contains(
                MonitoringDrawingFieldNames.ArmMountingHeight) ||
            viewModel.MonitoringMissingInputs.Count != 9 ||
            !viewModel.StatusMessage.Contains("第二次人工补录", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("FAIL 部分图纸候选没有采用可靠立杆高度并把未识别项转入空白二次补录。" );
            window.Close();
            return 1;
        }
        Console.WriteLine("PASS 部分图纸候选只采用可靠字段，未识别参数以空白项进入第二次人工补录。" );

        var reconciledDrawingCandidate = CreateReconciledMonitoringDrawingSmokeCandidate();
        var fieldsRequiringConfirmation = reconciledDrawingCandidate.Fields
            .Where(field => field.Value.HasValue && field.Warning.Contains("本地纠正", StringComparison.Ordinal))
            .ToArray();
        if (fieldsRequiringConfirmation.Length != 4 ||
            fieldsRequiringConfirmation.Any(field => field.IsSelected))
        {
            Console.Error.WriteLine("FAIL 原始规格与视觉结构化值冲突时，没有拦截并要求人工确认。" );
            window.Close();
            return 1;
        }
        foreach (var field in fieldsRequiringConfirmation)
        {
            field.IsSelected = true;
            field.IsManuallyConfirmed = true;
        }
        viewModel.Project.MonitoringPole.ArmMountingHeightM = 6.5;
        viewModel.Project.MonitoringDrawingCandidates.Add(reconciledDrawingCandidate);
        viewModel.MonitoringDrawingCandidates.Add(reconciledDrawingCandidate);
        viewModel.SelectedMonitoringDrawingCandidate = reconciledDrawingCandidate;
        viewModel.ApplyMonitoringDrawingCandidateCommand.Execute(null);
        if (Math.Abs(viewModel.Project.MonitoringPole.PoleHeightM - 6.5) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.PoleBottomDiameterM - 0.240) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.PoleTopDiameterM - 0.180) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.PoleWallThicknessM - 0.005) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.ArmLengthM - 3.0) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.ArmNearDiameterM - 0.160) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.ArmFarDiameterM - 0.090) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.ArmWallThicknessM - 0.004) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.AttachmentProjectedAreaM2 - originalAttachmentArea) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.AttachmentWeightKn - originalAttachmentWeight) > 1e-12)
        {
            Console.Error.WriteLine("FAIL H6.5-L3原始规格证据经人工确认后未正确回填，或缺失设备字段覆盖了原值。" );
            window.Close();
            return 1;
        }
        Console.WriteLine("PASS H6.5-L3视觉误值按原始规格语义纠正，图纸未给设备参数不标记为正式输入。" );

        var drawingCandidate = CreateMonitoringDrawingSmokeCandidate();
        viewModel.Project.MonitoringDrawingCandidates.Add(drawingCandidate);
        viewModel.MonitoringDrawingCandidates.Add(drawingCandidate);
        viewModel.SelectedMonitoringDrawingCandidate = drawingCandidate;
        if (!viewModel.ApplyMonitoringDrawingCandidateCommand.CanExecute(null))
        {
            Console.Error.WriteLine("FAIL 监控杆视觉候选采用命令未在荷载步骤启用。" );
            window.Close();
            return 1;
        }
        viewModel.ApplyMonitoringDrawingCandidateCommand.Execute(null);
        if (viewModel.Project.MonitoringPole.PoleSectionType != TubeSectionType.RegularOctagonDiagonalTube ||
            viewModel.Project.MonitoringPole.ArmSectionType != TubeSectionType.RegularOctagonDiagonalTube ||
            viewModel.Project.MonitoringPole.ArmSegments.Count != 2 ||
            Math.Abs(viewModel.Project.MonitoringPole.ArmSegments[0].WallThicknessM - 0.006) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.ArmSegments[1].WallThicknessM - 0.004) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.AttachmentProjectedAreaM2 - originalAttachmentArea) > 1e-12 ||
            Math.Abs(viewModel.Project.MonitoringPole.AttachmentWeightKn - originalAttachmentWeight) > 1e-12)
        {
            Console.Error.WriteLine("FAIL 监控杆视觉候选未正确设置八边形/分段，或缺失设备字段覆盖了原值。" );
            window.Close();
            return 1;
        }
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        missingInputPanel.BringIntoView();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        RenderWindow(window, AddSuffix(outputPath, "-monitoring-load-vision"));
        parameterGuideExpander.BringIntoView();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        RenderWindow(window, AddSuffix(outputPath, "-monitoring-parameter-guide"));
        if (!viewModel.HasMonitoringMissingInputs ||
            viewModel.MonitoringMissingInputs.Count != 3 ||
            viewModel.AttachmentProjectedAreaInput.HasValue ||
            viewModel.AttachmentWeightInput.HasValue ||
            viewModel.MonitoringMissingInputs.Any(item => !string.IsNullOrEmpty(item.InputText)))
        {
            Console.Error.WriteLine("FAIL H6.5-L14采用后未将横杆数量、设备面积和设备重量作为空白二次补录项突出显示。" );
            window.Close();
            return 1;
        }
        foreach (var item in viewModel.MonitoringMissingInputs)
        {
            item.InputText = item.FieldName switch
            {
                MonitoringDrawingFieldNames.ArmCount => "1",
                MonitoringDrawingFieldNames.AttachmentProjectedArea => "0.35",
                MonitoringDrawingFieldNames.AttachmentWeight => "0.25",
                _ => throw new InvalidOperationException($"unexpected missing field: {item.FieldName}")
            };
        }
        if (!viewModel.ApplyMonitoringMissingInputsCommand.CanExecute(null))
        {
            Console.Error.WriteLine("FAIL 二次人工补录命令未启用。" );
            window.Close();
            return 1;
        }
        viewModel.ApplyMonitoringMissingInputsCommand.Execute(null);
        if (viewModel.HasMonitoringMissingInputs ||
            viewModel.MonitoringMissingInputs.Count != 0 ||
            viewModel.AttachmentProjectedAreaInput != 0.35 ||
            viewModel.AttachmentWeightInput != 0.25 ||
            viewModel.Project.AuditTrail.All(item => item.Action != "人工补齐监控杆图纸缺失参数"))
        {
            Console.Error.WriteLine("FAIL 二次人工补录没有写入显式输入状态或留下审计记录。" );
            window.Close();
            return 1;
        }
        Console.WriteLine("PASS 图纸未识别项保持空白，二次补录通过本地校验后才解除流程门禁。" );
        if (window.FindName("LoadNextButton") is not Button loadNextButton)
        {
            Console.Error.WriteLine("FAIL 未找到荷载下一步按钮。");
            window.Close();
            return 1;
        }
        loadNextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Console.WriteLine("STAGE 荷载下一步点击完成");
        if (viewModel.SelectedStep != 4 ||
            viewModel.Project.AuditTrail.All(item => item.Action != "弹窗确认荷载输入") ||
            viewModel.Schemes.Count != 0 ||
            viewModel.SelectedCandidate is not null ||
            viewModel.Project.AuditTrail.All(item => item.Action != "确认专项参数与来源"))
        {
            Console.Error.WriteLine("FAIL 荷载下一步应只确认荷载、整理设计参数并进入方案页，不得提前执行尺寸搜索。" );
            window.Close();
            return 1;
        }
        if (window.FindName("StartAutomaticDesignButton") is not Button startAutomaticDesignButton ||
            startAutomaticDesignButton.Visibility != Visibility.Visible ||
            window.FindName("AwaitingSchemeGenerationCard") is not Border awaitingCard ||
            awaitingCard.Visibility != Visibility.Visible)
        {
            Console.Error.WriteLine("FAIL 基础方案页没有显示独立的“开始自动设计”入口。" );
            window.Close();
            return 1;
        }
        Console.WriteLine("STAGE 荷载下一步只完成参数准备，开始在方案页执行尺寸搜索");
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        RenderWindow(window, AddSuffix(outputPath, "-scheme-ready"));
        startAutomaticDesignButton.Command.Execute(startAutomaticDesignButton.CommandParameter);
        Console.WriteLine("STAGE 方案页自动设计完成");
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);

        if (viewModel.Schemes.Count != 3 ||
            viewModel.SelectedCandidate is null ||
            viewModel.SelectedCandidate.Preference != OptimizationPreference.Constructability ||
            viewModel.LoadPreview is null)
        {
            Console.Error.WriteLine(
                $"FAIL UI工作流未生成三方案：Schemes={viewModel.Schemes.Count}");
            window.Close();
            return 1;
        }
        viewModel.SelectedStep = 2;
        window.UpdateLayout();
        window.Dispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
        var workflowReturnButton = window.FindName("WorkflowReturnButton") as Button;
        if (!viewModel.IsBrowsingWorkflowStep ||
            window.FindName("GeotechnicalStepContent") is not FrameworkElement
            {
                IsEnabled: false
            } ||
            window.FindName("GeotechnicalNextButton") is not Button
            {
                Visibility: Visibility.Collapsed
            } ||
            workflowReturnButton is not
            {
                Visibility: Visibility.Visible,
                IsEnabled: true
            } ||
            viewModel.ImportGeotechnicalPdfCommand.CanExecute(null))
        {
            Console.Error.WriteLine("FAIL 跨流程查看未进入只读模式、未隐藏下一步或仍允许执行导入。" );
            window.Close();
            return 1;
        }
        RenderWindow(window, AddSuffix(outputPath, "-workflow-browse"));
        workflowReturnButton.Command.Execute(workflowReturnButton.CommandParameter);
        window.UpdateLayout();
        if (viewModel.SelectedStep != viewModel.CurrentWorkflowStep ||
            viewModel.IsBrowsingWorkflowStep ||
            window.FindName("FoundationStepContent") is not FrameworkElement
            {
                IsEnabled: true
            })
        {
            Console.Error.WriteLine(
                $"FAIL 回到当前执行流程未恢复真实步骤及可操作状态：" +
                $"Selected={viewModel.SelectedStep}, Current={viewModel.CurrentWorkflowStep}, " +
                $"Browsing={viewModel.IsBrowsingWorkflowStep}, " +
                $"FoundationEnabled={(window.FindName("FoundationStepContent") as FrameworkElement)?.IsEnabled}。" );
            window.Close();
            return 1;
        }
        if (window.FindName("AdvancedDesignParametersExpander") is not Expander
            {
                IsExpanded: false
            })
        {
            Console.Error.WriteLine("FAIL 普通流程没有默认折叠复杂的高级设计参数。" );
            window.Close();
            return 1;
        }
        if (viewModel.SelectedCandidate.ReinforcementDesigns.Count < 2 ||
            window.FindName("CandidateReinforcementGrid") is not DataGrid candidateRebarGrid ||
            candidateRebarGrid.Items.Count < 2)
        {
            Console.Error.WriteLine("FAIL 步骤5没有展示浅基础X/Y向结构化配筋结果。");
            window.Close();
            return 1;
        }
        if (window.FindName("SmartSpecialtyCompletionButton") is not Button smartCompletionButton)
        {
            Console.Error.WriteLine("FAIL 步骤5没有提供统一的智能补齐入口。" );
            window.Close();
            return 1;
        }
        if (window.FindName("AdvancedSpecialtyCompletionButton") is not Button)
        {
            Console.Error.WriteLine("FAIL 步骤5没有把复杂专业参数降为可选入口。" );
            window.Close();
            return 1;
        }
        Console.WriteLine("STAGE 开始应用专项智能补齐");
        smartCompletionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Console.WriteLine("STAGE 专项智能补齐点击处理完成");
        window.UpdateLayout();
        window.Dispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
        Console.WriteLine("STAGE 专项智能补齐布局完成");
        if (viewModel.Project.AuditTrail.All(item => item.Action != "确认专项参数与来源"))
        {
            Console.Error.WriteLine("FAIL 智能补齐入口没有应用参数并触发重新计算。" );
            window.Close();
            return 1;
        }
        if (!viewModel.Project.FoundationSettings.SpecialtyDesign.Crack.Source.IsConfirmed ||
            !viewModel.Project.FoundationSettings.SpecialtyDesign.PedestalStructure.Source.IsConfirmed ||
            !viewModel.Project.FoundationSettings.SpecialtyDesign.Hydrogeology.Source.IsConfirmed ||
            !viewModel.SpecialtyReadinessSummary.Contains("基础方案可以继续计算", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("FAIL 自动补齐没有确认安全候选、复用地勘地下水或更新傻瓜式完成提示。" );
            window.Close();
            return 1;
        }
        RenderWindow(window, AddSuffix(outputPath, "-auto-completion"));
        var centeredHeader = FindVisualChild<DataGridColumnHeader>(candidateRebarGrid);
        var centeredCell = FindVisualChild<DataGridCell>(candidateRebarGrid);
        if (centeredHeader is null || centeredCell is null ||
            centeredHeader.HorizontalContentAlignment != HorizontalAlignment.Center ||
            centeredHeader.VerticalContentAlignment != VerticalAlignment.Center ||
            centeredCell.HorizontalContentAlignment != HorizontalAlignment.Center ||
            centeredCell.VerticalContentAlignment != VerticalAlignment.Center)
        {
            Console.Error.WriteLine("FAIL DataGrid表头或单元格文字没有统一居中。");
            window.Close();
            return 1;
        }

        if (window.FindName("FoundationStepScrollViewer") is not ScrollViewer foundationScroller)
        {
            Console.Error.WriteLine("FAIL 未找到步骤5整页滚动容器。");
            window.Close();
            return 1;
        }
        foundationScroller.ScrollToTop();
        candidateRebarGrid.RaiseEvent(new MouseWheelEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            -120)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent
        });
        window.UpdateLayout();
        if (foundationScroller.VerticalOffset <= 0)
        {
            Console.Error.WriteLine("FAIL 步骤5鼠标位于DataGrid时整页滚轮没有继续移动。");
            window.Close();
            return 1;
        }

        viewModel.CustomBaseLengthM = 0.8;
        viewModel.CustomBaseWidthM = 0.8;
        viewModel.CustomBaseThicknessM = 0.3;
        Console.WriteLine("STAGE 开始复算不满足的小尺寸");
        viewModel.EvaluateCustomSchemeCommand.Execute(null);
        Console.WriteLine("STAGE 小尺寸复算完成");
        window.UpdateLayout();
        if (window.FindName("ScopeAndInputGrid") is not DataGrid scopeAndInputGrid ||
            window.FindName("DeliveryReminderGrid") is not DataGrid deliveryReminderGrid ||
            scopeAndInputGrid.Items.Count == 0 ||
            deliveryReminderGrid.Items.Count == 0 ||
            !scopeAndInputGrid.Columns.OfType<DataGridTemplateColumn>().Any(column =>
                string.Equals(Convert.ToString(column.Header), "处理", StringComparison.Ordinal)))
        {
            Console.Error.WriteLine("FAIL 待补参数、专项复核与施工交付提醒没有在步骤5正确分栏。" );
            window.Close();
            return 1;
        }
        if (viewModel.CustomScheme?.IsFeasible != false ||
            viewModel.AdjustmentAdvices.Count == 0)
        {
            Console.Error.WriteLine("FAIL UI尺寸复算未返回调整建议。");
            window.Close();
            return 1;
        }

        var candidateForCustomAdoption = viewModel.SelectedCandidate!;
        viewModel.CustomBaseLengthM = candidateForCustomAdoption.Geometry.BaseLengthM;
        viewModel.CustomBaseWidthM = candidateForCustomAdoption.Geometry.BaseWidthM;
        viewModel.CustomBaseThicknessM = candidateForCustomAdoption.Geometry.BaseThicknessM;
        viewModel.CustomPileDiameterM = candidateForCustomAdoption.Geometry.PileDiameterM;
        viewModel.CustomPileLengthM = candidateForCustomAdoption.Geometry.PileLengthM;
        Console.WriteLine("STAGE 开始复算可采用尺寸");
        viewModel.EvaluateCustomSchemeCommand.Execute(null);
        Console.WriteLine("STAGE 可采用尺寸复算完成");
        if (viewModel.CustomScheme?.IsFeasible != true ||
            !viewModel.AdoptCustomSchemeCommand.CanExecute(null))
        {
            Console.Error.WriteLine("FAIL 可行自定义尺寸没有启用采用方案按钮。");
            window.Close();
            return 1;
        }

        var auditCountBeforeAdoption = viewModel.Project.AuditTrail.Count;
        Console.WriteLine("STAGE 开始采用自定义方案");
        viewModel.AdoptCustomSchemeCommand.Execute(null);
        Console.WriteLine("STAGE 自定义方案采用完成");
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        if (!window.IsLoaded ||
            viewModel.SelectedStep != 5 ||
            viewModel.SelectedScheme?.Name != "自定义方案" ||
            viewModel.Project.AuditTrail.Count != auditCountBeforeAdoption + 1 ||
            window.FindName("AuditTrailGrid") is not DataGrid adoptedAuditGrid ||
            adoptedAuditGrid.Items.Count != viewModel.Project.AuditTrail.Count)
        {
            Console.Error.WriteLine("FAIL 采用自定义方案后未稳定进入成果页，或过程审计表未同步。");
            window.Close();
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        RenderWindow(window, outputPath);

        viewModel.SelectSchemeCommand.Execute(null);
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var selectedSchemeForOutput = viewModel.SelectedScheme;
        if (selectedSchemeForOutput is null ||
            selectedSchemeForOutput.ReinforcementDesigns.Count < 4 ||
            !selectedSchemeForOutput.ReinforcementDesigns.Any(item =>
                item.Component.Contains("短柱纵筋", StringComparison.Ordinal)) ||
            !selectedSchemeForOutput.ReinforcementDesigns.Any(item =>
                item.Component.Contains("短柱箍筋", StringComparison.Ordinal)) ||
            window.FindName("SelectedReinforcementGrid") is not DataGrid selectedRebarGrid ||
            window.FindName("ExportAllButton") is not Button exportAllButton ||
            window.FindName("ExportAsButton") is not Button exportAsButton ||
            !exportAllButton.IsEnabled ||
            !exportAsButton.IsEnabled)
        {
            Console.Error.WriteLine("FAIL 步骤6配筋表或成果导出按钮未启用。");
            window.Close();
            return 1;
        }
        if (window.FindName("OutputStepScrollViewer") is not ScrollViewer outputScroller ||
            window.FindName("AuditTrailGrid") is not DataGrid auditGrid)
        {
            Console.Error.WriteLine("FAIL 未找到步骤6滚动容器或过程审计表。");
            window.Close();
            return 1;
        }
        outputScroller.ScrollToTop();
        auditGrid.RaiseEvent(new MouseWheelEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            -120)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent
        });
        window.UpdateLayout();
        if (outputScroller.VerticalOffset <= 0)
        {
            Console.Error.WriteLine("FAIL 步骤6鼠标位于DataGrid时整页滚轮没有继续移动。");
            window.Close();
            return 1;
        }
        var outputStepPath = AddSuffix(outputPath, "-output-step");
        RenderWindow(window, outputStepPath);
        outputScroller.ScrollToEnd();
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var outputExportPath = AddSuffix(outputPath, "-output-export");
        RenderWindow(window, outputExportPath);

        viewModel.NewProjectCommand.Execute(null);
        viewModel.SelectTowerProjectCommand.Execute(null);
        viewModel.NavigateToStep(
            2,
            TowerFoundation.Domain.ProjectStage.SiteReady);
        viewModel.SelectedProvince = viewModel.Provinces.First(item => item.Name == "甘肃省");
        viewModel.SelectedCity = viewModel.Cities.First(item => item.Name == "兰州市");
        viewModel.SelectedCounty = viewModel.Counties.First(item => item.Name == "城关区");
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var towerProvinceBox = window.FindName("ProvinceBox") as ComboBox;
        var towerCityBox = window.FindName("CityBox") as ComboBox;
        var towerCountyBox = window.FindName("CountyBox") as ComboBox;
        if (viewModel.Project.Province != "甘肃省" ||
            viewModel.Project.City != "兰州市" ||
            viewModel.Project.County != "城关区" ||
            viewModel.Project.Geotechnical.SeismicIntensityDegree != 8 ||
            Math.Abs(viewModel.Project.Geotechnical.DesignBasicGroundAccelerationG - 0.20) > 1e-9 ||
            viewModel.Project.Geotechnical.DesignEarthquakeGroup != "第三组" ||
            towerProvinceBox is null ||
            towerCityBox is null ||
            towerCountyBox is null ||
            towerProvinceBox.Visibility != Visibility.Visible)
        {
            Console.Error.WriteLine(
                $"FAIL 通信塔桅地勘页未保留建设地点或未带出抗震参数：" +
                $"Province='{viewModel.Project.Province}', City='{viewModel.Project.City}', " +
                $"County='{viewModel.Project.County}', Intensity={viewModel.Project.Geotechnical.SeismicIntensityDegree}, " +
                $"Acceleration={viewModel.Project.Geotechnical.DesignBasicGroundAccelerationG:F2}, " +
                $"Group='{viewModel.Project.Geotechnical.DesignEarthquakeGroup}'。");
            window.Close();
            return 1;
        }
        var towerProjectPagePath = AddSuffix(outputPath, "-tower-project");
        RenderWindow(window, towerProjectPagePath);
        viewModel.Project.Geotechnical.IsConfirmed = true;
        var towerPagePath = AddSuffix(outputPath, "-tower-load");
        var towerPickerPopupPath = AddSuffix(outputPath, "-tower-picker-popup");
        if (catalogStatus.HasCurrentRecords)
        {
            viewModel.IsTowerCatalogLoad = true;
        viewModel.NavigateToStep(
            3,
            TowerFoundation.Domain.ProjectStage.GeotechnicalReady);
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        if (FindVisualChildren<TextBlock>(window).Any(item =>
                item.Text == "塔桅识别") ||
            window.FindName("TowerManualMetadataPanel") is not Border towerManualMetadataPanel ||
            towerManualMetadataPanel.Visibility != Visibility.Collapsed)
        {
            Console.Error.WriteLine("FAIL 企业塔型库模式仍显示重复的塔桅识别/手工信息区。");
            window.Close();
            return 1;
        }
        if (viewModel.TowerCatalogSources.Count != 4 ||
            viewModel.TowerCatalogTypes.Count != 11 ||
            viewModel.FilteredTowerCatalogRecords.Count != 446 ||
            viewModel.SelectedTowerCatalogRecord is not null ||
            viewModel.ApplyTowerCatalogLoadCommand.CanExecute(null) ||
            viewModel.TowerCatalogStatus.Contains("废止", StringComparison.Ordinal) ||
            viewModel.TowerCatalogStatus.Contains("part1", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("FAIL V2.0塔型分类或型号级联没有载入446条现行反力记录。");
            window.Close();
            return 1;
        }

        viewModel.SelectedTowerCatalogType = "双斜杆三管塔";
        if (viewModel.FilteredTowerCatalogRecords.Count != 60 ||
            viewModel.FilteredTowerCatalogRecords.Any(item => item.TowerType != "双斜杆三管塔"))
        {
            Console.Error.WriteLine("FAIL 按双斜杆三管塔分类筛选后型号列表不正确。");
            window.Close();
            return 1;
        }
        viewModel.TowerCatalogSearchText = "3GT(SX)-20-0.45-1NPT-6F";
        if (viewModel.FilteredTowerCatalogRecords.Count != 1 ||
            viewModel.FilteredTowerCatalogRecords[0].TowerCode != "3GT(SX)-20-0.45-1NPT-6F" ||
            viewModel.SelectedTowerCatalogRecord is not null)
        {
            Console.Error.WriteLine(
                $"FAIL 塔型型号关键词不能准确筛选到V2.0反力记录：" +
                $"keyword='{viewModel.TowerCatalogSearchText}', " +
                $"count={viewModel.FilteredTowerCatalogRecords.Count}, " +
                $"selected='{viewModel.SelectedTowerCatalogRecord?.TowerCode}'。搜索结果不得在用户点选前自动提交。");
            window.Close();
            return 1;
        }
        viewModel.SelectedTowerCatalogType = "全部塔型";
        viewModel.TowerCatalogSearchText = string.Empty;
        viewModel.SelectedTowerCatalogHeight = viewModel.TowerCatalogHeights
            .First(item => item.Value == 20);
        viewModel.SelectedTowerCatalogWindPressure = viewModel.TowerCatalogWindPressures
            .First(item => item.Value == 0.45);
        viewModel.TowerCatalogSearchText = "20 6F";
        if (viewModel.FilteredTowerCatalogRecords.Count == 0 ||
            viewModel.FilteredTowerCatalogRecords.Any(item =>
                EnterpriseTowerLoadService.ParseHeight(item) != 20 ||
                EnterpriseTowerLoadService.ParseWindPressure(item) != 0.45 ||
                !item.TowerCode.Contains("6F", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("FAIL 塔高、风压和空格分隔多关键词组合筛选不正确。");
            window.Close();
            return 1;
        }
        viewModel.SelectedTowerCatalogHeight = viewModel.TowerCatalogHeights[0];
        viewModel.SelectedTowerCatalogWindPressure = viewModel.TowerCatalogWindPressures[0];
        viewModel.TowerCatalogSearchText = string.Empty;
        RenderWindow(window, towerPagePath);
        if (window.FindName("TowerCatalogPanel") is not Border towerCatalogPanel ||
            towerCatalogPanel.Visibility != Visibility.Visible ||
            window.FindName("TowerCatalogHeightBox") is not ComboBox towerCatalogHeightBox ||
            towerCatalogHeightBox.Items.Count < 2 ||
            window.FindName("TowerCatalogWindPressureBox") is not ComboBox towerCatalogWindBox ||
            towerCatalogWindBox.Items.Count < 2 ||
            window.FindName("TowerCatalogSearchBox") is not TextBox towerCatalogSearchBox ||
            window.FindName("TowerCatalogDropDownButton") is not Button towerCatalogDropDownButton ||
            window.FindName("TowerCatalogResultsPopup") is not Popup towerCatalogResultsPopup ||
            window.FindName("TowerCatalogResultsBorder") is not Border towerCatalogResultsBorder ||
            window.FindName("TowerCatalogResultsList") is not ListBox towerCatalogResultsList ||
            towerCatalogResultsList.Items.Count != 446)
        {
            Console.Error.WriteLine("FAIL 荷载页独立搜索框或下拉结果面板没有载入446条V2.0记录。");
            window.Close();
            return 1;
        }

        towerCatalogSearchBox.Focus();
        Keyboard.Focus(towerCatalogSearchBox);
        towerCatalogSearchBox.Text = "20 0.45 6F";
        towerCatalogSearchBox
            .GetBindingExpression(TextBox.TextProperty)?
            .UpdateSource();
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        if (!towerCatalogResultsPopup.IsOpen ||
            viewModel.TowerCatalogSearchText != "20 0.45 6F" ||
            viewModel.FilteredTowerCatalogRecords.Count == 0 ||
            viewModel.FilteredTowerCatalogRecords.Any(item =>
                EnterpriseTowerLoadService.ParseHeight(item) != 20 ||
                EnterpriseTowerLoadService.ParseWindPressure(item) != 0.45 ||
                !item.TowerCode.Contains("6F", StringComparison.OrdinalIgnoreCase)) ||
            viewModel.SelectedTowerCatalogRecord is not null)
        {
            Console.Error.WriteLine("FAIL 搜索框输入没有实时传入多关键词过滤，或搜索时错误地自动选中记录。");
            window.Close();
            return 1;
        }
        RenderElement(towerCatalogResultsBorder, towerPickerPopupPath);

        if (!towerCatalogResultsPopup.IsOpen)
        {
            towerCatalogDropDownButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        if (!towerCatalogResultsPopup.IsOpen ||
            towerCatalogResultsList.ItemContainerGenerator.ContainerFromIndex(0) is not
                ListBoxItem firstTowerRecordItem ||
            firstTowerRecordItem.DataContext is not TowerLoadCatalogRecord firstClickedRecord)
        {
            Console.Error.WriteLine("FAIL 搜索结果下拉面板没有展开或没有生成可点击记录行。");
            window.Close();
            return 1;
        }

        firstTowerRecordItem.RaiseEvent(new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
            Source = firstTowerRecordItem
        });
        window.UpdateLayout();
        if (viewModel.SelectedTowerCatalogRecord?.Id != firstClickedRecord.Id ||
            towerCatalogResultsPopup.IsOpen ||
            !viewModel.ApplyTowerCatalogLoadCommand.CanExecute(null) ||
            !string.IsNullOrEmpty(viewModel.TowerCatalogSearchText) ||
            !string.IsNullOrEmpty(towerCatalogSearchBox.Text) ||
            viewModel.FilteredTowerCatalogRecords.Count != 446)
        {
            Console.Error.WriteLine(
                $"FAIL 第一次点选状态异常：selected='{viewModel.SelectedTowerCatalogRecord?.Id}', " +
                $"expected='{firstClickedRecord.Id}', popup={towerCatalogResultsPopup.IsOpen}, " +
                $"canApply={viewModel.ApplyTowerCatalogLoadCommand.CanExecute(null)}, " +
                $"vmSearch='{viewModel.TowerCatalogSearchText}', uiSearch='{towerCatalogSearchBox.Text}', " +
                $"count={viewModel.FilteredTowerCatalogRecords.Count}。");
            window.Close();
            return 1;
        }

        towerCatalogDropDownButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        if (towerCatalogResultsList.ItemContainerGenerator.ContainerFromIndex(1) is not
                ListBoxItem secondTowerRecordItem ||
            secondTowerRecordItem.DataContext is not TowerLoadCatalogRecord secondClickedRecord ||
            secondClickedRecord.Id == firstClickedRecord.Id)
        {
            Console.Error.WriteLine("FAIL 第二次展开后没有恢复其他可选型号。");
            window.Close();
            return 1;
        }

        secondTowerRecordItem.RaiseEvent(new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
            Source = secondTowerRecordItem
        });
        window.UpdateLayout();
        if (viewModel.SelectedTowerCatalogRecord?.Id != secondClickedRecord.Id ||
            viewModel.SelectedTowerCatalogRecord.Id == firstClickedRecord.Id ||
            towerCatalogResultsPopup.IsOpen ||
            !viewModel.SelectedTowerCatalogDisplay.Contains(
                secondClickedRecord.TowerCode,
                StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("FAIL 连续第二次选择没有替换当前型号或更新当前选择显示。");
            window.Close();
            return 1;
        }

        var repeatedSelectionIds = new List<string>();
        var repeatedlySelectableRecords = viewModel.FilteredTowerCatalogRecords
            .Where(item => item.CanApplyOverallDesignLoads)
            .Skip(2)
            .Take(5)
            .ToArray();
        if (repeatedlySelectableRecords.Length != 5)
        {
            Console.Error.WriteLine("FAIL 当前基础形式没有找到五个可连续换选的整塔反力型号。");
            window.Close();
            return 1;
        }
        for (var selectionCycle = 0; selectionCycle < 5; selectionCycle++)
        {
            towerCatalogDropDownButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            window.Dispatcher.Invoke(
                static () => { },
                DispatcherPriority.ContextIdle);
            var targetRecord = repeatedlySelectableRecords[selectionCycle];
            var targetIndex = towerCatalogResultsList.Items.IndexOf(targetRecord);
            towerCatalogResultsList.ScrollIntoView(targetRecord);
            window.UpdateLayout();
            if (towerCatalogResultsList.ItemContainerGenerator.ContainerFromIndex(targetIndex) is not
                    ListBoxItem targetItem)
            {
                Console.Error.WriteLine($"FAIL 第{selectionCycle + 3}次选择时没有生成目标记录行。");
                window.Close();
                return 1;
            }

            targetItem.RaiseEvent(new MouseButtonEventArgs(
                Mouse.PrimaryDevice,
                Environment.TickCount,
                MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                Source = targetItem
            });
            window.UpdateLayout();
            if (viewModel.SelectedTowerCatalogRecord?.Id != targetRecord.Id ||
                towerCatalogResultsPopup.IsOpen ||
                !string.IsNullOrEmpty(viewModel.TowerCatalogSearchText) ||
                !viewModel.ApplyTowerCatalogLoadCommand.CanExecute(null))
            {
                Console.Error.WriteLine(
                    $"FAIL 第{selectionCycle + 3}次连续选择状态异常：" +
                    $"selected='{viewModel.SelectedTowerCatalogRecord?.Id}', expected='{targetRecord.Id}', " +
                    $"popup={towerCatalogResultsPopup.IsOpen}, search='{viewModel.TowerCatalogSearchText}', " +
                    $"canApply={viewModel.ApplyTowerCatalogLoadCommand.CanExecute(null)}。");
                window.Close();
                return 1;
            }
            repeatedSelectionIds.Add(targetRecord.Id);
        }
        if (repeatedSelectionIds.Distinct(StringComparer.Ordinal).Count() != 5)
        {
            Console.Error.WriteLine("FAIL 五次连续换选没有分别提交五个不同型号。");
            window.Close();
            return 1;
        }

        var selectedBeforeNoMatchSearch = viewModel.SelectedTowerCatalogRecord;
        towerCatalogSearchBox.Text = "不存在的型号关键词";
        towerCatalogSearchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        window.UpdateLayout();
        if (viewModel.FilteredTowerCatalogRecords.Count != 0 ||
            viewModel.SelectedTowerCatalogRecord?.Id != selectedBeforeNoMatchSearch?.Id ||
            !viewModel.ApplyTowerCatalogLoadCommand.CanExecute(null))
        {
            Console.Error.WriteLine("FAIL 无结果搜索不应悄悄撤销已选择的有效型号。");
            window.Close();
            return 1;
        }

        towerCatalogSearchBox.Clear();
        towerCatalogSearchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        window.UpdateLayout();
        if (viewModel.FilteredTowerCatalogRecords.Count != 446 ||
            viewModel.SelectedTowerCatalogRecord?.Id != selectedBeforeNoMatchSearch?.Id)
        {
            Console.Error.WriteLine("FAIL 清空搜索后没有恢复446条列表或错误撤销当前选择。");
            window.Close();
            return 1;
        }

        towerCatalogSearchBox.Text = "DGT(Z)-20-0.45-2ZJ-6F";
        towerCatalogSearchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        window.UpdateLayout();
        if (viewModel.FilteredTowerCatalogRecords.Count != 1)
        {
            Console.Error.WriteLine("FAIL 键盘选择测试的精确型号搜索没有得到唯一结果。");
            window.Close();
            return 1;
        }
        var keyboardSelectedRecord = viewModel.FilteredTowerCatalogRecords[0];
        towerCatalogSearchBox.RaiseEvent(new KeyEventArgs(
            Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(towerCatalogSearchBox)!,
            Environment.TickCount,
            Key.Down)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        });
        towerCatalogResultsList.RaiseEvent(new KeyEventArgs(
            Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(towerCatalogResultsList)!,
            Environment.TickCount,
            Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        });
        window.UpdateLayout();
        if (viewModel.SelectedTowerCatalogRecord?.Id != keyboardSelectedRecord.Id ||
            towerCatalogResultsPopup.IsOpen ||
            !string.IsNullOrEmpty(viewModel.TowerCatalogSearchText))
        {
            Console.Error.WriteLine("FAIL 键盘向下加回车没有提交唯一搜索结果。");
            window.Close();
            return 1;
        }
        towerCatalogResultsPopup.IsOpen = false;
        }
        else
        {
            viewModel.IsTowerCatalogLoad = true;
            viewModel.NavigateToStep(
                3,
                TowerFoundation.Domain.ProjectStage.GeotechnicalReady);
            window.UpdateLayout();
            window.Dispatcher.Invoke(
                static () => { },
                DispatcherPriority.ContextIdle);
            if (viewModel.FilteredTowerCatalogRecords.Count != 0 ||
                viewModel.ApplyTowerCatalogLoadCommand.CanExecute(null) ||
                !viewModel.TowerCatalogStatus.Contains("手工荷载", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("FAIL 公开占位库没有保持空结果、禁用采用按钮或提示手工荷载输入。");
                window.Close();
                return 1;
            }
            RenderWindow(window, towerPagePath);
        }

        viewModel.IsTowerManualLoad = true;
        viewModel.Project.TowerMast.TowerModel = "现行厂家反力手工样本";
        viewModel.Project.TowerMast.StructureType =
            TowerFoundation.Domain.TowerStructureType.SingleTube;
        viewModel.Project.TowerMast.HeightM = 20;
        viewModel.Project.TowerMast.VerticalKn = 30;
        viewModel.Project.TowerMast.ShearXKn = 20;
        viewModel.Project.TowerMast.ShearYKn = 0;
        viewModel.Project.TowerMast.MomentXKnM = 0;
        viewModel.Project.TowerMast.MomentYKnM = 300;
        viewModel.Project.TowerMast.TorsionKnM = 0;
        window.DataContext = null;
        window.DataContext = viewModel;
        viewModel.SelectedStep = 3;
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);

        if (window.FindName("TowerManualMetadataPanel") is not Border visibleManualMetadataPanel ||
            visibleManualMetadataPanel.Visibility != Visibility.Visible)
        {
            Console.Error.WriteLine("FAIL 手工荷载模式没有显示必要的塔型、结构类型和塔高输入。");
            window.Close();
            return 1;
        }

        if (FindVisualChildren<TextBlock>(window).Any(item =>
                item.Text?.Contains("点击“下一步”后，软件会汇总", StringComparison.Ordinal) == true))
        {
            Console.Error.WriteLine("FAIL 荷载页仍存在无实际作用的下一步确认说明条。");
            window.Close();
            return 1;
        }

        loadNextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!viewModel.Project.TowerMast.IsConfirmed ||
            viewModel.SelectedStep != 4 ||
            viewModel.Schemes.Count != 0 ||
            viewModel.SelectedCandidate is not null ||
            viewModel.Project.AuditTrail.All(item =>
                item.Action != "弹窗确认荷载输入"))
        {
            Console.Error.WriteLine("FAIL 塔桅荷载下一步应只确认并进入方案页，不得提前执行尺寸搜索。");
            window.Close();
            return 1;
        }
        viewModel.SelectedStep = 3;

        window.Width = 1920;
        window.Height = 1000;
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var loadStepContent = window.FindName("LoadStepContent") as FrameworkElement;
        if (loadStepContent is null || loadStepContent.ActualWidth < 1200)
        {
            Console.Error.WriteLine(
                $"FAIL 荷载页宽屏内容未伸展：{loadStepContent?.ActualWidth:F0}px。");
            window.Close();
            return 1;
        }
        var loadStepWideWidth = loadStepContent.ActualWidth;

        var towerWidePagePath = AddSuffix(outputPath, "-tower-load-wide");
        RenderWindow(window, towerWidePagePath);
        viewModel.ReturnToCurrentWorkflow();
        window.Width = 1480;
        window.Height = 920;
        window.UpdateLayout();
        viewModel.GenerateSchemesCommand.Execute(null);
        if (viewModel.Schemes.Count != 3)
        {
            Console.Error.WriteLine("FAIL 塔桅UI流程未生成三种方案。");
            window.Close();
            return 1;
        }

        viewModel.IsCircularFoundation = true;
        viewModel.Project.Geotechnical.IsConfirmed = true;
        viewModel.GenerateSchemesCommand.Execute(null);
        if (viewModel.Schemes.Count != 3 ||
            viewModel.Schemes.Any(item =>
                item.FoundationType != TowerFoundation.Domain.FoundationType.CircularShortColumn))
        {
            Console.Error.WriteLine("FAIL 独立基础－圆形柱UI未生成三种类型化方案。");
            window.Close();
            return 1;
        }

        viewModel.IsRaftFoundation = true;
        viewModel.Project.Geotechnical.IsConfirmed = true;
        viewModel.GenerateSchemesCommand.Execute(null);
        if (viewModel.Schemes.Count != 3 ||
            viewModel.Schemes.Any(item =>
                item.FoundationType != TowerFoundation.Domain.FoundationType.Raft))
        {
            Console.Error.WriteLine("FAIL 筏板基础UI未生成三种类型化方案。");
            window.Close();
            return 1;
        }

        viewModel.IsRigidShortPileFoundation = true;
        viewModel.Project.Geotechnical.IsConfirmed = true;
        viewModel.Project.Geotechnical.SoilUnitWeightKnPerM3 = 18;
        viewModel.Project.Geotechnical.InternalFrictionAngleDegree = 7;
        viewModel.Project.FoundationSettings.RigidShortPile.MinimumDiameterM = 1.8;
        viewModel.Project.FoundationSettings.RigidShortPile.MaximumDiameterM = 3.6;
        viewModel.Project.FoundationSettings.RigidShortPile.DiameterStepM = 0.2;
        viewModel.Project.FoundationSettings.RigidShortPile.MinimumEmbeddedDepthM = 5;
        viewModel.Project.FoundationSettings.RigidShortPile.MaximumEmbeddedDepthM = 12;
        viewModel.Project.FoundationSettings.RigidShortPile.EmbeddedDepthStepM = 1;
        viewModel.Project.FoundationSettings.RigidShortPile.LongitudinalBarDiameterMm = 32;
        viewModel.Project.FoundationSettings.RigidShortPile.LongitudinalBarCount = 72;
        viewModel.Project.FoundationSettings.RigidShortPile.StirrupDiameterMm = 14;
        viewModel.Project.FoundationSettings.RigidShortPile.StirrupSpacingMm = 100;
        viewModel.Project.FoundationSettings.RigidShortPile.StirrupLegCount = 4;
        viewModel.Project.FoundationSettings.RigidShortPile.SoilLayers =
        [
            new TowerFoundation.Domain.RigidShortPileSoilLayerInput
            {
                Name = "填土",
                ThicknessM = 1,
                HorizontalResistanceCoefficientMnPerM4 = 0
            },
            new TowerFoundation.Domain.RigidShortPileSoilLayerInput
            {
                Name = "粉质黏土",
                ThicknessM = 5,
                HorizontalResistanceCoefficientMnPerM4 = 18
            },
            new TowerFoundation.Domain.RigidShortPileSoilLayerInput
            {
                Name = "密实土层",
                ThicknessM = 10,
                HorizontalResistanceCoefficientMnPerM4 = 30
            }
        ];
        viewModel.Project.FoundationSettings.RigidShortPile.IsConfirmed = true;
        viewModel.GenerateSchemesCommand.Execute(null);
        if (viewModel.Schemes.Count != 3 ||
            viewModel.Schemes.Any(item =>
                item.FoundationType != TowerFoundation.Domain.FoundationType.RigidShortPile) ||
            viewModel.Schemes.Any(item => item.ReinforcementDesigns.Count != 2))
        {
            Console.Error.WriteLine("FAIL 刚性短柱桩UI未生成含纵筋、箍筋的三种类型化方案。");
            window.Close();
            return 1;
        }

        viewModel.SelectedStep = 2;
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var rigidGeotechnicalPagePath = AddSuffix(
            outputPath,
            "-rigid-short-pile-geotechnical");
        RenderWindow(window, rigidGeotechnicalPagePath);

        viewModel.SelectedStep = 4;
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var rigidFoundationPagePath = AddSuffix(
            outputPath,
            "-rigid-short-pile-foundation");
        RenderWindow(window, rigidFoundationPagePath);

        viewModel.IsRigidRectangularShortPileFoundation = true;
        viewModel.Project.Geotechnical.IsConfirmed = true;
        viewModel.Project.FoundationSettings.RigidShortPile.IsConfirmed = true;
        viewModel.Project.FoundationSettings.RigidShortPile.MinimumRectangularLengthM = 3.0;
        viewModel.Project.FoundationSettings.RigidShortPile.MaximumRectangularLengthM = 3.6;
        viewModel.Project.FoundationSettings.RigidShortPile.RectangularLengthStepM = 0.3;
        viewModel.Project.FoundationSettings.RigidShortPile.MinimumRectangularWidthM = 3.0;
        viewModel.Project.FoundationSettings.RigidShortPile.MaximumRectangularWidthM = 3.6;
        viewModel.Project.FoundationSettings.RigidShortPile.RectangularWidthStepM = 0.3;
        viewModel.Project.FoundationSettings.RigidShortPile.MinimumEmbeddedDepthM = 8;
        viewModel.Project.FoundationSettings.RigidShortPile.MaximumEmbeddedDepthM = 12;
        viewModel.Project.FoundationSettings.RigidShortPile.EmbeddedDepthStepM = 2;
        viewModel.GenerateSchemesCommand.Execute(null);
        if (viewModel.Schemes.Count != 3 ||
            viewModel.Schemes.Any(item =>
                item.FoundationType !=
                TowerFoundation.Domain.FoundationType.RigidRectangularShortPile) ||
            viewModel.Schemes.Any(item =>
                item.ReinforcementDesigns.All(rebar =>
                    rebar.Component != "刚性短柱桩－矩形纵筋")))
        {
            Console.Error.WriteLine("FAIL 矩形刚性短柱桩UI未生成X/Y逐向计算和矩形配筋的三种方案。");
            window.Close();
            return 1;
        }
        viewModel.SelectedStep = 4;
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var rigidRectangularFoundationPagePath = AddSuffix(
            outputPath,
            "-rigid-rectangular-short-pile-foundation");
        RenderWindow(window, rigidRectangularFoundationPagePath);

        viewModel.IsPileFoundation = true;
        viewModel.IsTowerManualLoad = true;
        viewModel.SelectedTowerStructureType =
            TowerFoundation.Domain.TowerStructureType.ThreeTube;
        viewModel.Project.TowerMast.TowerModel = "三管塔手工反力测试";
        viewModel.Project.TowerMast.IndividualPileCompressionKn = 450;
        viewModel.Project.TowerMast.IndividualPileUpliftKn = 380;
        viewModel.Project.TowerMast.IndividualPileHorizontalKn = 35;
        viewModel.Project.TowerMast.IsConfirmed = true;
        if (!viewModel.IsMultiLegPileFoundation ||
            !viewModel.Project.TowerMast.UsesIndividualPileReactions ||
            viewModel.Project.FoundationSettings.Pile.PileCount != 3 ||
            !viewModel.Project.FoundationSettings.Pile.TieBeamRequired)
        {
            Console.Error.WriteLine("FAIL 三管塔没有自动切换为3根独立桩、单腿反力和连梁布置。");
            window.Close();
            return 1;
        }
        viewModel.SelectedStep = 3;
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var threePileLoadPagePath = AddSuffix(outputPath, "-three-pile-load");
        RenderWindow(window, threePileLoadPagePath);
        viewModel.ReturnToCurrentWorkflow();
        viewModel.Project.Geotechnical.IsConfirmed = true;
        viewModel.Project.FoundationSettings.Pile.IsConfirmed = true;
        viewModel.GenerateSchemesCommand.Execute(null);
        if (viewModel.Schemes.Count != 3 ||
            viewModel.Schemes.Any(item =>
                item.FoundationType != TowerFoundation.Domain.FoundationType.Pile) ||
            viewModel.Schemes.Any(item =>
                item.Geometry.PileCount != 3 ||
                item.Geometry.TieBeamCount != 3 ||
                item.Geometry.BaseLengthM != 0))
        {
            Console.Error.WriteLine("FAIL 三管塔桩基础UI未生成3根独立桩、3根连梁、无承台的三种方案。");
            window.Close();
            return 1;
        }

        viewModel.SelectedStep = 2;
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var pileGeotechnicalPagePath = AddSuffix(
            outputPath,
            "-pile-geotechnical");
        RenderWindow(window, pileGeotechnicalPagePath);

        viewModel.SelectedStep = 4;
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ContextIdle);
        var pilePagePath = AddSuffix(outputPath, "-pile-foundation");
        RenderWindow(window, pilePagePath);

        var settingsPagePath = AddSuffix(outputPath, "-settings");
        var settingsStoragePagePath = AddSuffix(outputPath, "-settings-storage");
        var bailianGuidePagePath = AddSuffix(outputPath, "-settings-bailian-api-guide");
        var deepSeekGuidePagePath = AddSuffix(outputPath, "-settings-deepseek-api-guide");
        var settingsDirectory = Path.Combine(
            Path.GetTempPath(),
            "TowerFoundation.DesktopSmoke",
            Guid.NewGuid().ToString("N"));
        var settingsService = new LocalApplicationSettingsService(settingsDirectory);
        using (var deepSeekService = new DeepSeekService(settingsService))
        using (var visualService = new VisualGeotechnicalAiService(settingsService))
        {
            var settingsWindow = new SettingsWindow(
                settingsService,
                deepSeekService,
                visualService)
            {
                Owner = window,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -9000,
                Top = -9000
            };
            settingsWindow.Show();
            settingsWindow.UpdateLayout();
            if (settingsWindow.FindName("ModelBox") is not ComboBox modelBox ||
                modelBox.Items.Count != 2 ||
                !Equals(modelBox.SelectedItem, "deepseek-v4-pro"))
            {
                Console.Error.WriteLine(
                    "FAIL DeepSeek模型下拉应包含Pro/Flash且默认选中deepseek-v4-pro。");
                settingsWindow.Close();
                window.Close();
                return 1;
            }

            if (settingsWindow.FindName("VisionModelBox") is not ComboBox visionModelBox ||
                visionModelBox.Items.Count != 6 ||
                !Equals(visionModelBox.SelectedItem, "qwen3.7-plus") ||
                !visionModelBox.Items.Cast<string>().Contains("qwen3.7-plus-2026-05-26") ||
                visionModelBox.Items.Cast<string>().Any(model =>
                    model.Contains("qwen-image", StringComparison.OrdinalIgnoreCase) ||
                    model.Contains("wan", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine(
                    "FAIL 视觉模型下拉应包含qwen3.7-plus持续更新别名及2026-05-26固定快照，且默认使用持续更新别名。" );
                settingsWindow.Close();
                window.Close();
                return 1;
            }

            if (window.FindName("VisionGeotechnicalButton") is not Button visionButton ||
                !Equals(visionButton.Content, "视觉大模型直接分析 PDF"))
            {
                Console.Error.WriteLine("FAIL 地勘页面必须提供视觉大模型直接分析PDF入口。" );
                settingsWindow.Close();
                window.Close();
                return 1;
            }

            if (settingsWindow.FindName("ClearButton") is not Button clearButton ||
                !Equals(clearButton.Content, "清除") ||
                settingsWindow.FindName("TestButton") is not Button testButton ||
                !Equals(testButton.Content, "测试") ||
                settingsWindow.FindName("SaveAndTestButton") is not Button saveAndTestButton ||
                !Equals(saveAndTestButton.Content, "保存并测试") ||
                settingsWindow.FindName("ClearApiKeyCheckBox") is not null ||
                settingsWindow.FindName("AiProgressPanel") is not StackPanel ||
                settingsWindow.FindName("AiProgressBar") is not ProgressBar settingsAiProgressBar ||
                !settingsAiProgressBar.IsIndeterminate ||
                settingsWindow.FindName("SettingsLogoImage") is not Image settingsLogo ||
                settingsLogo.Source is null ||
                settingsWindow.FindName("ProjectDirectoryBox") is not TextBox projectDirectoryBox ||
                string.IsNullOrWhiteSpace(projectDirectoryBox.Text) ||
                settingsWindow.FindName("ExportDirectoryBox") is not TextBox exportDirectoryBox ||
                string.IsNullOrWhiteSpace(exportDirectoryBox.Text) ||
                settingsWindow.FindName("GeotechnicalHistoryDirectoryBox") is not TextBox historyDirectoryBox ||
                string.IsNullOrWhiteSpace(historyDirectoryBox.Text) ||
                settingsWindow.FindName("MonitoringDrawingHistoryDirectoryBox") is not TextBox monitoringHistoryDirectoryBox ||
                string.IsNullOrWhiteSpace(monitoringHistoryDirectoryBox.Text) ||
                settingsWindow.FindName("BrowseProjectDirectoryButton") is not Button ||
                settingsWindow.FindName("BrowseExportDirectoryButton") is not Button ||
                settingsWindow.FindName("BrowseGeotechnicalHistoryDirectoryButton") is not Button ||
                settingsWindow.FindName("BrowseMonitoringDrawingHistoryDirectoryButton") is not Button ||
                settingsWindow.FindName("BailianApiGuideExpander") is not Expander bailianGuide ||
                bailianGuide.IsExpanded ||
                settingsWindow.FindName("DeepSeekApiGuideExpander") is not Expander deepSeekGuide ||
                deepSeekGuide.IsExpanded ||
                settingsWindow.FindName("OpenBailianConsoleButton") is not Button bailianConsoleButton ||
                !Equals(bailianConsoleButton.Content, "打开百炼控制台") ||
                !Equals(bailianConsoleButton.Tag, "https://bailian.console.aliyun.com/?tab=model") ||
                settingsWindow.FindName("OpenBailianApiKeyHelpButton") is not Button bailianHelpButton ||
                !Equals(bailianHelpButton.Tag, "https://help.aliyun.com/zh/model-studio/get-api-key") ||
                settingsWindow.FindName("OpenDeepSeekApiKeysButton") is not Button deepSeekKeysButton ||
                !Equals(deepSeekKeysButton.Content, "打开 DeepSeek API Keys") ||
                !Equals(deepSeekKeysButton.Tag, "https://platform.deepseek.com/api_keys") ||
                settingsWindow.FindName("OpenDeepSeekApiDocsButton") is not Button deepSeekDocsButton ||
                !Equals(deepSeekDocsButton.Tag, "https://api-docs.deepseek.com/api/deepseek-api"))
            {
                Console.Error.WriteLine("FAIL 设置页按钮、AI进度条、四个默认目录、API申请引导、官网链接或品牌Logo状态异常。");
                settingsWindow.Close();
                window.Close();
                return 1;
            }

            RenderWindow(settingsWindow, settingsPagePath);
            bailianGuide.IsExpanded = true;
            settingsWindow.UpdateLayout();
            bailianGuide.BringIntoView();
            settingsWindow.Dispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
            RenderWindow(settingsWindow, bailianGuidePagePath);
            bailianGuide.IsExpanded = false;

            deepSeekGuide.IsExpanded = true;
            settingsWindow.UpdateLayout();
            deepSeekGuide.BringIntoView();
            settingsWindow.Dispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
            RenderWindow(settingsWindow, deepSeekGuidePagePath);
            deepSeekGuide.IsExpanded = false;
            if (settingsWindow.FindName("SettingsScrollViewer") is ScrollViewer settingsScroller)
            {
                settingsScroller.ScrollToEnd();
                settingsWindow.UpdateLayout();
                RenderWindow(settingsWindow, settingsStoragePagePath);
            }
            settingsWindow.Close();
        }

        if (Directory.Exists(settingsDirectory))
        {
            Directory.Delete(settingsDirectory, recursive: true);
        }

        var projectCatalogPagePath = AddSuffix(outputPath, "-project-catalog");
        var catalogEntry = new ProjectCatalogEntry(
            Path.Combine(Path.GetTempPath(), "界面测试塔桅项目.tjproj"),
            "界面测试塔桅项目",
            TowerFoundation.Domain.ProjectType.CommunicationTower,
            TowerFoundation.Domain.FoundationType.CircularShortColumn,
            "河北省 · 廊坊市 · 三河市",
            DateTimeOffset.Now,
            IsReadable: true);
        var projectCatalogWindow = new ProjectCatalogWindow(
            [catalogEntry],
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "塔基智设",
                "项目"))
        {
            Owner = window,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -9000,
            Top = -9000
        };
        var catalogSafetyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        catalogSafetyTimer.Tick += (_, _) =>
        {
            catalogSafetyTimer.Stop();
            if (projectCatalogWindow.IsVisible)
            {
                projectCatalogWindow.DialogResult = false;
            }
        };
        projectCatalogWindow.Loaded += (_, _) =>
        {
            projectCatalogWindow.Dispatcher.BeginInvoke(
                () =>
                {
                    var catalogList = projectCatalogWindow.FindName("ProjectCatalogList") as ListBox;
                    if (catalogList is null)
                    {
                        projectCatalogWindow.DialogResult = false;
                        return;
                    }

                    catalogList.SelectedIndex = 0;
                    projectCatalogWindow.UpdateLayout();
                    RenderWindow(projectCatalogWindow, projectCatalogPagePath);
                    if (catalogList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item)
                    {
                        var doubleClickArguments = new MouseButtonEventArgs(
                            Mouse.PrimaryDevice,
                            Environment.TickCount,
                            MouseButton.Left)
                        {
                            RoutedEvent = Control.MouseDoubleClickEvent,
                            Source = item
                        };
                        typeof(ProjectCatalogWindow)
                            .GetMethod(
                                "ProjectCatalogList_MouseDoubleClick",
                                BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(
                                projectCatalogWindow,
                                [catalogList, doubleClickArguments]);
                    }
                },
                DispatcherPriority.ContextIdle);
        };
        catalogSafetyTimer.Start();
        var catalogResult = projectCatalogWindow.ShowDialog();
        catalogSafetyTimer.Stop();
        if (catalogResult != true ||
            projectCatalogWindow.SelectedProjectPath != catalogEntry.FilePath)
        {
            Console.Error.WriteLine("FAIL 项目目录未能通过双击打开所选项目。");
            window.Close();
            return 1;
        }

        var exitPagePath = AddSuffix(outputPath, "-exit-confirmation");
        var exitWindow = new ExitConfirmationWindow(
            viewModel.CurrentFileDisplay,
            viewModel.ProgressText)
        {
            Owner = window,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -9000,
            Top = -9000
        };
        exitWindow.Show();
        exitWindow.UpdateLayout();
        RenderWindow(exitWindow, exitPagePath);
        exitWindow.Close();

        var stepConfirmationPagePath = AddSuffix(outputPath, "-step-confirmation");
        var stepWindow = new StepConfirmationWindow(
            new StepConfirmationRequest(
                "确认独立灌注桩地勘参数",
                "确认后每根独立桩将采用同一组经核对的桩土参数分别验算",
                "地下水埋深 5.00 m；单桩水平承载力 120.00 kN；桩土分层 3 层、合计有效厚度 12.00 m。",
                [
                    "地下水、土层描述及特殊地基风险已与原始资料核对。",
                    "分层侧阻、端阻、抗拔系数和水平承载力来自地勘或试桩资料。",
                    "允许软件将以上参数用于方案搜索、复算及成果输出。"
                ]))
        {
            Owner = window,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -9000,
            Top = -9000
        };
        stepWindow.Show();
        stepWindow.UpdateLayout();
        RenderWindow(stepWindow, stepConfirmationPagePath);
        stepWindow.Close();

        var appDialogPagePath = AddSuffix(outputPath, "-app-dialog");
        var appDialogWindow = new AppDialogWindow(
            "退回工程类型会清除已经形成的荷载、方案选择和成果状态；原始录入值仍保留，重新选择业务类型后可继续修改。\n\n确定退回修改吗？",
            "退回修改前序参数",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No)
        {
            Owner = window,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -9000,
            Top = -9000
        };
        appDialogWindow.Show();
        appDialogWindow.UpdateLayout();
        var appDialogPrimary = appDialogWindow.FindName("PrimaryActionButton") as Button;
        var appDialogSecondary = appDialogWindow.FindName("SecondaryActionButton") as Button;
        if (appDialogWindow.WindowStyle != WindowStyle.None ||
            appDialogWindow.AllowsTransparency != true ||
            appDialogPrimary?.Content?.ToString() != "确认" ||
            appDialogSecondary?.Content?.ToString() != "暂不" ||
            appDialogSecondary.IsDefault != true)
        {
            Console.Error.WriteLine("FAIL 统一提示框未采用品牌窗口、中文按钮或安全默认项。");
            appDialogWindow.Close();
            window.Close();
            return 1;
        }
        RenderWindow(appDialogWindow, appDialogPagePath);
        appDialogWindow.Close();

        var appDialogInteraction = new AppDialogWindow(
            "确定继续本次操作吗？",
            "提示框交互测试",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No)
        {
            Owner = window,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -9000,
            Top = -9000
        };
        appDialogInteraction.Loaded += (_, _) =>
            appDialogInteraction.Dispatcher.BeginInvoke(
                () =>
                {
                    var confirmButton = appDialogInteraction.FindName("PrimaryActionButton") as Button;
                    confirmButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                },
                DispatcherPriority.ContextIdle);
        var appDialogInteractionResult = appDialogInteraction.ShowDialog();
        if (appDialogInteractionResult != true || appDialogInteraction.Result != MessageBoxResult.Yes)
        {
            Console.Error.WriteLine("FAIL 统一提示框确认按钮未保持原有 Yes 返回语义。");
            window.Close();
            return 1;
        }

        Console.WriteLine(
            $"PASS WPF窗口加载 {window.ActualWidth:F0}×{window.ActualHeight:F0}");
        Console.WriteLine(
            $"PASS UI离线生成三方案，控制弯矩 {viewModel.LoadPreview.MomentYKnM:F2} kN·m");
        Console.WriteLine("PASS 监控杆与通信塔桅双入口UI流程");
        Console.WriteLine("PASS 首页左侧通信塔桅、右侧监控杆入口顺序");
        Console.WriteLine("PASS 内置项目目录与双击打开交互");
        Console.WriteLine("PASS 六种基础类型均可从UI生成三种方案");
        Console.WriteLine("PASS 自定义尺寸复算与调整建议UI流程");
        Console.WriteLine("PASS 步骤5/6嵌套表格区域鼠标滚轮连续滚动");
        Console.WriteLine("PASS 步骤5/6结构化配筋展示与成果导出按钮启用");
        Console.WriteLine("PASS 全国地址级联与基本风压来源联动");
        Console.WriteLine("PASS 甘肃省默认选择、列表置顶且其他省份继续保留");
        if (catalogStatus.HasCurrentRecords)
        {
            Console.WriteLine("PASS V2.0企业塔型分类、型号关键词和446条反力记录级联筛选");
            Console.WriteLine("PASS 具体塔型独立搜索、多关键词过滤、无结果清空、连续七次鼠标换选及键盘选择");
        }
        else
        {
            Console.WriteLine("PASS 公开占位库保持空结果、禁用采用按钮并提示手工荷载输入");
        }
        Console.WriteLine("PASS 省市县选择及城市下拉展开前后窗口和内容尺寸保持不变");
        Console.WriteLine("PASS DeepSeek Pro默认与Pro/Flash模型下拉");
        Console.WriteLine("PASS 设置页清除/测试/保存并测试三按钮与品牌Logo");
        Console.WriteLine("PASS 地勘分析与设置连接测试均已配置AI进度条");
        Console.WriteLine("PASS 表格表头与单元格文字统一居中");
        Console.WriteLine("PASS 地勘与荷载改为下一步二级弹窗确认，无旧式确认勾选框");
        Console.WriteLine("PASS 全部原生提示框已统一为品牌弹窗，中文按钮、取消默认项和确认返回语义正确");
        Console.WriteLine("PASS 荷载下一步只准备参数，方案页再执行尺寸搜索并默认选中施工型，高级参数默认折叠");
        Console.WriteLine("PASS 企业库模式删除重复塔桅识别卡，手工模式保留必要基本信息");
        Console.WriteLine("PASS 真实采用自定义方案并进入成果页，审计表实时同步且窗口未退出");
        Console.WriteLine(
            $"PASS 荷载页宽屏内容已伸展至 {loadStepWideWidth:F0}px");
        Console.WriteLine($"PASS 工程类型页截图 {Path.GetFullPath(typePagePath)}");
        Console.WriteLine($"PASS 跨流程只读浏览截图 {Path.GetFullPath(AddSuffix(outputPath, "-workflow-browse"))}");
        Console.WriteLine($"PASS 截图 {Path.GetFullPath(outputPath)}");
        Console.WriteLine($"PASS 成果页截图 {Path.GetFullPath(outputStepPath)}");
        Console.WriteLine($"PASS 塔桅项目页截图 {Path.GetFullPath(towerProjectPagePath)}");
        Console.WriteLine($"PASS 塔桅荷载页截图 {Path.GetFullPath(towerPagePath)}");
        if (catalogStatus.HasCurrentRecords)
        {
            Console.WriteLine($"PASS 塔型搜索结果面板截图 {Path.GetFullPath(towerPickerPopupPath)}");
        }
        Console.WriteLine($"PASS 塔桅荷载宽屏页截图 {Path.GetFullPath(towerWidePagePath)}");
        Console.WriteLine($"PASS 桩基础地勘页截图 {Path.GetFullPath(pileGeotechnicalPagePath)}");
        Console.WriteLine($"PASS 桩基础页截图 {Path.GetFullPath(pilePagePath)}");
        Console.WriteLine($"PASS 刚性短柱桩地勘页截图 {Path.GetFullPath(rigidGeotechnicalPagePath)}");
        Console.WriteLine($"PASS 刚性短柱桩方案页截图 {Path.GetFullPath(rigidFoundationPagePath)}");
        Console.WriteLine($"PASS 矩形刚性短柱桩方案页截图 {Path.GetFullPath(rigidRectangularFoundationPagePath)}");
        Console.WriteLine($"PASS 设置页截图 {Path.GetFullPath(settingsPagePath)}");
        Console.WriteLine($"PASS 百炼API申请引导截图 {Path.GetFullPath(bailianGuidePagePath)}");
        Console.WriteLine($"PASS DeepSeek API申请引导截图 {Path.GetFullPath(deepSeekGuidePagePath)}");
        Console.WriteLine($"PASS 设置存储位置截图 {Path.GetFullPath(settingsStoragePagePath)}");
        Console.WriteLine($"PASS 项目目录截图 {Path.GetFullPath(projectCatalogPagePath)}");
        Console.WriteLine($"PASS 场址页截图 {Path.GetFullPath(addressPagePath)}");
        Console.WriteLine($"PASS 地勘承载力修正页截图 {Path.GetFullPath(geotechnicalPagePath)}");
        Console.WriteLine($"PASS AI原文与候选复核截图 {Path.GetFullPath(evidenceReviewPath)}");
        Console.WriteLine($"PASS 退出确认截图 {Path.GetFullPath(exitPagePath)}");
        Console.WriteLine($"PASS 步骤确认弹窗截图 {Path.GetFullPath(stepConfirmationPagePath)}");
        Console.WriteLine($"PASS 统一提示框截图 {Path.GetFullPath(appDialogPagePath)}");

        var retainedBearingCapacity = viewModel.Project.Geotechnical.BearingCapacityKpa;
        viewModel.Project.Stage = ProjectStage.CandidateReady;
        viewModel.Project.Geotechnical.IsConfirmed = true;
        viewModel.Project.TowerMast.IsConfirmed = true;
        if (viewModel.Project.Schemes.Count == 0)
        {
            var rollbackSentinel = new FoundationScheme { Name = "退回修改作废哨兵" };
            viewModel.Project.Schemes.Add(rollbackSentinel);
            viewModel.Schemes.Add(rollbackSentinel);
        }
        viewModel.SelectedStep = 2;
        window.UpdateLayout();
        var workflowReviseButton = window.FindName("WorkflowReviseButton") as Button;
        if (workflowReviseButton is not { Visibility: Visibility.Visible, IsEnabled: true })
        {
            Console.Error.WriteLine("FAIL 回看历史步骤时没有提供“从此步骤重新修改”入口。" );
            window.Close();
            return 1;
        }
        workflowReviseButton.Command.Execute(workflowReviseButton.CommandParameter);
        window.UpdateLayout();
        if (viewModel.Project.Stage != ProjectStage.SiteReady ||
            viewModel.SelectedStep != 2 ||
            viewModel.IsBrowsingWorkflowStep ||
            viewModel.Project.Geotechnical.IsConfirmed ||
            viewModel.Project.TowerMast.IsConfirmed ||
            viewModel.Project.Schemes.Count != 0 ||
            viewModel.Schemes.Count != 0 ||
            Math.Abs(viewModel.Project.Geotechnical.BearingCapacityKpa - retainedBearingCapacity) > 1e-9 ||
            window.FindName("GeotechnicalStepContent") is not FrameworkElement { IsEnabled: true })
        {
            Console.Error.WriteLine("FAIL 退回地勘步骤没有保留原始参数并作废后续荷载、方案和成果状态。" );
            window.Close();
            return 1;
        }
        Console.WriteLine("PASS 历史步骤可退回修改，原始参数保留且后续结果自动作废");

        window.Close();
        application.Shutdown();
        return 0;
    }

    private static MonitoringDrawingCandidate CreateMonitoringDrawingSmokeCandidate()
    {
        static MonitoringDrawingFieldCandidate Field(
            string name,
            string display,
            double? value,
            string unit = "m") => new()
        {
            FieldName = name,
            DisplayName = display,
            Value = value,
            Unit = unit,
            Confidence = value.HasValue ? 0.96 : 0,
            RawAnnotation = value.HasValue ? "自动化视觉候选证据" : string.Empty,
            Region = value.HasValue ? "PDF第1页主视图" : string.Empty,
            PageNumber = 1,
            IsSelected = value.HasValue
        };

        return new MonitoringDrawingCandidate
        {
            SourceFileName = "H6.5-L14-smoke.pdf",
            SourceFileSha256 = new string('a', 64),
            PageNumber = 1,
            DrawingModel = "H6.5-L14",
            VisionModel = "qwen3.7-plus",
            Fields =
            [
                Field(MonitoringDrawingFieldNames.PoleHeight, "立杆高度", 6.5),
                Field(MonitoringDrawingFieldNames.PoleBottomDimension, "立杆下端尺寸", 0.34),
                Field(MonitoringDrawingFieldNames.PoleTopDimension, "立杆上端尺寸", 0.28),
                Field(MonitoringDrawingFieldNames.PoleWallThickness, "立杆壁厚", 0.010),
                Field(MonitoringDrawingFieldNames.ArmMountingHeight, "横杆安装高度", 6.5),
                Field(MonitoringDrawingFieldNames.ArmLength, "横杆长度", 14),
                Field(MonitoringDrawingFieldNames.ArmNearDimension, "横杆近端尺寸", 0.28),
                Field(MonitoringDrawingFieldNames.ArmFarDimension, "横杆远端尺寸", 0.11),
                Field(MonitoringDrawingFieldNames.ArmWallThickness, "横杆壁厚", null),
                Field(MonitoringDrawingFieldNames.AttachmentProjectedArea, "设备迎风面积", null, "m²"),
                Field(MonitoringDrawingFieldNames.AttachmentWeight, "设备重量", null, "kN"),
                new MonitoringDrawingFieldCandidate
                {
                    FieldName = MonitoringDrawingFieldNames.ArmSegments,
                    DisplayName = "横杆分段明细",
                    Value = 2,
                    Unit = "段",
                    Confidence = 0.95,
                    RawAnnotation = "八角对角(110-195-280)×(4+6)×14000",
                    Region = "PDF第1页横杆规格与δ壁厚标注",
                    PageNumber = 1,
                    IsSelected = true
                }
            ],
            ArmSegments =
            [
                new MonitoringPoleArmSegment
                {
                    LengthM = 7,
                    NearDimensionM = 0.28,
                    FarDimensionM = 0.195,
                    WallThicknessM = 0.006
                },
                new MonitoringPoleArmSegment
                {
                    LengthM = 7,
                    NearDimensionM = 0.195,
                    FarDimensionM = 0.11,
                    WallThicknessM = 0.004
                }
            ]
        };
    }

    private static MonitoringDrawingCandidate CreateReconciledMonitoringDrawingSmokeCandidate()
    {
        const string json = """
            {
              "drawing_model":"H6.5-L3",
              "fields":{
                "pole_height":{"value":6.5,"unit":"m","raw_annotation":"八角对角(180-240)×5×6500","region":"主视图左侧标注","confidence":0.97,"conflict":false,"warning":""},
                "pole_bottom_dimension":{"value":240,"unit":"mm","raw_annotation":"","region":"","confidence":0.93,"conflict":false,"warning":""},
                "pole_top_dimension":{"value":120,"unit":"mm","raw_annotation":"","region":"","confidence":0.93,"conflict":false,"warning":""},
                "pole_wall_thickness":{"value":5,"unit":"mm","raw_annotation":"","region":"","confidence":0.94,"conflict":false,"warning":""},
                "arm_length":{"value":4,"unit":"m","raw_annotation":"八角对角(90-160)×4×3000","region":"横杆规格标注","confidence":0.96,"conflict":false,"warning":""},
                "arm_near_dimension":{"value":140,"unit":"mm","raw_annotation":"","region":"","confidence":0.90,"conflict":false,"warning":""},
                "arm_far_dimension":{"value":80,"unit":"mm","raw_annotation":"","region":"","confidence":0.90,"conflict":false,"warning":""},
                "arm_wall_thickness":{"value":4,"unit":"mm","raw_annotation":"","region":"","confidence":0.94,"conflict":false,"warning":""},
                "attachment_projected_area":{"value":null,"unit":"m2","raw_annotation":"","region":"","confidence":0,"conflict":false,"warning":"图纸未给"},
                "attachment_weight":{"value":null,"unit":"kN","raw_annotation":"","region":"","confidence":0,"conflict":false,"warning":"图纸未给"}
              },
              "arm_segments":[],
              "warnings":[]
            }
            """;
        var candidate = MonitoringDrawingVisionAiService.ParseCandidateResponse(
            json,
            "H6.5-L3-smoke.pdf",
            new string('c', 64),
            1,
            "vision-smoke");
        MonitoringDrawingCandidateRules.ValidateAndInitialize(candidate);
        return candidate;
    }

    private static MonitoringDrawingCandidate CreatePartialMonitoringDrawingSmokeCandidate()
    {
        static MonitoringDrawingFieldCandidate Field(
            string name,
            string display,
            double value) => new()
        {
            FieldName = name,
            DisplayName = display,
            Value = value,
            Unit = "m",
            Confidence = 0.96,
            RawAnnotation = "自动化部分候选证据",
            Region = "PDF第1页主视图",
            PageNumber = 1,
            IsSelected = true
        };

        return new MonitoringDrawingCandidate
        {
            SourceFileName = "H6.5-L3-partial-smoke.pdf",
            SourceFileSha256 = new string('b', 64),
            PageNumber = 1,
            DrawingModel = "H6.5-L3-partial",
            VisionModel = "qwen3.7-plus",
            Fields =
            [
                Field(MonitoringDrawingFieldNames.PoleHeight, "立杆高度", 6.5),
                Field(MonitoringDrawingFieldNames.PoleBottomDimension, "立杆下端尺寸", 0.24),
                Field(MonitoringDrawingFieldNames.PoleWallThickness, "立杆壁厚", 0.005),
                new MonitoringDrawingFieldCandidate
                {
                    FieldName = MonitoringDrawingFieldNames.ArmMountingHeight,
                    DisplayName = "横杆安装高度",
                    Unit = "m",
                    Value = null,
                    IsSelected = false
                }
            ]
        };
    }

    private static string AddSuffix(string path, string suffix)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        return Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(fullPath) + suffix + Path.GetExtension(fullPath));
    }

    private static void RenderWindow(Window window, string outputPath)
    {
        if (window.Content is not FrameworkElement content)
        {
            throw new InvalidOperationException("主窗口没有可渲染的根内容。");
        }

        var logicalWidth = window.ActualWidth;
        var logicalHeight = window.ActualHeight;
        content.Measure(new Size(logicalWidth, logicalHeight));
        content.Arrange(new Rect(0, 0, logicalWidth, logicalHeight));
        content.UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(content);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(logicalWidth * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(logicalHeight * dpi.DpiScaleY));

        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private static void RenderElement(FrameworkElement element, string outputPath)
    {
        element.UpdateLayout();
        var logicalWidth = Math.Max(1, element.ActualWidth);
        var logicalHeight = Math.Max(1, element.ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(logicalWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(logicalHeight * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        return FindVisualChildren<T>(parent).FirstOrDefault();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
