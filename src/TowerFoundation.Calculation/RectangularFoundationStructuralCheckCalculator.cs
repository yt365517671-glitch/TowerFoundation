using TowerFoundation.Domain;

namespace TowerFoundation.Calculation;

internal static class RectangularFoundationStructuralCheckCalculator
{
    public static IReadOnlyList<FoundationCheckResult> Calculate(
        FoundationGeometry geometry,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings,
        double effectiveFoundationAndSoilWeightKn)
    {
        ValidateSettings(settings);

        var effectiveHeightMm =
            geometry.BaseThicknessM * 1000 -
            settings.ConcreteCoverMm -
            settings.BottomBarDiameterMm / 2;
        if (effectiveHeightMm <= 0)
        {
            throw new ArgumentException("基础底板有效高度必须大于0。");
        }

        var effectiveHeightM = effectiveHeightMm / 1000;
        var concreteTensileStrengthKpa = settings.ConcreteTensileStrengthMpa * 1000;
        var importance = settings.StructureImportanceFactor;
        var structuralLoad = appliedLoad.ResolveStructuralDesignLoad(settings);
        var permanentFactor = settings.FoundationPermanentLoadFactor * importance;
        var designFoundationAndSoilWeight =
            effectiveFoundationAndSoilWeightKn * permanentFactor;
        var designVertical = structuralLoad.VerticalKn;
        var designMomentX =
            Math.Abs(structuralLoad.MomentXKnM) +
            Math.Abs(structuralLoad.ShearYKn) * geometry.EmbedmentDepthM;
        var designMomentY =
            Math.Abs(structuralLoad.MomentYKnM) +
            Math.Abs(structuralLoad.ShearXKn) * geometry.EmbedmentDepthM;
        var totalDesignVertical =
            designVertical + designFoundationAndSoilWeight;
        var area = geometry.BaseLengthM * geometry.BaseWidthM;
        var grossAveragePressure = totalDesignVertical / area;
        var pressureFromMomentX =
            6 * designMomentX /
            (geometry.BaseLengthM * Math.Pow(geometry.BaseWidthM, 2));
        var pressureFromMomentY =
            6 * designMomentY /
            (geometry.BaseWidthM * Math.Pow(geometry.BaseLengthM, 2));
        var grossMaximumPressure =
            grossAveragePressure + pressureFromMomentX + pressureFromMomentY;
        var grossMinimumPressure =
            grossAveragePressure - pressureFromMomentX - pressureFromMomentY;
        var designPermanentPressure = designFoundationAndSoilWeight / area;
        var maximumNetPressure =
            Math.Max(0, grossMaximumPressure - designPermanentPressure);
        var averageNetPressure = Math.Max(0, designVertical / area);

        var checks = new List<FoundationCheckResult>
        {
            new()
            {
                Code = "STRUCTURAL_COMBINATION",
                Name = "结构设计组合参数",
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
                Explanation =
                    appliedLoad.DescribeStructuralCombination(settings) + "；" +
                    $"基础及覆土永久作用系数{settings.FoundationPermanentLoadFactor:F2}，结构重要性系数{settings.StructureImportanceFactor:F2}。",
                RuleReference = "GB 50068-2018第8.2.9条；项目结构组合参数"
            }
        };

        AddPunchingOrShearChecks(
            checks,
            "X",
            geometry.BaseLengthM,
            geometry.BaseWidthM,
            geometry.PedestalLengthM,
            geometry.PedestalWidthM,
            geometry.BaseThicknessM,
            effectiveHeightM,
            maximumNetPressure,
            averageNetPressure,
            concreteTensileStrengthKpa,
            structuralLoad.GoverningCase);
        AddPunchingOrShearChecks(
            checks,
            "Y",
            geometry.BaseWidthM,
            geometry.BaseLengthM,
            geometry.PedestalWidthM,
            geometry.PedestalLengthM,
            geometry.BaseThicknessM,
            effectiveHeightM,
            maximumNetPressure,
            averageNetPressure,
            concreteTensileStrengthKpa,
            structuralLoad.GoverningCase);

        var projectionX =
            (geometry.BaseLengthM - geometry.PedestalLengthM) / 2;
        var projectionY =
            (geometry.BaseWidthM - geometry.PedestalWidthM) / 2;
        var eccentricityX = totalDesignVertical > 0
            ? designMomentY / totalDesignVertical
            : double.PositiveInfinity;
        var eccentricityY = totalDesignVertical > 0
            ? designMomentX / totalDesignVertical
            : double.PositiveInfinity;
        var hasMomentX = designMomentX > 1e-9;
        var hasMomentY = designMomentY > 1e-9;
        var isBiaxialEccentric = hasMomentX && hasMomentY;
        var applicabilityUtilization = new[]
        {
            projectionX / (2.5 * geometry.BaseThicknessM),
            projectionY / (2.5 * geometry.BaseThicknessM),
            eccentricityX / (geometry.BaseLengthM / 6),
            eccentricityY / (geometry.BaseWidthM / 6),
            grossMinimumPressure >= 0 ? 0 : double.PositiveInfinity
        }.Max();
        var bendingApplicable =
            double.IsFinite(applicabilityUtilization) &&
            applicabilityUtilization <= 1 + 1e-9;

        checks.Add(new FoundationCheckResult
        {
            Code = "BENDING_APPLICABILITY",
            Name = isBiaxialEccentric
                ? "底板双向偏心保守包络适用条件"
                : "底板简化受弯公式适用条件",
            Status = bendingApplicable ? CheckStatus.Pass : CheckStatus.Fail,
            Demand = applicabilityUtilization,
            Capacity = 1,
            Utilization = applicabilityUtilization,
            Unit = "无量纲",
            GoverningCase = structuralLoad.GoverningCase,
            Explanation =
                $"X/Y向悬挑宽厚比为{projectionX / geometry.BaseThicknessM:F2}/" +
                $"{projectionY / geometry.BaseThicknessM:F2}（均应≤2.50）；" +
                $"X/Y向偏心距为{eccentricityX:F3}/{eccentricityY:F3} m，" +
                $"限值为{geometry.BaseLengthM / 6:F3}/{geometry.BaseWidthM / 6:F3} m；" +
                $"基本组合基底最小反力{grossMinimumPressure:F2} kPa；" +
                (isBiaxialEccentric
                    ? "当前为双向偏心荷载，不直接套用单向偏心结果；后续两个方向均叠加正交方向最不利边缘压力，按保守条带包络计算。"
                    : "当前为轴心或单向偏心荷载。"),
            RuleReference = "GB 50007-2011第8.2.11条"
        });

        if (bendingApplicable)
        {
            AddBendingAndReinforcementCheck(
                checks,
                "X",
                geometry.BaseLengthM,
                geometry.BaseWidthM,
                geometry.PedestalLengthM,
                geometry.PedestalWidthM,
                pressureFromMomentY,
                isBiaxialEccentric ? pressureFromMomentX : 0,
                grossAveragePressure,
                designPermanentPressure,
                geometry.BaseThicknessM,
                effectiveHeightMm,
                settings,
                structuralLoad.GoverningCase);
            AddBendingAndReinforcementCheck(
                checks,
                "Y",
                geometry.BaseWidthM,
                geometry.BaseLengthM,
                geometry.PedestalWidthM,
                geometry.PedestalLengthM,
                pressureFromMomentX,
                isBiaxialEccentric ? pressureFromMomentY : 0,
                grossAveragePressure,
                designPermanentPressure,
                geometry.BaseThicknessM,
                effectiveHeightMm,
                settings,
                structuralLoad.GoverningCase);
        }
        else
        {
            checks.Add(BuildNotEvaluated(
                "BOTTOM_REINFORCEMENT_X",
                "X向底板受弯及配筋",
                structuralLoad.GoverningCase));
            checks.Add(BuildNotEvaluated(
                "BOTTOM_REINFORCEMENT_Y",
                "Y向底板受弯及配筋",
                structuralLoad.GoverningCase));
        }

        if (Math.Abs(structuralLoad.TorsionKnM) > 1e-9)
        {
            checks.Add(new FoundationCheckResult
            {
                Code = "TORSION_SCOPE",
                Name = "基础端扭矩适用范围",
                Status = CheckStatus.SpecialReview,
                Demand = Math.Abs(structuralLoad.TorsionKnM),
                Capacity = 0,
                Utilization = 0,
                Unit = "kN·m",
                GoverningCase = structuralLoad.GoverningCase,
                Explanation =
                    $"已记录基本组合基础端扭矩{Math.Abs(structuralLoad.TorsionKnM):F2} kN·m；" +
                    "当前扭矩不参与底板受弯公式，应由短柱、锚栓及连接构造专项验算。",
                RuleReference = "结构计算范围门禁"
            });
        }

        return checks;
    }

    private static void AddPunchingOrShearChecks(
        ICollection<FoundationCheckResult> checks,
        string direction,
        double baseDimension,
        double orthogonalBaseDimension,
        double pedestalDimension,
        double orthogonalPedestalDimension,
        double baseThickness,
        double effectiveHeight,
        double maximumNetPressure,
        double averageNetPressure,
        double concreteTensileStrengthKpa,
        string governingCase)
    {
        var lowerConeDimension = pedestalDimension + 2 * effectiveHeight;
        var lowerConeOrthogonalDimension = Math.Min(
            orthogonalBaseDimension,
            orthogonalPedestalDimension + 2 * effectiveHeight);
        var betaHp = CalculatePunchingHeightFactor(baseThickness);

        if (lowerConeDimension < baseDimension - 1e-9)
        {
            var projection = (baseDimension - lowerConeDimension) / 2;
            var punchingArea =
                projection *
                (orthogonalBaseDimension + lowerConeOrthogonalDimension) /
                2;
            var am =
                (orthogonalPedestalDimension + lowerConeOrthogonalDimension) /
                2;
            var demand = maximumNetPressure * punchingArea;
            var capacity =
                0.7 *
                betaHp *
                concreteTensileStrengthKpa *
                am *
                effectiveHeight;
            checks.Add(BuildCheck(
                $"PUNCHING_{direction}",
                $"{direction}向柱边冲切",
                demand,
                capacity,
                "kN",
                $"最大净反力pj={maximumNetPressure:F2} kPa，冲切取用面积Al={punchingArea:F3} m²，" +
                $"βhp={betaHp:F3}，am={am:F3} m，h0={effectiveHeight:F3} m。",
                governingCase,
                "GB 50007-2011式(8.2.8-1)～式(8.2.8-3)"));
            return;
        }

        var shearProjection = Math.Max(0, (baseDimension - pedestalDimension) / 2);
        var betaHs = Math.Pow(
            800 / Math.Clamp(effectiveHeight * 1000, 800, 2000),
            0.25);
        var demandShear =
            averageNetPressure * shearProjection * orthogonalBaseDimension;
        var effectiveArea = orthogonalBaseDimension * effectiveHeight;
        var capacityShear =
            0.7 *
            betaHs *
            concreteTensileStrengthKpa *
            effectiveArea;
        checks.Add(BuildCheck(
            $"SHEAR_{direction}",
            $"{direction}向柱边受剪",
            demandShear,
            capacityShear,
            "kN",
            $"冲切锥底已到达基础边缘，按受剪验算；平均净反力{averageNetPressure:F2} kPa，" +
            $"βhs={betaHs:F3}，有效截面面积A0={effectiveArea:F3} m²。",
            governingCase,
            "GB 50007-2011式(8.2.9-1)、式(8.2.9-2)"));
    }

    private static void AddBendingAndReinforcementCheck(
        ICollection<FoundationCheckResult> checks,
        string direction,
        double baseDimension,
        double orthogonalBaseDimension,
        double pedestalDimension,
        double orthogonalPedestalDimension,
        double pressureFromAxisMoment,
        double orthogonalPressureEnvelope,
        double grossAveragePressure,
        double permanentPressure,
        double baseThickness,
        double effectiveHeightMm,
        FoundationDesignSettings settings,
        string governingCase)
    {
        var projection = (baseDimension - pedestalDimension) / 2;
        var edgeMaximum =
            grossAveragePressure +
            orthogonalPressureEnvelope +
            pressureFromAxisMoment;
        var edgeMinimum =
            grossAveragePressure +
            orthogonalPressureEnvelope -
            pressureFromAxisMoment;
        var pressureAtPedestalFace =
            edgeMaximum -
            (edgeMaximum - edgeMinimum) * projection / baseDimension;
        var bendingMoment = projection * projection / 12 *
                            ((2 * orthogonalBaseDimension +
                              orthogonalPedestalDimension) *
                             (edgeMaximum +
                              pressureAtPedestalFace -
                              2 * permanentPressure) +
                             (edgeMaximum - pressureAtPedestalFace) *
                             orthogonalBaseDimension);
        bendingMoment = Math.Max(0, bendingMoment);

        var calculatedArea =
            bendingMoment * 1_000_000 /
            (0.9 *
             settings.ReinforcementYieldStrengthMpa *
             effectiveHeightMm);
        var minimumArea =
            settings.MinimumReinforcementRatio *
            orthogonalBaseDimension * 1000 *
            baseThickness * 1000;
        var requiredArea = Math.Max(calculatedArea, minimumArea);
        var usableDistributionWidthMm = Math.Max(
            0,
            orthogonalBaseDimension * 1000 -
            2 * settings.ConcreteCoverMm);
        var barCount =
            (int)Math.Floor(
                usableDistributionWidthMm /
                settings.BottomBarSpacingMm) +
            1;
        var singleBarArea =
            Math.PI * Math.Pow(settings.BottomBarDiameterMm, 2) / 4;
        var actualArea = barCount * singleBarArea;

        checks.Add(BuildCheck(
            $"BOTTOM_REINFORCEMENT_{direction}",
            $"{direction}向底板受弯及底筋",
            requiredArea,
            actualArea,
            "mm²",
            $"M={bendingMoment:F2} kN·m，" +
            (orthogonalPressureEnvelope > 0
                ? $"已叠加正交方向最不利边缘压力{orthogonalPressureEnvelope:F2} kPa，"
                : string.Empty) +
            $"计算As={calculatedArea:F0} mm²，" +
            $"最小配筋As,min={minimumArea:F0} mm²；采用Φ{settings.BottomBarDiameterMm:F0}@" +
            $"{settings.BottomBarSpacingMm:F0}，共{barCount}根，实配As={actualArea:F0} mm²。",
            governingCase,
            orthogonalPressureEnvelope > 0
                ? "GB 50007-2011第8.2.11条、式(8.2.12)；双向偏心最不利条带保守包络"
                : "GB 50007-2011式(8.2.11-1)、式(8.2.12)"));
    }

    private static FoundationCheckResult BuildCheck(
        string code,
        string name,
        double demand,
        double capacity,
        string unit,
        string explanation,
        string governingCase,
        string ruleReference)
    {
        var utilization = SafeRatio(demand, capacity);
        return new FoundationCheckResult
        {
            Code = code,
            Name = name,
            Status = utilization <= 1 ? CheckStatus.Pass : CheckStatus.Fail,
            Demand = demand,
            Capacity = capacity,
            Utilization = utilization,
            Unit = unit,
            Explanation = explanation,
            GoverningCase = governingCase,
            RuleReference = ruleReference
        };
    }

    private static FoundationCheckResult BuildNotEvaluated(
        string code,
        string name,
        string governingCase)
    {
        return new FoundationCheckResult
        {
            Code = code,
            Name = name,
            Status = CheckStatus.NotEvaluated,
            Demand = 0,
            Capacity = 0,
            Utilization = double.PositiveInfinity,
            Unit = string.Empty,
            GoverningCase = governingCase,
            Explanation =
                "当前几何、偏心或基底接触状态超出GB 50007-2011第8.2.11条简化公式适用范围，未生成配筋通过结论。",
            RuleReference = "GB 50007-2011第8.2.11条适用性门禁"
        };
    }

    private static double CalculatePunchingHeightFactor(double heightM)
    {
        if (heightM <= 0.8)
        {
            return 1;
        }

        if (heightM >= 2.0)
        {
            return 0.9;
        }

        return 1 - (heightM - 0.8) / 12;
    }

    private static double SafeRatio(double numerator, double denominator)
    {
        if (Math.Abs(numerator) < 1e-12)
        {
            return 0;
        }

        return denominator > 0
            ? numerator / denominator
            : double.PositiveInfinity;
    }

    private static void ValidateSettings(FoundationDesignSettings settings)
    {
        if (settings.StructuralDesignLoadFactor <= 0 ||
            settings.FoundationPermanentLoadFactor <= 0 ||
            settings.StructureImportanceFactor <= 0 ||
            settings.ConcreteTensileStrengthMpa <= 0 ||
            settings.ReinforcementYieldStrengthMpa <= 0 ||
            settings.ConcreteCoverMm <= 0 ||
            settings.BottomBarDiameterMm <= 0 ||
            settings.BottomBarSpacingMm <= 0 ||
            settings.MinimumReinforcementRatio <= 0)
        {
            throw new ArgumentException("结构设计组合、材料或配筋参数无效。");
        }
    }
}
