using TowerFoundation.Domain;

namespace TowerFoundation.Calculation;

public sealed class RigidShortPileFoundationCalculator
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

        var diameter = geometry.PileDiameterM;
        var embeddedDepth = geometry.PileLengthM;
        var aboveGroundHeight = geometry.PedestalHeightM;
        var area = Pi * diameter * diameter / 4;
        var vertical = Math.Max(0, appliedLoad.VerticalKn);
        var shear = Math.Sqrt(
            appliedLoad.ShearXKn * appliedLoad.ShearXKn +
            appliedLoad.ShearYKn * appliedLoad.ShearYKn);
        var moment = Math.Sqrt(
            appliedLoad.MomentXKnM * appliedLoad.MomentXKnM +
            appliedLoad.MomentYKnM * appliedLoad.MomentYKnM);
        var structuralLoad = appliedLoad.ResolveStructuralDesignLoad(settings);
        var structuralVertical = Math.Max(0, structuralLoad.VerticalKn);
        var structuralShear = Math.Sqrt(
            structuralLoad.ShearXKn * structuralLoad.ShearXKn +
            structuralLoad.ShearYKn * structuralLoad.ShearYKn);
        var structuralMoment = Math.Sqrt(
            structuralLoad.MomentXKnM * structuralLoad.MomentXKnM +
            structuralLoad.MomentYKnM * structuralLoad.MomentYKnM);

        var submergedLength = Math.Clamp(
            embeddedDepth - geotechnical.GroundwaterDepthM,
            0,
            embeddedDepth);
        var dryLength = embeddedDepth - submergedLength;
        var effectiveSelfWeight = area *
            ((dryLength + aboveGroundHeight) * settings.ConcreteUnitWeightKnPerM3 +
             submergedLength *
             (settings.ConcreteUnitWeightKnPerM3 - settings.WaterUnitWeightKnPerM3));

        var frictionAngleRad = geotechnical.InternalFrictionAngleDegree * Pi / 180;
        var soilPileFriction = Math.Tan(frictionAngleRad);
        var soilPressureCoefficient =
            geotechnical.SoilUnitWeightKnPerM3 *
            Math.Pow(Math.Tan(Pi / 4 + frictionAngleRad / 2), 2);
        var eccentricity = rigid.VerticalReactionEccentricityCoefficient * diameter;
        var beta = SolveBeta(
            diameter,
            embeddedDepth,
            vertical + effectiveSelfWeight,
            shear,
            frictionAngleRad,
            soilPileFriction,
            soilPressureCoefficient,
            rigid.LateralResistanceWidthCoefficient);

        var checks = new List<FoundationCheckResult>();
        if (beta is null)
        {
            checks.Add(FailedCheck(
                "RIGID_BETA",
                "刚性短柱桩参数β求解",
                "在0.05～1.45 rad范围内未找到平衡方程根，请检查土重度、内摩擦角、桩径、埋深及荷载。",
                appliedLoad.GoverningCase,
                "旧《单管塔规程计算刚性桩(yy).xls》6.2.2-5公式审计"));
            return BuildFailedScheme(geometry, checks);
        }

        var lateralWidth = diameter *
            (1 + 2 * embeddedDepth * rigid.LateralResistanceWidthCoefficient *
                Math.Cos(Pi / 4 + frictionAngleRad / 2) * Math.Tan(beta.Value) /
                (3 * diameter));
        var totalLateralResistance =
            soilPressureCoefficient * lateralWidth * embeddedDepth * embeddedDepth / 2;
        var baseVerticalReaction =
            ((vertical + effectiveSelfWeight) - shear * soilPileFriction) /
            (1 + soilPileFriction * soilPileFriction);
        var resistingMoment =
            2 * totalLateralResistance * embeddedDepth *
            (1 - 2 * Math.Pow(beta.Value, 3)) / 3 +
            baseVerticalReaction * (eccentricity + soilPileFriction * embeddedDepth) +
            soilPileFriction * diameter * totalLateralResistance / 2;
        var groundMoment = moment + shear * aboveGroundHeight;

        checks.Add(new FoundationCheckResult
        {
            Code = "RIGID_OVERTURNING",
            Name = "刚性短柱桩抗倾覆",
            Status = groundMoment <= resistingMoment / 2
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            Demand = groundMoment,
            Capacity = resistingMoment / 2,
            Utilization = SafeRatio(groundMoment, resistingMoment / 2),
            Unit = "kN·m",
            GoverningCase = appliedLoad.GoverningCase,
            Explanation =
                $"地面处作用弯矩Mkd={groundMoment:F2} kN·m；土侧向抗力E={totalLateralResistance:F2} kN，柱底竖向反力Fy={baseVerticalReaction:F2} kN，抗倾覆力矩Mu={resistingMoment:F2} kN·m，按Mkd≤Mu/2校核。",
            RuleReference =
                "旧《单管塔规程计算刚性桩(yy).xls》6.2.2-3～8公式审计；YD/T 5131-2019第7章适用边界"
        });

        var horizontalCoefficient = CalculateWeightedHorizontalCoefficient(
            rigid.SoilLayers,
            embeddedDepth,
            diameter);
        var reinforcementRatio =
            rigid.LongitudinalBarCount *
            Math.Pow(rigid.LongitudinalBarDiameterMm / (diameter * 1000), 2);
        var modularRatio =
            settings.ReinforcementYieldStrengthMpa > 0 && rigid.ConcreteElasticModulusMpa > 0
                ? 200_000d / rigid.ConcreteElasticModulusMpa
                : 1;
        var transformedInertia =
            Pi * diameter * diameter *
            (diameter * diameter +
             2 * (modularRatio - 1) * reinforcementRatio *
             Math.Pow(Math.Max(0, diameter - 0.1), 2)) / 64;
        var flexuralRigidity =
            0.85 * rigid.ConcreteElasticModulusMpa * 1000 * transformedInertia;
        var effectiveWidth = diameter > 1
            ? 0.9 * (diameter + 1)
            : 0.9 * (1.5 * diameter + 0.5);
        var deformationCoefficient = Math.Pow(
            horizontalCoefficient * effectiveWidth / flexuralRigidity,
            0.2);
        var alphaH = deformationCoefficient * embeddedDepth;
        checks.Add(new FoundationCheckResult
        {
            Code = "RIGID_CLASSIFICATION",
            Name = "刚性桩适用性",
            Status = alphaH <= 2.5 ? CheckStatus.Pass : CheckStatus.Fail,
            Demand = alphaH,
            Capacity = 2.5,
            Utilization = SafeRatio(alphaH, 2.5),
            Unit = string.Empty,
            GoverningCase = appliedLoad.GoverningCase,
            Explanation =
                $"按影响深度内分层m值折算m={horizontalCoefficient:F0} kN/m⁴，b0={effectiveWidth:F3} m，EI={flexuralRigidity:F0} kN·m²，αh={alphaH:F3}；仅αh≤2.5时采用刚性短柱桩模型。",
            RuleReference = "JGJ 94-2008第5.7.2、5.7.5条；旧计算书刚性桩适用性判别"
        });

        var k0 = horizontalCoefficient * embeddedDepth * diameter;
        var topDisplacement =
            24 * (moment + 0.75 * shear * embeddedDepth) /
            (k0 * embeddedDepth * embeddedDepth);
        var topRotation =
            12 * (3 * moment / embeddedDepth + 2 * shear) /
            (k0 * embeddedDepth * embeddedDepth);
        checks.Add(ResultOnlyCheck(
            "RIGID_TOP_DISPLACEMENT",
            "短柱桩顶水平位移",
            topDisplacement,
            "m",
            $"δk={topDisplacement:F5} m；原计算书只给出计算式，未给统一限值，须按塔型及连接要求确认。",
            appliedLoad.GoverningCase,
            "旧计算书6.2.2-9；JGJ 94-2008第5.7节及附录C水平变位方法边界"));
        checks.Add(ResultOnlyCheck(
            "RIGID_TOP_ROTATION",
            "短柱桩顶转角",
            topRotation,
            "rad",
            $"θk={topRotation:F6} rad；原计算书只给出计算式，未给统一限值，须按上部结构允许值确认。",
            appliedLoad.GoverningCase,
            "旧计算书6.2.2-11；JGJ 94-2008第5.7节及附录C水平变位方法边界"));

        checks.Add(new FoundationCheckResult
        {
            Code = "RIGID_STRUCTURAL_COMBINATION",
            Name = "刚性短柱桩结构基本组合",
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

        var structuralTopDisplacement =
            24 * (structuralMoment + 0.75 * structuralShear * embeddedDepth) /
            (k0 * embeddedDepth * embeddedDepth);
        var structuralTopRotation =
            12 * (3 * structuralMoment / embeddedDepth + 2 * structuralShear) /
            (k0 * embeddedDepth * embeddedDepth);
        var rotationCenter = Math.Clamp(
            topDisplacement / beta.Value,
            0,
            embeddedDepth);
        var rotationSection = CalculateSectionForces(
            rotationCenter,
            area,
            aboveGroundHeight,
            structuralVertical,
            structuralShear,
            structuralMoment,
            k0,
            structuralTopDisplacement,
            structuralTopRotation,
            embeddedDepth,
            settings.FoundationPermanentLoadFactor *
            settings.StructureImportanceFactor,
            settings.ConcreteUnitWeightKnPerM3);
        var maximumMomentDepth = FindMaximumMomentDepth(
            structuralShear,
            k0,
            structuralTopDisplacement,
            structuralTopRotation,
            embeddedDepth);
        var maximumMomentSection = CalculateSectionForces(
            maximumMomentDepth,
            area,
            aboveGroundHeight,
            structuralVertical,
            structuralShear,
            structuralMoment,
            k0,
            structuralTopDisplacement,
            structuralTopRotation,
            embeddedDepth,
            settings.FoundationPermanentLoadFactor *
            settings.StructureImportanceFactor,
            settings.ConcreteUnitWeightKnPerM3);
        checks.Add(ResultOnlyCheck(
            "RIGID_INTERNAL_FORCE",
            "最不利截面内力",
            maximumMomentSection.MomentKnM,
            "kN·m",
            $"旋转中心y={rotationCenter:F3} m：N={rotationSection.AxialKn:F2} kN、V={rotationSection.ShearKn:F2} kN、M={rotationSection.MomentKnM:F2} kN·m；最大弯矩截面y={maximumMomentDepth:F3} m：N={maximumMomentSection.AxialKn:F2} kN、V={maximumMomentSection.ShearKn:F2} kN、M={maximumMomentSection.MomentKnM:F2} kN·m。",
            structuralLoad.GoverningCase,
            "旧计算书6.2.2-12～14公式审计；最大弯矩项按土抗力平衡式纠正原表疑似单元格引用错误"));

        var longitudinal = DesignLongitudinalReinforcement(
            diameter,
            rotationSection,
            maximumMomentSection,
            settings,
            rigid,
            structuralLoad.GoverningCase);
        checks.Add(longitudinal.Check);

        var shearDesign = DesignStirrups(
            diameter,
            rotationSection,
            maximumMomentSection,
            settings,
            rigid,
            structuralLoad.GoverningCase);
        checks.Add(shearDesign.GrossShearCheck);
        checks.Add(shearDesign.StirrupCheck);
        checks.Add(new FoundationCheckResult
        {
            Code = "RIGID_REMAINING_SCOPE",
            Name = "刚性短柱桩剩余专项范围",
            Status = CheckStatus.SpecialReview,
            Demand = 0,
            Capacity = 0,
            Utilization = 0,
            Unit = string.Empty,
            GoverningCase = appliedLoad.GoverningCase,
            Explanation =
                "已计算刚性判别、抗倾覆、位移/转角、最不利内力、圆形截面纵筋及箍筋；沉降、裂缝、锚栓连接、特殊地基、抗震和施工构造仍需专项复核。",
            RuleReference = "计算范围门禁（2026-08-02）"
        });

        var totalHeight = embeddedDepth + aboveGroundHeight;
        var longitudinalLength = rigid.LongitudinalBarCount * totalHeight;
        var longitudinalUnitWeight =
            rigid.LongitudinalBarDiameterMm * rigid.LongitudinalBarDiameterMm / 162;
        var hoopCenterDiameter = Math.Max(
            0,
            diameter - 2 * settings.ConcreteCoverMm / 1000 -
            rigid.StirrupDiameterMm / 1000);
        var hoopCount =
            (int)Math.Floor(totalHeight * 1000 / rigid.StirrupSpacingMm) + 1;
        var hoopLength = Pi * hoopCenterDiameter;
        var stirrupUnitWeight =
            rigid.StirrupDiameterMm * rigid.StirrupDiameterMm / 162;
        var reinforcementDesigns = new List<ReinforcementDesignResult>
        {
            new()
            {
                Component = "刚性短柱桩纵筋",
                Direction = "周向均布",
                BarSpecification = $"{rigid.LongitudinalBarCount}Φ{rigid.LongitudinalBarDiameterMm:F0}",
                RequiredAreaMm2 = longitudinal.RequiredAreaMm2,
                ProvidedAreaMm2 = longitudinal.ProvidedAreaMm2,
                BarCount = rigid.LongitudinalBarCount,
                BarDiameterMm = rigid.LongitudinalBarDiameterMm,
                BarSpacingMm = 0,
                SingleBarLengthM = totalHeight,
                TotalLengthM = longitudinalLength,
                UnitWeightKgPerM = longitudinalUnitWeight,
                CalculatedWeightKg = longitudinalLength * longitudinalUnitWeight,
                Status = longitudinal.Check.Status,
                RuleReference = longitudinal.Check.RuleReference
            },
            new()
            {
                Component = "刚性短柱桩箍筋",
                Direction = "环向",
                BarSpecification = $"Φ{rigid.StirrupDiameterMm:F0}@{rigid.StirrupSpacingMm:F0}",
                RequiredAreaMm2 = shearDesign.RequiredAsPerSMm2PerM,
                ProvidedAreaMm2 = shearDesign.ProvidedAsPerSMm2PerM,
                BarCount = hoopCount,
                BarDiameterMm = rigid.StirrupDiameterMm,
                BarSpacingMm = rigid.StirrupSpacingMm,
                SingleBarLengthM = hoopLength,
                TotalLengthM = hoopCount * hoopLength,
                UnitWeightKgPerM = stirrupUnitWeight,
                CalculatedWeightKg = hoopCount * hoopLength * stirrupUnitWeight,
                Status = shearDesign.StirrupCheck.Status,
                RuleReference = shearDesign.StirrupCheck.RuleReference
            }
        };
        var concreteVolume = area * totalHeight;
        return FoundationUnitQuantityScaler.Apply(new FoundationScheme
        {
            FoundationType = FoundationType.RigidShortPile,
            Geometry = geometry,
            Checks = checks,
            ReinforcementDesigns = reinforcementDesigns,
            Quantities = new QuantitySummary
            {
                ConcreteM3 = concreteVolume,
                ExcavationM3 = area * embeddedDepth,
                BackfillM3 = 0,
                EstimatedReinforcementKg = reinforcementDesigns.Sum(item => item.CalculatedWeightKg)
            }
        });
    }

    private static double CalculateWeightedHorizontalCoefficient(
        IReadOnlyList<RigidShortPileSoilLayerInput> layers,
        double embeddedDepth,
        double diameter)
    {
        var influenceDepth = Math.Min(embeddedDepth, 2 * (diameter + 1));
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
                $"刚性短柱桩m值分层厚度仅覆盖{currentDepth:F2} m，小于主要影响深度{influenceDepth:F2} m。");
        }
        return 1000 * 0.4 * weighted / (influenceDepth * influenceDepth);
    }

    private static double? SolveBeta(
        double diameter,
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
                (3 * diameter);
            var denominator =
                soilPressureCoefficient * effectiveWidthFactor * diameter *
                depth * depth * (1 + soilPileFriction * soilPileFriction);
            return beta * beta -
                   (shear + vertical * soilPileFriction) / denominator - 0.5;
        }
        return FindRootByScan(Equation, 0.05, 1.45, 600);
    }

    private static SectionForces CalculateSectionForces(
        double y,
        double area,
        double aboveGroundHeight,
        double vertical,
        double shear,
        double moment,
        double k0,
        double displacement,
        double rotation,
        double depth,
        double permanentLoadFactor,
        double concreteUnitWeight)
    {
        var axial = vertical + permanentLoadFactor * area *
            (aboveGroundHeight + y) * concreteUnitWeight;
        var sectionShear =
            shear - k0 * displacement * y * y / (2 * depth) +
            k0 * rotation * y * y * y / (3 * depth);
        var sectionMoment =
            moment + shear * y - k0 * displacement * Math.Pow(y, 3) / (6 * depth) +
            k0 * rotation * Math.Pow(y, 4) / (12 * depth);
        return new SectionForces(axial, Math.Abs(sectionShear), Math.Abs(sectionMoment));
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

    private static LongitudinalDesign DesignLongitudinalReinforcement(
        double diameter,
        SectionForces first,
        SectionForces second,
        FoundationDesignSettings settings,
        RigidShortPileSettings rigid,
        string governingCase)
    {
        var area = Pi * diameter * diameter / 4;
        var radius = diameter / 2;
        var steelRadius = radius - settings.ConcreteCoverMm / 1000;
        var fc = rigid.ConcreteCompressiveStrengthMpa * 1000;
        var fy = settings.ReinforcementYieldStrengthMpa * 1000;
        var required = Math.Max(
            RequiredCircularArea(first.AxialKn, first.MomentKnM, fc, fy, area, radius, steelRadius),
            RequiredCircularArea(second.AxialKn, second.MomentKnM, fc, fy, area, radius, steelRadius));
        required = Math.Max(required, rigid.MinimumLongitudinalReinforcementRatio * area);
        var provided =
            rigid.LongitudinalBarCount * Pi *
            Math.Pow(rigid.LongitudinalBarDiameterMm / 1000, 2) / 4;
        var requiredMm2 = required * 1_000_000;
        var providedMm2 = provided * 1_000_000;
        var check = new FoundationCheckResult
        {
            Code = "RIGID_LONGITUDINAL_REINFORCEMENT",
            Name = "刚性短柱桩纵向钢筋",
            Status = provided + 1e-12 >= required ? CheckStatus.Pass : CheckStatus.Fail,
            Demand = requiredMm2,
            Capacity = providedMm2,
            Utilization = SafeRatio(requiredMm2, providedMm2),
            Unit = "mm²",
            GoverningCase = governingCase,
            Explanation =
                $"按旋转中心和最大弯矩截面圆形偏心受压计算并与最小配筋率{rigid.MinimumLongitudinalReinforcementRatio:P2}比较，需As={requiredMm2:F0} mm²；采用{rigid.LongitudinalBarCount}Φ{rigid.LongitudinalBarDiameterMm:F0}，实配As={providedMm2:F0} mm²。",
            RuleReference = "GB/T 50010-2010（2024年版）附录E；旧计算书E.0.4公式审计"
        };
        return new LongitudinalDesign(requiredMm2, providedMm2, check);
    }

    private static double RequiredCircularArea(
        double axial,
        double moment,
        double fc,
        double fy,
        double area,
        double radius,
        double steelRadius)
    {
        double Equation(double alpha)
        {
            var sinAlpha = Math.Sin(Pi * alpha);
            var concreteAxial =
                alpha * fc * area *
                (1 - Math.Sin(2 * Pi * alpha) / (2 * Pi * alpha));
            return steelRadius *
                       (sinAlpha + Math.Sin(1.25 * Pi - 2 * Pi * alpha)) *
                       (axial - concreteAxial) -
                   Pi * (3 * alpha - 1.25) *
                       (moment - 2 * fc * area * radius * Math.Pow(sinAlpha, 3) / (3 * Pi));
        }
        var alpha = FindRootByScan(Equation, 0.05, 0.40, 800);
        if (alpha is null)
        {
            return 0;
        }
        var concreteAxialAtRoot =
            alpha.Value * fc * area *
            (1 - Math.Sin(2 * Pi * alpha.Value) / (2 * Pi * alpha.Value));
        var denominator = (3 * alpha.Value - 1.25) * fy;
        return Math.Max(0, (axial - concreteAxialAtRoot) / denominator);
    }

    private static ShearDesign DesignStirrups(
        double diameter,
        SectionForces first,
        SectionForces second,
        FoundationDesignSettings settings,
        RigidShortPileSettings rigid,
        string governingCase)
    {
        var radius = diameter / 2;
        var b = 1.76 * radius;
        var h0 = 1.60 * radius;
        var fc = rigid.ConcreteCompressiveStrengthMpa * 1000;
        var ft = settings.ConcreteTensileStrengthMpa * 1000;
        var fy = settings.ReinforcementYieldStrengthMpa * 1000;
        var grossCapacity = 0.25 * fc * b * h0;
        var maximumShear = Math.Max(first.ShearKn, second.ShearKn);
        var grossCheck = new FoundationCheckResult
        {
            Code = "RIGID_GROSS_SHEAR",
            Name = "刚性短柱桩斜截面受剪上限",
            Status = maximumShear <= grossCapacity ? CheckStatus.Pass : CheckStatus.Fail,
            Demand = maximumShear,
            Capacity = grossCapacity,
            Utilization = SafeRatio(maximumShear, grossCapacity),
            Unit = "kN",
            GoverningCase = governingCase,
            Explanation =
                $"换算矩形截面b={b:F3} m、h0={h0:F3} m，最大设计剪力V={maximumShear:F2} kN，0.25βc·fc·b·h0={grossCapacity:F2} kN。",
            RuleReference = "GB/T 50010-2010（2024年版）第6.3.1、6.3.15条"
        };
        var firstRequired = RequiredAsPerS(first, ft, fy, fc, b, h0);
        var secondRequired = RequiredAsPerS(second, ft, fy, fc, b, h0);
        var required = Math.Max(firstRequired, secondRequired);
        var provided =
            rigid.StirrupLegCount * Pi *
            Math.Pow(rigid.StirrupDiameterMm / 1000, 2) / 4 /
            (rigid.StirrupSpacingMm / 1000);
        var requiredMm2PerM = required * 1_000_000;
        var providedMm2PerM = provided * 1_000_000;
        var stirrupCheck = new FoundationCheckResult
        {
            Code = "RIGID_STIRRUP_REINFORCEMENT",
            Name = "刚性短柱桩箍筋",
            Status = provided + 1e-12 >= required ? CheckStatus.Pass : CheckStatus.Fail,
            Demand = requiredMm2PerM,
            Capacity = providedMm2PerM,
            Utilization = SafeRatio(requiredMm2PerM, providedMm2PerM),
            Unit = "mm²/m",
            GoverningCase = governingCase,
            Explanation = required <= 1e-12
                ? $"混凝土及轴力项已满足受剪，按构造采用Φ{rigid.StirrupDiameterMm:F0}@{rigid.StirrupSpacingMm:F0}。"
                : $"两控制截面需Asv/s={requiredMm2PerM:F0} mm²/m；{rigid.StirrupLegCount}肢Φ{rigid.StirrupDiameterMm:F0}@{rigid.StirrupSpacingMm:F0}实配{providedMm2PerM:F0} mm²/m。",
            RuleReference = "GB/T 50010-2010（2024年版）第6.3.12、6.3.13、6.3.15条"
        };
        return new ShearDesign(
            requiredMm2PerM,
            providedMm2PerM,
            grossCheck,
            stirrupCheck);
    }

    private static double RequiredAsPerS(
        SectionForces section,
        double ft,
        double fy,
        double fc,
        double b,
        double h0)
    {
        const double lambda = 1.5;
        var axialLimit = 0.3 * fc * Pi * Math.Pow(b / 1.76 * 2, 2) / 4;
        var axial = Math.Min(section.AxialKn, axialLimit);
        var concreteCapacity = 1.75 * ft * b * h0 / (lambda + 1) + 0.07 * axial;
        return Math.Max(0, (section.ShearKn - concreteCapacity) / (fy * h0));
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
            FoundationType = FoundationType.RigidShortPile,
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
        if (geometry.PileDiameterM <= 0 ||
            geometry.PileLengthM <= 0 ||
            geometry.PedestalHeightM < 0)
        {
            throw new ArgumentException("刚性短柱桩直径、埋深必须大于0，出地面高度不得小于0。");
        }
        if (appliedLoad.VerticalKn < 0)
        {
            throw new ArgumentException("刚性短柱桩当前模型只接受向下轴力为正的标准组合荷载。");
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
            throw new ArgumentException("请按地勘报告完整填写刚性短柱桩m值分层。");
        }
        if (rigid.LateralResistanceWidthCoefficient <= 0 ||
            rigid.VerticalReactionEccentricityCoefficient <= 0 ||
            rigid.ConcreteElasticModulusMpa <= 0 ||
            rigid.ConcreteCompressiveStrengthMpa <= 0 ||
            rigid.LongitudinalBarDiameterMm <= 0 ||
            rigid.LongitudinalBarCount < 6 ||
            rigid.StirrupDiameterMm <= 0 ||
            rigid.StirrupSpacingMm <= 0 ||
            rigid.StirrupLegCount <= 0 ||
            settings.StructuralDesignLoadFactor <= 0)
        {
            throw new ArgumentException("刚性短柱桩土抗力、材料、纵筋或箍筋参数无效。");
        }
    }

    private static double SafeRatio(double demand, double capacity) =>
        capacity <= 0 ? double.PositiveInfinity : demand / capacity;

    private sealed record SectionForces(
        double AxialKn,
        double ShearKn,
        double MomentKnM);

    private sealed record LongitudinalDesign(
        double RequiredAreaMm2,
        double ProvidedAreaMm2,
        FoundationCheckResult Check);

    private sealed record ShearDesign(
        double RequiredAsPerSMm2PerM,
        double ProvidedAsPerSMm2PerM,
        FoundationCheckResult GrossShearCheck,
        FoundationCheckResult StirrupCheck);
}
