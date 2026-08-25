using TowerFoundation.Domain;

namespace TowerFoundation.Calculation;

/// <summary>
/// 矩形刚性短柱桩计算分支。水平土抗力、刚度、位移、内力、受剪均按两个主轴分别计算；
/// 截面承载力按 GB 50010 平截面假定、矩形应力图和双向偏压近似式复核。
/// </summary>
public sealed class RigidRectangularShortPileFoundationCalculator
{
    private const double Pi = Math.PI;

    public FoundationScheme Calculate(
        FoundationGeometry geometry,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        geometry.FoundationUnitCount = Math.Max(1, appliedLoad.FoundationUnitCount);
        var rigid = settings.RigidShortPile;
        ValidateInputs(geometry, appliedLoad, geotechnical, settings, rigid);

        var lengthX = geometry.BaseLengthM;
        var widthY = geometry.BaseWidthM;
        var embeddedDepth = geometry.PileLengthM;
        var aboveGroundHeight = geometry.PedestalHeightM;
        var area = lengthX * widthY;
        var vertical = Math.Max(0, appliedLoad.VerticalKn);
        var structuralLoad = appliedLoad.ResolveStructuralDesignLoad(settings);
        var structuralVertical = Math.Max(0, structuralLoad.VerticalKn);
        var bars = BuildPerimeterBars(
            lengthX,
            widthY,
            settings.ConcreteCoverMm / 1000,
            rigid.LongitudinalBarDiameterMm / 1000,
            rigid.LongitudinalBarCount);

        var submergedLength = Math.Clamp(
            embeddedDepth - geotechnical.GroundwaterDepthM,
            0,
            embeddedDepth);
        var dryLength = embeddedDepth - submergedLength;
        var effectiveSelfWeight = area *
            ((dryLength + aboveGroundHeight) * settings.ConcreteUnitWeightKnPerM3 +
             submergedLength *
             (settings.ConcreteUnitWeightKnPerM3 - settings.WaterUnitWeightKnPerM3));

        var responseX = BuildDirectionalResponse(
            "X",
            Math.Abs(appliedLoad.ShearXKn),
            Math.Abs(appliedLoad.MomentYKnM),
            lengthX,
            widthY,
            bars.Select(bar => bar.X).ToList(),
            embeddedDepth,
            aboveGroundHeight,
            vertical + effectiveSelfWeight,
            geotechnical,
            settings,
            rigid,
            appliedLoad.GoverningCase);
        var responseY = BuildDirectionalResponse(
            "Y",
            Math.Abs(appliedLoad.ShearYKn),
            Math.Abs(appliedLoad.MomentXKnM),
            widthY,
            lengthX,
            bars.Select(bar => bar.Y).ToList(),
            embeddedDepth,
            aboveGroundHeight,
            vertical + effectiveSelfWeight,
            geotechnical,
            settings,
            rigid,
            appliedLoad.GoverningCase);

        var checks = new List<FoundationCheckResult>();
        checks.AddRange(responseX.Checks);
        checks.AddRange(responseY.Checks);
        if (!responseX.IsValid || !responseY.IsValid)
        {
            return BuildFailedScheme(geometry, checks);
        }

        checks.Add(new FoundationCheckResult
        {
            Code = "RIGID_RECT_STRUCTURAL_COMBINATION",
            Name = "矩形短柱桩结构基本组合",
            Status = appliedLoad.HasExplicitStructuralCombination
                ? CheckStatus.Result
                : CheckStatus.PendingInput,
            Demand = appliedLoad.HasExplicitStructuralCombination
                ? 1
                : settings.StructuralDesignLoadFactor,
            Capacity = appliedLoad.HasExplicitStructuralCombination
                ? 1
                : settings.StructuralDesignLoadFactor,
            Utilization = 0,
            Unit = "系数",
            GoverningCase = structuralLoad.GoverningCase,
            Explanation = appliedLoad.DescribeStructuralCombination(settings),
            RuleReference = "YD/T 5131-2019第7.1.7条第4款；GB 50007-2011第3.0.5条第4款"
        });

        var structuralPermanentFactor =
            settings.FoundationPermanentLoadFactor *
            settings.StructureImportanceFactor;
        var structuralResponseX = BuildDirectionalResponse(
            "X",
            Math.Abs(structuralLoad.ShearXKn),
            Math.Abs(structuralLoad.MomentYKnM),
            lengthX,
            widthY,
            bars.Select(bar => bar.X).ToList(),
            embeddedDepth,
            aboveGroundHeight,
            structuralVertical + effectiveSelfWeight * structuralPermanentFactor,
            geotechnical,
            settings,
            rigid,
            structuralLoad.GoverningCase);
        var structuralResponseY = BuildDirectionalResponse(
            "Y",
            Math.Abs(structuralLoad.ShearYKn),
            Math.Abs(structuralLoad.MomentXKnM),
            widthY,
            lengthX,
            bars.Select(bar => bar.Y).ToList(),
            embeddedDepth,
            aboveGroundHeight,
            structuralVertical + effectiveSelfWeight * structuralPermanentFactor,
            geotechnical,
            settings,
            rigid,
            structuralLoad.GoverningCase);
        if (!structuralResponseX.IsValid || !structuralResponseY.IsValid)
        {
            checks.Add(FailedCheck(
                "RIGID_RECT_BASIC_RESPONSE",
                "矩形短柱桩基本组合内力求解",
                "基本组合下未能建立两个主轴的土抗力平衡，不能继续进行截面和配筋验算。",
                structuralLoad.GoverningCase,
                "YD/T 5131-2019第7.1.7条第4款；基本组合结构验算门禁"));
            return BuildFailedScheme(geometry, checks);
        }

        var candidateDepths = new[]
            {
                structuralResponseX.RotationCenterDepthM,
                structuralResponseX.MaximumMomentDepthM,
                structuralResponseY.RotationCenterDepthM,
                structuralResponseY.MaximumMomentDepthM
            }
            .Select(value => Math.Clamp(value, 0, embeddedDepth))
            .DistinctBy(value => Math.Round(value, 6))
            .ToList();
        var sectionStates = candidateDepths
            .Select(depth => BuildSectionState(
                depth,
                area,
                aboveGroundHeight,
                structuralVertical,
                structuralResponseX,
                structuralResponseY,
                settings,
                structuralPermanentFactor))
            .ToList();
        var controllingMomentState = sectionStates
            .OrderByDescending(state =>
                Math.Sqrt(state.MomentXKnM * state.MomentXKnM +
                          state.MomentYKnM * state.MomentYKnM))
            .First();
        checks.Add(ResultOnlyCheck(
            "RIGID_RECT_INTERNAL_FORCE",
            "矩形短柱桩最不利截面内力",
            Math.Sqrt(
                controllingMomentState.MomentXKnM * controllingMomentState.MomentXKnM +
                controllingMomentState.MomentYKnM * controllingMomentState.MomentYKnM),
            "kN·m",
            $"控制深度y={controllingMomentState.DepthM:F3} m：N={controllingMomentState.AxialKn:F2} kN、" +
            $"Vx={controllingMomentState.ShearXKn:F2} kN、Vy={controllingMomentState.ShearYKn:F2} kN、" +
            $"Mx={controllingMomentState.MomentXKnM:F2} kN·m、My={controllingMomentState.MomentYKnM:F2} kN·m。",
            structuralLoad.GoverningCase,
            "JGJ 94-2008第5.7节；矩形截面按两个主轴分别建立m法内力"));

        var serviceDepths = new[]
            {
                responseX.RotationCenterDepthM,
                responseX.MaximumMomentDepthM,
                responseY.RotationCenterDepthM,
                responseY.MaximumMomentDepthM
            }
            .Select(value => Math.Clamp(value, 0, embeddedDepth))
            .DistinctBy(value => Math.Round(value, 6))
            .ToList();
        var serviceStates = serviceDepths
            .Select(depth => BuildSectionState(
                depth,
                area,
                aboveGroundHeight,
                vertical,
                responseX,
                responseY,
                settings,
                1.0))
            .ToList();
        checks.Add(ResultOnlyCheck(
            "RIGID_RECT_SERVICE_MOMENT_X",
            "矩形短柱桩X向标准组合最大弯矩",
            serviceStates.Max(state => Math.Abs(state.MomentXKnM)),
            "kN·m",
            "按基础端标准组合及桩身有效自重，沿旋转中心和最大弯矩候选截面取X向包络，供裂缝宽度验算使用。",
            appliedLoad.GoverningCase,
            "JGJ 94-2008第5.7节；GB/T 50010-2010（2024年版）第7.1节"));
        checks.Add(ResultOnlyCheck(
            "RIGID_RECT_SERVICE_MOMENT_Y",
            "矩形短柱桩Y向标准组合最大弯矩",
            serviceStates.Max(state => Math.Abs(state.MomentYKnM)),
            "kN·m",
            "按基础端标准组合及桩身有效自重，沿旋转中心和最大弯矩候选截面取Y向包络，供裂缝宽度验算使用。",
            appliedLoad.GoverningCase,
            "JGJ 94-2008第5.7节；GB/T 50010-2010（2024年版）第7.1节"));

        var longitudinal = DesignLongitudinalReinforcement(
            lengthX,
            widthY,
            bars,
            sectionStates,
            settings,
            rigid,
            structuralLoad.GoverningCase);
        checks.Add(longitudinal.CapacityCheck);
        checks.Add(longitudinal.ReinforcementCheck);

        var shearDesign = DesignStirrups(
            lengthX,
            widthY,
            area,
            sectionStates,
            settings,
            rigid,
            structuralLoad.GoverningCase);
        checks.AddRange(shearDesign.Checks);
        checks.Add(new FoundationCheckResult
        {
            Code = "RIGID_RECT_REMAINING_SCOPE",
            Name = "矩形刚性短柱桩剩余专项范围",
            Status = CheckStatus.SpecialReview,
            Demand = 0,
            Capacity = 0,
            Utilization = 0,
            Unit = string.Empty,
            GoverningCase = appliedLoad.GoverningCase,
            Explanation =
                "已逐向计算抗倾覆、b0、EI、αh、位移、内力、矩形截面双向偏压纵筋及双向受剪箍筋；矩形截面的旧塔规程抗倾覆公式属于按投影宽度的工程推广，沉降、裂缝、锚栓连接、抗震、特殊地基和施工构造仍需专项复核。",
            RuleReference = "计算范围门禁（2026-08-02）"
        });

        var totalHeight = embeddedDepth + aboveGroundHeight;
        var longitudinalUnitWeight =
            rigid.LongitudinalBarDiameterMm * rigid.LongitudinalBarDiameterMm / 162;
        var longitudinalLength = rigid.LongitudinalBarCount * totalHeight;
        var stirrupCutLength =
            RebarCutLengthCalculator.CalculateRectangularClosedStirrup(
                lengthX,
                widthY,
                settings.ConcreteCoverMm,
                rigid.StirrupDiameterMm,
                RebarCutLengthCalculator.ShouldUseSeismicDetailing(
                    geotechnical.SeismicIntensityDegree),
                Math.Abs(appliedLoad.TorsionKnM) > 1e-9);
        var stirrupCount =
            (int)Math.Floor(totalHeight * 1000 / rigid.StirrupSpacingMm) + 1;
        var stirrupLength = stirrupCutLength.TotalCutLengthM;
        var stirrupUnitWeight =
            rigid.StirrupDiameterMm * rigid.StirrupDiameterMm / 162;
        var reinforcementDesigns = new List<ReinforcementDesignResult>
        {
            new()
            {
                Component = "刚性短柱桩－矩形纵筋",
                Direction = "截面周边均布",
                BarSpecification =
                    $"{rigid.LongitudinalBarCount}Φ{rigid.LongitudinalBarDiameterMm:F0}",
                RequiredAreaMm2 = longitudinal.RequiredAreaMm2,
                ProvidedAreaMm2 = longitudinal.ProvidedAreaMm2,
                BarCount = rigid.LongitudinalBarCount,
                BarDiameterMm = rigid.LongitudinalBarDiameterMm,
                BarSpacingMm = 0,
                SingleBarLengthM = totalHeight,
                TotalLengthM = longitudinalLength,
                UnitWeightKgPerM = longitudinalUnitWeight,
                CalculatedWeightKg = longitudinalLength * longitudinalUnitWeight,
                Status = longitudinal.ReinforcementCheck.Status,
                RuleReference = longitudinal.ReinforcementCheck.RuleReference
            },
            new()
            {
                Component = "刚性短柱桩－矩形箍筋",
                Direction = "矩形闭合箍（双向）",
                BarSpecification =
                    $"Φ{rigid.StirrupDiameterMm:F0}@{rigid.StirrupSpacingMm:F0}",
                RequiredAreaMm2 = shearDesign.RequiredAsPerSMm2PerM,
                ProvidedAreaMm2 = shearDesign.ProvidedAsPerSMm2PerM,
                BarCount = stirrupCount,
                BarDiameterMm = rigid.StirrupDiameterMm,
                BarSpacingMm = rigid.StirrupSpacingMm,
                SingleBarLengthM = stirrupLength,
                TotalLengthM = stirrupCount * stirrupLength,
                UnitWeightKgPerM = stirrupUnitWeight,
                CalculatedWeightKg = stirrupCount * stirrupLength * stirrupUnitWeight,
                StirrupBodyPerimeterM = stirrupCutLength.BodyPerimeterM,
                HookBendAllowanceM = stirrupCutLength.HookBendAllowanceM,
                HookStraightAllowanceM = stirrupCutLength.HookStraightAllowanceM,
                CuttingLengthExplanation = stirrupCutLength.FormulaDescription,
                Status = shearDesign.StirrupStatus,
                RuleReference = "GB/T 50010-2010（2024年版）第6.3.12、6.3.16～6.3.18、9.3.2、11.1.8条；22G101-3第2-7页"
            }
        };

        return FoundationUnitQuantityScaler.Apply(new FoundationScheme
        {
            FoundationType = FoundationType.RigidRectangularShortPile,
            Geometry = geometry,
            Checks = checks,
            ReinforcementDesigns = reinforcementDesigns,
            Quantities = new QuantitySummary
            {
                ConcreteM3 = area * totalHeight,
                ExcavationM3 = area * embeddedDepth,
                BackfillM3 = 0,
                EstimatedReinforcementKg =
                    reinforcementDesigns.Sum(item => item.CalculatedWeightKg)
            }
        });
    }

    private static DirectionResponse BuildDirectionalResponse(
        string direction,
        double horizontal,
        double towerMoment,
        double sectionDepth,
        double soilWidth,
        IReadOnlyList<double> barCoordinates,
        double embeddedDepth,
        double aboveGroundHeight,
        double verticalWithEffectiveSelfWeight,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings,
        RigidShortPileSettings rigid,
        string governingCase)
    {
        var checks = new List<FoundationCheckResult>();
        var frictionAngleRad = geotechnical.InternalFrictionAngleDegree * Pi / 180;
        var soilPileFriction = Math.Tan(frictionAngleRad);
        var soilPressureCoefficient =
            geotechnical.SoilUnitWeightKnPerM3 *
            Math.Pow(Math.Tan(Pi / 4 + frictionAngleRad / 2), 2);
        var beta = SolveBeta(
            sectionDepth,
            soilWidth,
            embeddedDepth,
            verticalWithEffectiveSelfWeight,
            horizontal,
            frictionAngleRad,
            soilPileFriction,
            soilPressureCoefficient,
            rigid.LateralResistanceWidthCoefficient);
        if (beta is null)
        {
            checks.Add(FailedCheck(
                $"RIGID_RECT_BETA_{direction}",
                $"{direction}向矩形短柱桩参数β求解",
                "在0.05～1.45 rad范围内未找到平衡方程根，请检查土参数、截面边长、埋深及荷载。",
                governingCase,
                "旧塔规程6.2.2-5公式按矩形投影宽度推广；必须专项复核"));
            return DirectionResponse.Invalid(direction, checks);
        }

        var lateralWidth = soilWidth *
            (1 + 2 * embeddedDepth * rigid.LateralResistanceWidthCoefficient *
                Math.Cos(Pi / 4 + frictionAngleRad / 2) * Math.Tan(beta.Value) /
                (3 * sectionDepth));
        var totalLateralResistance =
            soilPressureCoefficient * lateralWidth * embeddedDepth * embeddedDepth / 2;
        var baseVerticalReaction =
            (verticalWithEffectiveSelfWeight - horizontal * soilPileFriction) /
            (1 + soilPileFriction * soilPileFriction);
        var baseEccentricity =
            rigid.VerticalReactionEccentricityCoefficient * sectionDepth;
        var resistingMoment =
            2 * totalLateralResistance * embeddedDepth *
            (1 - 2 * Math.Pow(beta.Value, 3)) / 3 +
            baseVerticalReaction *
            (baseEccentricity + soilPileFriction * embeddedDepth) +
            soilPileFriction * sectionDepth * totalLateralResistance / 2;
        var groundMoment = towerMoment + horizontal * aboveGroundHeight;
        checks.Add(new FoundationCheckResult
        {
            Code = $"RIGID_RECT_OVERTURNING_{direction}",
            Name = $"{direction}向矩形短柱桩抗倾覆",
            Status = groundMoment <= resistingMoment / 2
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            Demand = groundMoment,
            Capacity = resistingMoment / 2,
            Utilization = SafeRatio(groundMoment, resistingMoment / 2),
            Unit = "kN·m",
            GoverningCase = governingCase,
            Explanation =
                $"{direction}向采用受力方向边长{sectionDepth:F2} m、垂直荷载方向投影宽度{soilWidth:F2} m；" +
                $"Mkd={groundMoment:F2} kN·m，E={totalLateralResistance:F2} kN，Mu/2={resistingMoment / 2:F2} kN·m。",
            RuleReference =
                "旧《单管塔规程计算刚性桩(yy).xls》6.2.2-3～8按矩形投影宽度推广；YD/T 5131-2019适用边界；须专项复核"
        });

        var horizontalCoefficient = CalculateWeightedHorizontalCoefficient(
            rigid.SoilLayers,
            embeddedDepth,
            soilWidth);
        var barArea = Pi * Math.Pow(rigid.LongitudinalBarDiameterMm / 1000, 2) / 4;
        var modularRatio = 200_000d / rigid.ConcreteElasticModulusMpa;
        var grossInertia = soilWidth * Math.Pow(sectionDepth, 3) / 12;
        var transformedInertia = grossInertia +
            (modularRatio - 1) * barCoordinates.Sum(value => barArea * value * value);
        var flexuralRigidity =
            0.85 * rigid.ConcreteElasticModulusMpa * 1000 * transformedInertia;
        var effectiveWidth = soilWidth > 1
            ? soilWidth + 1
            : 1.5 * soilWidth + 0.5;
        var deformationCoefficient = Math.Pow(
            horizontalCoefficient * effectiveWidth / flexuralRigidity,
            0.2);
        var alphaH = deformationCoefficient * embeddedDepth;
        checks.Add(new FoundationCheckResult
        {
            Code = $"RIGID_RECT_CLASSIFICATION_{direction}",
            Name = $"{direction}向矩形桩刚性判别",
            Status = alphaH <= 2.5 ? CheckStatus.Pass : CheckStatus.Fail,
            Demand = alphaH,
            Capacity = 2.5,
            Utilization = SafeRatio(alphaH, 2.5),
            Unit = string.Empty,
            GoverningCase = governingCase,
            Explanation =
                $"{direction}向按边宽b={soilWidth:F2} m取b0={effectiveWidth:F3} m，" +
                $"m={horizontalCoefficient:F0} kN/m⁴，EI={flexuralRigidity:F0} kN·m²，αh={alphaH:F3}。",
            RuleReference =
                "JGJ 94-2008第5.7.2、5.7.5条（方形桩b0按受力方向逐向推广至矩形截面）"
        });

        var k0 = horizontalCoefficient * embeddedDepth * soilWidth;
        var topDisplacement =
            24 * (groundMoment + 0.75 * horizontal * embeddedDepth) /
            (k0 * embeddedDepth * embeddedDepth);
        var topRotation =
            12 * (3 * groundMoment / embeddedDepth + 2 * horizontal) /
            (k0 * embeddedDepth * embeddedDepth);
        var rotationCenter = topRotation <= 1e-12
            ? 0
            : Math.Clamp(topDisplacement / topRotation, 0, embeddedDepth);
        var maximumMomentDepth = FindMaximumMomentDepth(
            horizontal,
            k0,
            topDisplacement,
            topRotation,
            embeddedDepth);
        checks.Add(ResultOnlyCheck(
            $"RIGID_RECT_DISPLACEMENT_{direction}",
            $"{direction}向桩顶水平位移",
            topDisplacement,
            "m",
            $"δ{direction.ToLowerInvariant()}={topDisplacement:F5} m；项目允许值须由塔型及连接要求确认。",
            governingCase,
            "JGJ 94-2008第5.7节；旧计算书6.2.2-9～11公式链按主轴逐向计算"));
        checks.Add(ResultOnlyCheck(
            $"RIGID_RECT_ROTATION_{direction}",
            $"{direction}向桩顶转角",
            topRotation,
            "rad",
            $"θ{direction.ToLowerInvariant()}={topRotation:F6} rad；项目允许值须由塔型及连接要求确认。",
            governingCase,
            "JGJ 94-2008第5.7节；旧计算书6.2.2-9～11公式链按主轴逐向计算"));

        return new DirectionResponse(
            direction,
            horizontal,
            groundMoment,
            k0,
            embeddedDepth,
            topDisplacement,
            topRotation,
            rotationCenter,
            maximumMomentDepth,
            checks,
            true);
    }

    private static SectionState BuildSectionState(
        double depth,
        double area,
        double aboveGroundHeight,
        double vertical,
        DirectionResponse responseX,
        DirectionResponse responseY,
        FoundationDesignSettings settings,
        double permanentLoadFactor)
    {
        var x = responseX.SectionForces(depth, 1);
        var y = responseY.SectionForces(depth, 1);
        var axial =
            vertical +
            permanentLoadFactor * area *
            (aboveGroundHeight + depth) * settings.ConcreteUnitWeightKnPerM3;
        return new SectionState(
            depth,
            axial,
            x.ShearKn,
            y.ShearKn,
            y.MomentKnM,
            x.MomentKnM);
    }

    private static LongitudinalDesign DesignLongitudinalReinforcement(
        double lengthX,
        double widthY,
        IReadOnlyList<BarPoint> bars,
        IReadOnlyList<SectionState> sectionStates,
        FoundationDesignSettings settings,
        RigidShortPileSettings rigid,
        string governingCase)
    {
        var area = lengthX * widthY;
        var barArea = Pi * Math.Pow(rigid.LongitudinalBarDiameterMm / 1000, 2) / 4;
        var provided = barArea * bars.Count;
        var minimum = rigid.MinimumLongitudinalReinforcementRatio * area;
        var capacities = sectionStates
            .Select(state => EvaluateBiaxialCapacity(
                state,
                lengthX,
                widthY,
                bars,
                barArea,
                rigid.ConcreteCompressiveStrengthMpa,
                settings.ReinforcementYieldStrengthMpa,
                settings.ConcreteCoverMm / 1000,
                rigid.LongitudinalBarDiameterMm / 1000))
            .ToList();
        var controllingIndex = Enumerable.Range(0, capacities.Count)
            .OrderByDescending(index =>
                SafeRatio(sectionStates[index].AxialKn, capacities[index]))
            .First();
        var controllingState = sectionStates[controllingIndex];
        var controllingCapacity = capacities[controllingIndex];

        bool Meets(double candidateBarArea) => sectionStates.All(state =>
            state.AxialKn <= EvaluateBiaxialCapacity(
                state,
                lengthX,
                widthY,
                bars,
                candidateBarArea,
                rigid.ConcreteCompressiveStrengthMpa,
                settings.ReinforcementYieldStrengthMpa,
                settings.ConcreteCoverMm / 1000,
                rigid.LongitudinalBarDiameterMm / 1000));

        var low = 0d;
        var high = Math.Max(barArea, minimum / bars.Count);
        while (!Meets(high) && high < barArea * 64)
        {
            high *= 2;
        }
        var foundCapacity = Meets(high);
        if (foundCapacity)
        {
            for (var iteration = 0; iteration < 60; iteration++)
            {
                var middle = (low + high) / 2;
                if (Meets(middle))
                {
                    high = middle;
                }
                else
                {
                    low = middle;
                }
            }
        }
        var required = Math.Max(minimum, high * bars.Count);
        if (!foundCapacity)
        {
            required = Math.Max(required, provided * 64);
        }

        var requiredMm2 = required * 1_000_000;
        var providedMm2 = provided * 1_000_000;
        var capacityCheck = new FoundationCheckResult
        {
            Code = "RIGID_RECT_BIAXIAL_COMPRESSION",
            Name = "矩形截面双向偏心受压",
            Status = controllingState.AxialKn <= controllingCapacity
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            Demand = controllingState.AxialKn,
            Capacity = controllingCapacity,
            Utilization = SafeRatio(controllingState.AxialKn, controllingCapacity),
            Unit = "kN",
            GoverningCase = governingCase,
            Explanation =
                $"控制深度y={controllingState.DepthM:F3} m，N={controllingState.AxialKn:F2} kN、" +
                $"Mx={controllingState.MomentXKnM:F2} kN·m、My={controllingState.MomentYKnM:F2} kN·m；" +
                $"按双向偏压近似式得到Nu={controllingCapacity:F2} kN。",
            RuleReference =
                "GB/T 50010-2010（2024年版）第6.2.1、6.2.5、6.2.17、6.2.21条"
        };
        var reinforcementCheck = new FoundationCheckResult
        {
            Code = "RIGID_RECT_LONGITUDINAL_REINFORCEMENT",
            Name = "矩形刚性短柱桩纵向钢筋",
            Status = foundCapacity && provided + 1e-12 >= required
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            Demand = requiredMm2,
            Capacity = providedMm2,
            Utilization = SafeRatio(requiredMm2, providedMm2),
            Unit = "mm²",
            GoverningCase = governingCase,
            Explanation =
                $"纵筋按矩形截面周边均布并计入X、Y双向偏压；与最小配筋率{rigid.MinimumLongitudinalReinforcementRatio:P2}比较后" +
                $"需As={requiredMm2:F0} mm²，采用{bars.Count}Φ{rigid.LongitudinalBarDiameterMm:F0}，实配As={providedMm2:F0} mm²。",
            RuleReference =
                "GB/T 50010-2010（2024年版）第6.2.1、6.2.17、6.2.21条；JGJ 94-2008第4.1节"
        };
        return new LongitudinalDesign(
            requiredMm2,
            providedMm2,
            capacityCheck,
            reinforcementCheck);
    }

    private static double EvaluateBiaxialCapacity(
        SectionState state,
        double lengthX,
        double widthY,
        IReadOnlyList<BarPoint> bars,
        double barArea,
        double concreteCompressiveStrengthMpa,
        double reinforcementYieldStrengthMpa,
        double coverM,
        double barDiameterM)
    {
        var fc = concreteCompressiveStrengthMpa * 1000;
        var fy = reinforcementYieldStrengthMpa * 1000;
        var n0 = 0.9 * (fc * lengthX * widthY + fy * barArea * bars.Count);
        var eccentricityForMx = state.AxialKn <= 1e-9
            ? double.MaxValue
            : Math.Abs(state.MomentXKnM) / state.AxialKn;
        var eccentricityForMy = state.AxialKn <= 1e-9
            ? double.MaxValue
            : Math.Abs(state.MomentYKnM) / state.AxialKn;
        eccentricityForMx += Math.Max(0.02, widthY / 30);
        eccentricityForMy += Math.Max(0.02, lengthX / 30);
        var nx = state.MomentXKnM == 0
            ? n0
            : CalculateUniaxialAxialCapacity(
                widthY,
                lengthX,
                bars.Select(bar => bar.Y).ToList(),
                barArea,
                fc,
                fy,
                eccentricityForMx);
        var ny = state.MomentYKnM == 0
            ? n0
            : CalculateUniaxialAxialCapacity(
                lengthX,
                widthY,
                bars.Select(bar => bar.X).ToList(),
                barArea,
                fc,
                fy,
                eccentricityForMy);
        nx = Math.Clamp(nx, 1e-6, n0);
        ny = Math.Clamp(ny, 1e-6, n0);
        var reciprocal = 1 / nx + 1 / ny - 1 / n0;
        return reciprocal <= 0 ? n0 : 1 / reciprocal;
    }

    private static double CalculateUniaxialAxialCapacity(
        double sectionDepth,
        double sectionWidth,
        IReadOnlyList<double> barCoordinates,
        double barArea,
        double fc,
        double fy,
        double eccentricity)
    {
        if (eccentricity <= 1e-9)
        {
            return 0.9 *
                   (fc * sectionDepth * sectionWidth +
                    fy * barArea * barCoordinates.Count);
        }

        (double N, double M, double Residual) Evaluate(double neutralAxisDepth)
        {
            const double beta1 = 0.80;
            const double ultimateCompressionStrain = 0.0033;
            const double steelElasticModulus = 200_000_000;
            var compressionBlockDepth = Math.Min(
                sectionDepth,
                beta1 * neutralAxisDepth);
            var concreteForce = fc * sectionWidth * compressionBlockDepth;
            var concreteMoment = concreteForce *
                (sectionDepth / 2 - compressionBlockDepth / 2);
            var steelForce = 0d;
            var steelMoment = 0d;
            foreach (var coordinate in barCoordinates)
            {
                var distanceFromCompressionEdge =
                    sectionDepth / 2 - coordinate;
                var strain = ultimateCompressionStrain *
                    (1 - distanceFromCompressionEdge / neutralAxisDepth);
                var stress = Math.Clamp(
                    steelElasticModulus * strain,
                    -fy,
                    fy);
                var force = stress * barArea;
                steelForce += force;
                steelMoment += force * coordinate;
            }
            var axial = concreteForce + steelForce;
            var moment = concreteMoment + steelMoment;
            var residual = axial <= 1e-9
                ? double.NaN
                : Math.Abs(moment) / axial - eccentricity;
            return (axial, moment, residual);
        }

        var roots = new List<double>();
        var previousDepth = sectionDepth * 0.005;
        var previous = Evaluate(previousDepth);
        const int segments = 800;
        for (var index = 1; index <= segments; index++)
        {
            var exponent = Math.Log(20d / 0.005) * index / segments;
            var currentDepth = sectionDepth * 0.005 * Math.Exp(exponent);
            var current = Evaluate(currentDepth);
            if (double.IsFinite(previous.Residual) &&
                double.IsFinite(current.Residual) &&
                Math.Sign(previous.Residual) != Math.Sign(current.Residual))
            {
                var left = previousDepth;
                var right = currentDepth;
                var leftResidual = previous.Residual;
                for (var iteration = 0; iteration < 70; iteration++)
                {
                    var middle = Math.Sqrt(left * right);
                    var middleResult = Evaluate(middle);
                    if (!double.IsFinite(middleResult.Residual))
                    {
                        left = middle;
                        continue;
                    }
                    if (Math.Sign(leftResidual) == Math.Sign(middleResult.Residual))
                    {
                        left = middle;
                        leftResidual = middleResult.Residual;
                    }
                    else
                    {
                        right = middle;
                    }
                }
                var rootResult = Evaluate(Math.Sqrt(left * right));
                if (rootResult.N > 0)
                {
                    roots.Add(rootResult.N);
                }
            }
            previousDepth = currentDepth;
            previous = current;
        }
        if (roots.Count > 0)
        {
            return roots.Max();
        }

        var pureAxial = 0.9 *
            (fc * sectionDepth * sectionWidth +
             fy * barArea * barCoordinates.Count);
        return pureAxial /
               (1 + 2 * eccentricity / Math.Max(sectionDepth, 1e-6));
    }

    private static ShearDesign DesignStirrups(
        double lengthX,
        double widthY,
        double area,
        IReadOnlyList<SectionState> states,
        FoundationDesignSettings settings,
        RigidShortPileSettings rigid,
        string governingCase)
    {
        var effectiveDepthX =
            lengthX - settings.ConcreteCoverMm / 1000 -
            rigid.LongitudinalBarDiameterMm / 2000;
        var effectiveDepthY =
            widthY - settings.ConcreteCoverMm / 1000 -
            rigid.LongitudinalBarDiameterMm / 2000;
        var maximumShearX = states.Max(state => state.ShearXKn);
        var maximumShearY = states.Max(state => state.ShearYKn);
        var maximumAxial = states.Max(state => state.AxialKn);
        var fc = rigid.ConcreteCompressiveStrengthMpa * 1000;
        var ft = settings.ConcreteTensileStrengthMpa * 1000;
        var fy = settings.ReinforcementYieldStrengthMpa * 1000;
        var grossCapacityX = 0.25 * fc * widthY * effectiveDepthX;
        var grossCapacityY = 0.25 * fc * lengthX * effectiveDepthY;
        var checks = new List<FoundationCheckResult>
        {
            new()
            {
                Code = "RIGID_RECT_GROSS_SHEAR_X",
                Name = "矩形短柱桩X向受剪上限",
                Status = maximumShearX <= grossCapacityX
                    ? CheckStatus.Pass
                    : CheckStatus.Fail,
                Demand = maximumShearX,
                Capacity = grossCapacityX,
                Utilization = SafeRatio(maximumShearX, grossCapacityX),
                Unit = "kN",
                GoverningCase = governingCase,
                Explanation =
                    $"X向b={widthY:F3} m、h0={effectiveDepthX:F3} m，Vx={maximumShearX:F2} kN，受剪上限={grossCapacityX:F2} kN。",
                RuleReference = "GB/T 50010-2010（2024年版）第6.3.1、6.3.11条"
            },
            new()
            {
                Code = "RIGID_RECT_GROSS_SHEAR_Y",
                Name = "矩形短柱桩Y向受剪上限",
                Status = maximumShearY <= grossCapacityY
                    ? CheckStatus.Pass
                    : CheckStatus.Fail,
                Demand = maximumShearY,
                Capacity = grossCapacityY,
                Utilization = SafeRatio(maximumShearY, grossCapacityY),
                Unit = "kN",
                GoverningCase = governingCase,
                Explanation =
                    $"Y向b={lengthX:F3} m、h0={effectiveDepthY:F3} m，Vy={maximumShearY:F2} kN，受剪上限={grossCapacityY:F2} kN。",
                RuleReference = "GB/T 50010-2010（2024年版）第6.3.1、6.3.11条"
            }
        };
        var requiredX = RequiredAsPerS(
            maximumShearX,
            maximumAxial,
            ft,
            fy,
            fc,
            area,
            widthY,
            effectiveDepthX);
        var requiredY = RequiredAsPerS(
            maximumShearY,
            maximumAxial,
            ft,
            fy,
            fc,
            area,
            lengthX,
            effectiveDepthY);
        var required = Math.Max(requiredX, requiredY);
        var provided =
            rigid.StirrupLegCount * Pi *
            Math.Pow(rigid.StirrupDiameterMm / 1000, 2) / 4 /
            (rigid.StirrupSpacingMm / 1000);
        var requiredMm2PerM = required * 1_000_000;
        var providedMm2PerM = provided * 1_000_000;
        var stirrupStatus = provided + 1e-12 >= required
            ? CheckStatus.Pass
            : CheckStatus.Fail;
        checks.Add(new FoundationCheckResult
        {
            Code = "RIGID_RECT_STIRRUP_REINFORCEMENT",
            Name = "矩形刚性短柱桩箍筋",
            Status = stirrupStatus,
            Demand = requiredMm2PerM,
            Capacity = providedMm2PerM,
            Utilization = SafeRatio(requiredMm2PerM, providedMm2PerM),
            Unit = "mm²/m",
            GoverningCase = governingCase,
            Explanation =
                $"X向需{requiredX * 1_000_000:F0}、Y向需{requiredY * 1_000_000:F0} mm²/m；" +
                $"采用{rigid.StirrupLegCount}肢Φ{rigid.StirrupDiameterMm:F0}@{rigid.StirrupSpacingMm:F0}，实配{providedMm2PerM:F0} mm²/m。",
            RuleReference =
                "GB/T 50010-2010（2024年版）第6.3.12、6.3.16～6.3.18条"
        });
        return new ShearDesign(
            requiredMm2PerM,
            providedMm2PerM,
            stirrupStatus,
            checks);
    }

    private static double RequiredAsPerS(
        double shear,
        double axial,
        double ft,
        double fy,
        double fc,
        double area,
        double b,
        double h0)
    {
        const double lambda = 1.5;
        var axialLimit = 0.3 * fc * area;
        var concreteCapacity =
            1.75 * ft * b * h0 / (lambda + 1) +
            0.07 * Math.Min(axial, axialLimit);
        return Math.Max(0, (shear - concreteCapacity) / (fy * h0));
    }

    private static IReadOnlyList<BarPoint> BuildPerimeterBars(
        double lengthX,
        double widthY,
        double cover,
        double barDiameter,
        int count)
    {
        var halfX = Math.Max(0.01, lengthX / 2 - cover - barDiameter / 2);
        var halfY = Math.Max(0.01, widthY / 2 - cover - barDiameter / 2);
        var horizontalLength = 2 * halfX;
        var verticalLength = 2 * halfY;
        var perimeter = 2 * (horizontalLength + verticalLength);
        var bars = new List<BarPoint>(count);
        for (var index = 0; index < count; index++)
        {
            var distance = perimeter * index / count;
            if (distance <= horizontalLength)
            {
                bars.Add(new BarPoint(-halfX + distance, -halfY));
            }
            else if (distance <= horizontalLength + verticalLength)
            {
                bars.Add(new BarPoint(
                    halfX,
                    -halfY + distance - horizontalLength));
            }
            else if (distance <= 2 * horizontalLength + verticalLength)
            {
                bars.Add(new BarPoint(
                    halfX - (distance - horizontalLength - verticalLength),
                    halfY));
            }
            else
            {
                bars.Add(new BarPoint(
                    -halfX,
                    halfY - (distance - 2 * horizontalLength - verticalLength)));
            }
        }
        return bars;
    }

    private static double CalculateWeightedHorizontalCoefficient(
        IReadOnlyList<RigidShortPileSoilLayerInput> layers,
        double embeddedDepth,
        double projectedWidth)
    {
        var influenceDepth = Math.Min(embeddedDepth, 2 * (projectedWidth + 1));
        var currentDepth = 0d;
        var weighted = 0d;
        foreach (var layer in layers.Where(item => item.ThicknessM > 0))
        {
            if (currentDepth >= influenceDepth)
            {
                break;
            }
            var thickness = Math.Min(layer.ThicknessM, influenceDepth - currentDepth);
            weighted +=
                layer.HorizontalResistanceCoefficientMnPerM4 *
                (2 * currentDepth + thickness) * thickness;
            currentDepth += thickness;
        }
        if (currentDepth + 1e-9 < influenceDepth)
        {
            throw new ArgumentException(
                $"矩形刚性短柱桩m值分层厚度仅覆盖{currentDepth:F2} m，小于{projectedWidth:F2} m投影宽度对应的主要影响深度{influenceDepth:F2} m。");
        }
        return 1000 * 0.4 * weighted / (influenceDepth * influenceDepth);
    }

    private static double? SolveBeta(
        double sectionDepth,
        double soilWidth,
        double depth,
        double vertical,
        double shear,
        double frictionAngle,
        double soilPileFriction,
        double soilPressureCoefficient,
        double widthCoefficient)
    {
        double Equation(double beta)
        {
            var effectiveWidthFactor =
                1 + 2 * depth * widthCoefficient *
                Math.Cos(Pi / 4 + frictionAngle / 2) * Math.Tan(beta) /
                (3 * sectionDepth);
            var denominator =
                soilPressureCoefficient * effectiveWidthFactor * soilWidth *
                depth * depth * (1 + soilPileFriction * soilPileFriction);
            return beta * beta -
                   (shear + vertical * soilPileFriction) / denominator - 0.5;
        }
        return FindRootByScan(Equation, 0.05, 1.45, 600);
    }

    private static double FindMaximumMomentDepth(
        double shear,
        double k0,
        double displacement,
        double rotation,
        double depth)
    {
        double SectionShear(double y) =>
            shear - k0 * displacement * y * y / (2 * depth) +
            k0 * rotation * y * y * y / (3 * depth);
        return FindRootByScan(SectionShear, 0, depth, 800) ?? depth;
    }

    private static double? FindRootByScan(
        Func<double, double> equation,
        double start,
        double end,
        int segments)
    {
        var left = start;
        var leftValue = equation(left);
        for (var index = 1; index <= segments; index++)
        {
            var right = start + (end - start) * index / segments;
            var rightValue = equation(right);
            if (!double.IsFinite(leftValue) || !double.IsFinite(rightValue))
            {
                left = right;
                leftValue = rightValue;
                continue;
            }
            if (Math.Abs(leftValue) < 1e-10)
            {
                return left;
            }
            if (Math.Sign(leftValue) != Math.Sign(rightValue))
            {
                for (var iteration = 0; iteration < 80; iteration++)
                {
                    var middle = (left + right) / 2;
                    var middleValue = equation(middle);
                    if (Math.Abs(middleValue) < 1e-10)
                    {
                        return middle;
                    }
                    if (Math.Sign(leftValue) == Math.Sign(middleValue))
                    {
                        left = middle;
                        leftValue = middleValue;
                    }
                    else
                    {
                        right = middle;
                    }
                }
                return (left + right) / 2;
            }
            left = right;
            leftValue = rightValue;
        }
        return null;
    }

    private static FoundationCheckResult ResultOnlyCheck(
        string code,
        string name,
        double value,
        string unit,
        string explanation,
        string governingCase,
        string reference) => new()
        {
            Code = code,
            Name = name,
            Status = CheckStatus.Result,
            Demand = value,
            Capacity = 0,
            Utilization = 0,
            Unit = unit,
            GoverningCase = governingCase,
            Explanation = explanation,
            RuleReference = reference
        };

    private static FoundationCheckResult FailedCheck(
        string code,
        string name,
        string explanation,
        string governingCase,
        string reference) => new()
        {
            Code = code,
            Name = name,
            Status = CheckStatus.Fail,
            Demand = 1,
            Capacity = 0,
            Utilization = double.PositiveInfinity,
            Unit = string.Empty,
            GoverningCase = governingCase,
            Explanation = explanation,
            RuleReference = reference
        };

    private static FoundationScheme BuildFailedScheme(
        FoundationGeometry geometry,
        List<FoundationCheckResult> checks) => new()
        {
            FoundationType = FoundationType.RigidRectangularShortPile,
            Geometry = geometry,
            Checks = checks
        };

    private static void ValidateInputs(
        FoundationGeometry geometry,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings,
        RigidShortPileSettings rigid)
    {
        if (geometry.BaseLengthM <= 0 ||
            geometry.BaseWidthM <= 0 ||
            geometry.PileLengthM <= 0 ||
            geometry.PedestalHeightM < 0)
        {
            throw new ArgumentException(
                "矩形刚性短柱桩截面长、宽、埋深必须大于0，出地面高度不得小于0。");
        }
        if (appliedLoad.VerticalKn < 0)
        {
            throw new ArgumentException(
                "矩形刚性短柱桩当前模型只接受向下轴力为正的标准组合荷载。");
        }
        if (geotechnical.SoilUnitWeightKnPerM3 <= 0 ||
            geotechnical.InternalFrictionAngleDegree < 0 ||
            geotechnical.InternalFrictionAngleDegree >= 45)
        {
            throw new ArgumentException("土重度必须大于0，内摩擦角应在0～45°之间。");
        }
        if (rigid.SoilLayers.Count == 0 ||
            rigid.SoilLayers.Any(item =>
                item.ThicknessM <= 0 ||
                item.HorizontalResistanceCoefficientMnPerM4 < 0))
        {
            throw new ArgumentException("请按地勘报告完整填写矩形刚性短柱桩m值分层。");
        }
        if (rigid.LateralResistanceWidthCoefficient <= 0 ||
            rigid.VerticalReactionEccentricityCoefficient <= 0 ||
            rigid.ConcreteElasticModulusMpa <= 0 ||
            rigid.ConcreteCompressiveStrengthMpa <= 0 ||
            rigid.LongitudinalBarDiameterMm <= 0 ||
            rigid.LongitudinalBarCount < 8 ||
            rigid.StirrupDiameterMm <= 0 ||
            rigid.StirrupSpacingMm <= 0 ||
            rigid.StirrupLegCount < 2 ||
            settings.StructuralDesignLoadFactor <= 0 ||
            settings.FoundationPermanentLoadFactor <= 0 ||
            settings.ConcreteCoverMm <= 0 ||
            settings.ReinforcementYieldStrengthMpa <= 0 ||
            settings.ConcreteTensileStrengthMpa <= 0)
        {
            throw new ArgumentException("矩形刚性短柱桩土抗力、材料、纵筋或箍筋参数无效。");
        }
    }

    private static double SafeRatio(double demand, double capacity) =>
        capacity <= 0 ? double.PositiveInfinity : demand / capacity;

    private sealed record BarPoint(double X, double Y);

    private sealed record DirectionSectionForces(double ShearKn, double MomentKnM);

    private sealed record DirectionResponse(
        string Direction,
        double HorizontalKn,
        double GroundMomentKnM,
        double K0,
        double EmbeddedDepthM,
        double TopDisplacementM,
        double TopRotationRad,
        double RotationCenterDepthM,
        double MaximumMomentDepthM,
        List<FoundationCheckResult> Checks,
        bool IsValid)
    {
        public DirectionSectionForces SectionForces(double depth, double loadFactor)
        {
            var shear = loadFactor *
                (HorizontalKn - K0 * TopDisplacementM * depth * depth /
                    (2 * EmbeddedDepthM) +
                 K0 * TopRotationRad * depth * depth * depth /
                    (3 * EmbeddedDepthM));
            var moment = loadFactor *
                (GroundMomentKnM + HorizontalKn * depth -
                 K0 * TopDisplacementM * Math.Pow(depth, 3) /
                    (6 * EmbeddedDepthM) +
                 K0 * TopRotationRad * Math.Pow(depth, 4) /
                    (12 * EmbeddedDepthM));
            return new DirectionSectionForces(Math.Abs(shear), Math.Abs(moment));
        }

        public static DirectionResponse Invalid(
            string direction,
            List<FoundationCheckResult> checks) =>
            new(direction, 0, 0, 1, 1, 0, 0, 0, 0, checks, false);
    }

    private sealed record SectionState(
        double DepthM,
        double AxialKn,
        double ShearXKn,
        double ShearYKn,
        double MomentXKnM,
        double MomentYKnM);

    private sealed record LongitudinalDesign(
        double RequiredAreaMm2,
        double ProvidedAreaMm2,
        FoundationCheckResult CapacityCheck,
        FoundationCheckResult ReinforcementCheck);

    private sealed record ShearDesign(
        double RequiredAsPerSMm2PerM,
        double ProvidedAsPerSMm2PerM,
        CheckStatus StirrupStatus,
        IReadOnlyList<FoundationCheckResult> Checks);
}
