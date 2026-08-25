using System.Windows;
using TowerFoundation.Application;

namespace TowerFoundation.Desktop;

public sealed record EvidenceCandidateRow(string Field, string Value, string Evidence);

public partial class GeotechnicalEvidenceReviewWindow : Window
{
    public GeotechnicalEvidenceReviewWindow(GeotechnicalDocumentImportResult import)
    {
        var result = import.AiResult ??
                     throw new ArgumentException("AI候选结果为空。", nameof(import));
        SourceName = import.Document.SourceName;
        SourceText = import.Document.Content;
        EvidencePaneTitle = import.EvidencePaneTitle;
        ReviewInstruction = import.AiSourceType == TowerFoundation.Domain.ParameterSourceType.VisualAi
            ? "左侧保留视觉模型按PDF页码形成的证据摘录，右侧列出候选字段；确认后才写入项目。"
            : "左侧保留本机提取的原文，右侧列出AI候选；确认后才写入项目，取消则保持原参数不变。";
        CandidateRows = BuildRows(result);
        ReviewSummary =
            $"AI置信度{result.Confidence:P0}；共{CandidateRows.Count}项非空候选，" +
            (result.CriticalWarnings.Count == 0
                ? "未报告关键冲突。"
                : $"有{result.CriticalWarnings.Count}项关键冲突，采用后仍须逐项核对。");
        InitializeComponent();
        DataContext = this;
    }

    public string SourceName { get; }

    public string SourceText { get; }

    public string EvidencePaneTitle { get; }

    public string ReviewInstruction { get; }

    public string ReviewSummary { get; }

    public IReadOnlyList<EvidenceCandidateRow> CandidateRows { get; }

    private void Accept_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static IReadOnlyList<EvidenceCandidateRow> BuildRows(
        GeotechnicalAiExtractionResult result)
    {
        var rows = new List<EvidenceCandidateRow>();
        void Add(string field, string value, string? evidence = null)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add(new EvidenceCandidateRow(
                    field,
                    value,
                    string.IsNullOrWhiteSpace(evidence) ? result.Evidence : evidence));
            }
        }

        Add("项目名称", result.ProjectName);
        Add("场址", result.SiteLocation);
        Add("省市县", result.Province + result.City + result.County);
        Add("修正后承载力fa", Format(result.BearingCapacityKpa, "kPa"));
        Add("承载力fak", Format(result.CharacteristicBearingCapacityKpa, "kPa"));
        Add("土重度", Format(result.SoilUnitWeightKnPerM3, "kN/m³"));
        Add("内摩擦角", Format(result.InternalFrictionAngleDegree, "°"));
        Add("压缩模量Es", Format(result.CompressionModulusMpa, "MPa"));
        Add("基底摩擦系数", Format(result.BaseFrictionCoefficient, string.Empty));
        Add("地下水埋深", Format(result.GroundwaterDepthM, "m"));
        Add("设防烈度", result.SeismicIntensityDegree is { } intensity ? $"{intensity}度" : string.Empty);
        Add("基本地震加速度", Format(result.DesignBasicGroundAccelerationG, "g"));
        Add("设计地震分组", result.DesignEarthquakeGroup);
        Add("场地类别", result.SiteClass);
        Add("特殊土风险", result.SpecialSoilRisks);
        Add("基础建议", result.RecommendedFoundationType);
        foreach (var layer in result.PileSoilLayers)
        {
            Add(
                "桩土分层",
                $"{layer.Name}：h={layer.ThicknessM:F2}m，qsik={layer.SideResistanceKpa:F1}kPa，qpk={layer.TipResistanceKpa:F1}kPa",
                result.Evidence);
        }
        foreach (var warning in result.CriticalWarnings)
        {
            Add("关键冲突", warning, "冲突字段不会被视为已人工确认");
        }
        return rows;
    }

    private static string Format(double? value, string unit) => value is { } number
        ? $"{number:0.###}{unit}"
        : string.Empty;
}
