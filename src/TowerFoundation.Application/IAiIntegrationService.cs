namespace TowerFoundation.Application;

public enum AiOperatingMode
{
    OnlinePreferred,
    OfflineOnly
}

public sealed class ApplicationSettings
{
    public int SchemaVersion { get; set; } = 6;

    public AiOperatingMode AiMode { get; set; } = AiOperatingMode.OnlinePreferred;

    public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com";

    public string DeepSeekModel { get; set; } = "deepseek-v4-pro";

    public string VisionBaseUrl { get; set; } =
        "https://dashscope.aliyuncs.com/compatible-mode/v1";

    public string VisionModel { get; set; } = "qwen3.7-plus";

    public int VisionPagesPerBatch { get; set; } = 2;

    public int RequestTimeoutSeconds { get; set; } = 45;

    public string DefaultProjectDirectory { get; set; } =
        ApplicationPathDefaults.ResolveProjectDirectory();

    public string DefaultExportDirectory { get; set; } =
        ApplicationPathDefaults.ResolveExportDirectory();

    public string DefaultGeotechnicalHistoryDirectory { get; set; } =
        ApplicationPathDefaults.ResolveGeotechnicalHistoryDirectory();

    public string DefaultMonitoringDrawingHistoryDirectory { get; set; } =
        ApplicationPathDefaults.ResolveMonitoringDrawingHistoryDirectory();

    public int OcrStartPage { get; set; } = 1;

    public int OcrEndPage { get; set; }

    public bool HasApiKey { get; set; }

    public string ApiKeyLastFour { get; set; } = string.Empty;

    public bool HasVisionApiKey { get; set; }

    public string VisionApiKeyLastFour { get; set; } = string.Empty;
}

public static class VisualAiModelCatalog
{
    public const string DefaultModel = "qwen3.7-plus";

    public static IReadOnlyList<string> SupportedModels { get; } =
    [
        "qwen3.7-plus",
        "qwen3.7-plus-2026-05-26",
        "qwen3.6-plus",
        "qwen3.6-flash",
        "qwen3-vl-plus",
        "qwen3-vl-flash"
    ];

    public static bool IsSupported(string? model) =>
        !string.IsNullOrWhiteSpace(model) &&
        SupportedModels.Contains(model.Trim(), StringComparer.OrdinalIgnoreCase);
}

public static class ApplicationPathDefaults
{
    public static string ResolveProjectDirectory() =>
        Path.Combine(ResolveApplicationDirectory(), "塔基智设", "项目");

    public static string ResolveExportDirectory() =>
        Path.Combine(ResolveApplicationDirectory(), "塔基智设", "成果");

    public static string ResolveGeotechnicalHistoryDirectory() =>
        Path.Combine(ResolveApplicationDirectory(), "塔基智设", "地勘分析记录");

    public static string ResolveMonitoringDrawingHistoryDirectory() =>
        Path.Combine(ResolveApplicationDirectory(), "塔基智设", "监控杆图纸识别记录");

    public static string NormalizeDirectory(string? configuredPath, string fallbackPath)
    {
        try
        {
            return Path.GetFullPath(
                string.IsNullOrWhiteSpace(configuredPath)
                    ? fallbackPath
                    : configuredPath.Trim());
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Path.GetFullPath(fallbackPath);
        }
    }

    private static string ResolveApplicationDirectory()
    {
        var baseDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
        }

        return string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;
    }
}

public sealed class AiConnectionResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed record AiOperationProgress(
    int CurrentStep,
    int TotalSteps,
    string Message);

public sealed record VisionModelSwitchRequest(
    string CurrentModel,
    string ProposedModel,
    string Operation,
    string FailureReason);

public sealed class VisionModelSwitchOptions
{
    public Func<VisionModelSwitchRequest, CancellationToken, Task<bool>>? ConfirmAsync { get; init; }
}

public sealed class DocumentTextExtractionResult
{
    public string SourceName { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public int CharacterCount => Content.Length;
}

public sealed class GeotechnicalAiExtractionResult
{
    public string ProjectName { get; init; } = string.Empty;

    public string SiteLocation { get; init; } = string.Empty;

    public string Province { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string County { get; init; } = string.Empty;

    public double? Longitude { get; init; }

    public double? Latitude { get; init; }

    public string InvestigationStage { get; init; } = string.Empty;

    public string InvestigationGrade { get; init; } = string.Empty;

    public string BuildingSafetyGrade { get; init; } = string.Empty;

    public double? BearingCapacityKpa { get; init; }

    public double? CharacteristicBearingCapacityKpa { get; init; }

    public double? BearingCapacityWidthCorrectionFactor { get; init; }

    public double? BearingCapacityDepthCorrectionFactor { get; init; }

    public double? SoilUnitWeightKnPerM3 { get; init; }

    public double? CohesionKpa { get; init; }

    public double? InternalFrictionAngleDegree { get; init; }

    public double? CompressionModulusMpa { get; init; }

    public double? SoilBelowBaseUnitWeightKnPerM3 { get; init; }

    public double? SoilAboveBaseAverageUnitWeightKnPerM3 { get; init; }

    public double? BaseFrictionCoefficient { get; init; }

    public double? GroundwaterDepthM { get; init; }

    public IReadOnlyList<double> GroundwaterDepthCandidatesM { get; init; } = [];

    public string SoilDescription { get; init; } = string.Empty;

    public string Evidence { get; init; } = string.Empty;

    public IReadOnlyList<int> EvidencePages { get; init; } = [];

    public IReadOnlyList<string> EvidenceLocations { get; init; } = [];

    public string RecommendedFoundationType { get; init; } = string.Empty;

    public string SpecialSoilRisks { get; init; } = string.Empty;

    public IReadOnlyList<PileSoilLayerCandidate> PileSoilLayers { get; init; } = [];

    public IReadOnlyList<PileParameterSetCandidate> PileParameterOptions { get; init; } = [];

    public double? SinglePileHorizontalCapacityKn { get; init; }

    public int? SeismicIntensityDegree { get; init; }

    public double? DesignBasicGroundAccelerationG { get; init; }

    public string DesignEarthquakeGroup { get; init; } = string.Empty;

    public string SiteClass { get; init; } = string.Empty;

    public double? CharacteristicPeriodS { get; init; }

    public IReadOnlyList<string> CriticalWarnings { get; init; } = [];

    public double Confidence { get; init; }
}

public sealed class PileParameterSetCandidate
{
    public string PileMethod { get; init; } = string.Empty;

    public string LayerName { get; init; } = string.Empty;

    public double? TopDepthM { get; init; }

    public double? BottomDepthM { get; init; }

    public double? ThicknessM { get; init; }

    public double? SoilUnitWeightKnPerM3 { get; init; }

    public double? CharacteristicBearingCapacityKpa { get; init; }

    public double? CompressionModulusMpa { get; init; }

    public double? HorizontalResistanceCoefficientMnPerM4 { get; init; }

    public double? SideResistanceLimitStandardKpa { get; init; }

    public double? TipResistanceLimitStandardKpa { get; init; }

    public double? UpliftCoefficient { get; init; }

    public string Evidence { get; init; } = string.Empty;
}

public sealed class PileSoilLayerCandidate
{
    public string Name { get; init; } = string.Empty;

    public double ThicknessM { get; init; }

    public double SideResistanceKpa { get; init; }

    public double TipResistanceKpa { get; init; }

    public double UpliftCoefficient { get; init; }
}

public sealed class AnchorBoltAiExtractionResult
{
    public int? BoltCount { get; init; }

    public double? NominalDiameterMm { get; init; }

    public double? BoltCircleDiameterMm { get; init; }

    public double? EmbedmentDepthMm { get; init; }

    public double? TensileStrengthDesignMpa { get; init; }

    public double? ShearStrengthDesignMpa { get; init; }

    public double? ThreadStressAreaFactor { get; init; }

    public string MaterialGrade { get; init; } = string.Empty;

    public string Evidence { get; init; } = string.Empty;

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public double Confidence { get; init; }
}

public interface IApplicationSettingsService
{
    ApplicationSettings Load();

    void Save(
        ApplicationSettings settings,
        string? replacementApiKey = null,
        bool clearApiKey = false,
        string? replacementVisionApiKey = null,
        bool clearVisionApiKey = false);

    string? GetApiKey();

    string? GetVisionApiKey();
}

public interface IDeepSeekService
{
    Task<AiConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<GeotechnicalAiExtractionResult> ExtractGeotechnicalParametersAsync(
        string documentText,
        IProgress<AiOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IAnchorDrawingAiService
{
    Task<AnchorBoltAiExtractionResult> ExtractAnchorBoltParametersAsync(
        string documentText,
        IProgress<AiOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class VisualGeotechnicalAnalysisResult
{
    public string SourceName { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public int PageCount { get; init; }

    public int ProcessedPageCount { get; init; }

    public string EvidenceText { get; init; } = string.Empty;

    public GeotechnicalAiExtractionResult AiResult { get; init; } = new();

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IVisualGeotechnicalAiService
{
    Task<AiConnectionResult> TestConnectionAsync(
        CancellationToken cancellationToken = default);

    Task<VisualGeotechnicalAnalysisResult> AnalyzePdfAsync(
        string path,
        string foundationRequirement,
        IProgress<AiOperationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        VisionModelSwitchOptions? switchOptions = null);
}

public interface IWordTextExtractor
{
    Task<DocumentTextExtractionResult> ExtractAsync(
        string path,
        CancellationToken cancellationToken = default);
}
