using TowerFoundation.Domain;

namespace TowerFoundation.Calculation;

internal sealed record PileStructuralVerificationResult(
    IReadOnlyList<FoundationCheckResult> Checks,
    IReadOnlyList<ReinforcementDesignResult> ReinforcementDesigns,
    double AddedReinforcementKg);

/// <summary>
/// 独立灌注桩桩身与连梁的确定性验算。水平响应采用JGJ 94 m法的
/// 线弹性地基梁离散模型，土弹簧k(z)=m·b0·z；计算结果必须与用户
/// 确认的m值、结构基本组合和变形限值一起使用。
/// </summary>
internal static class PileStructuralVerificationCalculator
{
    private const double Pi = Math.PI;

    public static PileStructuralVerificationResult Calculate(
        FoundationGeometry geometry,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        var pile = settings.Pile;
        var checks = new List<FoundationCheckResult>();
        var reinforcement = new List<ReinforcementDesignResult>();
        var structuralLoad = appliedLoad.ResolveStructuralDesignLoad(settings);

        checks.Add(new FoundationCheckResult
        {
            Code = "PILE_STRUCTURAL_COMBINATION",
            Name = "桩身结构基本组合",
            Status = appliedLoad.HasExplicitStructuralCombination
                ? CheckStatus.Pass
                : CheckStatus.PendingInput,
            Demand = appliedLoad.HasExplicitStructuralCombination ? 1 : settings.StructuralDesignLoadFactor,
            Capacity = appliedLoad.HasExplicitStructuralCombination ? 1 : 0,
            Utilization = 0,
            Unit = "组合",
            GoverningCase = structuralLoad.GoverningCase,
            Explanation = appliedLoad.DescribeStructuralCombination(settings),
            RuleReference = "GB 50007-2011第3.0.5条第4款；JGJ 94-2008第3.1.7条"
        });

        var ready = pile.IsConfirmed &&
                    pile.HorizontalResistanceCoefficientMnPerM4 > 0 &&
                    pile.ConcreteElasticModulusMpa > 0 &&
                    pile.ConcreteCompressiveStrengthMpa > 0 &&
                    pile.PileMainBarDiameterMm > 0 &&
                    pile.PileMainBarCount >= 6 &&
                    pile.StirrupDiameterMm > 0 &&
                    pile.StirrupSpacingMm > 0 &&
                    pile.StirrupLegCount >= 2;
        if (!ready)
        {
            checks.Add(Pending(
                "PILE_M_METHOD_INPUT",
                "灌注桩m法与桩身结构参数",
                "请确认地基土水平抗力比例系数m、桩身混凝土弹性模量/抗压强度、纵筋及箍筋。未确认前只保留竖向承载力结果。",
                structuralLoad.GoverningCase,
                "JGJ 94-2008第5.7节、第5.8节"));
            AddTieBeamChecks(
                geometry,
                structuralLoad,
                geotechnical,
                settings,
                checks,
                reinforcement);
            return BuildResult(checks, reinforcement);
        }

        var horizontal = pile.UseUserConfirmedPileHeadStructuralForces
            ? Math.Abs(pile.MaximumPileHeadHorizontalKn)
            : structuralLoad.UsesIndividualPileReactions
                ? Math.Abs(structuralLoad.IndividualPileHorizontalKn)
                : Math.Sqrt(
                    structuralLoad.ShearXKn * structuralLoad.ShearXKn +
                    structuralLoad.ShearYKn * structuralLoad.ShearYKn);
        var pileHeadMoment = pile.UseUserConfirmedPileHeadStructuralForces
            ? Math.Abs(pile.MaximumPileHeadMomentKnM)
            : structuralLoad.UsesIndividualPileReactions
                ? 0
                : Math.Sqrt(
                    structuralLoad.MomentXKnM * structuralLoad.MomentXKnM +
                    structuralLoad.MomentYKnM * structuralLoad.MomentYKnM);
        var groundMoment = pileHeadMoment + horizontal * geometry.PedestalHeightM;
        var analysis = AnalyzeBeamOnLinearSoil(
            geometry.PileDiameterM,
            geometry.PileLengthM,
            horizontal,
            groundMoment,
            pile,
            settings);

        checks.Add(new FoundationCheckResult
        {
            Code = "PILE_M_METHOD_CLASSIFICATION",
            Name = "灌注桩m法换算深度",
            Status = CheckStatus.Result,
            Demand = analysis.AlphaH,
            Capacity = analysis.AlphaH,
            Unit = string.Empty,
            GoverningCase = structuralLoad.GoverningCase,
            Explanation =
                $"m={pile.HorizontalResistanceCoefficientMnPerM4:F2} MN/m⁴，b0={analysis.EffectiveWidthM:F3} m，EI={analysis.FlexuralRigidityKnM2:F0} kN·m²，αh={analysis.AlphaH:F3}；采用有限长线弹性地基梁离散求解，不套用刚性短柱桩αh≤2.5公式。",
            RuleReference = "JGJ 94-2008式(5.7.5)、第5.7.2条及附录C的m法力学模型"
        });
        checks.Add(new FoundationCheckResult
        {
            Code = "PILE_TOP_DISPLACEMENT",
            Name = "灌注桩桩顶水平位移",
            Status = CheckStatus.Result,
            Demand = analysis.TopDisplacementM,
            Capacity = analysis.TopDisplacementM,
            Unit = "m",
            GoverningCase = structuralLoad.GoverningCase,
            Explanation =
                $"地面处H={horizontal:F2} kN、M={groundMoment:F2} kN·m，m法离散求解δ={analysis.TopDisplacementM * 1000:F2} mm；须与塔型/连接允许值比较。",
            RuleReference = "JGJ 94-2008第5.7节；线弹性地基梁k(z)=m·b0·z"
        });
        checks.Add(new FoundationCheckResult
        {
            Code = "PILE_TOP_ROTATION",
            Name = "灌注桩桩顶转角",
            Status = CheckStatus.Result,
            Demand = analysis.TopRotationRad,
            Capacity = analysis.TopRotationRad,
            Unit = "rad",
            GoverningCase = structuralLoad.GoverningCase,
            Explanation = $"m法离散求解桩顶转角θ={analysis.TopRotationRad:F6} rad；须与塔型/连接允许值比较。",
            RuleReference = "JGJ 94-2008第5.7节；线弹性地基梁k(z)=m·b0·z"
        });
        checks.Add(new FoundationCheckResult
        {
            Code = "PILE_INTERNAL_FORCE_ENVELOPE",
            Name = "灌注桩桩身内力包络",
            Status = CheckStatus.Result,
            Demand = analysis.MaximumMomentKnM,
            Capacity = analysis.MaximumMomentKnM,
            Unit = "kN·m",
            GoverningCase = structuralLoad.GoverningCase,
            Explanation =
                $"最大弯矩|M|max={analysis.MaximumMomentKnM:F2} kN·m（深度{analysis.MaximumMomentDepthM:F2} m），最大剪力|V|max={analysis.MaximumShearKn:F2} kN；离散单元数{analysis.ElementCount}。",
            RuleReference = "JGJ 94-2008第5.7节、第5.8.10条；m法内力包络"
        });

        var pileAreaMm2 = Pi * Math.Pow(geometry.PileDiameterM * 1000, 2) / 4;
        var actualMainBarAreaMm2 =
            pile.PileMainBarCount * Pi * pile.PileMainBarDiameterMm * pile.PileMainBarDiameterMm / 4;
        var minimumAreaMm2 = pile.MinimumLongitudinalReinforcementRatio * pileAreaMm2;
        var h0Mm = Math.Max(
            1,
            geometry.PileDiameterM * 1000 -
            settings.ConcreteCoverMm - pile.PileMainBarDiameterMm / 2);
        var bendingAreaMm2 =
            2 * analysis.MaximumMomentKnM * 1_000_000 /
            (0.9 * settings.ReinforcementYieldStrengthMpa * h0Mm);
        var compressionDemand = structuralLoad.UsesIndividualPileReactions
            ? Math.Max(0, structuralLoad.IndividualPileCompressionKn)
            : Math.Max(0, structuralLoad.VerticalKn);
        var upliftDemand = structuralLoad.UsesIndividualPileReactions
            ? Math.Max(0, structuralLoad.IndividualPileUpliftKn)
            : Math.Max(0, -structuralLoad.VerticalKn);
        var tensionAreaMm2 =
            upliftDemand * 1000 / settings.ReinforcementYieldStrengthMpa;
        var requiredMainBarAreaMm2 = Math.Max(
            minimumAreaMm2,
            Math.Max(bendingAreaMm2, tensionAreaMm2 + bendingAreaMm2));
        var compressionCapacityKn =
            0.85 *
            (pile.ConcreteCompressiveStrengthMpa * pileAreaMm2 +
             settings.ReinforcementYieldStrengthMpa * actualMainBarAreaMm2) / 1000;
        var tensionCapacityKn =
            settings.ReinforcementYieldStrengthMpa * actualMainBarAreaMm2 / 1000;
        var momentCapacityKnM =
            0.9 * settings.ReinforcementYieldStrengthMpa *
            Math.Max(1, actualMainBarAreaMm2 / 2) * h0Mm / 1_000_000;
        var compressionInteraction =
            SafeRatio(compressionDemand, compressionCapacityKn) +
            SafeRatio(analysis.MaximumMomentKnM, momentCapacityKnM);
        var tensionInteraction =
            SafeRatio(upliftDemand, tensionCapacityKn) +
            SafeRatio(analysis.MaximumMomentKnM, momentCapacityKnM);
        var interaction = Math.Max(compressionInteraction, tensionInteraction);

        AddVerification(
            checks,
            "PILE_AXIAL_BENDING_INTERACTION",
            "灌注桩轴力－弯矩组合",
            interaction,
            1,
            "无量纲",
            $"抗压包络N/N0+M/M0={compressionInteraction:F3}，抗拔包络T/T0+M/M0={tensionInteraction:F3}；采用较不利值。桩身工艺系数ψc=0.85。",
            structuralLoad.GoverningCase,
            "JGJ 94-2008第5.8.2～5.8.8条；GB/T 50010-2010（2024年版）；保守线性包络");
        AddVerification(
            checks,
            "PILE_STRUCTURAL_LONGITUDINAL_REINFORCEMENT",
            "灌注桩桩身纵筋",
            requiredMainBarAreaMm2,
            actualMainBarAreaMm2,
            "mm²",
            $"最小配筋需{minimumAreaMm2:F0} mm²，弯矩包络需{bendingAreaMm2:F0} mm²，上拔与弯矩组合需{tensionAreaMm2 + bendingAreaMm2:F0} mm²；实配{pile.PileMainBarCount}Φ{pile.PileMainBarDiameterMm:F0}为{actualMainBarAreaMm2:F0} mm²。",
            structuralLoad.GoverningCase,
            "JGJ 94-2008第4.1节、第5.8节；GB/T 50010-2010（2024年版）");

        var radiusMm = geometry.PileDiameterM * 500;
        var equivalentWidthMm = 1.76 * radiusMm;
        var equivalentDepthMm = 1.60 * radiusMm;
        var grossShearCapacityKn =
            0.25 * pile.ConcreteCompressiveStrengthMpa *
            equivalentWidthMm * equivalentDepthMm / 1000;
        var concreteShearCapacityKn =
            0.7 * settings.ConcreteTensileStrengthMpa *
            equivalentWidthMm * equivalentDepthMm / 1000;
        var requiredAsvPerSMm2PerM = Math.Max(
            0,
            (analysis.MaximumShearKn - concreteShearCapacityKn) * 1_000_000 /
            (settings.ReinforcementYieldStrengthMpa * equivalentDepthMm));
        var providedAsvPerSMm2PerM =
            pile.StirrupLegCount * Pi * pile.StirrupDiameterMm * pile.StirrupDiameterMm / 4 /
            pile.StirrupSpacingMm * 1000;
        AddVerification(
            checks,
            "PILE_GROSS_SHEAR",
            "灌注桩斜截面受剪上限",
            analysis.MaximumShearKn,
            grossShearCapacityKn,
            "kN",
            $"换算矩形截面b={equivalentWidthMm:F0} mm、h0={equivalentDepthMm:F0} mm，0.25βc·fc·b·h0={grossShearCapacityKn:F2} kN。",
            structuralLoad.GoverningCase,
            "GB/T 50010-2010（2024年版）第6.3节；圆形截面等效换算");
        AddVerification(
            checks,
            "PILE_STIRRUP_REINFORCEMENT",
            "灌注桩桩身箍筋",
            requiredAsvPerSMm2PerM,
            providedAsvPerSMm2PerM,
            "mm²/m",
            requiredAsvPerSMm2PerM <= 1e-9
                ? $"混凝土受剪项已满足，按构造采用{pile.StirrupLegCount}肢Φ{pile.StirrupDiameterMm:F0}@{pile.StirrupSpacingMm:F0}。"
                : $"需Asv/s={requiredAsvPerSMm2PerM:F0} mm²/m，实配{providedAsvPerSMm2PerM:F0} mm²/m。",
            structuralLoad.GoverningCase,
            "GB/T 50010-2010（2024年版）第6.3节、第8.4节");

        AddPileCrackCheck(
            checks,
            geometry,
            appliedLoad,
            settings,
            analysis,
            actualMainBarAreaMm2);

        var totalPileLength = geometry.PileLengthM + Math.Max(0, geometry.PedestalHeightM);
        var mainUnitWeight = pile.PileMainBarDiameterMm * pile.PileMainBarDiameterMm / 162;
        var mainTotalLength = geometry.PileCount * pile.PileMainBarCount * totalPileLength;
        var mainDesign = new ReinforcementDesignResult
        {
            Component = "独立灌注桩桩身纵筋（结构设计）",
            Direction = $"{geometry.PileCount}根桩分别圆周均布",
            BarSpecification = $"每桩{pile.PileMainBarCount}Φ{pile.PileMainBarDiameterMm:F0}",
            RequiredAreaMm2 = requiredMainBarAreaMm2,
            ProvidedAreaMm2 = actualMainBarAreaMm2,
            BarCount = geometry.PileCount * pile.PileMainBarCount,
            BarDiameterMm = pile.PileMainBarDiameterMm,
            SingleBarLengthM = totalPileLength,
            TotalLengthM = mainTotalLength,
            UnitWeightKgPerM = mainUnitWeight,
            CalculatedWeightKg = mainTotalLength * mainUnitWeight,
            Status = actualMainBarAreaMm2 + 1e-9 >= requiredMainBarAreaMm2
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            RuleReference = "JGJ 94-2008第5.8节；GB/T 50010-2010（2024年版）"
        };
        var hoopCenterDiameterM = Math.Max(
            0,
            geometry.PileDiameterM -
            2 * settings.ConcreteCoverMm / 1000 -
            pile.StirrupDiameterMm / 1000);
        var hoopCountPerPile =
            (int)Math.Floor(totalPileLength * 1000 / pile.StirrupSpacingMm) + 1;
        var hoopLengthM = Pi * hoopCenterDiameterM;
        var hoopTotalLength = geometry.PileCount * hoopCountPerPile * hoopLengthM;
        var hoopUnitWeight = pile.StirrupDiameterMm * pile.StirrupDiameterMm / 162;
        var hoopDesign = new ReinforcementDesignResult
        {
            Component = "独立灌注桩桩身箍筋",
            Direction = $"{geometry.PileCount}根桩分别环向",
            BarSpecification = $"Φ{pile.StirrupDiameterMm:F0}@{pile.StirrupSpacingMm:F0}",
            RequiredAreaMm2 = requiredAsvPerSMm2PerM,
            ProvidedAreaMm2 = providedAsvPerSMm2PerM,
            BarCount = geometry.PileCount * hoopCountPerPile,
            BarDiameterMm = pile.StirrupDiameterMm,
            BarSpacingMm = pile.StirrupSpacingMm,
            SingleBarLengthM = hoopLengthM,
            TotalLengthM = hoopTotalLength,
            UnitWeightKgPerM = hoopUnitWeight,
            CalculatedWeightKg = hoopTotalLength * hoopUnitWeight,
            Status = providedAsvPerSMm2PerM + 1e-9 >= requiredAsvPerSMm2PerM
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            RuleReference = "GB/T 50010-2010（2024年版）第6.3节、第8.4节"
        };
        reinforcement.Add(mainDesign);
        reinforcement.Add(hoopDesign);

        AddTieBeamChecks(
            geometry,
            structuralLoad,
            geotechnical,
            settings,
            checks,
            reinforcement);
        return BuildResult(checks, reinforcement);
    }

    private static void AddPileCrackCheck(
        ICollection<FoundationCheckResult> checks,
        FoundationGeometry geometry,
        FoundationLoad appliedLoad,
        FoundationDesignSettings settings,
        BeamAnalysisResult structuralAnalysis,
        double actualMainBarAreaMm2)
    {
        var input = settings.SpecialtyDesign.Crack;
        if (!input.Source.IsConfirmed ||
            input.MaximumCrackWidthMm <= 0 ||
            input.ConcreteTensileStrengthStandardMpa <= 0 ||
            input.ReinforcementElasticModulusMpa <= 0)
        {
            checks.Add(Pending(
                "PILE_CRACK_WIDTH",
                "灌注桩桩身裂缝宽度",
                "请确认环境类别、最大裂缝宽度限值、ftk及钢筋弹性模量；软件将按标准组合m法弯矩包络计算。",
                appliedLoad.GoverningCase,
                "JGJ 94-2008第5.8.8条；GB/T 50010-2010（2024年版）裂缝控制"));
            return;
        }

        var serviceHorizontal = appliedLoad.UsesIndividualPileReactions
            ? Math.Abs(appliedLoad.IndividualPileHorizontalKn)
            : Math.Sqrt(
                appliedLoad.ShearXKn * appliedLoad.ShearXKn +
                appliedLoad.ShearYKn * appliedLoad.ShearYKn);
        var serviceMoment = appliedLoad.UsesIndividualPileReactions
            ? 0
            : Math.Sqrt(
                appliedLoad.MomentXKnM * appliedLoad.MomentXKnM +
                appliedLoad.MomentYKnM * appliedLoad.MomentYKnM);
        serviceMoment += serviceHorizontal * geometry.PedestalHeightM;
        var serviceAnalysis = AnalyzeBeamOnLinearSoil(
            geometry.PileDiameterM,
            geometry.PileLengthM,
            serviceHorizontal,
            serviceMoment,
            settings.Pile,
            settings);
        var h0Mm = Math.Max(
            1,
            geometry.PileDiameterM * 1000 -
            settings.ConcreteCoverMm - settings.Pile.PileMainBarDiameterMm / 2);
        var tensionSteelArea = Math.Max(1, actualMainBarAreaMm2 / 2);
        var steelStress = serviceAnalysis.MaximumMomentKnM * 1_000_000 /
                          (0.87 * h0Mm * tensionSteelArea);
        var effectiveTensionArea = 0.5 * Pi * Math.Pow(geometry.PileDiameterM * 500, 2);
        var rho = Math.Max(0.01, tensionSteelArea / effectiveTensionArea);
        var psi = steelStress <= 1e-9
            ? 0.2
            : Math.Clamp(
                1.1 - 0.65 * input.ConcreteTensileStrengthStandardMpa /
                (rho * steelStress),
                0.2,
                1.0);
        var equivalentBarSpacing =
            Pi * Math.Max(1, geometry.PileDiameterM * 1000 - 2 * settings.ConcreteCoverMm) /
            settings.Pile.PileMainBarCount;
        var crackWidth =
            1.9 * psi * steelStress / input.ReinforcementElasticModulusMpa *
            (1.9 * settings.ConcreteCoverMm +
             0.08 * equivalentBarSpacing / rho);
        AddVerification(
            checks,
            "PILE_CRACK_WIDTH",
            "灌注桩桩身裂缝宽度",
            crackWidth,
            input.MaximumCrackWidthMm,
            "mm",
            $"标准组合m法最大弯矩Msk={serviceAnalysis.MaximumMomentKnM:F2} kN·m，钢筋应力σs={steelStress:F2} MPa，圆形截面按等效受拉面积和周向间距计算wmax={crackWidth:F3} mm；限值{input.MaximumCrackWidthMm:F2} mm。",
            appliedLoad.GoverningCase,
            "JGJ 94-2008第5.8.8条；GB/T 50010-2010（2024年版）裂缝宽度公式" );
    }

    private static void AddTieBeamChecks(
        FoundationGeometry geometry,
        FoundationLoad structuralLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings,
        ICollection<FoundationCheckResult> checks,
        ICollection<ReinforcementDesignResult> reinforcement)
    {
        if (geometry.PileCount <= 1 || geometry.TieBeamCount <= 0)
        {
            return;
        }

        var pile = settings.Pile;
        if (!pile.UseUserConfirmedTieBeamForces)
        {
            checks.Add(Pending(
                "TIE_BEAM_FORCE_INPUT",
                "连梁控制内力",
                "三桩/四桩体系的连梁不按整塔反力平均分配。请填写经整体受力分析确认的连梁轴向拉力、弯矩和剪力，软件再自动完成配筋与工程量。",
                structuralLoad.GoverningCase,
                "JGJ 94-2008第4.2节；无承台独立桩连梁整体受力门禁"));
            return;
        }

        var widthMm = geometry.TieBeamWidthM * 1000;
        var depthMm = geometry.TieBeamHeightM * 1000;
        var h0Mm = Math.Max(
            1,
            depthMm - settings.ConcreteCoverMm - pile.TieBeamMainBarDiameterMm / 2);
        var fy = settings.ReinforcementYieldStrengthMpa;
        var minimumTotalArea =
            settings.MinimumReinforcementRatio * widthMm * depthMm;
        var axialArea = pile.TieBeamAxialTensionKn * 1000 / fy;
        var flexuralAreaPerFace =
            pile.TieBeamMomentKnM * 1_000_000 / (0.9 * fy * h0Mm);
        var requiredTotalArea = Math.Max(
            minimumTotalArea,
            axialArea + 2 * flexuralAreaPerFace);
        var actualTotalArea =
            pile.TieBeamMainBarCount * Pi * pile.TieBeamMainBarDiameterMm *
            pile.TieBeamMainBarDiameterMm / 4;
        var grossShearCapacity =
            0.25 * pile.ConcreteCompressiveStrengthMpa * widthMm * h0Mm / 1000;
        var concreteShearCapacity =
            0.7 * settings.ConcreteTensileStrengthMpa * widthMm * h0Mm / 1000;
        var requiredAsvPerS = Math.Max(
            0,
            (pile.TieBeamShearKn - concreteShearCapacity) * 1_000_000 /
            (fy * h0Mm));
        var providedAsvPerS =
            pile.TieBeamStirrupLegCount * Pi * pile.TieBeamStirrupDiameterMm *
            pile.TieBeamStirrupDiameterMm / 4 /
            pile.TieBeamStirrupSpacingMm * 1000;
        AddVerification(
            checks,
            "TIE_BEAM_LONGITUDINAL_REINFORCEMENT",
            "无承台体系连梁纵筋",
            requiredTotalArea,
            actualTotalArea,
            "mm²",
            $"确认内力Nt={pile.TieBeamAxialTensionKn:F2} kN、M={pile.TieBeamMomentKnM:F2} kN·m；轴向拉力与双面受弯合计需As={requiredTotalArea:F0} mm²，实配{pile.TieBeamMainBarCount}Φ{pile.TieBeamMainBarDiameterMm:F0}为{actualTotalArea:F0} mm²。",
            structuralLoad.GoverningCase,
            "GB/T 50010-2010（2024年版）受拉及受弯构件；用户确认的连梁控制内力" );
        AddVerification(
            checks,
            "TIE_BEAM_MAIN_BAR_COUNT_DETAILING",
            "无承台体系连梁上下纵筋根数构造",
            4,
            pile.TieBeamMainBarCount,
            "根/梁",
            $"每根连梁上下纵筋合计不少于4根（上、下各不少于2根）；当前每梁{pile.TieBeamMainBarCount}根。",
            structuralLoad.GoverningCase,
            "GB 50007-2011第8.5.23条" );
        AddVerification(
            checks,
            "TIE_BEAM_MAIN_BAR_DIAMETER_DETAILING",
            "无承台体系连梁纵筋直径构造",
            12,
            pile.TieBeamMainBarDiameterMm,
            "mm",
            $"连梁纵向钢筋直径不应小于12mm；当前Φ{pile.TieBeamMainBarDiameterMm:F0}。",
            structuralLoad.GoverningCase,
            "GB 50007-2011第8.5.23条" );
        AddVerification(
            checks,
            "TIE_BEAM_GROSS_SHEAR",
            "无承台体系连梁受剪上限",
            pile.TieBeamShearKn,
            grossShearCapacity,
            "kN",
            $"确认剪力V={pile.TieBeamShearKn:F2} kN，受剪上限{grossShearCapacity:F2} kN。",
            structuralLoad.GoverningCase,
            "GB/T 50010-2010（2024年版）第6.3节" );
        AddVerification(
            checks,
            "TIE_BEAM_STIRRUP_REINFORCEMENT",
            "无承台体系连梁箍筋",
            requiredAsvPerS,
            providedAsvPerS,
            "mm²/m",
            $"需Asv/s={requiredAsvPerS:F0} mm²/m，实配{providedAsvPerS:F0} mm²/m。",
            structuralLoad.GoverningCase,
            "GB/T 50010-2010（2024年版）第6.3节、第8.4节" );

        var clearLength = Math.Max(
            0,
            geometry.PileCenterSpacingM - geometry.PileDiameterM);
        var mainUnitWeight =
            pile.TieBeamMainBarDiameterMm * pile.TieBeamMainBarDiameterMm / 162;
        var totalMainLength =
            geometry.TieBeamCount * pile.TieBeamMainBarCount * clearLength;
        reinforcement.Add(new ReinforcementDesignResult
        {
            Component = "独立桩连梁纵筋",
            Direction = $"{geometry.TieBeamCount}根连梁",
            BarSpecification = $"每梁{pile.TieBeamMainBarCount}Φ{pile.TieBeamMainBarDiameterMm:F0}",
            RequiredAreaMm2 = requiredTotalArea,
            ProvidedAreaMm2 = actualTotalArea,
            BarCount = geometry.TieBeamCount * pile.TieBeamMainBarCount,
            BarDiameterMm = pile.TieBeamMainBarDiameterMm,
            SingleBarLengthM = clearLength,
            TotalLengthM = totalMainLength,
            UnitWeightKgPerM = mainUnitWeight,
            CalculatedWeightKg = totalMainLength * mainUnitWeight,
            Status = actualTotalArea + 1e-9 >= requiredTotalArea &&
                     pile.TieBeamMainBarCount >= 4 &&
                     pile.TieBeamMainBarDiameterMm + 1e-9 >= 12
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            RuleReference = "GB/T 50010-2010（2024年版）受拉及受弯构件"
        });
        var tieBeamStirrupCutLength =
            RebarCutLengthCalculator.CalculateRectangularClosedStirrup(
                geometry.TieBeamWidthM,
                geometry.TieBeamHeightM,
                settings.ConcreteCoverMm,
                pile.TieBeamStirrupDiameterMm,
                RebarCutLengthCalculator.ShouldUseSeismicDetailing(
                    geotechnical.SeismicIntensityDegree),
                Math.Abs(structuralLoad.TorsionKnM) > 1e-9);
        var stirrupCountPerBeam =
            (int)Math.Floor(clearLength * 1000 / pile.TieBeamStirrupSpacingMm) + 1;
        var stirrupTotalLength =
            geometry.TieBeamCount * stirrupCountPerBeam *
            tieBeamStirrupCutLength.TotalCutLengthM;
        var stirrupUnitWeight =
            pile.TieBeamStirrupDiameterMm * pile.TieBeamStirrupDiameterMm / 162;
        reinforcement.Add(new ReinforcementDesignResult
        {
            Component = "独立桩连梁箍筋",
            Direction = $"{geometry.TieBeamCount}根连梁",
            BarSpecification = $"Φ{pile.TieBeamStirrupDiameterMm:F0}@{pile.TieBeamStirrupSpacingMm:F0}",
            RequiredAreaMm2 = requiredAsvPerS,
            ProvidedAreaMm2 = providedAsvPerS,
            BarCount = geometry.TieBeamCount * stirrupCountPerBeam,
            BarDiameterMm = pile.TieBeamStirrupDiameterMm,
            BarSpacingMm = pile.TieBeamStirrupSpacingMm,
            SingleBarLengthM = tieBeamStirrupCutLength.TotalCutLengthM,
            TotalLengthM = stirrupTotalLength,
            UnitWeightKgPerM = stirrupUnitWeight,
            CalculatedWeightKg = stirrupTotalLength * stirrupUnitWeight,
            StirrupBodyPerimeterM = tieBeamStirrupCutLength.BodyPerimeterM,
            HookBendAllowanceM = tieBeamStirrupCutLength.HookBendAllowanceM,
            HookStraightAllowanceM = tieBeamStirrupCutLength.HookStraightAllowanceM,
            CuttingLengthExplanation = tieBeamStirrupCutLength.FormulaDescription,
            Status = providedAsvPerS + 1e-9 >= requiredAsvPerS
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            RuleReference = "GB/T 50010-2010（2024年版）第6.3节、第9.3.2条、第11.1.8条；22G101-3第2-7页"
        });
    }

    private static BeamAnalysisResult AnalyzeBeamOnLinearSoil(
        double diameterM,
        double lengthM,
        double horizontalKn,
        double momentKnM,
        PileFoundationSettings pile,
        FoundationDesignSettings settings)
    {
        var elementCount = Math.Clamp((int)Math.Ceiling(lengthM / 0.15), 40, 160);
        var elementLength = lengthM / elementCount;
        var dofCount = 2 * (elementCount + 1);
        var matrix = new double[dofCount, dofCount];
        var force = new double[dofCount];
        var grossInertia = Pi * Math.Pow(diameterM, 4) / 64;
        var steelAreaEach = Pi * Math.Pow(pile.PileMainBarDiameterMm / 1000, 2) / 4;
        var steelRadius = Math.Max(
            0,
            diameterM / 2 - settings.ConcreteCoverMm / 1000 -
            pile.PileMainBarDiameterMm / 2000);
        var transformedSteelInertia =
            (200_000 / pile.ConcreteElasticModulusMpa - 1) *
            pile.PileMainBarCount * steelAreaEach * steelRadius * steelRadius / 2;
        var transformedInertia = Math.Max(grossInertia, grossInertia + transformedSteelInertia);
        var flexuralRigidity =
            0.85 * pile.ConcreteElasticModulusMpa * 1000 * transformedInertia;
        var effectiveWidth = diameterM <= 1
            ? 0.9 * (1.5 * diameterM + 0.5)
            : 0.9 * (diameterM + 1);
        var mKnPerM4 = pile.HorizontalResistanceCoefficientMnPerM4 * 1000;
        var alpha = Math.Pow(mKnPerM4 * effectiveWidth / flexuralRigidity, 0.2);
        var elementStiffness = BeamElementStiffness(flexuralRigidity, elementLength);
        for (var element = 0; element < elementCount; element++)
        {
            var map = new[] { 2 * element, 2 * element + 1, 2 * element + 2, 2 * element + 3 };
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    matrix[map[row], map[column]] += elementStiffness[row, column];
                }
            }
        }

        for (var node = 0; node <= elementCount; node++)
        {
            var depth = node * elementLength;
            var tributary = node == 0 || node == elementCount
                ? elementLength / 2
                : elementLength;
            var spring = mKnPerM4 * effectiveWidth * depth * tributary;
            matrix[2 * node, 2 * node] += spring;
        }

        force[0] = horizontalKn;
        force[1] = momentKnM;
        // Numerical regularization only; value is many orders below soil/beam terms.
        matrix[dofCount - 2, dofCount - 2] += 1e-9;
        matrix[dofCount - 1, dofCount - 1] += 1e-9;
        var displacement = SolveLinearSystem(matrix, force);
        var maximumMoment = 0d;
        var maximumShear = 0d;
        var maximumMomentDepth = 0d;
        for (var element = 0; element < elementCount; element++)
        {
            var local = new[]
            {
                displacement[2 * element],
                displacement[2 * element + 1],
                displacement[2 * element + 2],
                displacement[2 * element + 3]
            };
            var endForce = Multiply(elementStiffness, local);
            var elementMoment = Math.Max(Math.Abs(endForce[1]), Math.Abs(endForce[3]));
            if (elementMoment > maximumMoment)
            {
                maximumMoment = elementMoment;
                maximumMomentDepth = element * elementLength +
                                     (Math.Abs(endForce[3]) > Math.Abs(endForce[1])
                                         ? elementLength
                                         : 0);
            }
            maximumShear = Math.Max(
                maximumShear,
                Math.Max(Math.Abs(endForce[0]), Math.Abs(endForce[2])));
        }

        return new BeamAnalysisResult(
            Math.Abs(displacement[0]),
            Math.Abs(displacement[1]),
            maximumMoment,
            maximumShear,
            maximumMomentDepth,
            effectiveWidth,
            flexuralRigidity,
            alpha * lengthM,
            elementCount);
    }

    private static double[,] BeamElementStiffness(double ei, double length)
    {
        var l2 = length * length;
        var l3 = l2 * length;
        return new[,]
        {
            { 12 * ei / l3, 6 * ei / l2, -12 * ei / l3, 6 * ei / l2 },
            { 6 * ei / l2, 4 * ei / length, -6 * ei / l2, 2 * ei / length },
            { -12 * ei / l3, -6 * ei / l2, 12 * ei / l3, -6 * ei / l2 },
            { 6 * ei / l2, 2 * ei / length, -6 * ei / l2, 4 * ei / length }
        };
    }

    private static double[] SolveLinearSystem(double[,] source, double[] right)
    {
        var count = right.Length;
        var matrix = (double[,])source.Clone();
        var vector = (double[])right.Clone();
        for (var pivot = 0; pivot < count; pivot++)
        {
            var best = pivot;
            var bestValue = Math.Abs(matrix[pivot, pivot]);
            for (var row = pivot + 1; row < count; row++)
            {
                var candidate = Math.Abs(matrix[row, pivot]);
                if (candidate > bestValue)
                {
                    best = row;
                    bestValue = candidate;
                }
            }
            if (bestValue < 1e-14)
            {
                throw new InvalidOperationException("m法地基梁矩阵奇异，请检查m值、桩长和桩身刚度。" );
            }
            if (best != pivot)
            {
                for (var column = pivot; column < count; column++)
                {
                    (matrix[pivot, column], matrix[best, column]) =
                        (matrix[best, column], matrix[pivot, column]);
                }
                (vector[pivot], vector[best]) = (vector[best], vector[pivot]);
            }

            var diagonal = matrix[pivot, pivot];
            for (var column = pivot; column < count; column++)
            {
                matrix[pivot, column] /= diagonal;
            }
            vector[pivot] /= diagonal;
            for (var row = pivot + 1; row < count; row++)
            {
                var factor = matrix[row, pivot];
                if (Math.Abs(factor) < 1e-20)
                {
                    continue;
                }
                for (var column = pivot; column < count; column++)
                {
                    matrix[row, column] -= factor * matrix[pivot, column];
                }
                vector[row] -= factor * vector[pivot];
            }
        }

        var result = new double[count];
        for (var row = count - 1; row >= 0; row--)
        {
            var value = vector[row];
            for (var column = row + 1; column < count; column++)
            {
                value -= matrix[row, column] * result[column];
            }
            result[row] = value;
        }
        return result;
    }

    private static double[] Multiply(double[,] matrix, double[] vector)
    {
        var result = new double[vector.Length];
        for (var row = 0; row < vector.Length; row++)
        {
            for (var column = 0; column < vector.Length; column++)
            {
                result[row] += matrix[row, column] * vector[column];
            }
        }
        return result;
    }

    private static PileStructuralVerificationResult BuildResult(
        IReadOnlyList<FoundationCheckResult> checks,
        IReadOnlyList<ReinforcementDesignResult> reinforcement) => new(
            checks,
            reinforcement,
            reinforcement.Sum(item => item.CalculatedWeightKg));

    private static void AddVerification(
        ICollection<FoundationCheckResult> checks,
        string code,
        string name,
        double demand,
        double capacity,
        string unit,
        string explanation,
        string governingCase,
        string ruleReference)
    {
        checks.Add(new FoundationCheckResult
        {
            Code = code,
            Name = name,
            Status = demand <= capacity ? CheckStatus.Pass : CheckStatus.Fail,
            Demand = demand,
            Capacity = capacity,
            Utilization = SafeRatio(demand, capacity),
            Unit = unit,
            Explanation = explanation,
            GoverningCase = governingCase,
            RuleReference = ruleReference
        });
    }

    private static FoundationCheckResult Pending(
        string code,
        string name,
        string explanation,
        string governingCase,
        string ruleReference) => new()
        {
            Code = code,
            Name = name,
            Status = CheckStatus.PendingInput,
            GoverningCase = governingCase,
            Explanation = explanation,
            RuleReference = ruleReference
        };

    private static double SafeRatio(double demand, double capacity) =>
        demand <= 1e-12
            ? 0
            : capacity > 1e-12
                ? demand / capacity
                : double.PositiveInfinity;

    private sealed record BeamAnalysisResult(
        double TopDisplacementM,
        double TopRotationRad,
        double MaximumMomentKnM,
        double MaximumShearKn,
        double MaximumMomentDepthM,
        double EffectiveWidthM,
        double FlexuralRigidityKnM2,
        double AlphaH,
        int ElementCount);
}
