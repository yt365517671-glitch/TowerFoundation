using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using TowerFoundation.Application;
using TowerFoundation.Domain;

namespace TowerFoundation.Desktop;

public sealed record AnchorConnectionChoice(string Name, AnchorConnectionType Value);

public sealed record RiskStateChoice(string Name, EngineeringRiskState Value);

public sealed record PileSettlementMethodChoice(string Name, PileSettlementMethod Value);

public partial class SmartSpecialtyCompletionWindow : Window
{
    private readonly SpecialtyAutoFillService _autoFillService = new();
    private readonly IApplicationSettingsService _settingsService;
    private readonly IDeepSeekService _deepSeekService;
    private readonly IWordTextExtractor _wordTextExtractor;
    private readonly ILocalPdfOcrService _localPdfOcrService;
    private readonly string? _focusCode;
    private readonly string _originalSpecialtyJson;
    private readonly string _originalGeotechnicalJson;
    private readonly string _originalPileJson;
    private bool _accepted;

    public SmartSpecialtyCompletionWindow(
        ProjectModel project,
        IApplicationSettingsService settingsService,
        IDeepSeekService deepSeekService,
        IWordTextExtractor wordTextExtractor,
        ILocalPdfOcrService localPdfOcrService,
        string? focusCode = null)
    {
        Project = project;
        _settingsService = settingsService;
        _deepSeekService = deepSeekService;
        _wordTextExtractor = wordTextExtractor;
        _localPdfOcrService = localPdfOcrService;
        _focusCode = focusCode;
        _originalSpecialtyJson = JsonSerializer.Serialize(project.FoundationSettings.SpecialtyDesign);
        _originalGeotechnicalJson = JsonSerializer.Serialize(project.Geotechnical);
        _originalPileJson = JsonSerializer.Serialize(project.FoundationSettings.Pile);

        AnchorConnectionChoices =
        [
            new("请选择连接方式", AnchorConnectionType.NotDetermined),
            new("地脚锚栓笼连接", AnchorConnectionType.AnchorBoltCage),
            new("塔身直接埋入基础", AnchorConnectionType.DirectEmbedded),
            new("其他连接（专项复核）", AnchorConnectionType.Other)
        ];
        CrackEnvironmentChoices =
        [
            "普通室外（推荐）",
            "潮湿或地下水环境",
            "腐蚀、滨海或特殊介质环境（专项复核）"
        ];
        RiskStateChoices =
        [
            new("请选择地勘结论", EngineeringRiskState.NotAssessed),
            new("地勘明确无风险", EngineeringRiskState.NotPresent),
            new("存在，处理未确认", EngineeringRiskState.PresentTreatmentUnconfirmed),
            new("存在，已有专项处理", EngineeringRiskState.PresentTreatmentConfirmed)
        ];
        PileSettlementMethodChoices =
        [
            new("请选择沉降方法", PileSettlementMethod.NotSelected),
            new("静载试验 Q-s 曲线（优先）", PileSettlementMethod.StaticLoadTestCurve),
            new("经审查的专项计算结果", PileSettlementMethod.ReviewedSpecialCalculation),
            new("Mindlin 弹性复核估算（不判通过）", PileSettlementMethod.MindlinReviewEstimate)
        ];

        InitializeComponent();
        DataContext = this;
        Loaded += SmartSpecialtyCompletionWindow_Loaded;
        RefreshState();
    }

    public ProjectModel Project { get; }

    public IReadOnlyList<AnchorConnectionChoice> AnchorConnectionChoices { get; }

    public IReadOnlyList<string> CrackEnvironmentChoices { get; }

    public IReadOnlyList<RiskStateChoice> RiskStateChoices { get; }

    public IReadOnlyList<PileSettlementMethodChoice> PileSettlementMethodChoices { get; }

    public SpecialtyAutoFillResult ApplyRecommendedDefaultsForAutomation()
    {
        var result = _autoFillService.ApplyRecommendedDefaults(Project);
        RefreshBindings();
        return result;
    }

    private void SmartSpecialtyCompletionWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_focusCode?.StartsWith("ANCHOR", StringComparison.Ordinal) == true)
        {
            AnchorConnectionComboBox.Focus();
        }
        else if (_focusCode?.StartsWith("CRACK", StringComparison.Ordinal) == true && CrackCard.Visibility == Visibility.Visible)
        {
            CrackEnvironmentComboBox.Focus();
        }
    }

    private void ApplyRecommendedDefaults_Click(object sender, RoutedEventArgs e)
    {
        var result = ApplyRecommendedDefaultsForAutomation();
        FooterHintText.Text = result.Messages.Count == 0
            ? "当前可安全预填的项目已经完整。"
            : string.Join("；", result.Messages);
    }

    private void CrackEnvironmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || CrackEnvironmentComboBox.SelectedItem is not string environment)
        {
            return;
        }

        SpecialtyAutoFillService.ApplyCrackEnvironmentPreset(
            Project.FoundationSettings.SpecialtyDesign.Crack,
            environment);
        RefreshBindings();
        FooterHintText.Text = environment.Contains("专项复核", StringComparison.Ordinal)
            ? "已记录特殊环境；通用预设不会直接形成裂缝验算通过结论。"
            : "已按所选环境填入裂缝候选参数，请结合项目条件确认。";
    }

    private void AnchorConnectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        var anchor = Project.FoundationSettings.SpecialtyDesign.AnchorBolts;
        anchor.TemplateName = AnchorConnectionComboBox.Text;
        RefreshState();
    }

    private async void ImportAnchorDrawing_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择塔脚或锚栓详图",
            Filter = "支持的详图|*.pdf;*.docx|PDF 文件|*.pdf|Word 文档|*.docx"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SetBusy(true, "正在读取塔脚详图…", 5);
            string text;
            if (string.Equals(Path.GetExtension(dialog.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var ocrProgress = new Progress<OcrProgress>(value =>
                {
                    var percent = value.TotalPages <= 0
                        ? 20
                        : Math.Clamp(value.CurrentPage * 65 / value.TotalPages, 5, 70);
                    SetBusy(true, value.Message, percent);
                });
                var extraction = await _localPdfOcrService.ExtractAsync(dialog.FileName, ocrProgress);
                text = extraction.Content;
            }
            else
            {
                var extraction = await _wordTextExtractor.ExtractAsync(dialog.FileName);
                text = extraction.Content;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("文档未提取到可供分析的文字，请手工填写锚栓参数。");
            }

            var settings = _settingsService.Load();
            if (settings.AiMode == AiOperatingMode.OfflineOnly ||
                string.IsNullOrWhiteSpace(_settingsService.GetApiKey()) ||
                _deepSeekService is not IAnchorDrawingAiService anchorAiService)
            {
                FooterHintText.Text = "本地读取已完成；当前为离线模式或未配置AI，请按原图手工填写锚栓参数。";
                return;
            }

            var aiProgress = new Progress<AiOperationProgress>(value =>
            {
                var percent = value.TotalSteps <= 0
                    ? 75
                    : Math.Clamp(70 + value.CurrentStep * 28 / value.TotalSteps, 70, 98);
                SetBusy(true, value.Message, percent);
            });
            var result = await anchorAiService.ExtractAnchorBoltParametersAsync(text, aiProgress);
            ApplyAnchorExtraction(result, dialog.FileName);
            SetBusy(true, "识别完成，已回填有原文依据的参数。", 100);
            FooterHintText.Text = BuildAnchorResultMessage(result);
            RefreshBindings();
        }
        catch (Exception exception)
        {
            AppDialogWindow.Show(
                this,
                exception.Message + "\n\n你仍可直接在本窗口手工填写，不影响离线计算流程。",
                "详图识别未完成",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            FooterHintText.Text = "详图识别未完成，已保留手工填写入口。";
        }
        finally
        {
            SetBusy(false, string.Empty, 0);
        }
    }

    private void ApplyAnchorExtraction(AnchorBoltAiExtractionResult result, string sourcePath)
    {
        var anchor = Project.FoundationSettings.SpecialtyDesign.AnchorBolts;
        anchor.ConnectionType = AnchorConnectionType.AnchorBoltCage;
        anchor.TemplateName = "详图智能识别";
        if (result.BoltCount is > 0) anchor.BoltCount = result.BoltCount.Value;
        if (result.NominalDiameterMm is > 0) anchor.NominalDiameterMm = result.NominalDiameterMm.Value;
        if (result.BoltCircleDiameterMm is > 0) anchor.BoltCircleDiameterM = result.BoltCircleDiameterMm.Value / 1000;
        if (result.EmbedmentDepthMm is > 0) anchor.EmbedmentDepthM = result.EmbedmentDepthMm.Value / 1000;
        if (result.TensileStrengthDesignMpa is > 0) anchor.TensileStrengthDesignMpa = result.TensileStrengthDesignMpa.Value;
        if (result.ShearStrengthDesignMpa is > 0) anchor.ShearStrengthDesignMpa = result.ShearStrengthDesignMpa.Value;
        if (result.ThreadStressAreaFactor is > 0) anchor.ThreadStressAreaFactor = result.ThreadStressAreaFactor.Value;
        if (!string.IsNullOrWhiteSpace(result.MaterialGrade)) anchor.MaterialGrade = result.MaterialGrade;

        anchor.Source.SourceType = ParameterSourceType.DeepSeek;
        anchor.Source.SourceDocument = Path.GetFileName(sourcePath);
        anchor.Source.SourceLocation = result.Evidence;
        anchor.Source.Confidence = result.Confidence;
        anchor.Source.IsConfirmed = false;
        anchor.Source.Note = result.Warnings.Count == 0
            ? "只回填原文明确出现的数值，采用前请对照原图确认。"
            : string.Join("；", result.Warnings);
    }

    private static string BuildAnchorResultMessage(AnchorBoltAiExtractionResult result)
    {
        var recognized = new List<string>();
        if (result.BoltCount is > 0) recognized.Add($"数量{result.BoltCount}");
        if (result.NominalDiameterMm is > 0) recognized.Add($"直径{result.NominalDiameterMm:F0} mm");
        if (result.BoltCircleDiameterMm is > 0) recognized.Add($"锚栓圆{result.BoltCircleDiameterMm:F0} mm");
        if (result.EmbedmentDepthMm is > 0) recognized.Add($"埋深{result.EmbedmentDepthMm:F0} mm");
        return recognized.Count == 0
            ? "AI未找到有原文依据的锚栓数值，请按原图手工填写。"
            : "已回填：" + string.Join("、", recognized) + "；采用前请对照原图确认。";
    }

    private void ApplyAndClose_Click(object sender, RoutedEventArgs e)
    {
        var pile = Project.FoundationSettings.Pile;
        pile.UseConfirmedServiceSettlement =
            pile.SettlementMethod == PileSettlementMethod.ReviewedSpecialCalculation &&
            pile.ServiceSettlementFromTestOrSpecialCalculationMm >= 0;
        if (pile.SettlementMethod != PileSettlementMethod.NotSelected &&
            Project.FoundationSettings.SpecialtyDesign.Settlement.AllowableSettlementMm > 0)
        {
            pile.SettlementSource.IsConfirmed = true;
            Project.FoundationSettings.SpecialtyDesign.Settlement.Source.IsConfirmed = true;
        }
        if (pile.UseNegativeSkinFriction &&
            pile.NegativeSkinFrictionLayers.Any(layer =>
                layer.ThicknessM > 0 && layer.UnitNegativeSkinFrictionKpa >= 0))
        {
            pile.NegativeSkinFrictionSource.IsConfirmed = true;
        }

        var anchor = Project.FoundationSettings.SpecialtyDesign.AnchorBolts;
        if (anchor.ConnectionType == AnchorConnectionType.AnchorBoltCage &&
            anchor.BoltCount >= 3 &&
            anchor.NominalDiameterMm > 0 &&
            anchor.BoltCircleDiameterM > 0 &&
            anchor.EmbedmentDepthM > 0)
        {
            anchor.Source.IsConfirmed = true;
        }
        if (anchor.UseProgramCalculatedConcreteCapacity &&
            anchor.ConcreteMemberThicknessMm > 0 &&
            anchor.MinimumAnchorEdgeDistanceMm > 0 &&
            anchor.MinimumAnchorSpacingMm > 0 &&
            anchor.EffectiveEmbedmentDepthMm > 0 &&
            anchor.ConcreteBreakoutCoefficient > 0 &&
            anchor.PulloutBearingCoefficient > 0 &&
            anchor.EdgeBreakoutCoefficient > 0)
        {
            anchor.ProgramConcreteModelSource.IsConfirmed = true;
        }
        pile.UseUserConfirmedPileHeadStructuralForces =
            pile.MaximumPileHeadHorizontalKn > 0 ||
            pile.MaximumPileHeadMomentKnM > 0;
        pile.UseUserConfirmedTieBeamForces =
            Project.FoundationSettings.FoundationType == FoundationType.Pile &&
            pile.PileCount > 1 &&
            (pile.TieBeamAxialTensionKn > 0 ||
             pile.TieBeamMomentKnM > 0 ||
             pile.TieBeamShearKn > 0);
        pile.IsConfirmed = Project.FoundationSettings.FoundationType != FoundationType.Pile ||
            pile.HorizontalResistanceCoefficientMnPerM4 > 0 &&
            pile.ConcreteElasticModulusMpa > 0 &&
            pile.ConcreteCompressiveStrengthMpa > 0 &&
            pile.PileMainBarCount >= 6 &&
            pile.PileMainBarDiameterMm > 0 &&
            pile.StirrupDiameterMm > 0 &&
            pile.StirrupSpacingMm > 0;
        Project.ModifiedAt = DateTimeOffset.Now;
        _accepted = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_accepted)
        {
            Project.FoundationSettings.SpecialtyDesign =
                JsonSerializer.Deserialize<SpecialtyDesignInput>(_originalSpecialtyJson) ?? new SpecialtyDesignInput();
            Project.Geotechnical =
                JsonSerializer.Deserialize<GeotechnicalInput>(_originalGeotechnicalJson) ?? new GeotechnicalInput();
            Project.FoundationSettings.Pile =
                JsonSerializer.Deserialize<PileFoundationSettings>(_originalPileJson) ?? new PileFoundationSettings();
        }
        base.OnClosing(e);
    }

    private void RefreshBindings()
    {
        var current = DataContext;
        DataContext = null;
        DataContext = current;
        RefreshState();
    }

    private void RefreshState()
    {
        if (!IsInitialized)
        {
            return;
        }

        var applicability = _autoFillService.DetermineApplicability(Project);
        DeformationCard.Visibility = applicability.NeedsDeformationLimits ? Visibility.Visible : Visibility.Collapsed;
        SettlementCard.Visibility = applicability.NeedsSettlementParameters ? Visibility.Visible : Visibility.Collapsed;
        CrackCard.Visibility = applicability.NeedsCrackParameters ? Visibility.Visible : Visibility.Collapsed;
        AnchorParametersPanel.Visibility = applicability.NeedsAnchorParameters ? Visibility.Visible : Visibility.Collapsed;
        PedestalStructuralCard.Visibility = applicability.NeedsPedestalStructuralParameters ? Visibility.Visible : Visibility.Collapsed;
        PileStructuralCard.Visibility = applicability.NeedsPileStructuralParameters ? Visibility.Visible : Visibility.Collapsed;
        HighWaterCard.Visibility = applicability.NeedsHighWaterParameters ? Visibility.Visible : Visibility.Collapsed;
        var isPile = Project.FoundationSettings.FoundationType == FoundationType.Pile;
        SettlementExperiencePanel.Visibility = isPile ? Visibility.Collapsed : Visibility.Visible;
        SettlementLayerGrid.Visibility = isPile ? Visibility.Collapsed : Visibility.Visible;
        PileSettlementPanel.Visibility = isPile ? Visibility.Visible : Visibility.Collapsed;

        var applicableItems = new List<string>();
        if (applicability.NeedsDeformationLimits) applicableItems.Add("桩顶变形限值");
        if (applicability.NeedsSettlementParameters) applicableItems.Add("沉降参数");
        if (applicability.NeedsCrackParameters) applicableItems.Add("裂缝环境");
        if (applicability.NeedsPedestalStructuralParameters) applicableItems.Add("短柱结构与配筋");
        if (applicability.NeedsPileStructuralParameters) applicableItems.Add("桩身m法与连梁");
        if (applicability.NeedsHighWaterParameters) applicableItems.Add("设计最高水位抗浮");
        applicableItems.Add(applicability.NeedsAnchorParameters ? "锚栓参数" : "塔脚连接方式");
        SummaryText.Text = $"当前基础：{FoundationTypeName(Project.FoundationSettings.FoundationType)}；需要处理：{string.Join("、", applicableItems)}。特殊地基逐项选择，存在风险时自动转专项处理。";
    }

    private void SetBusy(bool isBusy, string message, int percent)
    {
        ProgressPanel.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        ProgressText.Text = message;
        AiProgressBar.Value = percent;
    }

    private static string FoundationTypeName(FoundationType type) => type switch
    {
        FoundationType.RectangularShortColumn => "独立基础-矩形柱",
        FoundationType.CircularShortColumn => "独立基础-圆形柱",
        FoundationType.Raft => "筏板基础",
        FoundationType.Pile => "独立灌注桩",
        FoundationType.RigidShortPile => "刚性短柱桩基础-圆形",
        FoundationType.RigidRectangularShortPile => "刚性短柱桩基础-矩形",
        _ => type.ToString()
    };
}
