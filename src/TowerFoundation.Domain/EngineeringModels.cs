using System.Collections.ObjectModel;

namespace TowerFoundation.Domain;

public enum ProjectType
{
    NotSelected,
    MonitoringPole,
    CommunicationTower
}

public enum ProjectStage
{
    Created,
    SiteReady,
    GeotechnicalReady,
    LoadReady,
    CandidateReady,
    SchemeSelected,
    Verified,
    OutputReady
}

public enum OptimizationPreference
{
    Economy,
    Constructability,
    Robustness
}

public enum TowerStructureType
{
    SingleTube,
    ThreeTube,
    HeighteningFrame,
    AngleSteel,
    GuyedMast,
    Other
}

public enum TowerLoadSourceType
{
    Manual,
    EnterpriseCatalog
}

public enum FoundationType
{
    RectangularShortColumn,
    CircularShortColumn,
    Raft,
    RigidShortPile,
    RigidRectangularShortPile,
    Pile
}

public enum CheckStatus
{
    Pass,
    Fail,
    Warning,
    NotEvaluated,
    Result,
    PendingInput,
    SpecialReview,
    Advisory
}

public enum AnchorConnectionType
{
    NotDetermined,
    AnchorBoltCage,
    DirectEmbedded,
    Other
}

public enum EngineeringRiskState
{
    NotAssessed,
    NotPresent,
    PresentTreatmentUnconfirmed,
    PresentTreatmentConfirmed
}

public enum ParameterSourceType
{
    Manual,
    ExcelImport,
    PdfText,
    LocalOcr,
    WordDocument,
    DeepSeek,
    VisualAi,
    EnterpriseCatalog,
    BuiltInDatabase
}

public enum BasicWindPressureSourceType
{
    Manual,
    DirectNormativeStation,
    ParentCityReference,
    NearestStationManualReference
}

public enum TubeSectionType
{
    CircularTube,
    RegularOctagonDiagonalTube
}

public enum LoadCombinationKind
{
    Standard,
    Basic,
    QuasiPermanent,
    Seismic,
    Accidental
}

public enum PileSettlementMethod
{
    NotSelected,
    StaticLoadTestCurve,
    ReviewedSpecialCalculation,
    MindlinReviewEstimate
}

public sealed class ProjectModel
{
    public int SchemaVersion { get; set; } = 6;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "新建监控杆基础项目";

    public ProjectType ProjectType { get; set; } = ProjectType.NotSelected;

    public ProjectStage Stage { get; set; } = ProjectStage.Created;

    public string Province { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string County { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;

    public string RulePackVersion { get; set; } = "GB55001-2021+GB55003-2021+GB50068-2018+GB50007-2011+JGJ94-2008+GBT50010-2024+YDT5131-2019+GB50009-2012+GB50135-2019-MultiUnit-2026.08.10";

    public GeotechnicalInput Geotechnical { get; set; } = new();

    public MonitoringPoleInput MonitoringPole { get; set; } = new();

    public List<MonitoringDrawingCandidate> MonitoringDrawingCandidates { get; set; } = [];

    public TowerMastInput TowerMast { get; set; } = new();

    public FoundationDesignSettings FoundationSettings { get; set; } = new();

    public FoundationLoad FoundationLoad { get; set; } = new();

    public List<FoundationScheme> Schemes { get; set; } = [];

    public Guid? SelectedSchemeId { get; set; }

    public ObservableCollection<AuditRecord> AuditTrail { get; set; } = [];
}

public sealed class GeotechnicalInput
{
    public double BearingCapacityKpa { get; set; } = 150;

    public bool UseBearingCapacityCorrection { get; set; }

    public double CharacteristicBearingCapacityKpa { get; set; } = 150;

    public double BearingCapacityWidthCorrectionFactor { get; set; }

    public double BearingCapacityDepthCorrectionFactor { get; set; } = 1.0;

    public double SoilBelowBaseUnitWeightKnPerM3 { get; set; } = 18;

    public double SoilAboveBaseAverageUnitWeightKnPerM3 { get; set; } = 18;

    public double SoilUnitWeightKnPerM3 { get; set; } = 18;

    public double BaseFrictionCoefficient { get; set; } = 0.30;

    public double InternalFrictionAngleDegree { get; set; } = 5;

    public double GroundwaterDepthM { get; set; } = 5;

    public double CompressionModulusMpa { get; set; }

    public int SeismicIntensityDegree { get; set; }

    public double DesignBasicGroundAccelerationG { get; set; }

    public string DesignEarthquakeGroup { get; set; } = string.Empty;

    public string SiteClass { get; set; } = string.Empty;

    public double CharacteristicPeriodS { get; set; }

    public string SeismicParameterSource { get; set; } = string.Empty;

    public string SpecialSoilRisks { get; set; } = string.Empty;

    public string Evidence { get; set; } = string.Empty;

    public double AiConfidence { get; set; }

    public string SoilDescription { get; set; } = "用户手工确认的地基参数";

    public bool IsConfirmed { get; set; }

    public ParameterSourceType SourceType { get; set; } = ParameterSourceType.Manual;
}

public sealed class SpecialtyDesignInput
{
    public DeformationLimitInput Deformation { get; set; } = new();

    public SettlementDesignInput Settlement { get; set; } = new();

    public CrackDesignInput Crack { get; set; } = new();

    public AnchorBoltDesignInput AnchorBolts { get; set; } = new();

    public PedestalStructuralDesignInput PedestalStructure { get; set; } = new();

    public HydrogeologyDesignInput Hydrogeology { get; set; } = new();

    public SpecialGroundDesignInput SpecialGround { get; set; } = new();
}

public sealed class EngineeringParameterSource
{
    public ParameterSourceType SourceType { get; set; } = ParameterSourceType.Manual;

    public string SourceDocument { get; set; } = string.Empty;

    public string SourceLocation { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public bool IsConfirmed { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Display => string.Join("；", new[]
    {
        SourceDocument,
        SourceLocation,
        Note
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class DeformationLimitInput
{
    public double AllowableTopDisplacementMm { get; set; }

    public double AllowableTopRotationRad { get; set; }

    public EngineeringParameterSource Source { get; set; } = new()
    {
        Note = "应采用塔型技术条件、厂家连接要求或项目专项要求，软件不设置静默默认值。"
    };
}

public sealed class SettlementDesignInput
{
    public double AllowableSettlementMm { get; set; }

    public double ExperienceCoefficient { get; set; } = 1.0;

    public List<SettlementSoilLayerInput> SoilLayers { get; set; } =
    [
        new() { Name = "基底以下第1层" },
        new() { Name = "基底以下第2层" },
        new() { Name = "基底以下第3层" },
        new() { Name = "基底以下第4层" }
    ];

    public EngineeringParameterSource Source { get; set; } = new()
    {
        Note = "分层厚度与压缩模量应来自地勘报告；允许沉降值应按结构类型和使用要求确认。"
    };
}

public sealed class SettlementSoilLayerInput
{
    public string Name { get; set; } = string.Empty;

    public double ThicknessM { get; set; }

    public double CompressionModulusMpa { get; set; }
}

public sealed class CrackDesignInput
{
    public double MaximumCrackWidthMm { get; set; } = 0.20;

    public string EnvironmentCategory { get; set; } = "待确认";

    public double ConcreteTensileStrengthStandardMpa { get; set; } = 2.01;

    public double ReinforcementElasticModulusMpa { get; set; } = 200_000;

    public EngineeringParameterSource Source { get; set; } = new()
    {
        SourceType = ParameterSourceType.BuiltInDatabase,
        SourceDocument = "GB/T 50010-2010（2024年版）",
        SourceLocation = "表3.4.5、第7.1.2条",
        Note = "0.20 mm为保守预填值；必须结合环境类别确认。"
    };
}

public sealed class AnchorBoltDesignInput
{
    public AnchorConnectionType ConnectionType { get; set; } =
        AnchorConnectionType.NotDetermined;

    public string TemplateName { get; set; } = string.Empty;

    public string MaterialGrade { get; set; } = string.Empty;

    public int BoltCount { get; set; }

    public double NominalDiameterMm { get; set; }

    public double BoltCircleDiameterM { get; set; }

    public double TensileStrengthDesignMpa { get; set; }

    public double ShearStrengthDesignMpa { get; set; }

    public double ThreadStressAreaFactor { get; set; } = 0.78;

    public double EmbedmentDepthM { get; set; }

    public double AnchorPlateOuterDiameterMm { get; set; }

    public double AnchorPlateThicknessMm { get; set; }

    public double AnchorPlateSteelYieldStrengthMpa { get; set; } = 215;

    public double ConcreteCompressiveStrengthMpa { get; set; } = 14.3;

    public double ConcreteBreakoutCapacityKn { get; set; }

    public double PulloutCapacityKn { get; set; }

    public double EdgeBreakoutCapacityKn { get; set; }

    public bool UseProgramCalculatedConcreteCapacity { get; set; }

    public double ConcreteMemberThicknessMm { get; set; }

    public double MinimumAnchorEdgeDistanceMm { get; set; }

    public double MinimumAnchorSpacingMm { get; set; }

    public double EffectiveEmbedmentDepthMm { get; set; }

    public double ConcreteTensileStrengthMpa { get; set; } = 1.43;

    public double ConcreteBreakoutCoefficient { get; set; }

    public double PulloutBearingCoefficient { get; set; }

    public double EdgeBreakoutCoefficient { get; set; }

    public EngineeringParameterSource ProgramConcreteModelSource { get; set; } = new()
    {
        Note = "程序内群锚混凝土破坏模型仅在项目明确给出权威计算方法、全部几何参数及系数时启用；系数为0时保持专项复核。"
    };

    public EngineeringParameterSource ConcreteCapacitySource { get; set; } = new()
    {
        Note = "混凝土锥体、拔出和边缘破坏承载力必须来自完整节点计算或经审查的厂家资料，软件不猜测。"
    };

    public EngineeringParameterSource Source { get; set; } = new()
    {
        Note = "应从塔脚板、地脚锚栓详图或厂家连接资料录入；不得由AI猜测规格。"
    };
}

public sealed class PedestalStructuralDesignInput
{
    public double ConcreteCompressiveStrengthMpa { get; set; } = 14.3;

    public double LongitudinalBarDiameterMm { get; set; } = 20;

    public int LongitudinalBarCount { get; set; } = 16;

    public double MinimumLongitudinalReinforcementRatio { get; set; } = 0.005;

    public double StirrupDiameterMm { get; set; } = 8;

    public double StirrupSpacingMm { get; set; } = 150;

    public int StirrupLegCount { get; set; } = 2;

    public EngineeringParameterSource Source { get; set; } = new()
    {
        SourceType = ParameterSourceType.BuiltInDatabase,
        SourceDocument = "GB/T 50010-2010（2024年版）",
        SourceLocation = "短柱截面与配筋候选",
        Note = "材料强度和配筋为可编辑候选；采用前须结合混凝土等级、环境及塔脚节点确认。"
    };
}

public sealed class HydrogeologyDesignInput
{
    public double DesignHighGroundwaterDepthM { get; set; } = -1;

    public double AntiFlotationSafetyFactor { get; set; } = 1.05;

    public EngineeringParameterSource Source { get; set; } = new()
    {
        SourceType = ParameterSourceType.BuiltInDatabase,
        SourceDocument = "GB 50007-2011",
        SourceLocation = "第5.4.3条",
        Note = "抗浮稳定安全系数1.05为规范一般情况取值；设计最高水位仍须由地勘或水文资料确认。"
    };
}

public sealed class SpecialGroundDesignInput
{
    public EngineeringRiskState CollapsibleLoess { get; set; } =
        EngineeringRiskState.NotAssessed;

    public EngineeringRiskState Liquefaction { get; set; } =
        EngineeringRiskState.NotAssessed;

    public EngineeringRiskState FrostHeave { get; set; } =
        EngineeringRiskState.NotAssessed;

    public double DesignFrostDepthM { get; set; }

    public string TreatmentDescription { get; set; } = string.Empty;

    public EngineeringParameterSource Source { get; set; } = new()
    {
        Note = "风险结论和处理措施应来自地勘结论、专项设计或审查意见；未评估不得按无风险处理。"
    };
}

public sealed class MonitoringPoleInput
{
    public const double MinimumBasicWindPressureKpa = 0.35;

    public double BasicWindPressureKpa { get; set; } = 0.55;

    public double SourceBasicWindPressureKpa { get; set; } = 0.55;

    public bool IsMinimumBasicWindPressureApplied { get; set; }

    public BasicWindPressureSourceType BasicWindPressureSourceType { get; set; } =
        BasicWindPressureSourceType.Manual;

    public string BasicWindPressureSourceStation { get; set; } = string.Empty;

    public string BasicWindPressureSourceNote { get; set; } =
        "默认值，必须由设计人员结合项目所在地确认。";

    public double WindVibrationFactor { get; set; } = 1.00;

    public double ShapeCoefficient { get; set; } = 1.20;

    public double TerrainHeightFactor { get; set; } = 1.00;

    public TubeSectionType PoleSectionType { get; set; } = TubeSectionType.CircularTube;

    public double PoleHeightM { get; set; } = 8;

    public double PoleBottomDiameterM { get; set; } = 0.24;

    public double PoleTopDiameterM { get; set; } = 0.12;

    public double PoleWallThicknessM { get; set; } = 0.006;

    public double ArmMountingHeightM { get; set; } = 7;

    public double ArmLengthM { get; set; } = 4;

    public double ArmNearDiameterM { get; set; } = 0.14;

    public double ArmFarDiameterM { get; set; } = 0.08;

    public double ArmWallThicknessM { get; set; } = 0.005;

    public TubeSectionType ArmSectionType { get; set; } = TubeSectionType.CircularTube;

    public List<MonitoringPoleArmSegment> ArmSegments { get; set; } = [];

    public int ArmCount { get; set; } = 1;

    public double AttachmentProjectedAreaM2 { get; set; } = 0.35;

    public double AttachmentWeightKn { get; set; } = 0.25;

    public double SteelUnitWeightKnPerM3 { get; set; } = 78.5;

    /// <summary>
    /// 新建监控杆项目启用后，图纸参数只有经AI候选采用或人工录入才参与正式计算。
    /// 旧项目保持false，以兼容历史项目中已经保存的数值。
    /// </summary>
    public bool RequireExplicitDrawingInputs { get; set; }

    /// <summary>
    /// 已由AI候选或人工明确提供的图纸参数字段名。
    /// 字段名使用 MonitoringDrawingFieldNames 中的稳定标识，随项目记录保存。
    /// </summary>
    public HashSet<string> ExplicitDrawingInputFields { get; set; } = [];
}

public static class MonitoringDrawingFieldKeys
{
    public const string TitleHeight = "title_height";
    public const string TitleArmLength = "title_arm_length";
    public const string PoleHeight = "pole_height";
    public const string PoleBottomDimension = "pole_bottom_dimension";
    public const string PoleTopDimension = "pole_top_dimension";
    public const string PoleWallThickness = "pole_wall_thickness";
    public const string ArmMountingHeight = "arm_mounting_height";
    public const string ArmLength = "arm_length";
    public const string ArmNearDimension = "arm_near_dimension";
    public const string ArmFarDimension = "arm_far_dimension";
    public const string ArmWallThickness = "arm_wall_thickness";
    public const string ArmCount = "arm_count";
    public const string AttachmentProjectedArea = "attachment_projected_area";
    public const string AttachmentWeight = "attachment_weight";
    public const string ArmSegments = "arm_segments";
}

public sealed class MonitoringPoleArmSegment
{
    public double LengthM { get; set; }

    public double NearDimensionM { get; set; }

    public double FarDimensionM { get; set; }

    public double WallThicknessM { get; set; }
}

public sealed class MonitoringDrawingFieldCandidate
{
    public string FieldName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public double? Value { get; set; }

    public string Unit { get; set; } = string.Empty;

    public string RawAnnotation { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public int PageNumber { get; set; } = 1;

    public double Confidence { get; set; }

    public bool HasConflict { get; set; }

    public string Warning { get; set; } = string.Empty;

    public bool IsSelected { get; set; }

    public bool IsManuallyConfirmed { get; set; }

    public string ConflictDisplay => HasConflict ? "有冲突" : "无";

    public bool IsMissing => !Value.HasValue;

    public bool IsHighConfidence =>
        Value.HasValue && Confidence >= 0.85 && !HasConflict && string.IsNullOrWhiteSpace(Warning);

    public string ConfidenceDisplay => Value.HasValue
        ? $"{Confidence:P0}"
        : "图纸未给";

    public string ValueDisplay
    {
        get
        {
            if (!Value.HasValue)
            {
                return "图纸未给，待人工补录";
            }

            return FieldName switch
            {
                "pole_bottom_dimension" or "pole_top_dimension" or
                "pole_wall_thickness" or "arm_near_dimension" or
                "arm_far_dimension" or "arm_wall_thickness" =>
                    $"{Value.Value * 1000:G6} mm",
                "arm_count" => $"{Value.Value:G6} 个",
                "arm_segments" => $"{Value.Value:G6} 段",
                _ => $"{Value.Value:G6} {Unit}".TrimEnd()
            };
        }
    }
}

public sealed class MonitoringDrawingCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SourceFileName { get; set; } = string.Empty;

    public string SourceFileSha256 { get; set; } = string.Empty;

    public int PageNumber { get; set; } = 1;

    public string DrawingModel { get; set; } = string.Empty;

    public string VisionModel { get; set; } = string.Empty;

    public DateTimeOffset RecognizedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? AppliedAt { get; set; }

    public List<MonitoringDrawingFieldCandidate> Fields { get; set; } = [];

    public List<MonitoringPoleArmSegment> ArmSegments { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public string EvidenceSummary { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(DrawingModel)
        ? $"{SourceFileName} 第{PageNumber}页"
        : $"{DrawingModel} · {SourceFileName}";

    public string WarningSummary => Warnings.Count == 0
        ? "本机合理性校验通过"
        : string.Join("；", Warnings);
}

public sealed class TowerMastInput
{
    public string TowerModel { get; set; } = string.Empty;

    public TowerStructureType StructureType { get; set; } = TowerStructureType.SingleTube;

    /// <summary>
    /// 实际塔柱/塔脚数量。0表示按结构类型自动判断；增高架等存在不同拓扑时可明确填写1、3或4。
    /// </summary>
    public int FoundationLegCount { get; set; }

    public TowerLoadSourceType LoadSourceType { get; set; } = TowerLoadSourceType.Manual;

    public string CatalogRecordId { get; set; } = string.Empty;

    public string CatalogSourceTitle { get; set; } = string.Empty;

    public string CatalogStandardNo { get; set; } = string.Empty;

    public string CatalogVersion { get; set; } = string.Empty;

    public int CatalogPdfPage { get; set; }

    public int CatalogTableRow { get; set; }

    public string CatalogReviewStatus { get; set; } = string.Empty;

    public double HeightM { get; set; } = 30;

    public string LoadCaseName { get; set; } = "厂家提供的基础端标准组合控制荷载";

    public string BasicLoadCaseName { get; set; } = string.Empty;

    public double VerticalKn { get; set; }

    public double ShearXKn { get; set; }

    public double ShearYKn { get; set; }

    public double MomentXKnM { get; set; }

    public double MomentYKnM { get; set; }

    public double TorsionKnM { get; set; }

    public double BasicVerticalKn { get; set; }

    public double BasicShearXKn { get; set; }

    public double BasicShearYKn { get; set; }

    public double BasicMomentXKnM { get; set; }

    public double BasicMomentYKnM { get; set; }

    public double BasicTorsionKnM { get; set; }

    public bool UsesIndividualPileReactions { get; set; }

    public double IndividualPileCompressionKn { get; set; }

    public double IndividualPileUpliftKn { get; set; }

    public double IndividualPileHorizontalKn { get; set; }

    public double BasicIndividualPileCompressionKn { get; set; }

    public double BasicIndividualPileUpliftKn { get; set; }

    public double BasicIndividualPileHorizontalKn { get; set; }

    public bool IsConfirmed { get; set; }
}

public sealed class FoundationDesignSettings
{
    public FoundationType FoundationType { get; set; } = FoundationType.RectangularShortColumn;

    public double PedestalLengthM { get; set; } = 0.80;

    public double PedestalWidthM { get; set; } = 0.80;

    public double PedestalDiameterM { get; set; } = 0.80;

    public double PedestalHeightM { get; set; } = 1.20;

    public double MinimumBaseLengthM { get; set; } = 1.60;

    public double MaximumBaseLengthM { get; set; } = 6.00;

    public double MinimumBaseWidthM { get; set; } = 1.60;

    public double MaximumBaseWidthM { get; set; } = 6.00;

    public double MinimumBaseThicknessM { get; set; } = 0.60;

    public double MaximumBaseThicknessM { get; set; } = 2.00;

    public double DimensionStepM { get; set; } = 0.20;

    public double RequiredSlidingSafetyFactor { get; set; } = 1.50;

    public double ConcreteUnitWeightKnPerM3 { get; set; } = 25;

    public double WaterUnitWeightKnPerM3 { get; set; } = 10;

    public double ExcavationWorkingSpaceM { get; set; } = 0.30;

    public double StructuralDesignLoadFactor { get; set; } = 1.50;

    public double FoundationPermanentLoadFactor { get; set; } = 1.30;

    public double StructureImportanceFactor { get; set; } = 1.00;

    public double ConcreteTensileStrengthMpa { get; set; } = 1.43;

    public double ReinforcementYieldStrengthMpa { get; set; } = 360;

    public double ConcreteCoverMm { get; set; } = 50;

    public double BottomBarDiameterMm { get; set; } = 16;

    public double BottomBarSpacingMm { get; set; } = 120;

    public double MinimumReinforcementRatio { get; set; } = 0.0015;

    public PileFoundationSettings Pile { get; set; } = new();

    public RigidShortPileSettings RigidShortPile { get; set; } = new();

    public SpecialtyDesignInput SpecialtyDesign { get; set; } = new();

    public LoadCombinationDesignInput LoadCombinations { get; set; } = new();

    public DrawingOutputSettings Drawing { get; set; } = new();
}

public sealed class LoadCombinationDesignInput
{
    public bool UseDecomposedActions { get; set; }

    public LoadCombinationKind ActiveStructuralCombination { get; set; } =
        LoadCombinationKind.Basic;

    public FoundationLoadCombination PermanentAction { get; set; } = new()
    {
        Kind = LoadCombinationKind.Standard,
        GoverningCase = "永久作用标准值"
    };

    public FoundationLoadCombination LeadingVariableAction { get; set; } = new()
    {
        Kind = LoadCombinationKind.Standard,
        GoverningCase = "主导可变作用标准值"
    };

    public FoundationLoadCombination SeismicAction { get; set; } = new()
    {
        Kind = LoadCombinationKind.Seismic,
        GoverningCase = "地震作用标准值"
    };

    public FoundationLoadCombination AccidentalAction { get; set; } = new()
    {
        Kind = LoadCombinationKind.Accidental,
        GoverningCase = "偶然作用代表值"
    };

    public double PermanentFactor { get; set; } = 1.30;

    public double VariableFactor { get; set; } = 1.50;

    public double QuasiPermanentVariableFactor { get; set; }

    public double SeismicPermanentFactor { get; set; } = 1.00;

    public double SeismicActionFactor { get; set; } = 1.00;

    public double SeismicVariableCombinationFactor { get; set; }

    public double AccidentalPermanentFactor { get; set; } = 1.00;

    public double AccidentalActionFactor { get; set; } = 1.00;

    public double AccidentalVariableCombinationFactor { get; set; }

    public EngineeringParameterSource Source { get; set; } = new()
    {
        SourceType = ParameterSourceType.BuiltInDatabase,
        SourceDocument = "GB 50068-2018",
        SourceLocation = "第8.2、8.3节",
        Note = "组合类别和表达式由软件生成；分项系数、组合值系数及各作用效应必须由项目资料或设计人员确认。"
    };
}

public sealed class DrawingOutputSettings
{
    public string CompanyName { get; set; } = "项目设计单位（待填写）";

    public string DrawingTitle { get; set; } = "塔桅基础配筋图";

    public string DrawingNumber { get; set; } = "塔基-01";

    public string Designer { get; set; } = string.Empty;

    public string Checker { get; set; } = string.Empty;

    public string Approver { get; set; } = string.Empty;

    public string DrawingScale { get; set; } = "1:50";

    public string PaperSize { get; set; } = "A3";

    public bool GenerateDwgConversionScript { get; set; } = true;
}

public sealed class RigidShortPileSettings
{
    public double MinimumDiameterM { get; set; } = 1.40;

    public double MaximumDiameterM { get; set; } = 2.40;

    public double DiameterStepM { get; set; } = 0.20;

    public double MinimumRectangularLengthM { get; set; } = 1.40;

    public double MaximumRectangularLengthM { get; set; } = 2.40;

    public double RectangularLengthStepM { get; set; } = 0.20;

    public double MinimumRectangularWidthM { get; set; } = 1.40;

    public double MaximumRectangularWidthM { get; set; } = 2.40;

    public double RectangularWidthStepM { get; set; } = 0.20;

    public double MinimumEmbeddedDepthM { get; set; } = 5.0;

    public double MaximumEmbeddedDepthM { get; set; } = 10.0;

    public double EmbeddedDepthStepM { get; set; } = 1.0;

    public double AboveGroundHeightM { get; set; } = 0.30;

    public double LateralResistanceWidthCoefficient { get; set; } = 0.65;

    public double VerticalReactionEccentricityCoefficient { get; set; } = 0.33;

    public double ConcreteElasticModulusMpa { get; set; } = 30_000;

    public double ConcreteCompressiveStrengthMpa { get; set; } = 14.3;

    public double LongitudinalBarDiameterMm { get; set; } = 22;

    public int LongitudinalBarCount { get; set; } = 36;

    public double MinimumLongitudinalReinforcementRatio { get; set; } = 0.005;

    public double StirrupDiameterMm { get; set; } = 10;

    public double StirrupSpacingMm { get; set; } = 150;

    public int StirrupLegCount { get; set; } = 2;

    public bool IsConfirmed { get; set; }

    public List<RigidShortPileSoilLayerInput> SoilLayers { get; set; } =
    [
        new() { Name = "表层土", ThicknessM = 1.0, HorizontalResistanceCoefficientMnPerM4 = 0 },
        new() { Name = "第2层", ThicknessM = 1.0, HorizontalResistanceCoefficientMnPerM4 = 12 },
        new() { Name = "主要影响层", ThicknessM = 6.0, HorizontalResistanceCoefficientMnPerM4 = 12 }
    ];
}

public sealed class RigidShortPileSoilLayerInput
{
    public string Name { get; set; } = string.Empty;

    public double ThicknessM { get; set; }

    public double HorizontalResistanceCoefficientMnPerM4 { get; set; }
}

public sealed class PileFoundationSettings
{
    public double MinimumPileDiameterM { get; set; } = 0.80;

    public double MaximumPileDiameterM { get; set; } = 1.20;

    public double PileDiameterStepM { get; set; } = 0.20;

    public double MinimumPileLengthM { get; set; } = 8.0;

    public double MaximumPileLengthM { get; set; } = 16.0;

    public double PileLengthStepM { get; set; } = 2.0;

    public double AboveGroundHeightM { get; set; } = 0.30;

    public int PileCount { get; set; } = 1;

    public bool TieBeamRequired { get; set; }

    public double PileCenterSpacingM { get; set; } = 3.0;

    public double TieBeamWidthM { get; set; } = 0.40;

    public double TieBeamHeightM { get; set; } = 0.60;

    public double CapacityReductionFactor { get; set; } = 2.0;

    public double SinglePileHorizontalCapacityKn { get; set; } = 100;

    public bool UseUserConfirmedPileHeadForces { get; set; }

    public double MaximumPileCompressionKn { get; set; } = 600;

    public double MaximumPileUpliftKn { get; set; } = 300;

    public double PileMainBarDiameterMm { get; set; } = 20;

    public int PileMainBarCount { get; set; } = 14;

    public double MinimumLongitudinalReinforcementRatio { get; set; } = 0.005;

    public double HorizontalResistanceCoefficientMnPerM4 { get; set; } = 12;

    public double ConcreteElasticModulusMpa { get; set; } = 30_000;

    public double ConcreteCompressiveStrengthMpa { get; set; } = 14.3;

    public double StirrupDiameterMm { get; set; } = 10;

    public double StirrupSpacingMm { get; set; } = 150;

    public int StirrupLegCount { get; set; } = 2;

    public bool UseUserConfirmedPileHeadStructuralForces { get; set; }

    public double MaximumPileHeadHorizontalKn { get; set; }

    public double MaximumPileHeadMomentKnM { get; set; }

    public bool UseConfirmedServiceSettlement { get; set; }

    public double ServiceSettlementFromTestOrSpecialCalculationMm { get; set; } = -1;

    public PileSettlementMethod SettlementMethod { get; set; } =
        PileSettlementMethod.NotSelected;

    public List<PileLoadTestPoint> StaticLoadTestCurve { get; set; } = [];

    public bool UseNegativeSkinFriction { get; set; }

    public List<NegativeSkinFrictionLayerInput> NegativeSkinFrictionLayers { get; set; } = [];

    public EngineeringParameterSource NegativeSkinFrictionSource { get; set; } = new()
    {
        Note = "仅在地勘明确存在欠固结土、填土固结、降水等负摩阻风险，并由设计人员确认分层厚度及单位负摩阻力后计入。"
    };

    public double MindlinEstimatePoissonRatio { get; set; } = 0.30;

    public double MindlinEstimateInfluenceFactor { get; set; } = 1.00;

    public EngineeringParameterSource SettlementSource { get; set; } = new()
    {
        Note = "静载曲线优先；专项沉降结果次之。Mindlin仅作为复核估算，未经专项确认不得形成正式通过结论。"
    };

    public bool UseUserConfirmedTieBeamForces { get; set; }

    public double TieBeamAxialTensionKn { get; set; }

    public double TieBeamMomentKnM { get; set; }

    public double TieBeamShearKn { get; set; }

    public double TieBeamMainBarDiameterMm { get; set; } = 18;

    public int TieBeamMainBarCount { get; set; } = 4;

    public double TieBeamStirrupDiameterMm { get; set; } = 8;

    public double TieBeamStirrupSpacingMm { get; set; } = 150;

    public int TieBeamStirrupLegCount { get; set; } = 2;

    public bool IsConfirmed { get; set; }

    public List<PileSoilLayerInput> SoilLayers { get; set; } =
    [
        new()
        {
            Name = "桩侧及桩端持力层",
            ThicknessM = 12,
            SideResistanceKpa = 60,
            TipResistanceKpa = 1300,
            UpliftCoefficient = 0.70
        },
        new() { Name = "第2层" },
        new() { Name = "第3层" },
        new() { Name = "第4层" },
        new() { Name = "第5层" },
        new() { Name = "第6层" }
    ];
}

public sealed class PileLoadTestPoint
{
    public double LoadKn { get; set; }

    public double SettlementMm { get; set; }
}

public sealed class NegativeSkinFrictionLayerInput
{
    public string Name { get; set; } = string.Empty;

    public double ThicknessM { get; set; }

    public double UnitNegativeSkinFrictionKpa { get; set; }
}

public sealed class PileSoilLayerInput
{
    public string Name { get; set; } = string.Empty;

    public double ThicknessM { get; set; }

    public double SideResistanceKpa { get; set; }

    public double TipResistanceKpa { get; set; }

    public double UpliftCoefficient { get; set; } = 0.70;

    public bool IsSandOrGravel { get; set; }
}

public sealed class FoundationLoad
{
    // These legacy top-level values are the serviceability/standard
    // combination. Keeping them in place preserves older project files.
    public double VerticalKn { get; set; }

    public double ShearXKn { get; set; }

    public double ShearYKn { get; set; }

    public double MomentXKnM { get; set; }

    public double MomentYKnM { get; set; }

    public double TorsionKnM { get; set; }

    public bool UsesIndividualPileReactions { get; set; }

    public double IndividualPileCompressionKn { get; set; }

    public double IndividualPileUpliftKn { get; set; }

    public double IndividualPileHorizontalKn { get; set; }

    public int FoundationUnitCount { get; set; } = 1;

    public bool TieBeamsRequired { get; set; }

    public string GoverningCase { get; set; } = "监控杆设计风荷载";

    public FoundationLoadCombination? BasicCombination { get; set; }

    public FoundationLoadCombination? QuasiPermanentCombination { get; set; }

    public FoundationLoadCombination? SeismicCombination { get; set; }

    public FoundationLoadCombination? AccidentalCombination { get; set; }

    public FoundationLoadCombination? ActiveStructuralCombination { get; set; }

    public List<FoundationLoadCombination> CombinationTrace { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasExplicitBasicCombination =>
        BasicCombination?.HasMeaningfulLoad == true;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasExplicitStructuralCombination =>
        ActiveStructuralCombination?.HasMeaningfulLoad == true ||
        HasExplicitBasicCombination;

    public FoundationLoad ResolveStructuralDesignLoad(
        FoundationDesignSettings settings)
    {
        var importance = settings.StructureImportanceFactor;
        var structuralCombination =
            ActiveStructuralCombination?.HasMeaningfulLoad == true
                ? ActiveStructuralCombination
                : BasicCombination;
        if (structuralCombination?.HasMeaningfulLoad == true)
        {
            return structuralCombination.ToFoundationLoad(
                importance,
                FoundationUnitCount,
                TieBeamsRequired);
        }

        var fallbackFactor = settings.StructuralDesignLoadFactor * importance;
        return new FoundationLoad
        {
            VerticalKn = VerticalKn * fallbackFactor,
            ShearXKn = ShearXKn * fallbackFactor,
            ShearYKn = ShearYKn * fallbackFactor,
            MomentXKnM = MomentXKnM * fallbackFactor,
            MomentYKnM = MomentYKnM * fallbackFactor,
            TorsionKnM = TorsionKnM * fallbackFactor,
            UsesIndividualPileReactions = UsesIndividualPileReactions,
            IndividualPileCompressionKn = IndividualPileCompressionKn * fallbackFactor,
            IndividualPileUpliftKn = IndividualPileUpliftKn * fallbackFactor,
            IndividualPileHorizontalKn = IndividualPileHorizontalKn * fallbackFactor,
            FoundationUnitCount = FoundationUnitCount,
            TieBeamsRequired = TieBeamsRequired,
            GoverningCase =
                $"{GoverningCase}；未提供基本组合，按标准组合×{settings.StructuralDesignLoadFactor:F2}推导"
        };
    }

    public string DescribeStructuralCombination(FoundationDesignSettings settings) =>
        HasExplicitStructuralCombination
            ? $"采用来源明确给出的{FormatCombinationKind((ActiveStructuralCombination ?? BasicCombination)!.Kind)}：{(ActiveStructuralCombination ?? BasicCombination)!.GoverningCase}；{(ActiveStructuralCombination ?? BasicCombination)!.Expression}"
            : $"未提供明确基本组合，暂按标准组合×{settings.StructuralDesignLoadFactor:F2}推导；正式成果应核对荷载分项组合";

    private static string FormatCombinationKind(LoadCombinationKind kind) => kind switch
    {
        LoadCombinationKind.Standard => "标准组合",
        LoadCombinationKind.Basic => "基本组合",
        LoadCombinationKind.QuasiPermanent => "准永久组合",
        LoadCombinationKind.Seismic => "地震设计状况组合",
        LoadCombinationKind.Accidental => "偶然设计状况组合",
        _ => kind.ToString()
    };
}

public sealed class FoundationLoadCombination
{
    public LoadCombinationKind Kind { get; set; } = LoadCombinationKind.Basic;

    public double VerticalKn { get; set; }

    public double ShearXKn { get; set; }

    public double ShearYKn { get; set; }

    public double MomentXKnM { get; set; }

    public double MomentYKnM { get; set; }

    public double TorsionKnM { get; set; }

    public bool UsesIndividualPileReactions { get; set; }

    public double IndividualPileCompressionKn { get; set; }

    public double IndividualPileUpliftKn { get; set; }

    public double IndividualPileHorizontalKn { get; set; }

    public string GoverningCase { get; set; } = "承载能力极限状态基本组合";

    public string Expression { get; set; } = string.Empty;

    public string SourceDocument { get; set; } = string.Empty;

    public bool IsConfirmed { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasMeaningfulLoad =>
        Math.Abs(VerticalKn) > 1e-9 ||
        Math.Abs(ShearXKn) > 1e-9 ||
        Math.Abs(ShearYKn) > 1e-9 ||
        Math.Abs(MomentXKnM) > 1e-9 ||
        Math.Abs(MomentYKnM) > 1e-9 ||
        Math.Abs(TorsionKnM) > 1e-9 ||
        Math.Abs(IndividualPileCompressionKn) > 1e-9 ||
        Math.Abs(IndividualPileUpliftKn) > 1e-9 ||
        Math.Abs(IndividualPileHorizontalKn) > 1e-9;

    public FoundationLoad ToFoundationLoad(
        double importanceFactor,
        int foundationUnitCount,
        bool tieBeamsRequired)
    {
        return new FoundationLoad
        {
            VerticalKn = VerticalKn * importanceFactor,
            ShearXKn = ShearXKn * importanceFactor,
            ShearYKn = ShearYKn * importanceFactor,
            MomentXKnM = MomentXKnM * importanceFactor,
            MomentYKnM = MomentYKnM * importanceFactor,
            TorsionKnM = TorsionKnM * importanceFactor,
            UsesIndividualPileReactions = UsesIndividualPileReactions,
            IndividualPileCompressionKn =
                IndividualPileCompressionKn * importanceFactor,
            IndividualPileUpliftKn =
                IndividualPileUpliftKn * importanceFactor,
            IndividualPileHorizontalKn =
                IndividualPileHorizontalKn * importanceFactor,
            FoundationUnitCount = foundationUnitCount,
            TieBeamsRequired = tieBeamsRequired,
            GoverningCase = GoverningCase
        };
    }
}

public sealed class MonitoringPoleLoadResult
{
    public FoundationLoad FoundationLoad { get; init; } = new();

    public double PoleWindForceKn { get; init; }

    public double ArmWindForceKn { get; init; }

    public double AttachmentWindForceKn { get; init; }

    public double PoleSelfWeightKn { get; init; }

    public double ArmSelfWeightKn { get; init; }

    public double PoleSteelVolumeM3 { get; init; }

    public double ArmSteelVolumeM3 { get; init; }

    public double ArmProjectedAreaM2 { get; init; }

    public double ArmGravityMomentKnM { get; init; }

    public double ArmWindTorsionKnM { get; init; }

    public double DesignWindPressureKpa { get; init; }
}

public sealed class FoundationGeometry
{
    public int FoundationUnitCount { get; set; } = 1;

    public double BaseLengthM { get; set; }

    public double BaseWidthM { get; set; }

    public double BaseThicknessM { get; set; }

    public double PedestalLengthM { get; set; }

    public double PedestalWidthM { get; set; }

    public double PedestalHeightM { get; set; }

    public double PileDiameterM { get; set; }

    public double PileLengthM { get; set; }

    public int PileCount { get; set; } = 1;

    public double PileCenterSpacingM { get; set; }

    public int TieBeamCount { get; set; }

    public double TieBeamWidthM { get; set; }

    public double TieBeamHeightM { get; set; }

    public double EmbedmentDepthM => BaseThicknessM + PedestalHeightM;
}

public sealed class FoundationCheckResult
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public CheckStatus Status { get; init; }

    public double Demand { get; init; }

    public double Capacity { get; init; }

    public double Utilization { get; init; }

    public string Unit { get; init; } = string.Empty;

    public string GoverningCase { get; init; } = string.Empty;

    public string Explanation { get; init; } = string.Empty;

    public string RuleReference { get; init; } = "开发校核规则 Prototype-2026.07";

    [System.Text.Json.Serialization.JsonIgnore]
    public string DemandDisplay => Status is CheckStatus.SpecialReview or CheckStatus.PendingInput or CheckStatus.Advisory
        ? "—"
        : $"{Demand:F2}";

    [System.Text.Json.Serialization.JsonIgnore]
    public string CapacityDisplay => Status is CheckStatus.Result or CheckStatus.SpecialReview or CheckStatus.PendingInput or CheckStatus.NotEvaluated or CheckStatus.Advisory
        ? "—"
        : $"{Capacity:F2}";

    [System.Text.Json.Serialization.JsonIgnore]
    public string UtilizationDisplay => Status is CheckStatus.Pass or CheckStatus.Fail
        ? double.IsFinite(Utilization) ? Utilization.ToString("P0") : "—"
        : "—";
}

public sealed class QuantitySummary
{
    public double ConcreteM3 { get; init; }

    public double ExcavationM3 { get; init; }

    public double BackfillM3 { get; init; }

    public double EstimatedReinforcementKg { get; init; }
}

public sealed class ReinforcementDesignResult
{
    public string Component { get; init; } = string.Empty;

    public string Direction { get; init; } = string.Empty;

    public string BarSpecification { get; init; } = string.Empty;

    public double RequiredAreaMm2 { get; init; }

    public double ProvidedAreaMm2 { get; init; }

    public int BarCount { get; init; }

    public double BarDiameterMm { get; init; }

    public double BarSpacingMm { get; init; }

    public double SingleBarLengthM { get; init; }

    public double TotalLengthM { get; init; }

    public double UnitWeightKgPerM { get; init; }

    public double CalculatedWeightKg { get; init; }

    public double StirrupBodyPerimeterM { get; init; }

    public double HookBendAllowanceM { get; init; }

    public double HookStraightAllowanceM { get; init; }

    public string CuttingLengthExplanation { get; init; } = string.Empty;

    public CheckStatus Status { get; init; }

    public string RuleReference { get; init; } = string.Empty;
}

public sealed class FoundationScheme
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public OptimizationPreference Preference { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public FoundationType FoundationType { get; set; } = FoundationType.RectangularShortColumn;

    public FoundationGeometry Geometry { get; set; } = new();

    public List<FoundationCheckResult> Checks { get; set; } = [];

    public QuantitySummary Quantities { get; set; } = new();

    public List<ReinforcementDesignResult> ReinforcementDesigns { get; set; } = [];

    public double Score { get; set; }

    public string GeometrySummary => FoundationType switch
    {
        FoundationType.Pile =>
            Geometry.PileCount <= 1
                ? $"单管塔1根灌注桩 Φ{Geometry.PileDiameterM:F2} m × 埋深{Geometry.PileLengthM:F1} m；出地面{Geometry.PedestalHeightM:F2} m"
                : $"{Geometry.PileCount}根独立灌注桩 Φ{Geometry.PileDiameterM:F2} m × 埋深{Geometry.PileLengthM:F1} m；{Geometry.TieBeamCount}根连梁拉接，无承台",
        FoundationType.RigidShortPile =>
            $"{FormatFoundationUnitPrefix(Geometry.FoundationUnitCount)}刚性短柱桩－圆形 Φ{Geometry.PileDiameterM:F2} m × 埋深{Geometry.PileLengthM:F1} m；出地面{Geometry.PedestalHeightM:F2} m{FormatTieBeamSuffix(Geometry)}",
        FoundationType.RigidRectangularShortPile =>
            $"{FormatFoundationUnitPrefix(Geometry.FoundationUnitCount)}刚性短柱桩－矩形 {Geometry.BaseLengthM:F2}×{Geometry.BaseWidthM:F2} m × 埋深{Geometry.PileLengthM:F1} m；出地面{Geometry.PedestalHeightM:F2} m{FormatTieBeamSuffix(Geometry)}",
        _ =>
            $"{FormatFoundationUnitPrefix(Geometry.FoundationUnitCount)}{Geometry.BaseLengthM:F1}×{Geometry.BaseWidthM:F1}×{Geometry.BaseThicknessM:F1} m{FormatTieBeamSuffix(Geometry)}"
    };

    private static string FormatFoundationUnitPrefix(int count) =>
        count > 1 ? $"{count}个独立基础，每个 " : string.Empty;

    private static string FormatTieBeamSuffix(FoundationGeometry geometry) =>
        geometry.TieBeamCount > 0
            ? $"；{geometry.TieBeamCount}根连系梁 {geometry.TieBeamWidthM:F2}×{geometry.TieBeamHeightM:F2} m闭合拉接"
            : string.Empty;

    public double MaximumUtilization =>
        Checks.Where(check => check.Status is CheckStatus.Pass or CheckStatus.Fail)
            .Select(check => check.Utilization)
            .DefaultIfEmpty(0)
            .Max();

    public bool IsFeasible =>
        Checks.Count > 0 &&
        Checks.All(check => check.Status is not CheckStatus.Fail and not CheckStatus.NotEvaluated);

    public bool IsFormalVerificationComplete =>
        Checks.Count > 0 &&
        Checks.All(check => check.Status is CheckStatus.Pass or CheckStatus.Result or CheckStatus.Advisory);

    public bool HasPendingInputs =>
        Checks.Any(check => check.Status is CheckStatus.PendingInput or CheckStatus.NotEvaluated);

    public bool HasSpecialReviews =>
        Checks.Any(check => check.Status is CheckStatus.SpecialReview or CheckStatus.Warning);

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<FoundationCheckResult> VerificationChecks => Checks
        .Where(check => check.Status is CheckStatus.Pass or CheckStatus.Fail)
        .ToList();

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<FoundationCheckResult> CalculatedResults => Checks
        .Where(check => check.Status == CheckStatus.Result)
        .ToList();

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<FoundationCheckResult> ScopeAndInputItems => Checks
        .Where(check => check.Status is CheckStatus.PendingInput or CheckStatus.SpecialReview or CheckStatus.Warning or CheckStatus.NotEvaluated)
        .ToList();

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<FoundationCheckResult> DeliveryReminders => Checks
        .Where(check => check.Status == CheckStatus.Advisory)
        .ToList();

    public string VerificationConclusion => !IsFeasible
        ? "存在不满足项"
        : IsFormalVerificationComplete
            ? "已完成当前规则包全部验算"
            : HasPendingInputs
                ? "复核稿－待补关键参数"
                : "复核稿－含专项复核项";
}

public sealed class AuditRecord
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public string Action { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;
}

public sealed class ValidationIssue
{
    public string Field { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public bool IsBlocking { get; init; } = true;
}

public sealed class FoundationAdjustmentAdvice
{
    public int Priority { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public bool IsBlocking { get; init; }
}
