using TowerFoundation.Calculation;
using TowerFoundation.Domain;
using TowerFoundation.Optimization;

namespace TowerFoundation.Application;

public sealed class DesignWorkflowService
{
    private readonly MonitoringPoleLoadCalculator _poleLoadCalculator;
    private readonly RectangularShortColumnFoundationCalculator _foundationCalculator;
    private readonly ThreeStrategyFoundationOptimizer _optimizer;
    private readonly FoundationAdjustmentAdvisor _adjustmentAdvisor;
    private readonly LoadCombinationEngine _loadCombinationEngine = new();

    public DesignWorkflowService(
        MonitoringPoleLoadCalculator poleLoadCalculator,
        RectangularShortColumnFoundationCalculator foundationCalculator,
        ThreeStrategyFoundationOptimizer optimizer,
        FoundationAdjustmentAdvisor adjustmentAdvisor)
    {
        _poleLoadCalculator = poleLoadCalculator;
        _foundationCalculator = foundationCalculator;
        _optimizer = optimizer;
        _adjustmentAdvisor = adjustmentAdvisor;
    }

    public IReadOnlyList<ValidationIssue> ValidateForDesign(ProjectModel project)
    {
        PileLayoutRules.Synchronize(project);
        var issues = new List<ValidationIssue>();

        if (project.ProjectType == ProjectType.NotSelected)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(project.ProjectType),
                Message = "请先选择“监控杆基础”或“通信塔桅基础”。"
            });
        }

        if (string.IsNullOrWhiteSpace(project.Name))
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(project.Name),
                Message = "请填写项目名称。"
            });
        }

        if (!project.Geotechnical.IsConfirmed)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(project.Geotechnical),
                Message = "地勘参数尚未由用户确认。"
            });
        }

        var foundationSettings = project.FoundationSettings;
        var requiresShallowBearingCapacity =
            foundationSettings.FoundationType is
                FoundationType.RectangularShortColumn or
                FoundationType.CircularShortColumn or
                FoundationType.Raft;
        if (requiresShallowBearingCapacity &&
            project.Geotechnical.BearingCapacityKpa <= 0)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(project.Geotechnical.BearingCapacityKpa),
                Message = "地基承载力必须大于0。"
            });
        }

        if (requiresShallowBearingCapacity &&
            project.Geotechnical.UseBearingCapacityCorrection &&
            (project.Geotechnical.CharacteristicBearingCapacityKpa <= 0 ||
             project.Geotechnical.BearingCapacityWidthCorrectionFactor < 0 ||
             project.Geotechnical.BearingCapacityDepthCorrectionFactor < 0 ||
             project.Geotechnical.SoilBelowBaseUnitWeightKnPerM3 <= 0 ||
             project.Geotechnical.SoilAboveBaseAverageUnitWeightKnPerM3 <= 0))
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(project.Geotechnical.UseBearingCapacityCorrection),
                Message = "请完整填写并确认fak、ηb、ηd及基底上下土重度。"
            });
        }

        if (foundationSettings.StructuralDesignLoadFactor <= 0 ||
            foundationSettings.FoundationPermanentLoadFactor <= 0 ||
            foundationSettings.StructureImportanceFactor <= 0 ||
            foundationSettings.ConcreteTensileStrengthMpa <= 0 ||
            foundationSettings.ReinforcementYieldStrengthMpa <= 0 ||
            foundationSettings.ConcreteCoverMm <= 0)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(project.FoundationSettings),
                Message = "请检查结构设计组合、混凝土、钢筋和保护层参数。"
            });
        }

        if (requiresShallowBearingCapacity &&
            (foundationSettings.BottomBarDiameterMm <= 0 ||
             foundationSettings.BottomBarSpacingMm <= 0 ||
             foundationSettings.MinimumReinforcementRatio <= 0))
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(project.FoundationSettings),
                Message = "请检查独立基础或筏板的底筋直径、间距和最小配筋率。"
            });
        }

        if (requiresShallowBearingCapacity &&
            foundationSettings.MinimumBaseThicknessM * 1000 <=
            foundationSettings.ConcreteCoverMm +
            foundationSettings.BottomBarDiameterMm / 2)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(foundationSettings.MinimumBaseThicknessM),
                Message = "底板最小厚度不足以形成有效高度，请调整保护层、钢筋直径或厚度范围。"
            });
        }

        switch (foundationSettings.FoundationType)
        {
            case FoundationType.CircularShortColumn
                when foundationSettings.PedestalDiameterM <= 0:
                issues.Add(new ValidationIssue
                {
                    Field = nameof(foundationSettings.PedestalDiameterM),
                    Message = "独立基础－圆形柱的柱直径必须大于0。"
                });
                break;
            case FoundationType.Raft
                when foundationSettings.PedestalLengthM <= 0 ||
                     foundationSettings.PedestalWidthM <= 0:
                issues.Add(new ValidationIssue
                {
                    Field = nameof(foundationSettings.PedestalLengthM),
                    Message = "请填写筏板中央塔柱或台座的长、宽尺寸。"
                });
                break;
            case FoundationType.Pile:
                ValidatePileSettings(foundationSettings.Pile, issues);
                break;
            case FoundationType.RigidShortPile:
                ValidateRigidShortPileSettings(
                    foundationSettings.RigidShortPile,
                    project.Geotechnical,
                    rectangular: false,
                    issues: issues);
                break;
            case FoundationType.RigidRectangularShortPile:
                ValidateRigidShortPileSettings(
                    foundationSettings.RigidShortPile,
                    project.Geotechnical,
                    rectangular: true,
                    issues: issues);
                break;
        }

        switch (project.ProjectType)
        {
            case ProjectType.MonitoringPole:
                if (string.IsNullOrWhiteSpace(project.Province) ||
                    string.IsNullOrWhiteSpace(project.City))
                {
                    issues.Add(new ValidationIssue
                    {
                        Field = nameof(project.City),
                        Message = "请在地勘参数页确认监控杆项目的省、市；地勘AI识别成功时会自动回填。"
                    });
                }

                issues.AddRange(_poleLoadCalculator.Validate(project.MonitoringPole));
                break;
            case ProjectType.CommunicationTower:
                issues.AddRange(ValidateTowerLoads(
                    project.TowerMast,
                    foundationSettings.FoundationType));
                break;
        }

        return issues;
    }

    private static void ValidatePileSettings(
        PileFoundationSettings pile,
        ICollection<ValidationIssue> issues)
    {
        if (!pile.IsConfirmed)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(pile.IsConfirmed),
                Message = "请根据地勘报告确认独立灌注桩的桩径、桩长、分层侧阻/端阻、抗拔系数及水平承载力参数。"
            });
        }

        if (pile.AboveGroundHeightM < 0 ||
            pile.PileCount is not (1 or 3 or 4) ||
            (pile.PileCount > 1 &&
             (!pile.TieBeamRequired ||
              pile.PileCenterSpacingM <= 0 ||
              pile.TieBeamWidthM <= 0 ||
              pile.TieBeamHeightM <= 0)) ||
            pile.SinglePileHorizontalCapacityKn <= 0 ||
            pile.CapacityReductionFactor <= 0 ||
            pile.PileMainBarDiameterMm <= 0 ||
            pile.PileMainBarCount <= 0 ||
            pile.MinimumLongitudinalReinforcementRatio <= 0)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(PileFoundationSettings),
                Message = "独立灌注桩的桩数、连梁几何、出地面高度、承载力安全系数、水平承载力或纵筋参数无效。"
            });
        }

        if (pile.MinimumPileDiameterM <= 0 ||
            pile.MaximumPileDiameterM < pile.MinimumPileDiameterM ||
            pile.PileDiameterStepM <= 0 ||
            pile.MinimumPileLengthM <= 0 ||
            pile.MaximumPileLengthM < pile.MinimumPileLengthM ||
            pile.PileLengthStepM <= 0)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(pile.MinimumPileDiameterM),
                Message = "独立灌注桩的桩径或桩长搜索范围无效。"
            });
        }

        var validLayers = pile.SoilLayers
            .Where(item => item.ThicknessM > 0)
            .ToList();
        if (validLayers.Count == 0 ||
            validLayers.Any(item =>
                item.SideResistanceKpa < 0 ||
                item.TipResistanceKpa < 0 ||
                item.UpliftCoefficient is < 0 or > 1) ||
            validLayers.Sum(item => item.ThicknessM) + 1e-9 <
            pile.MinimumPileLengthM)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(pile.SoilLayers),
                Message = "单桩土层参数无效，或累计土层厚度没有覆盖最小设计桩长。"
            });
        }

        if (pile.UseUserConfirmedPileHeadForces &&
            (pile.MaximumPileCompressionKn < 0 ||
             pile.MaximumPileUpliftKn < 0))
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(pile.UseUserConfirmedPileHeadForces),
                Message = "用户确认的最大桩顶压力和上拔力不能为负数。"
            });
        }
    }

    private static void ValidateRigidShortPileSettings(
        RigidShortPileSettings rigid,
        GeotechnicalInput geotechnical,
        bool rectangular,
        ICollection<ValidationIssue> issues)
    {
        if (!rigid.IsConfirmed)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(rigid.IsConfirmed),
                Message = "请根据地勘报告确认刚性短柱桩的分层m值、土重度、内摩擦角及地下水。"
            });
        }

        if (geotechnical.SoilUnitWeightKnPerM3 <= 0 ||
            geotechnical.InternalFrictionAngleDegree < 0 ||
            geotechnical.InternalFrictionAngleDegree >= 45)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(geotechnical.InternalFrictionAngleDegree),
                Message = "刚性短柱桩要求土重度大于0，内摩擦角在0～45°之间。"
            });
        }

        var invalidCircularRange =
            rigid.MinimumDiameterM <= 0 ||
            rigid.MaximumDiameterM < rigid.MinimumDiameterM ||
            rigid.DiameterStepM <= 0;
        var invalidRectangularRange =
            rigid.MinimumRectangularLengthM <= 0 ||
            rigid.MaximumRectangularLengthM < rigid.MinimumRectangularLengthM ||
            rigid.RectangularLengthStepM <= 0 ||
            rigid.MinimumRectangularWidthM <= 0 ||
            rigid.MaximumRectangularWidthM < rigid.MinimumRectangularWidthM ||
            rigid.RectangularWidthStepM <= 0;
        if ((!rectangular && invalidCircularRange) ||
            (rectangular && invalidRectangularRange) ||
            rigid.MinimumEmbeddedDepthM <= 0 ||
            rigid.MaximumEmbeddedDepthM < rigid.MinimumEmbeddedDepthM ||
            rigid.EmbeddedDepthStepM <= 0 ||
            rigid.AboveGroundHeightM < 0 ||
            rigid.LateralResistanceWidthCoefficient <= 0 ||
            rigid.VerticalReactionEccentricityCoefficient <= 0 ||
            rigid.ConcreteElasticModulusMpa <= 0 ||
            rigid.ConcreteCompressiveStrengthMpa <= 0 ||
            rigid.LongitudinalBarDiameterMm <= 0 ||
            rigid.LongitudinalBarCount < (rectangular ? 8 : 6) ||
            rigid.MinimumLongitudinalReinforcementRatio <= 0 ||
            rigid.StirrupDiameterMm <= 0 ||
            rigid.StirrupSpacingMm <= 0 ||
            rigid.StirrupLegCount <= 0)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(RigidShortPileSettings),
                Message = "刚性短柱桩的尺寸搜索、土抗力、混凝土或配筋参数无效。"
            });
        }

        if (rigid.SoilLayers.Count == 0 ||
            rigid.SoilLayers.Any(layer =>
                layer.ThicknessM <= 0 ||
                layer.HorizontalResistanceCoefficientMnPerM4 < 0))
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(rigid.SoilLayers),
                Message = "请完整填写刚性短柱桩主要影响深度内的分层m值。"
            });
        }

        var controllingMinimumWidth = rectangular
            ? Math.Max(
                rigid.MinimumRectangularLengthM,
                rigid.MinimumRectangularWidthM)
            : rigid.MinimumDiameterM;
        var minimumInfluenceDepth = Math.Min(
            rigid.MinimumEmbeddedDepthM,
            2 * (controllingMinimumWidth + 1));
        if (rigid.SoilLayers.Sum(layer => layer.ThicknessM) + 1e-9 <
            minimumInfluenceDepth)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(rigid.SoilLayers),
                Message =
                    $"m值分层厚度至少应覆盖最小搜索方案的主要影响深度{minimumInfluenceDepth:F2} m。"
            });
        }
    }

    public FoundationLoad CalculateFoundationLoad(ProjectModel project)
    {
        return CalculateFoundationLoad(project, recordAudit: true);
    }

    public MonitoringPoleLoadResult CalculateMonitoringPoleLoads(ProjectModel project)
    {
        if (project.ProjectType != ProjectType.MonitoringPole)
        {
            throw new InvalidOperationException("当前项目不是监控杆项目。");
        }

        var result = _poleLoadCalculator.Calculate(
            project.MonitoringPole,
            project.FoundationSettings);
        project.FoundationLoad = result.FoundationLoad;
        project.Stage = ProjectStage.LoadReady;
        project.ModifiedAt = DateTimeOffset.Now;
        project.AuditTrail.Add(new AuditRecord
        {
            Action = "计算监控杆基础端荷载",
            Details = $"控制弯矩{result.FoundationLoad.MomentYKnM:F2} kN·m，水平力{result.FoundationLoad.ShearXKn:F2} kN。"
        });

        return result;
    }

    public IReadOnlyList<FoundationScheme> GenerateSchemes(ProjectModel project)
    {
        var issues = ValidateForDesign(project);
        if (issues.Any(issue => issue.IsBlocking))
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, issues.Select(issue => issue.Message)));
        }

        var load = CalculateFoundationLoad(project, recordAudit: true);
        var schemes = _optimizer.Optimize(
            load,
            project.Geotechnical,
            project.FoundationSettings);

        project.Schemes = schemes.ToList();
        project.SelectedSchemeId = null;
        project.Stage = ProjectStage.CandidateReady;
        project.ModifiedAt = DateTimeOffset.Now;
        project.AuditTrail.Add(new AuditRecord
        {
            Action = "生成三策略方案",
            Details = $"生成{schemes.Count}个可行方案。"
        });

        return schemes;
    }

    public FoundationScheme EvaluateCustomScheme(
        ProjectModel project,
        FoundationGeometry geometry)
    {
        var issues = ValidateForDesign(project);
        if (issues.Any(issue => issue.IsBlocking))
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, issues.Select(issue => issue.Message)));
        }

        var load = CalculateFoundationLoad(project, recordAudit: false);
        var scheme = _foundationCalculator.Calculate(
            geometry,
            load,
            project.Geotechnical,
            project.FoundationSettings);
        scheme.Name = "自定义复算";
        scheme.Description = scheme.IsFeasible
            ? $"用户输入尺寸满足已完成的确定性校核；{scheme.VerificationConclusion}。"
            : "用户输入尺寸存在不满足项，请按调整建议修改。";

        project.AuditTrail.Add(new AuditRecord
        {
            Action = "复算自定义基础尺寸",
            Details =
                $"{geometry.BaseLengthM:F1}m×{geometry.BaseWidthM:F1}m×" +
                $"{geometry.BaseThicknessM:F1}m，" +
                (scheme.IsFeasible ? $"{scheme.VerificationConclusion}。" : "存在不满足项。")
        });
        project.ModifiedAt = DateTimeOffset.Now;
        return scheme;
    }

    public IReadOnlyList<FoundationAdjustmentAdvice> GetAdjustmentAdvice(
        ProjectModel project,
        FoundationScheme evaluatedScheme)
    {
        return _adjustmentAdvisor.Analyze(
            evaluatedScheme,
            project.FoundationLoad,
            project.Geotechnical,
            project.FoundationSettings);
    }

    public void AddAndSelectCustomScheme(ProjectModel project, FoundationScheme customScheme)
    {
        customScheme.Id = Guid.NewGuid();
        customScheme.Name = "自定义方案";
        customScheme.Description = "由用户调整尺寸并复算确认的方案。";
        project.Schemes.RemoveAll(item => item.Name == "自定义方案");
        project.Schemes.Add(customScheme);
        SelectScheme(project, customScheme.Id);
    }

    public void SelectScheme(ProjectModel project, Guid schemeId)
    {
        var scheme = project.Schemes.SingleOrDefault(item => item.Id == schemeId) ??
                     throw new ArgumentException("没有找到指定方案。", nameof(schemeId));

        project.SelectedSchemeId = scheme.Id;
        project.Stage = scheme.IsFormalVerificationComplete
            ? ProjectStage.Verified
            : ProjectStage.SchemeSelected;
        project.ModifiedAt = DateTimeOffset.Now;
        project.AuditTrail.Add(new AuditRecord
        {
            Action = "选择设计方案",
            Details = $"{scheme.Name}：{scheme.GeometrySummary}；{scheme.VerificationConclusion}。"
        });
    }

    private FoundationLoad CalculateFoundationLoad(ProjectModel project, bool recordAudit)
    {
        FoundationLoad load;
        string auditAction;

        switch (project.ProjectType)
        {
            case ProjectType.MonitoringPole:
            {
                var result = _poleLoadCalculator.Calculate(
                    project.MonitoringPole,
                    project.FoundationSettings);
                load = result.FoundationLoad;
                auditAction = "根据监控杆几何参数计算基础端荷载";
                break;
            }
            case ProjectType.CommunicationTower:
                load = BuildTowerFoundationLoad(
                    project.TowerMast,
                    project.FoundationSettings.FoundationType);
                auditAction = project.TowerMast.LoadSourceType == TowerLoadSourceType.EnterpriseCatalog
                    ? "读取企业塔型基础端荷载"
                    : "采用手工确认的塔桅基础端荷载";
                break;
            default:
                throw new InvalidOperationException("尚未选择项目类型。");
        }

        load = _loadCombinationEngine.Apply(
            load,
            project.FoundationSettings.LoadCombinations,
            load.FoundationUnitCount,
            load.TieBeamsRequired);

        project.FoundationLoad = load;
        project.Stage = ProjectStage.LoadReady;
        project.ModifiedAt = DateTimeOffset.Now;

        if (recordAudit)
        {
            project.AuditTrail.Add(new AuditRecord
            {
                Action = auditAction,
                Details = load.UsesIndividualPileReactions
                    ? $"单塔腿包络：最大压力{load.IndividualPileCompressionKn:F2} kN，" +
                      $"最大上拔力{load.IndividualPileUpliftKn:F2} kN，" +
                      $"最大水平力{load.IndividualPileHorizontalKn:F2} kN；" +
                      PileLayoutRules.DescribeFoundationLayout(
                          project.TowerMast,
                          project.FoundationSettings.FoundationType)
                    : $"竖向力{load.VerticalKn:F2} kN，" +
                      $"合水平力{Math.Sqrt(Math.Pow(load.ShearXKn, 2) + Math.Pow(load.ShearYKn, 2)):F2} kN，" +
                      $"Mx={load.MomentXKnM:F2} kN·m，My={load.MomentYKnM:F2} kN·m。"
            });
        }

        return load;
    }

    private static FoundationLoad BuildTowerFoundationLoad(
        TowerMastInput input,
        FoundationType foundationType)
    {
        var useIndividualReactions =
            input.UsesIndividualPileReactions &&
            PileLayoutRules.RequiresSingleLegReactions(
                input,
                foundationType);
        FoundationLoadCombination? basicCombination = null;
        var hasBasicOverall = new[]
        {
            input.BasicVerticalKn,
            input.BasicShearXKn,
            input.BasicShearYKn,
            input.BasicMomentXKnM,
            input.BasicMomentYKnM,
            input.BasicTorsionKnM
        }.Any(value => Math.Abs(value) > 1e-9);
        var hasBasicSingleLeg =
            Math.Abs(input.BasicIndividualPileCompressionKn) > 1e-9 ||
            Math.Abs(input.BasicIndividualPileUpliftKn) > 1e-9 ||
            Math.Abs(input.BasicIndividualPileHorizontalKn) > 1e-9;
        if (hasBasicOverall || hasBasicSingleLeg)
        {
            basicCombination = new FoundationLoadCombination
            {
                Kind = LoadCombinationKind.Basic,
                VerticalKn = useIndividualReactions
                    ? input.BasicIndividualPileCompressionKn
                    : input.BasicVerticalKn,
                ShearXKn = useIndividualReactions
                    ? input.BasicIndividualPileHorizontalKn
                    : input.BasicShearXKn,
                ShearYKn = useIndividualReactions ? 0 : input.BasicShearYKn,
                MomentXKnM = useIndividualReactions ? 0 : input.BasicMomentXKnM,
                MomentYKnM = useIndividualReactions ? 0 : input.BasicMomentYKnM,
                TorsionKnM = useIndividualReactions ? 0 : input.BasicTorsionKnM,
                UsesIndividualPileReactions = useIndividualReactions,
                IndividualPileCompressionKn = input.BasicIndividualPileCompressionKn,
                IndividualPileUpliftKn = input.BasicIndividualPileUpliftKn,
                IndividualPileHorizontalKn = input.BasicIndividualPileHorizontalKn,
                GoverningCase = string.IsNullOrWhiteSpace(input.BasicLoadCaseName)
                    ? "塔桅基础端承载能力极限状态基本组合"
                    : input.BasicLoadCaseName.Trim(),
                Expression = "来源图集或厂家文件明确给出的基本组合结果",
                SourceDocument = input.CatalogSourceTitle,
                IsConfirmed = input.IsConfirmed
            };
        }

        return new FoundationLoad
        {
            VerticalKn = useIndividualReactions
                ? input.IndividualPileCompressionKn
                : input.VerticalKn,
            ShearXKn = useIndividualReactions
                ? input.IndividualPileHorizontalKn
                : input.ShearXKn,
            ShearYKn = useIndividualReactions ? 0 : input.ShearYKn,
            MomentXKnM = useIndividualReactions ? 0 : input.MomentXKnM,
            MomentYKnM = useIndividualReactions ? 0 : input.MomentYKnM,
            TorsionKnM = useIndividualReactions ? 0 : input.TorsionKnM,
            UsesIndividualPileReactions = useIndividualReactions,
            IndividualPileCompressionKn = input.IndividualPileCompressionKn,
            IndividualPileUpliftKn = input.IndividualPileUpliftKn,
            IndividualPileHorizontalKn = input.IndividualPileHorizontalKn,
            FoundationUnitCount = PileLayoutRules.GetFoundationUnitCount(
                input,
                foundationType),
            TieBeamsRequired = PileLayoutRules.RequiresTieBeams(
                input,
                foundationType),
            GoverningCase = string.IsNullOrWhiteSpace(input.LoadCaseName)
                ? "塔桅基础端控制荷载"
                : input.LoadCaseName.Trim(),
            BasicCombination = basicCombination,
            ActiveStructuralCombination = basicCombination
        };
    }

    private static IReadOnlyList<ValidationIssue> ValidateTowerLoads(
        TowerMastInput input,
        FoundationType foundationType)
    {
        var issues = new List<ValidationIssue>();

        if (input.LoadSourceType == TowerLoadSourceType.EnterpriseCatalog)
        {
            if (string.IsNullOrWhiteSpace(input.CatalogRecordId) ||
                string.IsNullOrWhiteSpace(input.CatalogSourceTitle) ||
                input.CatalogPdfPage <= 0 ||
                input.CatalogTableRow <= 0)
            {
                issues.Add(new ValidationIssue
                {
                    Field = nameof(input.CatalogRecordId),
                    Message = "请先从企业标准塔型荷载库选择并采用一条可用记录。"
                });
            }
            else if (!TowerLoadCatalogAuthorityPolicy.IsCurrentStandard(
                         input.CatalogStandardNo))
            {
                issues.Add(new ValidationIssue
                {
                    Field = nameof(input.CatalogStandardNo),
                    Message =
                        "当前项目保存的是历史来源反力，不能直接生成新方案。请从当前企业塔型库重新选择，或手工录入已经确认的厂家反力。"
                });
            }

            var catalogHasBasic = PileLayoutRules.RequiresSingleLegReactions(
                                      input,
                                      foundationType)
                ? input.BasicIndividualPileCompressionKn > 0 &&
                  input.BasicIndividualPileUpliftKn > 0 &&
                  input.BasicIndividualPileHorizontalKn >= 0
                : input.BasicVerticalKn > 0 &&
                  (input.BasicShearXKn > 0 || input.BasicShearYKn > 0) &&
                  (input.BasicMomentXKnM > 0 || input.BasicMomentYKnM > 0);
            if (!catalogHasBasic)
            {
                issues.Add(new ValidationIssue
                {
                    Field = nameof(input.BasicVerticalKn),
                    Message =
                        "现行企业图集记录必须同时回填标准组合和基本组合，当前基本组合不完整，请重新采用塔型记录。"
                });
            }
        }

        if (string.IsNullOrWhiteSpace(input.TowerModel))
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.TowerModel),
                Message = "请填写塔型或塔桅名称。"
            });
        }

        if (input.HeightM <= 0)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.HeightM),
                Message = "塔高必须大于0。"
            });
        }

        var foundationUnitCount = PileLayoutRules.GetFoundationUnitCount(
            input,
            foundationType);
        var requiresIndividualReactions = foundationUnitCount > 1;
        if (requiresIndividualReactions &&
            (!input.UsesIndividualPileReactions ||
             input.IndividualPileCompressionKn <= 0 ||
             input.IndividualPileUpliftKn <= 0 ||
             input.IndividualPileHorizontalKn < 0))
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.IndividualPileCompressionKn),
                Message =
                    $"当前塔型采用{foundationUnitCount}个相互独立的基础单元，必须填写或从图集读取一个塔脚的最大压力、最大上拔力和水平力；不能由整塔反力平均分配。"
            });
        }

        if (!requiresIndividualReactions && input.VerticalKn <= 0)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.VerticalKn),
                Message = "塔桅基础端竖向压力必须大于0。"
            });
        }

        var loadValues = new[]
        {
            input.VerticalKn,
            input.ShearXKn,
            input.ShearYKn,
            input.MomentXKnM,
            input.MomentYKnM,
            input.TorsionKnM,
            input.BasicVerticalKn,
            input.BasicShearXKn,
            input.BasicShearYKn,
            input.BasicMomentXKnM,
            input.BasicMomentYKnM,
            input.BasicTorsionKnM,
            input.BasicIndividualPileCompressionKn,
            input.BasicIndividualPileUpliftKn,
            input.BasicIndividualPileHorizontalKn
        };
        if (requiresIndividualReactions)
        {
            loadValues =
            [
                .. loadValues,
                input.IndividualPileCompressionKn,
                input.IndividualPileUpliftKn,
                input.IndividualPileHorizontalKn
            ];
        }
        if (loadValues.Any(value => double.IsNaN(value) || double.IsInfinity(value)))
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(TowerMastInput),
                Message = "塔桅基础端荷载包含无效数值。"
            });
        }

        if (!input.IsConfirmed)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.IsConfirmed),
                Message = "请确认塔桅基础端荷载来源和数值。"
            });
        }

        return issues;
    }
}
