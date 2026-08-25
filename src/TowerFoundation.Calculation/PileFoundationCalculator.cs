using TowerFoundation.Domain;

namespace TowerFoundation.Calculation;

/// <summary>
/// 独立灌注桩及连梁基础计算。每根桩按对应塔腿控制反力独立验算，
/// 不设置承台，也不按整塔反力做群桩轴力分配。单管塔采用1根桩；
/// 三管塔/增高架采用3根桩；角钢塔采用4根桩，多桩之间以连梁拉接。
/// </summary>
public sealed class PileFoundationCalculator
{
    public FoundationScheme Calculate(
        FoundationGeometry geometry,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        var pile = settings.Pile;
        ValidateInputs(geometry, pile, settings);

        var pileArea =
            Math.PI * geometry.PileDiameterM * geometry.PileDiameterM / 4;
        var pilePerimeter = Math.PI * geometry.PileDiameterM;
        var resistance = CalculateLayerResistance(
            pile.SoilLayers,
            geometry.PileLengthM,
            geometry.PileDiameterM);

        // 输入的qsik、qpk为极限侧阻力/端阻力标准值，按用户确认的
        // 单桩竖向承载力安全系数折减为抗压承载力特征值。
        var sideCompressionCapacity =
            pilePerimeter *
            resistance.SizeAdjustedSideResistanceKnPerM /
            pile.CapacityReductionFactor;
        var tipCompressionCapacity =
            resistance.TipSizeEffect *
            pileArea *
            resistance.TipResistanceKpa /
            pile.CapacityReductionFactor;
        var compressionCapacity =
            sideCompressionCapacity + tipCompressionCapacity;

        var submergedLength = Math.Clamp(
            geometry.PileLengthM - geotechnical.GroundwaterDepthM,
            0,
            geometry.PileLengthM);
        var dryLength = geometry.PileLengthM - submergedLength;
        var aboveGroundHeight = Math.Max(0, geometry.PedestalHeightM);
        var effectivePileWeight =
            pileArea *
            (aboveGroundHeight * settings.ConcreteUnitWeightKnPerM3 +
             dryLength * settings.ConcreteUnitWeightKnPerM3 +
             submergedLength *
             (settings.ConcreteUnitWeightKnPerM3 -
              settings.WaterUnitWeightKnPerM3));
        var upliftCapacity =
            pilePerimeter *
            resistance.UpliftResistanceKnPerM /
            pile.CapacityReductionFactor +
            effectivePileWeight;

        var pileHeadCompressionDemand = appliedLoad.UsesIndividualPileReactions
            ? appliedLoad.IndividualPileCompressionKn
            : pile.UseUserConfirmedPileHeadForces
            ? pile.MaximumPileCompressionKn
            : Math.Max(0, appliedLoad.VerticalKn);
        var negativeSkinFriction = CalculateNegativeSkinFriction(
            pile,
            geometry.PileLengthM,
            pilePerimeter);
        var compressionDemand = pileHeadCompressionDemand +
                                negativeSkinFriction.ConfirmedDragLoadKn;
        var upliftDemand = appliedLoad.UsesIndividualPileReactions
            ? appliedLoad.IndividualPileUpliftKn
            : pile.UseUserConfirmedPileHeadForces
            ? pile.MaximumPileUpliftKn
            : Math.Max(0, -appliedLoad.VerticalKn);
        var forceSource = appliedLoad.UsesIndividualPileReactions
            ? $"采用图集或用户确认的单塔腿标准组合包络；共{geometry.PileCount}根独立桩，每根桩按同一控制包络分别验算。"
            : pile.UseUserConfirmedPileHeadForces
            ? "采用用户按多组荷载组合另行确认的单桩桩顶最大压力和最大上拔力。"
            : "采用本项目塔脚反力中的竖向力；向下为正、向上为负。";

        var totalHorizontal = appliedLoad.UsesIndividualPileReactions
            ? appliedLoad.IndividualPileHorizontalKn
            : Math.Sqrt(
                appliedLoad.ShearXKn * appliedLoad.ShearXKn +
                appliedLoad.ShearYKn * appliedLoad.ShearYKn);
        var actualMainBarArea =
            pile.PileMainBarCount *
            Math.PI *
            pile.PileMainBarDiameterMm *
            pile.PileMainBarDiameterMm /
            4;
        var requiredMainBarArea =
            pile.MinimumLongitudinalReinforcementRatio *
            pileArea *
            1_000_000;

        var minimumTieBeamHeightM = Math.Max(0.40, geometry.PileCenterSpacingM / 15.0);
        var tieBeamLayoutIsValid = geometry.PileCount == 1 ||
                                   (geometry.TieBeamCount == geometry.PileCount &&
                                    geometry.PileCenterSpacingM > geometry.PileDiameterM &&
                                    geometry.TieBeamWidthM + 1e-9 >= 0.25 &&
                                    geometry.TieBeamHeightM + 1e-9 >= minimumTieBeamHeightM);
        var checks = new List<FoundationCheckResult>
        {
            BuildGateCheck(
                "PILE_LAYOUT",
                "独立桩与连梁布置",
                geometry.PileCount,
                geometry.PileCount is 1 or 3 or 4 && tieBeamLayoutIsValid
                    ? geometry.PileCount
                    : 0,
                "根",
                geometry.PileCount switch
                {
                    3 => $"3根独立灌注桩以3根三角形连系梁闭合拉接，不设承台；连系梁采用{geometry.TieBeamWidthM:F2}×{geometry.TieBeamHeightM:F2}m，最低高度{minimumTieBeamHeightM:F2}m。",
                    4 => $"4根独立灌注桩以4根四角周边连系梁闭合拉接，不设承台；连系梁采用{geometry.TieBeamWidthM:F2}×{geometry.TieBeamHeightM:F2}m，最低高度{minimumTieBeamHeightM:F2}m。",
                    _ => "单管塔采用1根灌注桩直接承受塔脚反力，不设承台和连梁。"
                },
                appliedLoad.GoverningCase,
                "项目多塔柱分离基础统一设置策略；GB 50007-2011第8.5.23条（桩承台连系梁构造下限类比采用）；22G101-3基础联系梁JLL构造"),
            BuildGateCheck(
                "PILE_LAYER_LENGTH",
                "桩长与土层覆盖",
                geometry.PileLengthM,
                resistance.CoveredLengthM,
                "m",
                resistance.CoveredLengthM + 1e-9 >= geometry.PileLengthM
                    ? $"输入土层已覆盖桩长{geometry.PileLengthM:F2} m，桩端落在“{resistance.TipLayerName}”。"
                    : $"输入土层累计厚度仅{resistance.CoveredLengthM:F2} m，未覆盖桩长{geometry.PileLengthM:F2} m。",
                appliedLoad.GoverningCase,
                "JGJ 94-2008第5.3节；地勘分层参数人工确认"),
            BuildCheck(
                "PILE_COMPRESSION",
                "单桩竖向抗压承载力",
                compressionDemand,
                compressionCapacity,
                "kN",
                $"{forceSource}桩顶压力Nk={compressionDemand:F2} kN；侧阻{sideCompressionCapacity:F2} kN、端阻{tipCompressionCapacity:F2} kN，单桩抗压承载力特征值R={compressionCapacity:F2} kN，按Nk≤R校核。",
                appliedLoad.GoverningCase,
                "GB 55003-2021式(5.2.1-1)；JGJ 94-2008第5.3节及表5.3.6-2"),
            BuildCheck(
                "PILE_UPLIFT",
                "单桩抗拔承载力",
                upliftDemand,
                upliftCapacity,
                "kN",
                $"{forceSource}桩顶上拔力Nk={upliftDemand:F2} kN；按Tuk/{pile.CapacityReductionFactor:F2}+Gp计算，侧阻及有效桩重抗拔能力{upliftCapacity:F2} kN。",
                appliedLoad.GoverningCase,
                "JGJ 94-2008式(5.4.5-2)、式(5.4.6-1)；有效自重法"),
            BuildCheck(
                "PILE_HORIZONTAL",
                "单桩水平承载力",
                totalHorizontal,
                pile.SinglePileHorizontalCapacityKn,
                "kN",
                $"桩顶合水平力{totalHorizontal:F2} kN；用户按地勘、试桩或m法确认的单桩水平承载力为{pile.SinglePileHorizontalCapacityKn:F2} kN。",
                appliedLoad.GoverningCase,
                "JGJ 94-2008第5.7节；水平承载力必须由地勘、试桩或专项计算确认"),
            BuildCheck(
                "PILE_LONGITUDINAL_REINFORCEMENT",
                "灌注桩纵向最小配筋",
                requiredMainBarArea,
                actualMainBarArea,
                "mm²",
                $"桩径{geometry.PileDiameterM:F2} m，最小配筋率{pile.MinimumLongitudinalReinforcementRatio:P2}，需{requiredMainBarArea:F0} mm²；实配{pile.PileMainBarCount}Φ{pile.PileMainBarDiameterMm:F0}为{actualMainBarArea:F0} mm²。",
                appliedLoad.GoverningCase,
                "JGJ 94-2008第4.1节；GB 50010构造配筋门禁")
        };
        checks.Insert(2, BuildNegativeSkinFrictionCheck(
            pile,
            negativeSkinFriction,
            appliedLoad.GoverningCase));

        var pileCount = geometry.PileCount;
        var totalPileLength = geometry.PileLengthM + aboveGroundHeight;
        var pileConcreteVolume = pileCount * pileArea * totalPileLength;
        var drillingVolume = pileCount * pileArea * geometry.PileLengthM;
        var tieBeamClearLength = geometry.TieBeamCount > 0
            ? Math.Max(0, geometry.PileCenterSpacingM - geometry.PileDiameterM)
            : 0;
        var tieBeamConcreteVolume =
            geometry.TieBeamCount *
            tieBeamClearLength *
            geometry.TieBeamWidthM *
            geometry.TieBeamHeightM;
        var concreteVolume = pileConcreteVolume + tieBeamConcreteVolume;
        var pileMainBarUnitWeight =
            pile.PileMainBarDiameterMm * pile.PileMainBarDiameterMm / 162;
        var pileMainBarTotalLength =
            pileCount * pile.PileMainBarCount * totalPileLength;
        var pileReinforcement = new ReinforcementDesignResult
        {
            Component = "灌注桩桩身纵筋",
            Direction = pileCount == 1
                ? "1根桩圆周均布"
                : $"{pileCount}根独立桩分别圆周均布",
            BarSpecification =
                $"每桩{pile.PileMainBarCount}Φ{pile.PileMainBarDiameterMm:F0}",
            RequiredAreaMm2 = requiredMainBarArea,
            ProvidedAreaMm2 = actualMainBarArea,
            BarCount = pileCount * pile.PileMainBarCount,
            BarDiameterMm = pile.PileMainBarDiameterMm,
            BarSpacingMm = 0,
            SingleBarLengthM = totalPileLength,
            TotalLengthM = pileMainBarTotalLength,
            UnitWeightKgPerM = pileMainBarUnitWeight,
            CalculatedWeightKg = pileMainBarTotalLength * pileMainBarUnitWeight,
            Status = checks.Single(item =>
                item.Code == "PILE_LONGITUDINAL_REINFORCEMENT").Status,
            RuleReference = checks.Single(item =>
                item.Code == "PILE_LONGITUDINAL_REINFORCEMENT").RuleReference
        };

        var structuralVerification =
            PileStructuralVerificationCalculator.Calculate(
                geometry,
                appliedLoad,
                geotechnical,
                settings);
        checks.AddRange(structuralVerification.Checks);
        var reinforcementDesigns = new List<ReinforcementDesignResult>
        {
            pileReinforcement
        };
        if (structuralVerification.ReinforcementDesigns.Any(item =>
                item.Component.Contains("桩身纵筋", StringComparison.Ordinal)))
        {
            reinforcementDesigns.Clear();
        }
        reinforcementDesigns.AddRange(structuralVerification.ReinforcementDesigns);
        var reinforcementWeight = reinforcementDesigns.Sum(item =>
            item.CalculatedWeightKg);

        return new FoundationScheme
        {
            FoundationType = FoundationType.Pile,
            Geometry = geometry,
            Checks = checks,
            ReinforcementDesigns = reinforcementDesigns,
            Quantities = new QuantitySummary
            {
                ConcreteM3 = concreteVolume,
                ExcavationM3 = drillingVolume + tieBeamConcreteVolume,
                BackfillM3 = 0,
                EstimatedReinforcementKg = reinforcementWeight
            }
        };
    }

    private static PileLayerResistance CalculateLayerResistance(
        IReadOnlyList<PileSoilLayerInput> layers,
        double pileLengthM,
        double pileDiameterM)
    {
        var remaining = pileLengthM;
        var sizeAdjustedSideResistance = 0d;
        var upliftResistance = 0d;
        var covered = 0d;
        var tipResistance = 0d;
        var tipSizeEffect = 1d;
        var tipLayerName = "未覆盖";

        foreach (var layer in layers.Where(item => item.ThicknessM > 0))
        {
            if (remaining <= 1e-9)
            {
                break;
            }

            var usedThickness = Math.Min(layer.ThicknessM, remaining);
            var sideExponent = layer.IsSandOrGravel ? 1d / 3d : 1d / 5d;
            var sideSizeEffect = Math.Min(
                1,
                Math.Pow(0.8 / pileDiameterM, sideExponent));
            sizeAdjustedSideResistance +=
                usedThickness * layer.SideResistanceKpa * sideSizeEffect;
            upliftResistance +=
                usedThickness *
                layer.SideResistanceKpa *
                layer.UpliftCoefficient;
            covered += usedThickness;
            remaining -= usedThickness;
            tipResistance = layer.TipResistanceKpa;
            var tipExponent = layer.IsSandOrGravel ? 1d / 3d : 1d / 4d;
            tipSizeEffect = Math.Min(
                1,
                Math.Pow(0.8 / pileDiameterM, tipExponent));
            tipLayerName = string.IsNullOrWhiteSpace(layer.Name)
                ? "未命名土层"
                : layer.Name.Trim();
        }

        return new PileLayerResistance(
            covered,
            sizeAdjustedSideResistance,
            upliftResistance,
            tipResistance,
            tipSizeEffect,
            tipLayerName);
    }

    private static NegativeSkinFrictionResult CalculateNegativeSkinFriction(
        PileFoundationSettings pile,
        double pileLengthM,
        double pilePerimeterM)
    {
        if (!pile.UseNegativeSkinFriction)
        {
            return new NegativeSkinFrictionResult(false, true, 0, 0, []);
        }

        var layers = pile.NegativeSkinFrictionLayers
            .Where(layer => layer.ThicknessM > 0 ||
                            layer.UnitNegativeSkinFrictionKpa > 0)
            .ToList();
        var complete = pile.NegativeSkinFrictionSource.IsConfirmed &&
                       layers.Count > 0 &&
                       layers.All(layer =>
                           layer.ThicknessM > 0 &&
                           layer.UnitNegativeSkinFrictionKpa >= 0);
        if (!complete)
        {
            return new NegativeSkinFrictionResult(true, false, 0, 0, []);
        }

        var remaining = pileLengthM;
        var terms = new List<string>();
        var usedLength = 0d;
        var dragLoad = 0d;
        foreach (var layer in layers)
        {
            if (remaining <= 1e-9)
            {
                break;
            }

            var usedThickness = Math.Min(remaining, layer.ThicknessM);
            var layerDrag = pilePerimeterM *
                            usedThickness *
                            layer.UnitNegativeSkinFrictionKpa;
            dragLoad += layerDrag;
            usedLength += usedThickness;
            remaining -= usedThickness;
            terms.Add(
                $"{(string.IsNullOrWhiteSpace(layer.Name) ? "未命名层" : layer.Name)}:" +
                $"u×l×qni={pilePerimeterM:F3}×{usedThickness:F2}×" +
                $"{layer.UnitNegativeSkinFrictionKpa:F2}={layerDrag:F2} kN");
        }

        return new NegativeSkinFrictionResult(
            true,
            true,
            dragLoad,
            usedLength,
            terms);
    }

    private static FoundationCheckResult BuildNegativeSkinFrictionCheck(
        PileFoundationSettings pile,
        NegativeSkinFrictionResult result,
        string loadCase)
    {
        if (!result.Enabled)
        {
            return new FoundationCheckResult
            {
                Code = "PILE_NEGATIVE_SKIN_FRICTION",
                Name = "桩侧负摩阻力",
                Status = CheckStatus.Advisory,
                GoverningCase = loadCase,
                Explanation = "本项目未启用负摩阻力；如地勘提示欠固结填土、降水固结或桩周土相对下沉，应启用并补充分层参数。",
                RuleReference = "JGJ 94-2008第5.4节；地勘不良地质作用结论"
            };
        }

        if (!result.IsComplete)
        {
            return new FoundationCheckResult
            {
                Code = "PILE_NEGATIVE_SKIN_FRICTION",
                Name = "桩侧负摩阻力",
                Status = CheckStatus.PendingInput,
                GoverningCase = loadCase,
                Explanation = "已标记存在负摩阻风险，但分层厚度、单位负摩阻力或参数来源尚未确认。本轮抗压需求未擅自加入未知下拉荷载，方案不能据此形成完整通过结论。",
                RuleReference = "JGJ 94-2008第5.4节；须由地勘或专项计算确认负摩阻区段与参数"
            };
        }

        return new FoundationCheckResult
        {
            Code = "PILE_NEGATIVE_SKIN_FRICTION",
            Name = "桩侧负摩阻力",
            Status = CheckStatus.Result,
            Demand = result.ConfirmedDragLoadKn,
            Unit = "kN",
            GoverningCase = loadCase,
            Explanation =
                $"按确认的负摩阻分层累计下拉荷载Qn=uΣ(qni·li)={result.ConfirmedDragLoadKn:F2} kN，" +
                $"控制深度{result.UsedLengthM:F2} m；该值已叠加到单桩抗压需求。" +
                string.Join("；", result.Terms) +
                $"；来源：{pile.NegativeSkinFrictionSource.Display}。",
            RuleReference = "JGJ 94-2008第5.4节；项目确认的负摩阻分层参数"
        };
    }

    private static FoundationCheckResult BuildCheck(
        string code,
        string name,
        double demand,
        double capacity,
        string unit,
        string explanation,
        string loadCase,
        string ruleReference)
    {
        var utilization = demand <= 1e-12
            ? 0
            : capacity > 0
                ? demand / capacity
                : double.PositiveInfinity;
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
            GoverningCase = loadCase,
            RuleReference = ruleReference
        };
    }

    private static FoundationCheckResult BuildGateCheck(
        string code,
        string name,
        double demand,
        double capacity,
        string unit,
        string explanation,
        string loadCase,
        string ruleReference)
    {
        var passes = demand <= capacity + 1e-9;
        return new FoundationCheckResult
        {
            Code = code,
            Name = name,
            Status = passes ? CheckStatus.Pass : CheckStatus.Fail,
            Demand = demand,
            Capacity = capacity,
            Utilization = passes ? 0 : double.PositiveInfinity,
            Unit = unit,
            Explanation = explanation,
            GoverningCase = loadCase,
            RuleReference = ruleReference
        };
    }

    private static void ValidateInputs(
        FoundationGeometry geometry,
        PileFoundationSettings pile,
        FoundationDesignSettings settings)
    {
        if (geometry.PileDiameterM <= 0 ||
            geometry.PileLengthM <= 0 ||
            geometry.PedestalHeightM < 0 ||
            geometry.PileCount is not (1 or 3 or 4) ||
            (geometry.PileCount > 1 &&
             (geometry.TieBeamCount != geometry.PileCount ||
              geometry.PileCenterSpacingM <= geometry.PileDiameterM ||
              geometry.TieBeamWidthM <= 0 ||
              geometry.TieBeamHeightM <= 0)))
        {
            throw new ArgumentException(
                "独立灌注桩的桩径、埋深、桩数或连梁几何参数无效。");
        }

        if (pile.CapacityReductionFactor <= 0 ||
            pile.SinglePileHorizontalCapacityKn <= 0 ||
            pile.PileMainBarDiameterMm <= 0 ||
            pile.PileMainBarCount <= 0 ||
            pile.MinimumLongitudinalReinforcementRatio <= 0 ||
            pile.MaximumPileCompressionKn < 0 ||
            pile.MaximumPileUpliftKn < 0)
        {
            throw new ArgumentException(
                "单桩承载力、桩顶控制力或配筋参数无效。");
        }

        if (settings.ConcreteUnitWeightKnPerM3 <=
            settings.WaterUnitWeightKnPerM3)
        {
            throw new ArgumentException("混凝土重度必须大于水重度。");
        }

        var activeLayers = pile.SoilLayers
            .Where(item => item.ThicknessM > 0)
            .ToList();
        if (activeLayers.Count == 0 ||
            activeLayers.Any(item =>
                !double.IsFinite(item.ThicknessM) ||
                !double.IsFinite(item.SideResistanceKpa) ||
                !double.IsFinite(item.TipResistanceKpa) ||
                !double.IsFinite(item.UpliftCoefficient) ||
                item.SideResistanceKpa < 0 ||
                item.TipResistanceKpa < 0 ||
                item.UpliftCoefficient is < 0 or > 1))
        {
            throw new ArgumentException(
                "请至少填写一层有效的灌注桩侧阻、桩端阻及抗拔系数参数。");
        }
    }

    private sealed record PileLayerResistance(
        double CoveredLengthM,
        double SizeAdjustedSideResistanceKnPerM,
        double UpliftResistanceKnPerM,
        double TipResistanceKpa,
        double TipSizeEffect,
        string TipLayerName);

    private sealed record NegativeSkinFrictionResult(
        bool Enabled,
        bool IsComplete,
        double ConfirmedDragLoadKn,
        double UsedLengthM,
        IReadOnlyList<string> Terms);
}
