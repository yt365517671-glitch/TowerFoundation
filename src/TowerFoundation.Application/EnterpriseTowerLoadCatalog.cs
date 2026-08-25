using System.Globalization;
using System.Text.Json.Serialization;
using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public sealed class TowerLoadCatalogDocument
{
    public int SchemaVersion { get; init; }

    public string CatalogEdition { get; init; } = string.Empty;

    public string NoticeNumber { get; init; } = string.Empty;

    public string EffectiveDate { get; init; } = string.Empty;

    public bool IsCompleteForNewDesign { get; init; }

    public string StatusMessage { get; init; } = string.Empty;

    public List<string> StandardNumbers { get; init; } = [];

    public List<TowerLoadCatalogRecord> Records { get; init; } = [];
}

public sealed record TowerLoadCatalogStatus(
    string CatalogEdition,
    string NoticeNumber,
    string EffectiveDate,
    bool IsCompleteForNewDesign,
    int CurrentRecordCount,
    int LegacyRecordCount,
    IReadOnlyList<string> StandardNumbers,
    string StatusMessage)
{
    public bool HasCurrentRecords => CurrentRecordCount > 0;

    public string UserDisplay => IsCompleteForNewDesign && HasCurrentRecords
        ? $"现行{CatalogEdition}企业塔型荷载库已就绪，共{CurrentRecordCount}条经审查记录。"
        : $"现行{CatalogEdition}企业塔型荷载库暂不可用；{StatusMessage}";
}

public static class TowerLoadCatalogAuthorityPolicy
{
    private static readonly HashSet<string> CurrentStandardNumbers =
    [
        "Q/ZTT 1023-2025",
        "Q/ZTT 1032-2025"
    ];

    public static bool IsCurrentStandard(string? standardNo) =>
        !string.IsNullOrWhiteSpace(standardNo) &&
        CurrentStandardNumbers.Contains(standardNo.Trim());
}

public sealed class TowerLoadCatalogRecord
{
    public string Id { get; init; } = string.Empty;

    public string SourceId { get; init; } = string.Empty;

    public string SourceTitle { get; init; } = string.Empty;

    public string StandardNo { get; init; } = string.Empty;

    public string CatalogVersion { get; init; } = string.Empty;

    public int SourcePdfPage { get; init; }

    public int SourceTableRow { get; init; }

    public string Group { get; init; } = string.Empty;

    public string TowerType { get; init; } = string.Empty;

    public string TowerCode { get; init; } = string.Empty;

    public double? TowerWeightT { get; init; }

    public double? AttachmentWeightT { get; init; }

    public double? TotalWeightT { get; init; }

    public TowerLoadReactionPair? OverallBaseReaction { get; init; }

    public TowerSingleLegReactionPair? SingleLegReaction { get; init; }

    public bool UsableForAutomaticOverallLoad { get; init; }

    public bool UsableForAutomaticSingleLegLoad { get; init; }

    public string ReviewStatus { get; init; } = string.Empty;

    public IReadOnlyList<string> ReviewIssues { get; init; } = [];

    public double? SourceDeclaredHeightM { get; init; }

    public string? SuggestedCanonicalCode { get; init; }

    public string? CatalogAnomaly { get; init; }

    [JsonIgnore]
    public bool CanApplyOverallStandardLoad =>
        UsableForAutomaticOverallLoad &&
        OverallBaseReaction?.Standard is { AxialKn: > 0 };

    [JsonIgnore]
    public bool CanApplyOverallBasicLoad =>
        UsableForAutomaticOverallLoad &&
        OverallBaseReaction?.Basic is { AxialKn: > 0 };

    [JsonIgnore]
    public bool CanApplySingleLegStandardLoad =>
        UsableForAutomaticSingleLegLoad &&
        SingleLegReaction?.Standard?.CompressionControl is
            { CompressionKn: > 0 } &&
        SingleLegReaction.Standard.TensionControl is
            { TensionKn: > 0 };

    [JsonIgnore]
    public bool CanApplySingleLegBasicLoad =>
        UsableForAutomaticSingleLegLoad &&
        SingleLegReaction?.Basic?.CompressionControl is
            { CompressionKn: > 0 } &&
        SingleLegReaction.Basic.TensionControl is
            { TensionKn: > 0 };

    [JsonIgnore]
    public bool CanApplyOverallDesignLoads =>
        CanApplyOverallStandardLoad && CanApplyOverallBasicLoad;

    [JsonIgnore]
    public bool CanApplySingleLegDesignLoads =>
        CanApplySingleLegStandardLoad && CanApplySingleLegBasicLoad;

    [JsonIgnore]
    public bool CanApplyAnyStandardLoad =>
        CanApplyOverallDesignLoads || CanApplySingleLegDesignLoads;

    [JsonIgnore]
    public string SelectionDisplay => $"{TowerCode}　{TowerType}";

    [JsonIgnore]
    public string SourceDisplay => $"{SourceTitle} {CatalogVersion}（{StandardNo}）";

    [JsonIgnore]
    public double? SourceDeclaredWindPressureKpa =>
        EnterpriseTowerLoadService.ParseWindPressure(this);

    [JsonIgnore]
    public string AvailabilityDisplay =>
        CanApplyOverallDesignLoads && CanApplySingleLegDesignLoads
        ? "可按基础形式同步回填整塔或单塔腿标准/基本组合"
        : CanApplySingleLegDesignLoads
            ? "可同步回填单塔腿标准/基本组合"
        : CanApplyOverallDesignLoads
        ? "可同步回填整塔基础端标准/基本组合"
        : ReviewStatus == "source_catalog_conflict"
            ? "原图集编号冲突，禁止自动套用"
            : "图集未同时提供当前模型所需的标准组合和基本组合";

    [JsonIgnore]
    public string ReviewDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CatalogAnomaly))
            {
                return CatalogAnomaly;
            }

            return ReviewStatus == "consistency_checked"
                ? "已关联原图集页码和表格行号，可在项目成果中追溯。"
                : ReviewIssues.Count > 0
                    ? string.Join("；", ReviewIssues)
                    : "已保留原图集页码、表格行号和审查状态。";
        }
    }
}

public sealed class TowerLoadReactionPair
{
    public TowerLoadReaction? Standard { get; init; }

    public TowerLoadReaction? Basic { get; init; }
}

public sealed class TowerLoadReaction
{
    public double AxialKn { get; init; }

    public double ShearKn { get; init; }

    public double MomentKnM { get; init; }
}

public sealed class TowerSingleLegReactionPair
{
    public TowerSingleLegReactionCombination? Standard { get; init; }

    public TowerSingleLegReactionCombination? Basic { get; init; }
}

public sealed class TowerSingleLegReactionCombination
{
    public TowerLegCompressionControl? CompressionControl { get; init; }

    public TowerLegTensionControl? TensionControl { get; init; }
}

public sealed class TowerLegCompressionControl
{
    public double CompressionKn { get; init; }

    public double ShearKn { get; init; }
}

public sealed class TowerLegTensionControl
{
    public double TensionKn { get; init; }

    public double ShearKn { get; init; }
}

public interface ITowerLoadCatalog
{
    TowerLoadCatalogStatus Status { get; }

    IReadOnlyList<TowerLoadCatalogRecord> Records { get; }

    IReadOnlyList<TowerLoadCatalogRecord> LegacyRecords { get; }

    TowerLoadCatalogRecord? FindById(string id);

    bool IsCurrentRecord(string id);
}

public sealed class EnterpriseTowerLoadService
{
    private readonly ITowerLoadCatalog _catalog;

    public EnterpriseTowerLoadService(ITowerLoadCatalog catalog)
    {
        _catalog = catalog;
    }

    public IReadOnlyList<TowerLoadCatalogRecord> Records => _catalog.Records;

    public IReadOnlyList<TowerLoadCatalogRecord> LegacyRecords => _catalog.LegacyRecords;

    public TowerLoadCatalogStatus Status => _catalog.Status;

    public TowerLoadCatalogRecord? FindById(string id) => _catalog.FindById(id);

    public bool IsCurrentRecord(string id) => _catalog.IsCurrentRecord(id);

    public IReadOnlyList<string> GetSourceTitles()
    {
        return _catalog.Records
            .Select(item => item.SourceTitle)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<string> GetTowerTypes(string? sourceTitle)
    {
        return _catalog.Records
            .Where(item => string.IsNullOrWhiteSpace(sourceTitle) ||
                           item.SourceTitle.Equals(sourceTitle, StringComparison.Ordinal))
            .Select(item => item.TowerType)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<double> GetTowerHeights(
        string? sourceTitle,
        string? towerType)
    {
        return ApplyCategoryFilters(sourceTitle, towerType)
            .Select(ParseHeight)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
    }

    public IReadOnlyList<double> GetWindPressures(
        string? sourceTitle,
        string? towerType)
    {
        return ApplyCategoryFilters(sourceTitle, towerType)
            .Select(ParseWindPressure)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
    }

    public IReadOnlyList<TowerLoadCatalogRecord> Filter(
        string? sourceTitle,
        string? towerType,
        string? keyword,
        double? towerHeightM = null,
        double? windPressureKpa = null)
    {
        var keywords = (keyword ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return ApplyCategoryFilters(sourceTitle, towerType)
            .Where(item => !towerHeightM.HasValue ||
                           NearlyEqual(ParseHeight(item), towerHeightM.Value))
            .Where(item => !windPressureKpa.HasValue ||
                           NearlyEqual(ParseWindPressure(item), windPressureKpa.Value))
            .Where(item => keywords.Length == 0 ||
                           keywords.All(keywordPart =>
                               BuildSearchText(item).Contains(
                                   keywordPart,
                                   StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.CanApplyAnyStandardLoad)
            .ThenBy(item => ParseHeight(item), NullableDoubleComparer.Instance)
            .ThenBy(item => item.TowerCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourcePdfPage)
            .ThenBy(item => item.SourceTableRow)
            .ToArray();
    }

    private IEnumerable<TowerLoadCatalogRecord> ApplyCategoryFilters(
        string? sourceTitle,
        string? towerType) =>
        _catalog.Records
            .Where(item => string.IsNullOrWhiteSpace(sourceTitle) ||
                           item.SourceTitle.Equals(sourceTitle, StringComparison.Ordinal))
            .Where(item => string.IsNullOrWhiteSpace(towerType) ||
                           item.TowerType.Equals(towerType, StringComparison.Ordinal));

    private static bool NearlyEqual(double? actual, double expected) =>
        actual.HasValue && Math.Abs(actual.Value - expected) < 1e-9;

    private static string BuildSearchText(TowerLoadCatalogRecord item) =>
        string.Join(
            ' ',
            item.TowerCode,
            item.TowerType,
            item.Group,
            item.SourceTitle,
            item.SourceDeclaredHeightM?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
            ParseWindPressure(item)?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty);

    public TowerLoadCatalogRecord ApplyDesignLoads(
        ProjectModel project,
        string recordId)
    {
        if (project.ProjectType != ProjectType.CommunicationTower)
        {
            throw new InvalidOperationException("企业塔型荷载只能用于通信塔桅项目。");
        }

        var record = _catalog.FindById(recordId) ??
                     throw new InvalidOperationException("没有找到所选企业塔型记录。");
        if (!_catalog.IsCurrentRecord(recordId) ||
            !TowerLoadCatalogAuthorityPolicy.IsCurrentStandard(record.StandardNo))
        {
            throw new InvalidOperationException(
                "该项目保存的是历史来源反力，不能直接重新采用。请从当前企业塔型库重新选择，或手工录入已经确认的厂家反力。");
        }
        var tower = project.TowerMast;
        var structureType = InferStructureType(record.TowerType);
        var foundationType = project.FoundationSettings.FoundationType;
        var foundationUnitCount = PileLayoutRules.GetFoundationUnitCount(
            structureType,
            foundationType);
        var useSingleLeg = PileLayoutRules.RequiresSingleLegReactions(
            structureType,
            foundationType);
        if (useSingleLeg &&
            (!record.CanApplySingleLegDesignLoads ||
             record.SingleLegReaction?.Standard?.CompressionControl is not { } compression ||
             record.SingleLegReaction.Standard.TensionControl is not { } tension ||
             record.SingleLegReaction.Basic?.CompressionControl is not { } ||
             record.SingleLegReaction.Basic.TensionControl is not { }))
        {
            throw new InvalidOperationException(
                $"当前塔型采用{foundationUnitCount}个相互独立的基础单元，必须同时使用图集一个塔脚的标准组合和基本组合；该记录数据不完整，不能由整塔反力猜分配。" +
                record.ReviewDisplay);
        }

        if (!useSingleLeg &&
            (!record.CanApplyOverallDesignLoads ||
             record.OverallBaseReaction?.Standard is not { } ||
             record.OverallBaseReaction.Basic is not { }))
        {
            throw new InvalidOperationException(
                record.AvailabilityDisplay + "。当前基础形式需要整塔基础端标准组合和基本组合。" +
                record.ReviewDisplay);
        }

        tower.LoadSourceType = TowerLoadSourceType.EnterpriseCatalog;
        tower.CatalogRecordId = record.Id;
        tower.CatalogSourceTitle = record.SourceTitle;
        tower.CatalogStandardNo = record.StandardNo;
        tower.CatalogVersion = record.CatalogVersion;
        tower.CatalogPdfPage = record.SourcePdfPage;
        tower.CatalogTableRow = record.SourceTableRow;
        tower.CatalogReviewStatus = record.ReviewStatus;
        tower.TowerModel = record.TowerCode;
        tower.StructureType = structureType;
        tower.FoundationLegCount = 0;
        tower.HeightM = ParseHeight(record) ?? tower.HeightM;
        tower.UsesIndividualPileReactions = useSingleLeg;
        if (useSingleLeg)
        {
            var standardCompression = record.SingleLegReaction!.Standard!.CompressionControl!;
            var standardTension = record.SingleLegReaction.Standard.TensionControl!;
            var basicCompression = record.SingleLegReaction.Basic!.CompressionControl!;
            var basicTension = record.SingleLegReaction.Basic.TensionControl!;
            tower.LoadCaseName =
                $"{record.SourceTitle} {record.CatalogVersion}（{record.StandardNo}）第{record.SourcePdfPage}页第{record.SourceTableRow}行·单塔腿标准组合包络";
            tower.BasicLoadCaseName =
                $"{record.SourceTitle} {record.CatalogVersion}（{record.StandardNo}）第{record.SourcePdfPage}页第{record.SourceTableRow}行·单塔腿基本组合包络";
            tower.IndividualPileCompressionKn = Math.Abs(standardCompression.CompressionKn);
            tower.IndividualPileUpliftKn = Math.Abs(standardTension.TensionKn);
            tower.IndividualPileHorizontalKn = Math.Max(
                Math.Abs(standardCompression.ShearKn),
                Math.Abs(standardTension.ShearKn));
            tower.BasicIndividualPileCompressionKn =
                Math.Abs(basicCompression.CompressionKn);
            tower.BasicIndividualPileUpliftKn =
                Math.Abs(basicTension.TensionKn);
            tower.BasicIndividualPileHorizontalKn = Math.Max(
                Math.Abs(basicCompression.ShearKn),
                Math.Abs(basicTension.ShearKn));
            tower.VerticalKn = tower.IndividualPileCompressionKn;
            tower.ShearXKn = tower.IndividualPileHorizontalKn;
            tower.ShearYKn = 0;
            tower.MomentXKnM = 0;
            tower.MomentYKnM = 0;
            tower.TorsionKnM = 0;
            tower.BasicVerticalKn = tower.BasicIndividualPileCompressionKn;
            tower.BasicShearXKn = tower.BasicIndividualPileHorizontalKn;
            tower.BasicShearYKn = 0;
            tower.BasicMomentXKnM = 0;
            tower.BasicMomentYKnM = 0;
            tower.BasicTorsionKnM = 0;
        }
        else
        {
            var standard = record.OverallBaseReaction!.Standard!;
            var basic = record.OverallBaseReaction.Basic!;
            tower.LoadCaseName =
                $"{record.SourceTitle} {record.CatalogVersion}（{record.StandardNo}）第{record.SourcePdfPage}页第{record.SourceTableRow}行·整塔基础端标准组合";
            tower.BasicLoadCaseName =
                $"{record.SourceTitle} {record.CatalogVersion}（{record.StandardNo}）第{record.SourcePdfPage}页第{record.SourceTableRow}行·整塔基础端基本组合";
            tower.IndividualPileCompressionKn = 0;
            tower.IndividualPileUpliftKn = 0;
            tower.IndividualPileHorizontalKn = 0;
            tower.BasicIndividualPileCompressionKn = 0;
            tower.BasicIndividualPileUpliftKn = 0;
            tower.BasicIndividualPileHorizontalKn = 0;
            tower.VerticalKn = Math.Abs(standard.AxialKn);
            tower.ShearXKn = Math.Abs(standard.ShearKn);
            tower.ShearYKn = 0;
            tower.MomentXKnM = 0;
            tower.MomentYKnM = Math.Abs(standard.MomentKnM);
            tower.TorsionKnM = 0;
            tower.BasicVerticalKn = Math.Abs(basic.AxialKn);
            tower.BasicShearXKn = Math.Abs(basic.ShearKn);
            tower.BasicShearYKn = 0;
            tower.BasicMomentXKnM = 0;
            tower.BasicMomentYKnM = Math.Abs(basic.MomentKnM);
            tower.BasicTorsionKnM = 0;
        }
        tower.IsConfirmed = false;

        PileLayoutRules.Synchronize(project);

        project.FoundationLoad = new FoundationLoad();
        project.Schemes.Clear();
        project.SelectedSchemeId = null;
        project.Stage = project.Geotechnical.IsConfirmed
            ? ProjectStage.GeotechnicalReady
            : ProjectStage.SiteReady;
        project.ModifiedAt = DateTimeOffset.Now;
        project.AuditTrail.Add(new AuditRecord
        {
            Action = "从企业标准塔型库回填荷载",
            Details = useSingleLeg
                ? $"{record.TowerCode}；{PileLayoutRules.DescribeFoundationLayout(structureType, foundationType)}已同步回填一个塔脚的标准组合和基本组合，待用户确认。"
                : $"{record.TowerCode}；{record.SourceTitle}第{record.SourcePdfPage}页第{record.SourceTableRow}行；已同步回填整塔标准组合和基本组合，待用户确认。"
        });
        return record;
    }

    public TowerLoadCatalogRecord ApplyOverallStandardLoad(
        ProjectModel project,
        string recordId) =>
        ApplyDesignLoads(project, recordId);

    public static double? ParseHeight(TowerLoadCatalogRecord record)
    {
        if (record.SourceDeclaredHeightM is > 0)
        {
            return record.SourceDeclaredHeightM;
        }

        foreach (var segment in record.TowerCode.Split('-', StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(
                    segment,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value) &&
                value is >= 5 and <= 150)
            {
                return value;
            }
        }

        return null;
    }

    public static double? ParseWindPressure(TowerLoadCatalogRecord record)
    {
        foreach (var segment in record.TowerCode.Split('-', StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(
                    segment,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var value) &&
                value is >= 0.20 and <= 1.50)
            {
                return value;
            }
        }

        return null;
    }

    public static TowerStructureType InferStructureType(string towerType)
    {
        if (towerType.Contains("增高架", StringComparison.Ordinal))
        {
            return TowerStructureType.HeighteningFrame;
        }

        if (towerType.Contains("支撑杆", StringComparison.Ordinal))
        {
            return TowerStructureType.HeighteningFrame;
        }

        if (towerType.Contains("三管", StringComparison.Ordinal))
        {
            return TowerStructureType.ThreeTube;
        }

        if (towerType.Contains("角钢", StringComparison.Ordinal))
        {
            return TowerStructureType.AngleSteel;
        }

        if (towerType.Contains("拉线", StringComparison.Ordinal))
        {
            return TowerStructureType.GuyedMast;
        }

        if (towerType.Contains("单管", StringComparison.Ordinal) ||
            towerType.Contains("杆", StringComparison.Ordinal) ||
            towerType.Contains("景观", StringComparison.Ordinal) ||
            towerType.Contains("仿生", StringComparison.Ordinal))
        {
            return TowerStructureType.SingleTube;
        }

        return TowerStructureType.Other;
    }

    private sealed class NullableDoubleComparer : IComparer<double?>
    {
        public static NullableDoubleComparer Instance { get; } = new();

        public int Compare(double? x, double? y)
        {
            if (x is null)
            {
                return y is null ? 0 : 1;
            }

            return y is null ? -1 : x.Value.CompareTo(y.Value);
        }
    }
}
