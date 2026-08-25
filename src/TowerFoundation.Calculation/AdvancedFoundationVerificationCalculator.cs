using TowerFoundation.Domain;

namespace TowerFoundation.Calculation;

/// <summary>
/// 第二轮确定性验算。只在输入和来源已经确认时形成通过/不通过结论；
/// 设计最高水位、特殊地基处理和地震作用等未知条件不会由软件静默猜测。
/// </summary>
internal static class AdvancedFoundationVerificationCalculator
{
    private const double Pi = Math.PI;

    public static FoundationScheme Apply(
        FoundationScheme scheme,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings,
        SpecialtyDesignInput specialty)
    {
        AddPedestalStructuralChecks(
            scheme,
            appliedLoad,
            geotechnical,
            settings,
            specialty.PedestalStructure);
        AddHighWaterAntiFlotationCheck(scheme, geotechnical, settings, specialty.Hydrogeology);
        ReplaceSpecialGroundAndSeismicChecks(scheme, appliedLoad, geotechnical, specialty.SpecialGround);
        return scheme;
    }

    private static void AddPedestalStructuralChecks(
        FoundationScheme scheme,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings,
        PedestalStructuralDesignInput input)
    {
        if (scheme.FoundationType is not FoundationType.RectangularShortColumn and
            not FoundationType.CircularShortColumn)
        {
            return;
        }

        var ready = input.Source.IsConfirmed &&
                    input.ConcreteCompressiveStrengthMpa > 0 &&
                    input.LongitudinalBarDiameterMm > 0 &&
                    input.LongitudinalBarCount >= 6 &&
                    input.MinimumLongitudinalReinforcementRatio > 0 &&
                    input.StirrupDiameterMm > 0 &&
                    input.StirrupSpacingMm > 0 &&
                    input.StirrupLegCount >= 2;
        if (!ready)
        {
            scheme.Checks.Add(Pending(
                "PEDESTAL_STRUCTURAL_INPUT",
                "独立基础短柱结构参数",
                "请确认混凝土抗压强度、短柱纵筋数量/直径、最小配筋率及箍筋规格。软件已提供可编辑候选，但未确认前不形成短柱结构通过结论。",
                appliedLoad.ResolveStructuralDesignLoad(settings).GoverningCase,
                "GB/T 50010-2010（2024年版）第6章、第8章"));
            return;
        }

        var geometry = scheme.Geometry;
        var load = appliedLoad.ResolveStructuralDesignLoad(settings);
        var isCircular = scheme.FoundationType == FoundationType.CircularShortColumn;
        var length = Math.Max(geometry.PedestalLengthM, 1e-6);
        var width = isCircular
            ? length
            : Math.Max(geometry.PedestalWidthM, 1e-6);
        var areaM2 = isCircular
            ? Pi * length * length / 4
            : length * width;
        var momentX = Math.Abs(load.MomentXKnM) +
                      Math.Abs(load.ShearYKn) * geometry.PedestalHeightM;
        var momentY = Math.Abs(load.MomentYKnM) +
                      Math.Abs(load.ShearXKn) * geometry.PedestalHeightM;
        var axial = Math.Max(0, load.VerticalKn) +
                    settings.FoundationPermanentLoadFactor *
                    areaM2 * geometry.PedestalHeightM *
                    settings.ConcreteUnitWeightKnPerM3;
        var shearX = Math.Abs(load.ShearXKn);
        var shearY = Math.Abs(load.ShearYKn);
        var barAreaMm2 = Pi * input.LongitudinalBarDiameterMm * input.LongitudinalBarDiameterMm / 4;
        var providedAreaMm2 = input.LongitudinalBarCount * barAreaMm2;
        var minimumAreaMm2 = input.MinimumLongitudinalReinforcementRatio * areaM2 * 1_000_000;
        var coverMm = settings.ConcreteCoverMm;
        var fy = settings.ReinforcementYieldStrengthMpa;
        var fc = input.ConcreteCompressiveStrengthMpa;
        var ft = settings.ConcreteTensileStrengthMpa;
        var axialCapacityKn = fc * areaM2 * 1_000;

        double requiredAreaMm2;
        double interaction;
        double grossShearCapacityKn;
        double requiredAsvPerSMm2PerM;
        double providedAsvPerSMm2PerM;
        if (isCircular)
        {
            var effectiveDepthMm = Math.Max(1, length * 1000 - coverMm - input.LongitudinalBarDiameterMm / 2);
            var resultantMoment = Math.Sqrt(momentX * momentX + momentY * momentY);
            var requiredForMoment = resultantMoment * 1_000_000 /
                                    (0.9 * fy * effectiveDepthMm);
            requiredAreaMm2 = Math.Max(minimumAreaMm2, 2 * requiredForMoment);
            var momentCapacity = 0.9 * fy * Math.Max(1, providedAreaMm2 / 2) * effectiveDepthMm / 1_000_000;
            interaction = SafeRatio(axial, axialCapacityKn) + SafeRatio(resultantMoment, momentCapacity);

            var radiusMm = length * 500;
            var equivalentWidthMm = 1.76 * radiusMm;
            var equivalentDepthMm = 1.60 * radiusMm;
            grossShearCapacityKn = 0.25 * fc * equivalentWidthMm * equivalentDepthMm / 1000;
            var maximumShear = Math.Sqrt(shearX * shearX + shearY * shearY);
            var concreteShearKn = 0.7 * ft * equivalentWidthMm * equivalentDepthMm / 1000;
            requiredAsvPerSMm2PerM = Math.Max(
                0,
                (maximumShear - concreteShearKn) * 1_000_000 /
                (fy * equivalentDepthMm));
        }
        else
        {
            var h0Xmm = Math.Max(1, width * 1000 - coverMm - input.LongitudinalBarDiameterMm / 2);
            var h0Ymm = Math.Max(1, length * 1000 - coverMm - input.LongitudinalBarDiameterMm / 2);
            var asX = momentX * 1_000_000 / (0.9 * fy * h0Xmm);
            var asY = momentY * 1_000_000 / (0.9 * fy * h0Ymm);
            requiredAreaMm2 = Math.Max(minimumAreaMm2, 2 * Math.Max(asX, asY));
            var momentCapacityX = 0.9 * fy * Math.Max(1, providedAreaMm2 / 2) * h0Xmm / 1_000_000;
            var momentCapacityY = 0.9 * fy * Math.Max(1, providedAreaMm2 / 2) * h0Ymm / 1_000_000;
            interaction = SafeRatio(axial, axialCapacityKn) +
                          SafeRatio(momentX, momentCapacityX) +
                          SafeRatio(momentY, momentCapacityY);

            var grossX = 0.25 * fc * length * 1000 * h0Xmm / 1000;
            var grossY = 0.25 * fc * width * 1000 * h0Ymm / 1000;
            grossShearCapacityKn = Math.Min(grossX, grossY);
            var requiredX = Math.Max(
                0,
                (shearY - 0.7 * ft * length * 1000 * h0Xmm / 1000) * 1_000_000 /
                (fy * h0Xmm));
            var requiredY = Math.Max(
                0,
                (shearX - 0.7 * ft * width * 1000 * h0Ymm / 1000) * 1_000_000 /
                (fy * h0Ymm));
            requiredAsvPerSMm2PerM = Math.Max(requiredX, requiredY);
        }

        providedAsvPerSMm2PerM =
            input.StirrupLegCount * Pi * input.StirrupDiameterMm * input.StirrupDiameterMm / 4 /
            input.StirrupSpacingMm * 1000;
        var maximumShearDemand = isCircular
            ? Math.Sqrt(shearX * shearX + shearY * shearY)
            : Math.Max(shearX, shearY);

        AddVerification(
            scheme.Checks,
            "PEDESTAL_LONGITUDINAL_REINFORCEMENT",
            isCircular ? "圆形短柱纵向钢筋" : "矩形短柱纵向钢筋",
            requiredAreaMm2,
            providedAreaMm2,
            "mm²",
            $"柱脚设计内力N={axial:F2} kN、Mx={momentX:F2} kN·m、My={momentY:F2} kN·m；按双向受弯保守分配并与最小配筋率{input.MinimumLongitudinalReinforcementRatio:P2}比较，需As={requiredAreaMm2:F0} mm²，实配{input.LongitudinalBarCount}Φ{input.LongitudinalBarDiameterMm:F0}为{providedAreaMm2:F0} mm²。",
            load.GoverningCase,
            "GB/T 50010-2010（2024年版）第6.2节、第8.5节；双向受弯保守包络");
        AddVerification(
            scheme.Checks,
            "PEDESTAL_AXIAL_BENDING_INTERACTION",
            isCircular ? "圆形短柱轴力－双向弯矩包络" : "矩形短柱轴力－双向弯矩包络",
            interaction,
            1,
            "无量纲",
            $"采用不计轴压有利作用的保守线性包络N/N0+Mx/Mrx+My/Mry={interaction:F3}≤1.0；N0按fcAc计算。",
            load.GoverningCase,
            "GB/T 50010-2010（2024年版）第6.2节；项目保守包络规则");
        AddVerification(
            scheme.Checks,
            "PEDESTAL_GROSS_SHEAR",
            "短柱斜截面受剪上限",
            maximumShearDemand,
            grossShearCapacityKn,
            "kN",
            $"设计剪力V={maximumShearDemand:F2} kN，按0.25βc·fc·b·h0核对截面受剪上限{grossShearCapacityKn:F2} kN。",
            load.GoverningCase,
            "GB/T 50010-2010（2024年版）第6.3节");
        AddVerification(
            scheme.Checks,
            "PEDESTAL_STIRRUP_REINFORCEMENT",
            "短柱箍筋",
            requiredAsvPerSMm2PerM,
            providedAsvPerSMm2PerM,
            "mm²/m",
            requiredAsvPerSMm2PerM <= 1e-9
                ? $"混凝土受剪项已满足，按构造采用{input.StirrupLegCount}肢Φ{input.StirrupDiameterMm:F0}@{input.StirrupSpacingMm:F0}。"
                : $"计算需Asv/s={requiredAsvPerSMm2PerM:F0} mm²/m，实配{providedAsvPerSMm2PerM:F0} mm²/m。",
            load.GoverningCase,
            "GB/T 50010-2010（2024年版）第6.3节、第8.4节");

        var mainLength = Math.Max(
            geometry.PedestalHeightM,
            geometry.PedestalHeightM + geometry.BaseThicknessM - settings.ConcreteCoverMm / 1000);
        var mainUnitWeight = input.LongitudinalBarDiameterMm * input.LongitudinalBarDiameterMm / 162;
        var mainTotalLength = input.LongitudinalBarCount * mainLength;
        var stirrupCount = (int)Math.Floor(
            geometry.PedestalHeightM * 1000 / input.StirrupSpacingMm) + 1;
        var rectangularStirrupCutLength = isCircular
            ? null
            : RebarCutLengthCalculator.CalculateRectangularClosedStirrup(
                length,
                width,
                settings.ConcreteCoverMm,
                input.StirrupDiameterMm,
                RebarCutLengthCalculator.ShouldUseSeismicDetailing(
                    geotechnical.SeismicIntensityDegree),
                Math.Abs(appliedLoad.TorsionKnM) > 1e-9);
        var stirrupCenterLength = isCircular
            ? Pi * Math.Max(0, length - 2 * settings.ConcreteCoverMm / 1000)
            : rectangularStirrupCutLength!.TotalCutLengthM;
        var stirrupUnitWeight = input.StirrupDiameterMm * input.StirrupDiameterMm / 162;
        var stirrupTotalLength = stirrupCount * stirrupCenterLength;
        var mainDesign = new ReinforcementDesignResult
        {
            Component = isCircular ? "独立基础圆形短柱纵筋" : "独立基础矩形短柱纵筋",
            Direction = isCircular ? "圆周均布" : "截面周边均布",
            BarSpecification = $"{input.LongitudinalBarCount}Φ{input.LongitudinalBarDiameterMm:F0}",
            RequiredAreaMm2 = requiredAreaMm2,
            ProvidedAreaMm2 = providedAreaMm2,
            BarCount = input.LongitudinalBarCount,
            BarDiameterMm = input.LongitudinalBarDiameterMm,
            SingleBarLengthM = mainLength,
            TotalLengthM = mainTotalLength,
            UnitWeightKgPerM = mainUnitWeight,
            CalculatedWeightKg = mainTotalLength * mainUnitWeight,
            Status = providedAreaMm2 + 1e-9 >= requiredAreaMm2
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            RuleReference = "GB/T 50010-2010（2024年版）第6.2节、第8.5节"
        };
        var stirrupDesign = new ReinforcementDesignResult
        {
            Component = isCircular ? "独立基础圆形短柱箍筋" : "独立基础矩形短柱箍筋",
            Direction = isCircular ? "环向" : "闭合箍",
            BarSpecification = $"Φ{input.StirrupDiameterMm:F0}@{input.StirrupSpacingMm:F0}",
            RequiredAreaMm2 = requiredAsvPerSMm2PerM,
            ProvidedAreaMm2 = providedAsvPerSMm2PerM,
            BarCount = stirrupCount,
            BarDiameterMm = input.StirrupDiameterMm,
            BarSpacingMm = input.StirrupSpacingMm,
            SingleBarLengthM = stirrupCenterLength,
            TotalLengthM = stirrupTotalLength,
            UnitWeightKgPerM = stirrupUnitWeight,
            CalculatedWeightKg = stirrupTotalLength * stirrupUnitWeight,
            StirrupBodyPerimeterM = rectangularStirrupCutLength?.BodyPerimeterM ?? 0,
            HookBendAllowanceM = rectangularStirrupCutLength?.HookBendAllowanceM ?? 0,
            HookStraightAllowanceM = rectangularStirrupCutLength?.HookStraightAllowanceM ?? 0,
            CuttingLengthExplanation = rectangularStirrupCutLength?.FormulaDescription ?? string.Empty,
            Status = providedAsvPerSMm2PerM + 1e-9 >= requiredAsvPerSMm2PerM
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            RuleReference = isCircular
                ? "GB/T 50010-2010（2024年版）第6.3节、第8.4节"
                : "GB/T 50010-2010（2024年版）第6.3节、第9.3.2条、第11.1.8条；22G101-3第2-7页"
        };
        scheme.ReinforcementDesigns.Add(mainDesign);
        scheme.ReinforcementDesigns.Add(stirrupDesign);
        scheme.Quantities = CloneQuantities(
            scheme.Quantities,
            mainDesign.CalculatedWeightKg + stirrupDesign.CalculatedWeightKg);
    }

    private static void AddHighWaterAntiFlotationCheck(
        FoundationScheme scheme,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings,
        HydrogeologyDesignInput input)
    {
        if (scheme.FoundationType is FoundationType.Pile or
            FoundationType.RigidShortPile or
            FoundationType.RigidRectangularShortPile)
        {
            return;
        }

        if (!input.Source.IsConfirmed ||
            input.DesignHighGroundwaterDepthM < 0 ||
            input.AntiFlotationSafetyFactor < 1)
        {
            scheme.Checks.Add(Pending(
                "HIGH_WATER_ANTIFLOTATION",
                "设计最高水位抗浮稳定",
                "请从地勘或水文资料确认设计最高地下水位（以地面以下埋深填写）。规范一般抗浮稳定安全系数已预填1.05；未确认水位前不使用常年水位替代。",
                "设计最高水位",
                "GB 50007-2011第5.4.3条"));
            return;
        }

        var geometry = scheme.Geometry;
        var isCircular = scheme.FoundationType == FoundationType.CircularShortColumn;
        var pedestalArea = isCircular
            ? Pi * geometry.PedestalLengthM * geometry.PedestalLengthM / 4
            : geometry.PedestalLengthM * geometry.PedestalWidthM;
        var baseArea = geometry.BaseLengthM * geometry.BaseWidthM;
        var concreteVolume =
            baseArea * geometry.BaseThicknessM +
            pedestalArea * geometry.PedestalHeightM;
        var soilCoverArea = Math.Max(0, baseArea - pedestalArea);
        var soilCoverVolume = soilCoverArea * geometry.PedestalHeightM;
        var submergedPedestalHeight = Math.Clamp(
            geometry.PedestalHeightM - input.DesignHighGroundwaterDepthM,
            0,
            geometry.PedestalHeightM);
        var submergedSlabHeight = Math.Clamp(
            geometry.EmbedmentDepthM -
            Math.Max(input.DesignHighGroundwaterDepthM, geometry.PedestalHeightM),
            0,
            geometry.BaseThicknessM);
        var displacedVolume =
            pedestalArea * submergedPedestalHeight +
            baseArea * submergedSlabHeight +
            soilCoverArea * submergedPedestalHeight;
        var buoyancyKn = displacedVolume * settings.WaterUnitWeightKnPerM3;
        var stabilizingWeightKn =
            concreteVolume * settings.ConcreteUnitWeightKnPerM3 +
            soilCoverVolume * geotechnical.SoilUnitWeightKnPerM3;
        var ratio = buoyancyKn <= 1e-9
            ? double.PositiveInfinity
            : stabilizingWeightKn / buoyancyKn;
        AddVerification(
            scheme.Checks,
            "HIGH_WATER_ANTIFLOTATION",
            "设计最高水位抗浮稳定",
            input.AntiFlotationSafetyFactor,
            ratio,
            "安全系数",
            buoyancyKn <= 1e-9
                ? $"设计最高地下水埋深{input.DesignHighGroundwaterDepthM:F2} m不高于基础底面，浮力作用值为0。"
                : $"按Gk/Nw验算：基础及压重Gk={stabilizingWeightKn:F2} kN（覆土重度{geotechnical.SoilUnitWeightKnPerM3:F2} kN/m³），浮力Nw,k={buoyancyKn:F2} kN，Gk/Nw,k={ratio:F3}，要求Kw={input.AntiFlotationSafetyFactor:F2}；来源：{SourceText(input.Source)}。",
            "设计最高水位",
            "GB 50007-2011式(5.4.3)；一般情况Kw=1.05");
    }

    private static void ReplaceSpecialGroundAndSeismicChecks(
        FoundationScheme scheme,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        SpecialGroundDesignInput input)
    {
        scheme.Checks.RemoveAll(check =>
            check.Code is "SEISMIC_REVIEW" or "SPECIAL_SOIL_REVIEW");

        var seismicComplete = geotechnical.SeismicIntensityDegree > 0 &&
                              geotechnical.DesignBasicGroundAccelerationG > 0 &&
                              !string.IsNullOrWhiteSpace(geotechnical.DesignEarthquakeGroup) &&
                              !string.IsNullOrWhiteSpace(geotechnical.SiteClass);
        scheme.Checks.Add(seismicComplete
            ? SpecialReview(
                "SEISMIC_REVIEW",
                "抗震作用与场地效应",
                $"已记录设防烈度{geotechnical.SeismicIntensityDegree}度、设计基本地震加速度{geotechnical.DesignBasicGroundAccelerationG:F2}g、{geotechnical.DesignEarthquakeGroup}、场地类别{geotechnical.SiteClass}。当前荷载包尚无可追溯地震作用组合，不能用风荷载组合代替，须在形成地震组合后复核。",
                "地震作用组合",
                "GB 55002-2021；GB/T 50011-2010（2024年版）")
              : SpecialReview(
                  "SEISMIC_REVIEW",
                  "抗震基本参数",
                  "设防烈度、设计基本地震加速度和分组可优先由建设地点数据库补齐，场地类别仍应来自地勘；资料不足时已自动转专业核对，不阻断当前基础主体方案。",
                appliedLoad.GoverningCase,
                "GB 55002-2021；GB/T 50011-2010（2024年版）"));

        if (!input.Source.IsConfirmed ||
            input.CollapsibleLoess == EngineeringRiskState.NotAssessed ||
            input.Liquefaction == EngineeringRiskState.NotAssessed ||
            input.FrostHeave == EngineeringRiskState.NotAssessed)
        {
            scheme.Checks.Add(SpecialReview(
                "SPECIAL_SOIL_REVIEW",
                "湿陷、液化与冻胀结论",
                "AI只采用地勘原文明确的无风险或存在风险结论；报告没有明确表述时自动转交付前专业核对，不会猜成无风险，也不要求普通用户逐项编结论。",
                "地勘适用性",
                "GB 55003-2021；GB 50025-2018；JGJ 118-2011"));
            return;
        }

        var untreated = new List<string>();
        if (input.CollapsibleLoess == EngineeringRiskState.PresentTreatmentUnconfirmed) untreated.Add("湿陷性黄土");
        if (input.Liquefaction == EngineeringRiskState.PresentTreatmentUnconfirmed) untreated.Add("液化");
        if (input.FrostHeave == EngineeringRiskState.PresentTreatmentUnconfirmed) untreated.Add("冻胀");
        if (untreated.Count > 0)
        {
            scheme.Checks.Add(SpecialReview(
                "SPECIAL_SOIL_REVIEW",
                "特殊地基处理",
                $"已确认存在{string.Join("、", untreated)}风险，但处理方案尚未确认；当前基础尺寸不得作为正式结论。",
                "地勘适用性",
                "GB 55003-2021；对应特殊地基专项标准"));
            return;
        }

        var treated = new List<string>();
        if (input.CollapsibleLoess == EngineeringRiskState.PresentTreatmentConfirmed) treated.Add("湿陷性黄土");
        if (input.Liquefaction == EngineeringRiskState.PresentTreatmentConfirmed) treated.Add("液化");
        if (input.FrostHeave == EngineeringRiskState.PresentTreatmentConfirmed) treated.Add("冻胀");
        if (treated.Count > 0)
        {
            scheme.Checks.Add(SpecialReview(
                "SPECIAL_SOIL_REVIEW",
                "特殊地基处理复核",
                $"已记录{string.Join("、", treated)}专项处理：{input.TreatmentDescription}。软件已保留该设计边界，但处理效果、构造和施工验收仍须按专项设计复核。",
                "专项处理后工况",
                "GB 55003-2021；对应特殊地基专项标准"));
        }
        else
        {
            scheme.Checks.Add(new FoundationCheckResult
            {
                Code = "SPECIAL_SOIL_REVIEW",
                Name = "特殊土与不良地质结论",
                Status = CheckStatus.Result,
                Demand = 0,
                Capacity = 0,
                Unit = string.Empty,
                GoverningCase = "地勘适用性",
                Explanation = $"地勘已逐项确认无湿陷、液化和冻胀风险；来源：{SourceText(input.Source)}。",
                RuleReference = "GB 55003-2021；项目地勘结论"
            });
        }

        if (input.FrostHeave != EngineeringRiskState.NotPresent && input.DesignFrostDepthM > 0)
        {
            AddVerification(
                scheme.Checks,
                "FROST_EMBEDMENT",
                "基础埋深与设计冻深",
                input.DesignFrostDepthM,
                scheme.Geometry.EmbedmentDepthM,
                "m",
                $"设计冻深{input.DesignFrostDepthM:F2} m，基础埋深{scheme.Geometry.EmbedmentDepthM:F2} m；该项只核对埋深，不代替切向冻胀力和防冻胀构造验算。",
                "冻胀工况",
                "JGJ 118-2011；项目地勘设计冻深" );
        }
    }

    private static QuantitySummary CloneQuantities(QuantitySummary source, double addedSteelKg) => new()
    {
        ConcreteM3 = source.ConcreteM3,
        ExcavationM3 = source.ExcavationM3,
        BackfillM3 = source.BackfillM3,
        EstimatedReinforcementKg = source.EstimatedReinforcementKg + addedSteelKg
    };

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

    private static FoundationCheckResult SpecialReview(
        string code,
        string name,
        string explanation,
        string governingCase,
        string ruleReference) => new()
        {
            Code = code,
            Name = name,
            Status = CheckStatus.SpecialReview,
            GoverningCase = governingCase,
            Explanation = explanation,
            RuleReference = ruleReference
        };

    private static string SourceText(EngineeringParameterSource source) =>
        string.IsNullOrWhiteSpace(source.Display)
            ? source.SourceType.ToString()
            : source.Display;

    private static double SafeRatio(double demand, double capacity) =>
        demand <= 1e-12
            ? 0
            : capacity > 1e-12
                ? demand / capacity
                : double.PositiveInfinity;
}
