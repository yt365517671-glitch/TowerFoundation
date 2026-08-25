using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public sealed record SpecialtyApplicability(
    bool NeedsDeformationLimits,
    bool NeedsSettlementParameters,
    bool NeedsCrackParameters,
    bool NeedsAnchorDecision,
    bool NeedsAnchorParameters,
    bool NeedsPedestalStructuralParameters,
    bool NeedsPileStructuralParameters,
    bool NeedsHighWaterParameters);

public sealed record SpecialtyAutoFillResult(
    int FilledCategoryCount,
    IReadOnlyList<string> Messages);

public sealed class SpecialtyAutoFillService
{
    public const double SensitiveStructurePileTopDisplacementMm = 6;

    public const double GeneralPileTopDisplacementMm = 10;

    public SpecialtyApplicability DetermineApplicability(ProjectModel project)
    {
        var type = project.FoundationSettings.FoundationType;
        var connection = project.FoundationSettings.SpecialtyDesign.AnchorBolts.ConnectionType;
        return new SpecialtyApplicability(
            NeedsDeformationLimits: type is FoundationType.RigidShortPile or FoundationType.RigidRectangularShortPile or FoundationType.Pile,
            NeedsSettlementParameters: true,
            NeedsCrackParameters: type is FoundationType.RigidRectangularShortPile or FoundationType.Pile,
            NeedsAnchorDecision: connection == AnchorConnectionType.NotDetermined,
            NeedsAnchorParameters: connection == AnchorConnectionType.AnchorBoltCage,
            NeedsPedestalStructuralParameters: type is FoundationType.RectangularShortColumn or FoundationType.CircularShortColumn,
            NeedsPileStructuralParameters: type == FoundationType.Pile,
            NeedsHighWaterParameters: type is FoundationType.RectangularShortColumn or FoundationType.CircularShortColumn or FoundationType.Raft);
    }

    public SpecialtyAutoFillResult ApplyRecommendedDefaults(ProjectModel project)
    {
        var applicability = DetermineApplicability(project);
        var specialty = project.FoundationSettings.SpecialtyDesign;
        var geotechnical = project.Geotechnical;
        var pile = project.FoundationSettings.Pile;
        var messages = new List<string>();
        var filledCategories = 0;

        var seismicByLocation = new LocationSeismicReferenceService()
            .ApplyIfAvailable(project);
        if (seismicByLocation.Applied)
        {
            messages.Add(seismicByLocation.Message);
            filledCategories++;
        }

        if (applicability.NeedsDeformationLimits)
        {
            var deformation = specialty.Deformation;
            var heightM = ResolveStructureHeight(project);
            var displacementLimitMm = project.ProjectType == ProjectType.MonitoringPole
                ? GeneralPileTopDisplacementMm
                : SensitiveStructurePileTopDisplacementMm;
            var rotationLimitRad = ResolveHighRiseFoundationTiltLimit(heightM);
            var deformationChanged = false;
            if (deformation.AllowableTopDisplacementMm <= 0)
            {
                deformation.AllowableTopDisplacementMm = displacementLimitMm;
                deformationChanged = true;
            }
            if (deformation.AllowableTopRotationRad <= 0)
            {
                deformation.AllowableTopRotationRad = rotationLimitRad;
                deformationChanged = true;
            }
            if (deformationChanged)
            {
                deformation.Source.SourceType = ParameterSourceType.BuiltInDatabase;
                deformation.Source.SourceDocument =
                    "JGJ 94-2008；GB 50007-2011";
                deformation.Source.SourceLocation =
                    "JGJ 94-2008第5.7.3条、表5.5.4；GB 50007-2011表5.3.4";
                deformation.Source.Note =
                    $"桩顶水平位移按{displacementLimitMm:F0} mm工作限值；转角按塔高{heightM:F1} m对应的高耸结构基础整体倾斜限值{rotationLimitRad:F3}近似为小转角控制值。厂家或项目限值更严格时应覆盖。";
                deformation.Source.IsConfirmed = true;
                messages.Add(
                    $"已按规范自动采用桩顶位移{displacementLimitMm:F0} mm、转角{rotationLimitRad:F3} rad工作限值。" );
                filledCategories++;
            }
        }

        if (applicability.NeedsAnchorDecision)
        {
            specialty.AnchorBolts.ConnectionType = AnchorConnectionType.AnchorBoltCage;
            specialty.AnchorBolts.TemplateName = "地脚锚栓连接（软件工作默认）";
            specialty.AnchorBolts.Source.SourceType = ParameterSourceType.BuiltInDatabase;
            specialty.AnchorBolts.Source.SourceDocument = "塔基智设常用连接场景";
            specialty.AnchorBolts.Source.SourceLocation = "厂家塔脚详图可覆盖";
            specialty.AnchorBolts.Source.Note =
                "仅默认连接形式，不编造锚栓数量、直径、锚栓圆和埋深；详图未导入时自动转交付前专业核对。";
            messages.Add("塔脚连接已默认采用地脚锚栓；厂家详图导入后自动覆盖规格。" );
            filledCategories++;
        }

        if (applicability.NeedsSettlementParameters)
        {
            var settlementChanged = false;
            var isLegacyTwentyMillimeterPreset =
                Math.Abs(specialty.Settlement.AllowableSettlementMm - 20) < 1e-9 &&
                (specialty.Settlement.Source.SourceDocument.Contains(
                     "塔基智设保守工作预设",
                     StringComparison.Ordinal) ||
                 specialty.Settlement.Source.Note.Contains(
                     "允许沉降20 mm",
                     StringComparison.Ordinal));
            if (specialty.Settlement.AllowableSettlementMm <= 0 ||
                isLegacyTwentyMillimeterPreset)
            {
                specialty.Settlement.AllowableSettlementMm =
                    ResolveAllowableSettlementMm(project);
                if (isLegacyTwentyMillimeterPreset)
                {
                    specialty.Settlement.Source.SourceDocument = string.Empty;
                    specialty.Settlement.Source.SourceLocation = string.Empty;
                    specialty.Settlement.Source.Note = string.Empty;
                }
                settlementChanged = true;
            }
            if (specialty.Settlement.ExperienceCoefficient <= 0)
            {
                specialty.Settlement.ExperienceCoefficient = 1.0;
                settlementChanged = true;
            }
            if (settlementChanged)
            {
                PreserveSourceAndAppendNote(
                    specialty.Settlement.Source,
                    "JGJ 94-2008表5.5.4；GB 50007-2011表5.3.4",
                    $"允许沉降按结构高度和基础类型自动取{specialty.Settlement.AllowableSettlementMm:F0} mm；分层厚度与压缩模量仍必须采用地勘原文，不由软件猜测。");
                messages.Add(
                    $"已按塔高自动采用允许沉降{specialty.Settlement.AllowableSettlementMm:F0} mm和经验系数1.0；缺少分层时转专业核对。" );
                filledCategories++;
            }

            var populatedSettlementLayers = specialty.Settlement.SoilLayers
                .Where(layer => layer.ThicknessM > 0 && layer.CompressionModulusMpa > 0)
                .ToList();
            if (populatedSettlementLayers.Count == 0 &&
                geotechnical.CompressionModulusMpa > 0)
            {
                var firstLayer = specialty.Settlement.SoilLayers.First();
                if (firstLayer.CompressionModulusMpa <= 0)
                {
                    firstLayer.Name = "地勘主要受压层（厚度待报告分层）";
                    firstLayer.CompressionModulusMpa = geotechnical.CompressionModulusMpa;
                    PreserveSourceAndAppendNote(
                        specialty.Settlement.Source,
                        "已录入地勘参数",
                        $"已带入地勘中的Es={geotechnical.CompressionModulusMpa:F2} MPa；土层厚度没有原文依据时仍保持空白，不由软件猜测。");
                    messages.Add($"已从地勘带入压缩模量Es={geotechnical.CompressionModulusMpa:F2} MPa；仅缺原报告分层厚度。" );
                    filledCategories++;
                }
            }

            if (project.FoundationSettings.FoundationType == FoundationType.Pile &&
                pile.SettlementMethod == PileSettlementMethod.NotSelected)
            {
                pile.SettlementMethod = PileSettlementMethod.MindlinReviewEstimate;
                PreserveSourceAndAppendNote(
                    pile.SettlementSource,
                    "塔基智设弹性复核模型",
                    "尚无静载Q-s曲线或经审查专项结果，先自动输出Mindlin弹性量级供复核；该结果不会被标为正式通过。" );
                messages.Add("桩基尚无试桩曲线，已自动采用Mindlin量级复核，不要求现在手填试桩数据。" );
                filledCategories++;
            }
        }

        if (applicability.NeedsCrackParameters)
        {
            var crackChanged = false;
            if (string.IsNullOrWhiteSpace(specialty.Crack.EnvironmentCategory) ||
                specialty.Crack.EnvironmentCategory.Contains("待确认", StringComparison.Ordinal))
            {
                specialty.Crack.EnvironmentCategory = "普通室外（软件保守预设）";
                crackChanged = true;
            }
            if (specialty.Crack.MaximumCrackWidthMm <= 0)
            {
                specialty.Crack.MaximumCrackWidthMm = 0.20;
                crackChanged = true;
            }
            if (specialty.Crack.ConcreteTensileStrengthStandardMpa <= 0)
            {
                specialty.Crack.ConcreteTensileStrengthStandardMpa = 2.01;
                crackChanged = true;
            }
            if (specialty.Crack.ReinforcementElasticModulusMpa <= 0)
            {
                specialty.Crack.ReinforcementElasticModulusMpa = 200_000;
                crackChanged = true;
            }
            if (crackChanged)
            {
                specialty.Crack.Source.SourceType = ParameterSourceType.BuiltInDatabase;
                specialty.Crack.Source.SourceDocument = "GB/T 50010-2010（2024年版）";
                specialty.Crack.Source.SourceLocation = "表3.4.5、第7.1.2条；普通室外工作预设";
                specialty.Crack.Source.Note = "环境类别为软件候选，点击应用即表示用户按项目环境确认；腐蚀、滨海或特殊介质环境必须改选。";
                specialty.Crack.Source.IsConfirmed = false;
                messages.Add("已按普通室外场景预填裂缝参数；特殊环境请在下拉框改选。" );
                filledCategories++;
            }
        }

        if (applicability.NeedsPedestalStructuralParameters &&
            !specialty.PedestalStructure.Source.IsConfirmed)
        {
            messages.Add("已采用短柱C30级材料与配筋工作候选；需要时可在高级参数中修改。" );
            filledCategories++;
        }

        if (applicability.NeedsPileStructuralParameters && !pile.IsConfirmed)
        {
            messages.Add("已采用灌注桩C30、纵筋和箍筋工作候选；m值与桩土参数优先使用地勘AI结果。" );
            filledCategories++;
        }

        if (applicability.NeedsHighWaterParameters &&
            specialty.Hydrogeology.DesignHighGroundwaterDepthM < 0)
        {
            if (geotechnical.GroundwaterDepthM >= 0)
            {
                specialty.Hydrogeology.DesignHighGroundwaterDepthM = geotechnical.GroundwaterDepthM;
                specialty.Hydrogeology.Source.SourceType = geotechnical.SourceType;
                specialty.Hydrogeology.Source.SourceDocument =
                    geotechnical.SourceType is ParameterSourceType.DeepSeek or ParameterSourceType.VisualAi
                        ? "地勘AI候选及人工确认值"
                        : "地勘已录入地下水参数";
                specialty.Hydrogeology.Source.SourceLocation = geotechnical.Evidence;
                specialty.Hydrogeology.Source.Note =
                    "当前阶段采用地勘页已确认的地下水埋深作为设计水位候选；报告另有历史最高水位或抗浮水位时应以后者替换。";
                messages.Add($"已把地勘地下水埋深{geotechnical.GroundwaterDepthM:F2} m带入抗浮工作候选。" );
                filledCategories++;
            }
            else
            {
                messages.Add("抗浮系数已有默认值；地勘没有地下水候选时转为交付前核对，不要求现在猜数。" );
            }
        }

        if (string.IsNullOrWhiteSpace(geotechnical.SpecialSoilRisks))
        {
            geotechnical.SpecialSoilRisks =
                "地勘未明确特殊土与不良地质结论，需在专项复核中确认。";
            messages.Add("特殊土结论缺失时已标记为“需专项复核”，不会自动解释为无风险。" );
            filledCategories++;
        }
        else
        {
            var specialGroundChanged = ApplyExplicitSpecialGroundStatements(
                specialty.SpecialGround,
                geotechnical.SpecialSoilRisks,
                geotechnical.Evidence,
                geotechnical.SourceType);
            if (specialGroundChanged)
            {
                messages.Add("已把地勘AI/原文中明确写出的湿陷、液化和冻胀结论带入；未明确的项目仍保持专项复核。" );
                filledCategories++;
            }
        }

        project.ModifiedAt = DateTimeOffset.Now;
        return new SpecialtyAutoFillResult(filledCategories, messages);
    }

    private static double ResolveStructureHeight(ProjectModel project) =>
        Math.Max(
            0.1,
            project.ProjectType == ProjectType.MonitoringPole
                ? project.MonitoringPole.PoleHeightM
                : project.TowerMast.HeightM);

    private static double ResolveHighRiseFoundationTiltLimit(double heightM) =>
        heightM switch
        {
            <= 20 => 0.008,
            <= 50 => 0.006,
            <= 100 => 0.005,
            <= 150 => 0.004,
            <= 200 => 0.003,
            _ => 0.002
        };

    private static double ResolveAllowableSettlementMm(ProjectModel project)
    {
        var heightM = ResolveStructureHeight(project);
        var isPileFoundation = project.FoundationSettings.FoundationType is
            FoundationType.RigidShortPile or
            FoundationType.RigidRectangularShortPile or
            FoundationType.Pile;
        if (isPileFoundation)
        {
            return heightM <= 100
                ? 350
                : heightM <= 200
                    ? 250
                    : 150;
        }

        return heightM <= 100
            ? 400
            : heightM <= 200
                ? 300
                : 200;
    }

    private static bool ApplyExplicitSpecialGroundStatements(
        SpecialGroundDesignInput target,
        string sourceText,
        string evidence,
        ParameterSourceType sourceType)
    {
        var changed = false;
        var collapsibleLoess = ResolveRiskState(
            target.CollapsibleLoess,
            sourceText,
            ["无湿陷", "不具湿陷", "非湿陷", "不考虑湿陷"],
            ["湿陷性黄土", "具有湿陷", "存在湿陷"]);
        var liquefaction = ResolveRiskState(
            target.Liquefaction,
            sourceText,
            ["不液化", "无液化", "不存在液化", "可不考虑液化"],
            ["存在液化", "液化土", "液化等级"]);
        var frostHeave = ResolveRiskState(
            target.FrostHeave,
            sourceText,
            ["无冻胀", "不冻胀", "非冻胀", "可不考虑冻胀"],
            ["存在冻胀", "冻胀性", "冻土深度", "标准冻深"]);
        changed = collapsibleLoess != target.CollapsibleLoess ||
                  liquefaction != target.Liquefaction ||
                  frostHeave != target.FrostHeave;
        target.CollapsibleLoess = collapsibleLoess;
        target.Liquefaction = liquefaction;
        target.FrostHeave = frostHeave;

        if (changed)
        {
            target.Source.SourceType = sourceType;
            target.Source.SourceDocument = sourceType is ParameterSourceType.DeepSeek or ParameterSourceType.VisualAi
                ? "地勘AI候选及原文证据"
                : "地勘已录入特殊土结论";
            target.Source.SourceLocation = evidence;
            target.Source.Note = "只转换原文明确的存在/不存在结论；没有明确表述的风险保持未评估。";
            target.Source.IsConfirmed = false;
        }

        return changed;
    }

    private static EngineeringRiskState ResolveRiskState(
        EngineeringRiskState current,
        string sourceText,
        IReadOnlyList<string> negativePhrases,
        IReadOnlyList<string> positivePhrases)
    {
        if (current != EngineeringRiskState.NotAssessed)
        {
            return current;
        }

        if (negativePhrases.Any(phrase => sourceText.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return EngineeringRiskState.NotPresent;
        }

        if (positivePhrases.Any(phrase => sourceText.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return EngineeringRiskState.PresentTreatmentUnconfirmed;
        }

        return current;
    }

    public static void ApplyCrackEnvironmentPreset(
        CrackDesignInput crack,
        string environmentCategory)
    {
        crack.EnvironmentCategory = environmentCategory;
        crack.MaximumCrackWidthMm = 0.20;
        crack.ConcreteTensileStrengthStandardMpa = crack.ConcreteTensileStrengthStandardMpa > 0
            ? crack.ConcreteTensileStrengthStandardMpa
            : 2.01;
        crack.ReinforcementElasticModulusMpa = crack.ReinforcementElasticModulusMpa > 0
            ? crack.ReinforcementElasticModulusMpa
            : 200_000;
        crack.Source.SourceType = ParameterSourceType.BuiltInDatabase;
        crack.Source.SourceDocument = "GB/T 50010-2010（2024年版）";
        crack.Source.SourceLocation = "表3.4.5、第7.1.2条";
        crack.Source.Note = environmentCategory.Contains("腐蚀", StringComparison.Ordinal)
            ? "腐蚀性环境不能只依赖通用0.20 mm预设，须按专项耐久性要求复核。"
            : "用户已通过环境场景选择确认裂缝参数候选。";
        crack.Source.IsConfirmed = false;
    }

    private static void PreserveSourceAndAppendNote(
        EngineeringParameterSource source,
        string fallbackDocument,
        string note)
    {
        if (string.IsNullOrWhiteSpace(source.SourceDocument))
        {
            source.SourceType = ParameterSourceType.BuiltInDatabase;
            source.SourceDocument = fallbackDocument;
            source.SourceLocation = "智能补齐候选";
        }
        source.Note = string.IsNullOrWhiteSpace(source.Note)
            ? note
            : source.Note + "；" + note;
        source.IsConfirmed = false;
    }
}
