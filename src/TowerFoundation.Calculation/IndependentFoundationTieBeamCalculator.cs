using TowerFoundation.Domain;

namespace TowerFoundation.Calculation;

/// <summary>
/// 为多塔柱的非共用基础补充闭合周边连系梁。连系梁内力不得由单塔腿
/// 反力平均或臆测，只有用户确认整体分析内力后才完成配筋验算。
/// </summary>
public static class IndependentFoundationTieBeamCalculator
{
    private const double MinimumWidthM = 0.25;
    private const double MinimumHeightM = 0.40;

    public static FoundationScheme Apply(
        FoundationScheme scheme,
        FoundationLoad structuralLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(structuralLoad);
        ArgumentNullException.ThrowIfNull(geotechnical);
        ArgumentNullException.ThrowIfNull(settings);

        if (scheme.FoundationType == FoundationType.Pile ||
            !structuralLoad.TieBeamsRequired ||
            scheme.Geometry.FoundationUnitCount <= 1)
        {
            return scheme;
        }

        var geometry = scheme.Geometry;
        var tieBeam = settings.Pile;
        var unitCount = geometry.FoundationUnitCount;
        geometry.TieBeamCount = unitCount;
        geometry.PileCenterSpacingM = tieBeam.PileCenterSpacingM;
        geometry.TieBeamWidthM = tieBeam.TieBeamWidthM;
        geometry.TieBeamHeightM = tieBeam.TieBeamHeightM;

        var requiredHeightM = Math.Max(
            MinimumHeightM,
            geometry.PileCenterSpacingM / 15.0);
        var topologyIsValid = unitCount is 3 or 4 &&
                              geometry.TieBeamCount == unitCount;
        var sectionIsValid = geometry.TieBeamWidthM + 1e-9 >= MinimumWidthM &&
                             geometry.TieBeamHeightM + 1e-9 >= requiredHeightM;
        scheme.Checks.Add(new FoundationCheckResult
        {
            Code = "TIE_BEAM_LAYOUT",
            Name = "多塔柱基础连系梁布置与构造尺寸",
            Status = topologyIsValid && sectionIsValid
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            Demand = requiredHeightM,
            Capacity = geometry.TieBeamHeightM,
            Utilization = geometry.TieBeamHeightM > 0
                ? requiredHeightM / geometry.TieBeamHeightM
                : double.PositiveInfinity,
            Unit = "m",
            GoverningCase = structuralLoad.GoverningCase,
            Explanation = unitCount == 3
                ? $"3个塔脚基础按三角形闭合，设置3根周边连系梁；采用b×h={geometry.TieBeamWidthM:F2}×{geometry.TieBeamHeightM:F2}m。"
                : $"4个塔脚基础按四角闭合，设置4根周边连系梁；采用b×h={geometry.TieBeamWidthM:F2}×{geometry.TieBeamHeightM:F2}m。",
            RuleReference = "项目多塔柱分离基础统一设置策略；GB/T 50011-2010（2024年版）第6.1.11条（框架单独柱基条件性参考）；GB 50007-2011第8.5.23条（桩承台连系梁构造下限类比采用）；22G101-3基础联系梁JLL构造"
        });

        var supportPlanDimensionM = SupportPlanDimension(scheme);
        var clearLengthM = geometry.PileCenterSpacingM - supportPlanDimensionM;
        scheme.Checks.Add(new FoundationCheckResult
        {
            Code = "TIE_BEAM_CLEAR_LENGTH",
            Name = "连系梁轴线距离与基础净距",
            Status = clearLengthM > 1e-9 ? CheckStatus.Pass : CheckStatus.Fail,
            Demand = supportPlanDimensionM,
            Capacity = geometry.PileCenterSpacingM,
            Utilization = geometry.PileCenterSpacingM > 0
                ? supportPlanDimensionM / geometry.PileCenterSpacingM
                : double.PositiveInfinity,
            Unit = "m",
            GoverningCase = structuralLoad.GoverningCase,
            Explanation = clearLengthM > 0
                ? $"基础中心距/连系梁轴线长{geometry.PileCenterSpacingM:F2}m，按连接方向基础控制尺寸{supportPlanDimensionM:F2}m，梁净长暂按{clearLengthM:F2}m计量。"
                : $"基础中心距{geometry.PileCenterSpacingM:F2}m不大于单个基础控制尺寸{supportPlanDimensionM:F2}m；独立基础已重叠，应修改塔脚中心距、基础尺寸或改为共用整体基础。",
            RuleReference = "基础平面几何相容性；塔脚根开或基础中心距须由塔型图纸确认"
        });

        if (clearLengthM > 0 && sectionIsValid && topologyIsValid)
        {
            var beamConcreteM3 = geometry.TieBeamCount * clearLengthM *
                                 geometry.TieBeamWidthM * geometry.TieBeamHeightM;
            scheme.Quantities = new QuantitySummary
            {
                ConcreteM3 = scheme.Quantities.ConcreteM3 + beamConcreteM3,
                ExcavationM3 = scheme.Quantities.ExcavationM3,
                BackfillM3 = scheme.Quantities.BackfillM3,
                EstimatedReinforcementKg = scheme.Quantities.EstimatedReinforcementKg
            };
        }

        if (!tieBeam.UseUserConfirmedTieBeamForces)
        {
            scheme.Checks.Add(new FoundationCheckResult
            {
                Code = "TIE_BEAM_FORCE_INPUT",
                Name = "连系梁控制内力",
                Status = CheckStatus.PendingInput,
                GoverningCase = structuralLoad.GoverningCase,
                Explanation = "连系梁内力不得由整塔反力或单塔腿反力平均分配。请填写经塔架-基础整体分析确认的连系梁轴向拉力、弯矩和剪力，再完成纵筋、受剪及箍筋验算。",
                RuleReference = "项目多塔柱基础统一规则；GB/T 50010-2010（2024年版）构件承载力设计"
            });
            return scheme;
        }

        AddStructuralChecksAndReinforcement(
            scheme,
            structuralLoad,
            geotechnical,
            settings,
            Math.Max(0, clearLengthM));
        return scheme;
    }

    private static void AddStructuralChecksAndReinforcement(
        FoundationScheme scheme,
        FoundationLoad structuralLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings,
        double clearLengthM)
    {
        var geometry = scheme.Geometry;
        var tieBeam = settings.Pile;
        var widthMm = geometry.TieBeamWidthM * 1000;
        var depthMm = geometry.TieBeamHeightM * 1000;
        var h0Mm = Math.Max(
            1,
            depthMm - settings.ConcreteCoverMm - tieBeam.TieBeamMainBarDiameterMm / 2);
        var fy = settings.ReinforcementYieldStrengthMpa;
        var minimumTotalArea = settings.MinimumReinforcementRatio * widthMm * depthMm;
        var axialArea = tieBeam.TieBeamAxialTensionKn * 1000 / fy;
        var flexuralAreaPerFace =
            tieBeam.TieBeamMomentKnM * 1_000_000 / (0.9 * fy * h0Mm);
        var requiredTotalArea = Math.Max(
            minimumTotalArea,
            axialArea + 2 * flexuralAreaPerFace);
        var actualTotalArea =
            tieBeam.TieBeamMainBarCount * Math.PI *
            tieBeam.TieBeamMainBarDiameterMm * tieBeam.TieBeamMainBarDiameterMm / 4;
        var grossShearCapacity =
            0.25 * tieBeam.ConcreteCompressiveStrengthMpa * widthMm * h0Mm / 1000;
        var concreteShearCapacity =
            0.7 * settings.ConcreteTensileStrengthMpa * widthMm * h0Mm / 1000;
        var requiredAsvPerS = Math.Max(
            0,
            (tieBeam.TieBeamShearKn - concreteShearCapacity) * 1_000_000 /
            (fy * h0Mm));
        var providedAsvPerS =
            tieBeam.TieBeamStirrupLegCount * Math.PI *
            tieBeam.TieBeamStirrupDiameterMm * tieBeam.TieBeamStirrupDiameterMm / 4 /
            tieBeam.TieBeamStirrupSpacingMm * 1000;

        scheme.Checks.Add(Verification(
            "TIE_BEAM_LONGITUDINAL_REINFORCEMENT",
            "多塔柱基础连系梁纵筋",
            requiredTotalArea,
            actualTotalArea,
            "mm²",
            $"确认内力Nt={tieBeam.TieBeamAxialTensionKn:F2}kN、M={tieBeam.TieBeamMomentKnM:F2}kN·m；轴向拉力与双面受弯合计需As={requiredTotalArea:F0}mm²，实配{tieBeam.TieBeamMainBarCount}Φ{tieBeam.TieBeamMainBarDiameterMm:F0}为{actualTotalArea:F0}mm²。",
            structuralLoad.GoverningCase,
            "GB/T 50010-2010（2024年版）受拉及受弯构件；用户确认的连系梁控制内力"));
        scheme.Checks.Add(Verification(
            "TIE_BEAM_MAIN_BAR_COUNT_DETAILING",
            "多塔柱基础连系梁上下纵筋根数构造",
            4,
            tieBeam.TieBeamMainBarCount,
            "根/梁",
            $"每根连系梁上下纵筋合计不少于4根（上、下各不少于2根）；当前每梁{tieBeam.TieBeamMainBarCount}根。",
            structuralLoad.GoverningCase,
            "GB 50007-2011第8.5.23条"));
        scheme.Checks.Add(Verification(
            "TIE_BEAM_MAIN_BAR_DIAMETER_DETAILING",
            "多塔柱基础连系梁纵筋直径构造",
            12,
            tieBeam.TieBeamMainBarDiameterMm,
            "mm",
            $"连系梁纵向钢筋直径不应小于12mm；当前Φ{tieBeam.TieBeamMainBarDiameterMm:F0}。",
            structuralLoad.GoverningCase,
            "GB 50007-2011第8.5.23条"));
        scheme.Checks.Add(Verification(
            "TIE_BEAM_GROSS_SHEAR",
            "多塔柱基础连系梁受剪上限",
            tieBeam.TieBeamShearKn,
            grossShearCapacity,
            "kN",
            $"确认剪力V={tieBeam.TieBeamShearKn:F2}kN，受剪上限{grossShearCapacity:F2}kN。",
            structuralLoad.GoverningCase,
            "GB/T 50010-2010（2024年版）第6.3节"));
        scheme.Checks.Add(Verification(
            "TIE_BEAM_STIRRUP_REINFORCEMENT",
            "多塔柱基础连系梁箍筋",
            requiredAsvPerS,
            providedAsvPerS,
            "mm²/m",
            $"需Asv/s={requiredAsvPerS:F0}mm²/m，实配{providedAsvPerS:F0}mm²/m。",
            structuralLoad.GoverningCase,
            "GB/T 50010-2010（2024年版）第6.3节、第8.4节"));

        if (clearLengthM <= 0)
        {
            return;
        }

        var mainUnitWeight = tieBeam.TieBeamMainBarDiameterMm *
                             tieBeam.TieBeamMainBarDiameterMm / 162;
        var totalMainLength = geometry.TieBeamCount *
                              tieBeam.TieBeamMainBarCount * clearLengthM;
        scheme.ReinforcementDesigns.Add(new ReinforcementDesignResult
        {
            Component = "多塔柱基础连系梁纵筋",
            Direction = $"{geometry.TieBeamCount}根闭合周边连系梁",
            BarSpecification = $"每梁{tieBeam.TieBeamMainBarCount}Φ{tieBeam.TieBeamMainBarDiameterMm:F0}",
            RequiredAreaMm2 = requiredTotalArea,
            ProvidedAreaMm2 = actualTotalArea,
            BarCount = geometry.TieBeamCount * tieBeam.TieBeamMainBarCount,
            BarDiameterMm = tieBeam.TieBeamMainBarDiameterMm,
            SingleBarLengthM = clearLengthM,
            TotalLengthM = totalMainLength,
            UnitWeightKgPerM = mainUnitWeight,
            CalculatedWeightKg = totalMainLength * mainUnitWeight,
            Status = actualTotalArea + 1e-9 >= requiredTotalArea &&
                     tieBeam.TieBeamMainBarCount >= 4 &&
                     tieBeam.TieBeamMainBarDiameterMm + 1e-9 >= 12
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            RuleReference = "GB/T 50010-2010（2024年版）受拉及受弯构件"
        });

        var stirrupCutLength = RebarCutLengthCalculator.CalculateRectangularClosedStirrup(
            geometry.TieBeamWidthM,
            geometry.TieBeamHeightM,
            settings.ConcreteCoverMm,
            tieBeam.TieBeamStirrupDiameterMm,
            RebarCutLengthCalculator.ShouldUseSeismicDetailing(
                geotechnical.SeismicIntensityDegree),
            Math.Abs(structuralLoad.TorsionKnM) > 1e-9);
        var stirrupCountPerBeam =
            (int)Math.Floor(clearLengthM * 1000 / tieBeam.TieBeamStirrupSpacingMm) + 1;
        var totalStirrupLength = geometry.TieBeamCount * stirrupCountPerBeam *
                                 stirrupCutLength.TotalCutLengthM;
        var stirrupUnitWeight = tieBeam.TieBeamStirrupDiameterMm *
                                tieBeam.TieBeamStirrupDiameterMm / 162;
        var stirrupWeight = totalStirrupLength * stirrupUnitWeight;
        scheme.ReinforcementDesigns.Add(new ReinforcementDesignResult
        {
            Component = "多塔柱基础连系梁箍筋",
            Direction = $"{geometry.TieBeamCount}根闭合周边连系梁",
            BarSpecification = $"Φ{tieBeam.TieBeamStirrupDiameterMm:F0}@{tieBeam.TieBeamStirrupSpacingMm:F0}",
            RequiredAreaMm2 = requiredAsvPerS,
            ProvidedAreaMm2 = providedAsvPerS,
            BarCount = geometry.TieBeamCount * stirrupCountPerBeam,
            BarDiameterMm = tieBeam.TieBeamStirrupDiameterMm,
            BarSpacingMm = tieBeam.TieBeamStirrupSpacingMm,
            SingleBarLengthM = stirrupCutLength.TotalCutLengthM,
            TotalLengthM = totalStirrupLength,
            UnitWeightKgPerM = stirrupUnitWeight,
            CalculatedWeightKg = stirrupWeight,
            StirrupBodyPerimeterM = stirrupCutLength.BodyPerimeterM,
            HookBendAllowanceM = stirrupCutLength.HookBendAllowanceM,
            HookStraightAllowanceM = stirrupCutLength.HookStraightAllowanceM,
            CuttingLengthExplanation = stirrupCutLength.FormulaDescription,
            Status = providedAsvPerS + 1e-9 >= requiredAsvPerS
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            RuleReference = "GB/T 50010-2010（2024年版）第6.3节、第9.3.2条、第11.1.8条；22G101-3第2-7页"
        });
        scheme.Quantities = new QuantitySummary
        {
            ConcreteM3 = scheme.Quantities.ConcreteM3,
            ExcavationM3 = scheme.Quantities.ExcavationM3,
            BackfillM3 = scheme.Quantities.BackfillM3,
            EstimatedReinforcementKg = scheme.Quantities.EstimatedReinforcementKg +
                                       totalMainLength * mainUnitWeight + stirrupWeight
        };
    }

    private static double SupportPlanDimension(FoundationScheme scheme) =>
        scheme.FoundationType switch
        {
            FoundationType.RigidShortPile => scheme.Geometry.PileDiameterM,
            _ => Math.Max(scheme.Geometry.BaseLengthM, scheme.Geometry.BaseWidthM)
        };

    private static FoundationCheckResult Verification(
        string code,
        string name,
        double demand,
        double capacity,
        string unit,
        string explanation,
        string governingCase,
        string reference) => new()
    {
        Code = code,
        Name = name,
        Status = capacity + 1e-9 >= demand ? CheckStatus.Pass : CheckStatus.Fail,
        Demand = demand,
        Capacity = capacity,
        Utilization = capacity > 0 ? demand / capacity : double.PositiveInfinity,
        Unit = unit,
        Explanation = explanation,
        GoverningCase = governingCase,
        RuleReference = reference
    };
}
