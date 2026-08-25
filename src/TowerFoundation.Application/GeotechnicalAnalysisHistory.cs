using System.Text.Json.Serialization;
using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public enum GeotechnicalAnalysisMethod
{
    WordTextAi,
    PdfOcrAi,
    PdfOcrOnly,
    VisualPdfAi
}

public sealed record GeotechnicalAnalysisRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset? LastUsedAt { get; init; }

    public int UsageCount { get; init; }

    public bool WasApplied { get; init; }

    public string SourceFilePath { get; init; } = string.Empty;

    public string SourceName { get; init; } = string.Empty;

    public long SourceFileLength { get; init; }

    public DateTimeOffset? SourceFileLastWriteTime { get; init; }

    public GeotechnicalAnalysisMethod AnalysisMethod { get; init; }

    public FoundationType FoundationType { get; init; }

    public string ProviderDisplay { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public ParameterSourceType AiSourceType { get; init; } = ParameterSourceType.DeepSeek;

    public string EvidencePaneTitle { get; init; } = "地勘证据摘录";

    public string DocumentContent { get; init; } = string.Empty;

    public GeotechnicalAiExtractionResult? AiResult { get; init; }

    public int PageCount { get; init; }

    public int ProcessedPageCount { get; init; }

    public double MeanConfidence { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    [JsonIgnore]
    public bool CanReuse => AiResult is not null;

    [JsonIgnore]
    public string MethodDisplay => AnalysisMethod switch
    {
        GeotechnicalAnalysisMethod.WordTextAi => "Word文字AI",
        GeotechnicalAnalysisMethod.PdfOcrAi => "本地OCR+文字AI",
        GeotechnicalAnalysisMethod.PdfOcrOnly => "本地OCR",
        GeotechnicalAnalysisMethod.VisualPdfAi => "视觉AI",
        _ => "地勘分析"
    };

    [JsonIgnore]
    public string FoundationTypeDisplay => FoundationType switch
    {
        FoundationType.RectangularShortColumn => "独立基础－矩形柱",
        FoundationType.CircularShortColumn => "独立基础－圆形柱",
        FoundationType.Raft => "中央塔柱筏板基础",
        FoundationType.RigidShortPile => "刚性短柱桩基础－圆形",
        FoundationType.RigidRectangularShortPile => "刚性短柱桩基础－矩形",
        FoundationType.Pile => "独立灌注桩及连梁基础",
        _ => FoundationType.ToString()
    };

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(SourceName)
                ? Path.GetFileName(SourceFilePath)
                : SourceName;
            var confidence = AiResult is null
                ? string.Empty
                : $" · 置信度{AiResult.Confidence:P0}";
            return $"{CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {name} · {MethodDisplay} · {FoundationTypeDisplay}{confidence}";
        }
    }
}

public interface IGeotechnicalAnalysisHistoryService
{
    IReadOnlyList<GeotechnicalAnalysisRecord> Load();

    GeotechnicalAnalysisRecord Save(GeotechnicalAnalysisRecord record);

    void MarkApplied(Guid id);

    bool Delete(Guid id);
}
