using TowerFoundation.Domain;

namespace TowerFoundation.Calculation;

internal static class SpecialtyVerificationCalculator
{
    private const double Pi = Math.PI;

    public static FoundationScheme Apply(
        FoundationScheme scheme,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings,
        SpecialtyDesignInput specialty)
    {
        scheme.Checks.RemoveAll(check =>
            check.Code is "REMAINING_SCOPE" or
                "RIGID_REMAINING_SCOPE" or
                "RIGID_RECT_REMAINING_SCOPE");

        ApplyDeformationLimits(scheme, specialty.Deformation);
        AddSettlementCheck(scheme, appliedLoad, geotechnical, settings, specialty.Settlement);
        AddCrackCheck(scheme, settings, specialty.Crack);
        AddAnchorChecks(scheme, appliedLoad, settings, specialty.AnchorBolts);
        AddSeparatedScopeItems(scheme, geotechnical);
        return AdvancedFoundationVerificationCalculator.Apply(
            scheme,
            appliedLoad,
            geotechnical,
            settings,
            specialty);
    }

    private static void ApplyDeformationLimits(
        FoundationScheme scheme,
        DeformationLimitInput input)
    {
        var deformationCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "RIGID_TOP_DISPLACEMENT",
            "RIGID_TOP_ROTATION",
            "RIGID_RECT_DISPLACEMENT_X",
            "RIGID_RECT_ROTATION_X",
            "RIGID_RECT_DISPLACEMENT_Y",
            "RIGID_RECT_ROTATION_Y",
            "PILE_TOP_DISPLACEMENT",
            "PILE_TOP_ROTATION"
        };
        for (var index = 0; index < scheme.Checks.Count; index++)
        {
            var check = scheme.Checks[index];
            if (!deformationCodes.Contains(check.Code))
            {
                continue;
            }

            var isRotation = check.Code.Contains("ROTATION", StringComparison.Ordinal);
            var limit = isRotation
                ? input.AllowableTopRotationRad
                : input.AllowableTopDisplacementMm / 1000;
            var ready = input.Source.IsConfirmed && limit > 0;
                scheme.Checks[index] = CloneCheck(
                    check,
                    ready
                        ? check.Demand <= limit ? CheckStatus.Pass : CheckStatus.Fail
                    : CheckStatus.SpecialReview,
                ready ? limit : 0,
                ready ? SafeRatio(check.Demand, limit) : 0,
                ready
                    ? check.Explanation + $"；采用允许值{limit:F6} {check.Unit}，来源：{SourceText(input.Source)}。"
                    : check.Explanation + "；尚未确认塔型或连接允许值，本项仅保留计算结果，不形成通过结论。",
                ready
                    ? check.RuleReference + "；项目确认的塔型/连接变形限值"
                    : check.RuleReference);
        }
    }

    private static void AddSettlementCheck(
        FoundationScheme scheme,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings,
        SettlementDesignInput input)
    {
        if (scheme.FoundationType == FoundationType.Pile)
        {
            var pile = settings.Pile;
            var method = pile.SettlementMethod;
            if (method == PileSettlementMethod.NotSelected &&
                pile.UseConfirmedServiceSettlement)
            {
                // 兼容旧项目：旧版只有“采用已确认沉降值”的开关。
                method = PileSettlementMethod.ReviewedSpecialCalculation;
            }

            var source = pile.SettlementSource.IsConfirmed
                ? pile.SettlementSource
                : input.Source;
            if (input.AllowableSettlementMm <= 0 || !source.IsConfirmed)
            {
                scheme.Checks.Add(SpecialReview(
                    "SETTLEMENT_PILE_METHOD",
                    "独立灌注桩沉降",
                    "当前资料不足以形成正式单桩沉降结论，已自动转交付前专业核对；静载Q-s曲线优先，也可采用经审查的专项沉降结果。软件不把浅基础分层总和法直接套用于单桩。",
                    appliedLoad.GoverningCase,
                    "GB 50007-2011第8.5.15条及附录R；JGJ 94-2008第5.5节"));
                return;
            }

            var serviceDemand = appliedLoad.UsesIndividualPileReactions
                ? appliedLoad.IndividualPileCompressionKn
                : pile.UseUserConfirmedPileHeadForces
                    ? pile.MaximumPileCompressionKn
                    : Math.Max(
                        0,
                        appliedLoad.QuasiPermanentCombination?.HasMeaningfulLoad == true
                            ? appliedLoad.QuasiPermanentCombination.VerticalKn
                            : appliedLoad.VerticalKn);
            var governingCase =
                appliedLoad.QuasiPermanentCombination?.GoverningCase ??
                appliedLoad.GoverningCase;

            if (method == PileSettlementMethod.StaticLoadTestCurve)
            {
                AddPileLoadTestSettlementCheck(
                    scheme.Checks,
                    pile,
                    serviceDemand,
                    input.AllowableSettlementMm,
                    governingCase,
                    source);
                return;
            }

            if (method == PileSettlementMethod.ReviewedSpecialCalculation &&
                pile.UseConfirmedServiceSettlement &&
                pile.ServiceSettlementFromTestOrSpecialCalculationMm >= 0)
            {
                AddVerification(
                    scheme.Checks,
                    "SETTLEMENT_PILE_METHOD",
                    "独立灌注桩服务沉降",
                    pile.ServiceSettlementFromTestOrSpecialCalculationMm,
                    input.AllowableSettlementMm,
                    "mm",
                    $"采用经审查专项计算给出的服务沉降{pile.ServiceSettlementFromTestOrSpecialCalculationMm:F2} mm，与允许值{input.AllowableSettlementMm:F2} mm比较；来源：{SourceText(source)}。",
                    governingCase,
                    "JGJ 94-2008第5.5节；GB 50007-2011第8.5.15条；确认的专项计算结果");
                return;
            }

            if (method == PileSettlementMethod.MindlinReviewEstimate)
            {
                AddPileMindlinReviewEstimate(
                    scheme.Checks,
                    scheme.Geometry,
                    serviceDemand,
                    input,
                    pile,
                    governingCase,
                    source);
                return;
            }

            scheme.Checks.Add(SpecialReview(
                "SETTLEMENT_PILE_METHOD",
                "独立灌注桩沉降",
                "尚未选择可执行的沉降方法，或专项结果未填写。请选择静载Q-s曲线、经审查专项结果；Mindlin弹性值仅作复核提示，不能单独形成正式通过结论。",
                governingCase,
                "JGJ 94-2008第5.5节；GB 50007-2011第8.5.15条及附录R"));
            return;
        }

        var layers = input.SoilLayers
            .Where(layer => layer.ThicknessM > 0 && layer.CompressionModulusMpa > 0)
            .ToList();
        var ready = input.Source.IsConfirmed &&
                    input.AllowableSettlementMm > 0 &&
                    input.ExperienceCoefficient > 0 &&
                    layers.Count > 0;
        if (!ready)
        {
            scheme.Checks.Add(SpecialReview(
                "SETTLEMENT",
                "地基最终沉降",
                "允许沉降值已可按结构高度自动取值；原地勘缺少基底以下分层厚度或压缩模量Es时，软件不猜数并自动转交付前专业核对，不要求普通用户逐项硬填。",
                appliedLoad.GoverningCase,
                "GB 50007-2011第5.3.4、5.3.5条；桩基另见第8.5.15条及附录R"));
            return;
        }

        var geometry = scheme.Geometry;
        var isCircular = scheme.FoundationType == FoundationType.RigidShortPile;
        var baseLength = isCircular ? geometry.PileDiameterM : geometry.BaseLengthM;
        var baseWidth = isCircular ? geometry.PileDiameterM : geometry.BaseWidthM;
        var area = isCircular
            ? Pi * baseLength * baseLength / 4
            : baseLength * baseWidth;
        if (area <= 0)
        {
            scheme.Checks.Add(Pending(
                "SETTLEMENT",
                "地基最终沉降",
                "基础底面积无效，无法形成沉降计算。",
                appliedLoad.GoverningCase,
                "计算输入门禁"));
            return;
        }

        var embeddedConcreteVolume = scheme.FoundationType switch
        {
            FoundationType.RigidShortPile =>
                Pi * geometry.PileDiameterM * geometry.PileDiameterM / 4 *
                geometry.PileLengthM,
            FoundationType.RigidRectangularShortPile =>
                geometry.BaseLengthM * geometry.BaseWidthM * geometry.PileLengthM,
            _ => scheme.Quantities.ConcreteM3
        };
        var replacementWeight =
            (settings.ConcreteUnitWeightKnPerM3 - geotechnical.SoilUnitWeightKnPerM3) *
            embeddedConcreteVolume;
        var settlementVertical =
            appliedLoad.QuasiPermanentCombination?.HasMeaningfulLoad == true
                ? appliedLoad.QuasiPermanentCombination.VerticalKn
                : appliedLoad.VerticalKn;
        var netPressure = Math.Max(
            0,
            (Math.Max(0, settlementVertical) + replacementWeight) / area);
        var depth = 0d;
        var layerTerms = new List<string>();
        var settlementMm = 0d;
        foreach (var layer in layers)
        {
            var top = depth;
            var bottom = depth + layer.ThicknessM;
            var influenceIntegral = IntegrateInfluence(
                top,
                bottom,
                baseLength,
                baseWidth,
                isCircular);
            var layerSettlementMm =
                input.ExperienceCoefficient *
                netPressure *
                influenceIntegral /
                layer.CompressionModulusMpa;
            settlementMm += layerSettlementMm;
            layerTerms.Add(
                $"{layer.Name}:h={layer.ThicknessM:F2}m、Es={layer.CompressionModulusMpa:F2}MPa、Δs={layerSettlementMm:F2}mm");
            depth = bottom;
        }

        var status = settlementMm <= input.AllowableSettlementMm
            ? CheckStatus.Pass
            : CheckStatus.Fail;
        scheme.Checks.Add(new FoundationCheckResult
        {
            Code = "SETTLEMENT",
            Name = "地基最终沉降",
            Status = status,
            Demand = settlementMm,
            Capacity = input.AllowableSettlementMm,
            Utilization = SafeRatio(settlementMm, input.AllowableSettlementMm),
            Unit = "mm",
            GoverningCase = appliedLoad.QuasiPermanentCombination?.HasMeaningfulLoad == true
                ? appliedLoad.QuasiPermanentCombination.GoverningCase
                : appliedLoad.GoverningCase + "（无准永久组合时采用标准组合保守计算）",
            Explanation =
                $"净附加压力p0={netPressure:F2} kPa，经验系数ψs={input.ExperienceCoefficient:F2}；" +
                string.Join("；", layerTerms) +
                $"；Σs={settlementMm:F2} mm，允许值={input.AllowableSettlementMm:F2} mm；来源：{SourceText(input.Source)}。",
            RuleReference = scheme.FoundationType is FoundationType.RigidShortPile or FoundationType.RigidRectangularShortPile
                ? "GB 50007-2011第8.5.15条及附录R；等代实体深基础分层总和法，未折减桩侧分担的保守压力输入"
                : "GB 50007-2011第5.3.4、5.3.5条；分层总和法"
        });
    }

    private static void AddPileLoadTestSettlementCheck(
        ICollection<FoundationCheckResult> checks,
        PileFoundationSettings pile,
        double serviceDemandKn,
        double allowableSettlementMm,
        string governingCase,
        EngineeringParameterSource source)
    {
        var points = pile.StaticLoadTestCurve
            .Where(point => point.LoadKn >= 0 && point.SettlementMm >= 0)
            .OrderBy(point => point.LoadKn)
            .ToList();
        var valid = points.Count >= 2 &&
                    points.Zip(points.Skip(1), (left, right) =>
                            right.LoadKn > left.LoadKn &&
                            right.SettlementMm + 1e-9 >= left.SettlementMm)
                        .All(value => value);
        if (!valid)
        {
            checks.Add(Pending(
                "SETTLEMENT_PILE_METHOD",
                "独立灌注桩静载曲线沉降",
                "请至少填写2个荷载递增、沉降不减的Q-s试验点，并确认试桩代表性和数据来源。",
                governingCase,
                "JGJ 94-2008第5.5节；确认的单桩静载试验Q-s曲线"));
            return;
        }

        if (serviceDemandKn > points[^1].LoadKn + 1e-9)
        {
            checks.Add(Pending(
                "SETTLEMENT_PILE_METHOD",
                "独立灌注桩静载曲线沉降",
                $"服务压力{serviceDemandKn:F2} kN超过静载曲线最大试验荷载{points[^1].LoadKn:F2} kN，软件禁止外推；请补充覆盖该荷载的试验点或经审查专项计算。",
                governingCase,
                "JGJ 94-2008第5.5节；静载曲线仅在已试验荷载范围内插值"));
            return;
        }

        var settlementMm = InterpolateSettlement(points, serviceDemandKn);
        AddVerification(
            checks,
            "SETTLEMENT_PILE_METHOD",
            "独立灌注桩静载曲线沉降",
            settlementMm,
            allowableSettlementMm,
            "mm",
            $"在确认的Q-s曲线内对服务压力{serviceDemandKn:F2} kN作分段线性内插，得到沉降{settlementMm:F2} mm；不作试验范围外推。允许值{allowableSettlementMm:F2} mm；来源：{SourceText(source)}。",
            governingCase,
            "JGJ 94-2008第5.5节；确认的单桩静载试验Q-s曲线内插");
    }

    private static double InterpolateSettlement(
        IReadOnlyList<PileLoadTestPoint> points,
        double loadKn)
    {
        if (loadKn <= points[0].LoadKn)
        {
            if (points[0].LoadKn <= 1e-9)
            {
                return points[0].SettlementMm;
            }

            return points[0].SettlementMm * loadKn / points[0].LoadKn;
        }

        for (var index = 1; index < points.Count; index++)
        {
            if (loadKn > points[index].LoadKn)
            {
                continue;
            }

            var left = points[index - 1];
            var right = points[index];
            var ratio = (loadKn - left.LoadKn) /
                        (right.LoadKn - left.LoadKn);
            return left.SettlementMm +
                   ratio * (right.SettlementMm - left.SettlementMm);
        }

        return points[^1].SettlementMm;
    }

    private static void AddPileMindlinReviewEstimate(
        ICollection<FoundationCheckResult> checks,
        FoundationGeometry geometry,
        double serviceDemandKn,
        SettlementDesignInput input,
        PileFoundationSettings pile,
        string governingCase,
        EngineeringParameterSource source)
    {
        var layers = input.SoilLayers
            .Where(layer => layer.ThicknessM > 0 && layer.CompressionModulusMpa > 0)
            .ToList();
        if (layers.Count == 0 ||
            geometry.PileDiameterM <= 0 ||
            pile.MindlinEstimatePoissonRatio is <= 0 or >= 0.5 ||
            pile.MindlinEstimateInfluenceFactor <= 0)
        {
            checks.Add(SpecialReview(
                "SETTLEMENT_PILE_METHOD",
                "独立灌注桩Mindlin复核估算",
                "需补充桩径、分层压缩模量、泊松比和经审查的影响系数，才能形成弹性复核值。该方法仍不能替代试桩或完整专项沉降计算。",
                governingCase,
                "JGJ 94-2008第5.5节；Mindlin解仅作项目复核模型"));
            return;
        }

        var totalThickness = layers.Sum(layer => layer.ThicknessM);
        var weightedCompliance = layers.Sum(layer =>
            layer.ThicknessM / layer.CompressionModulusMpa);
        var equivalentModulusMpa = totalThickness / weightedCompliance;
        var poisson = pile.MindlinEstimatePoissonRatio;
        var estimateMm = serviceDemandKn /
                         (equivalentModulusMpa * 1000 * geometry.PileDiameterM) *
                         (1 - poisson * poisson) *
                         pile.MindlinEstimateInfluenceFactor *
                         1000;
        checks.Add(new FoundationCheckResult
        {
            Code = "SETTLEMENT_PILE_METHOD",
            Name = "独立灌注桩Mindlin复核估算",
            Status = CheckStatus.SpecialReview,
            Demand = estimateMm,
            Capacity = input.AllowableSettlementMm,
            Unit = "mm",
            GoverningCase = governingCase,
            Explanation =
                $"采用显式弹性复核式s=Q(1-ν²)I/(Es·d)，Q={serviceDemandKn:F2} kN、" +
                $"分层等效Es={equivalentModulusMpa:F2} MPa、ν={poisson:F2}、" +
                $"I={pile.MindlinEstimateInfluenceFactor:F2}、d={geometry.PileDiameterM:F2} m，" +
                $"得到s≈{estimateMm:F2} mm；来源：{SourceText(source)}。该值只用于发现量级异常，" +
                "未包含完整桩土相互作用、群桩及分层积分，不能据此判定通过。",
            RuleReference = "JGJ 94-2008第5.5节；项目明确的Mindlin弹性复核模型（非正式承载结论）"
        });
    }

    private static void AddCrackCheck(
        FoundationScheme scheme,
        FoundationDesignSettings settings,
        CrackDesignInput input)
    {
        if (scheme.FoundationType != FoundationType.RigidRectangularShortPile)
        {
            scheme.Checks.Add(SpecialReview(
                "CRACK_SECTION_METHOD",
                "正常使用裂缝宽度截面方法",
                "当前自动公式仅对矩形刚性短柱桩开放；本基础形式不要求用户填写裂缝参数，须待相应截面内力与有效受拉面积模型接入后再计算。",
                "正常使用极限状态标准/准永久组合",
                "GB/T 50010-2010（2024年版）第7.1.2条适用截面范围"));
            return;
        }

        var ready = input.Source.IsConfirmed &&
                    input.MaximumCrackWidthMm > 0 &&
                    input.ConcreteTensileStrengthStandardMpa > 0 &&
                    input.ReinforcementElasticModulusMpa > 0 &&
                    !string.IsNullOrWhiteSpace(input.EnvironmentCategory) &&
                    !input.EnvironmentCategory.Contains("待确认", StringComparison.Ordinal);
        if (!ready)
        {
            scheme.Checks.Add(Pending(
                "CRACK_WIDTH",
                "正常使用裂缝宽度",
                "需确认环境类别、最大裂缝宽度限值wlim、混凝土抗拉强度标准值ftk及参数来源。预填0.20 mm不等于已经确认。",
                "正常使用极限状态标准/准永久组合",
                "GB/T 50010-2010（2024年版）第3.4.5、7.1.1、7.1.2条"));
            return;
        }

        var momentX = scheme.Checks.FirstOrDefault(check =>
            check.Code == "RIGID_RECT_SERVICE_MOMENT_X")?.Demand;
        var momentY = scheme.Checks.FirstOrDefault(check =>
            check.Code == "RIGID_RECT_SERVICE_MOMENT_Y")?.Demand;
        if (momentX is null || momentY is null)
        {
            scheme.Checks.Add(SpecialReview(
                "CRACK_SERVICE_FORCE",
                "正常使用裂缝宽度内力",
                "当前方案没有形成可追溯的标准组合X、Y向截面弯矩，不能进行裂缝验算。",
                "正常使用极限状态标准/准永久组合",
                "GB/T 50010-2010（2024年版）第7.1.1条"));
            return;
        }

        var rigid = settings.RigidShortPile;
        var barArea = Pi * rigid.LongitudinalBarDiameterMm * rigid.LongitudinalBarDiameterMm / 4;
        var tensionBarArea = Math.Max(
            barArea,
            Math.Floor(rigid.LongitudinalBarCount / 2d) * barArea);
        var crackX = CalculateRectangularCrackWidth(
            momentX.Value,
            scheme.Geometry.BaseLengthM,
            scheme.Geometry.BaseWidthM,
            tensionBarArea,
            rigid.LongitudinalBarDiameterMm,
            settings.ConcreteCoverMm,
            input);
        var crackY = CalculateRectangularCrackWidth(
            momentY.Value,
            scheme.Geometry.BaseWidthM,
            scheme.Geometry.BaseLengthM,
            tensionBarArea,
            rigid.LongitudinalBarDiameterMm,
            settings.ConcreteCoverMm,
            input);
        var maximum = Math.Max(crackX.WidthMm, crackY.WidthMm);
        scheme.Checks.Add(new FoundationCheckResult
        {
            Code = "CRACK_WIDTH",
            Name = "矩形刚性短柱桩裂缝宽度",
            Status = maximum <= input.MaximumCrackWidthMm
                ? CheckStatus.Pass
                : CheckStatus.Fail,
            Demand = maximum,
            Capacity = input.MaximumCrackWidthMm,
            Utilization = SafeRatio(maximum, input.MaximumCrackWidthMm),
            Unit = "mm",
            GoverningCase = "基础端标准组合；按受弯钢筋应力上限考虑长期影响",
            Explanation =
                $"X向Msk={momentX:F2} kN·m、σs={crackX.SteelStressMpa:F2} MPa、wmax={crackX.WidthMm:F3} mm；" +
                $"Y向Msk={momentY:F2} kN·m、σs={crackY.SteelStressMpa:F2} MPa、wmax={crackY.WidthMm:F3} mm；" +
                $"环境类别={input.EnvironmentCategory}，限值={input.MaximumCrackWidthMm:F2} mm；来源：{SourceText(input.Source)}。" +
                "轴向压力对裂缝闭合作用未计入，因此结果为保守上限。",
            RuleReference = "GB/T 50010-2010（2024年版）表3.4.5、式(7.1.2-1)～式(7.1.2-4)、式(7.1.4-3)"
        });
    }

    private static void AddAnchorChecks(
        FoundationScheme scheme,
        FoundationLoad appliedLoad,
        FoundationDesignSettings settings,
        AnchorBoltDesignInput input)
    {
        if (input.ConnectionType == AnchorConnectionType.DirectEmbedded)
        {
            scheme.Checks.Add(Advisory(
                "ANCHOR_NOT_APPLICABLE",
                "塔脚连接形式",
                "已选择直埋或无锚栓连接，锚栓钢材验算不适用；施工图仍应明确连接和防腐构造。",
                "塔脚连接",
                "项目确认的连接形式"));
            return;
        }

        if (input.ConnectionType == AnchorConnectionType.Other)
        {
            scheme.Checks.Add(SpecialReview(
                "ANCHOR_OTHER_CONNECTION",
                "其他塔脚连接形式",
                "当前连接形式不是圆周锚栓笼，不能套用现有锚栓弹性分配模型，应按厂家节点详图专项复核。",
                appliedLoad.ResolveStructuralDesignLoad(settings).GoverningCase,
                "项目塔脚连接详图"));
            return;
        }

        if (input.ConnectionType == AnchorConnectionType.NotDetermined)
        {
            scheme.Checks.Add(SpecialReview(
                "ANCHOR_CONNECTION_TYPE",
                "塔脚连接形式",
                "尚无连接详图时软件采用地脚锚栓工作场景；若该默认尚未应用，本项自动转专业核对，不阻断基础主体方案计算。",
                appliedLoad.ResolveStructuralDesignLoad(settings).GoverningCase,
                "塔型或监控杆连接详图"));
            return;
        }

        var ready = input.Source.IsConfirmed &&
                    input.BoltCount >= 3 &&
                    input.NominalDiameterMm > 0 &&
                    input.BoltCircleDiameterM > 0 &&
                    input.TensileStrengthDesignMpa > 0 &&
                    input.ShearStrengthDesignMpa > 0 &&
                    input.ThreadStressAreaFactor is > 0 and <= 1 &&
                    input.EmbedmentDepthM > 0;
        if (!ready)
        {
            scheme.Checks.Add(SpecialReview(
                "ANCHOR_INPUT",
                "塔脚锚栓连接",
                "连接形式已采用地脚锚栓工作默认；数量、直径、锚栓圆、强度和埋深必须来自厂家详图，资料缺失时已自动转交付前专业核对。",
                appliedLoad.ResolveStructuralDesignLoad(settings).GoverningCase,
                "YD/T 5131-2019第5章、第7章；GB 50017连接设计；塔型锚栓详图"));
            return;
        }

        if (appliedLoad.UsesIndividualPileReactions)
        {
            scheme.Checks.Add(SpecialReview(
                "ANCHOR_INDIVIDUAL_LEG_FORCE",
                "单塔腿锚栓连接",
                "当前荷载库只给出单塔腿压力、上拔力和水平力包络，未给每个塔腿底板弯矩、锚栓布置和节点偏心，不能由整塔反力平均生成锚栓内力。",
                appliedLoad.ResolveStructuralDesignLoad(settings).GoverningCase,
                "YD/T 5131-2019节点连接要求；单塔腿反力适用性门禁"));
            return;
        }

        var load = appliedLoad.ResolveStructuralDesignLoad(settings);
        var moment = Math.Sqrt(load.MomentXKnM * load.MomentXKnM + load.MomentYKnM * load.MomentYKnM);
        var shear = Math.Sqrt(load.ShearXKn * load.ShearXKn + load.ShearYKn * load.ShearYKn);
        var tensileDemand =
            Math.Max(0, -load.VerticalKn) / input.BoltCount +
            4 * moment / (input.BoltCount * input.BoltCircleDiameterM);
        var shearDemand =
            shear / input.BoltCount +
            2 * Math.Abs(load.TorsionKnM) /
            (input.BoltCount * input.BoltCircleDiameterM);
        var nominalArea = Pi * input.NominalDiameterMm * input.NominalDiameterMm / 4;
        var stressArea = nominalArea * input.ThreadStressAreaFactor;
        var tensileCapacity = stressArea * input.TensileStrengthDesignMpa / 1000;
        var shearCapacity = stressArea * input.ShearStrengthDesignMpa / 1000;
        AddVerification(
            scheme.Checks,
            "ANCHOR_STEEL_TENSION",
            "单根锚栓钢材受拉",
            tensileDemand,
            tensileCapacity,
            "kN",
            $"按圆周均布锚栓线性分配，Nt,max=Nu/n+4M/(nD)={tensileDemand:F2} kN；有效螺纹面积Ase={stressArea:F1} mm²。",
            load.GoverningCase,
            "YD/T 5131-2019节点连接；GB 50017螺栓受拉承载力；塔脚圆周锚栓弹性分配");
        AddVerification(
            scheme.Checks,
            "ANCHOR_STEEL_SHEAR",
            "单根锚栓钢材受剪",
            shearDemand,
            shearCapacity,
            "kN",
            $"Vb=V/n+2T/(nD)={shearDemand:F2} kN；有效螺纹面积Ase={stressArea:F1} mm²。",
            load.GoverningCase,
            "YD/T 5131-2019节点连接；GB 50017螺栓受剪承载力；扭矩切向分配");
        var interaction = SafeRatio(tensileDemand, tensileCapacity) +
                          SafeRatio(shearDemand, shearCapacity);
        AddVerification(
            scheme.Checks,
            "ANCHOR_STEEL_INTERACTION",
            "锚栓拉剪组合",
            interaction,
            1,
            "无量纲",
            $"采用保守线性相互作用Nt/Nt,Rd+V/V,Rd={interaction:F3}≤1.0；来源：{SourceText(input.Source)}。",
            load.GoverningCase,
            "GB 50017连接承载力；项目规则包采用保守线性包络");
        var plateReady = input.AnchorPlateOuterDiameterMm > input.NominalDiameterMm &&
                         input.AnchorPlateThicknessMm > 0 &&
                         input.AnchorPlateSteelYieldStrengthMpa > 0 &&
                         input.ConcreteCompressiveStrengthMpa > 0;
        if (plateReady)
        {
            var bearingArea = Pi *
                              (input.AnchorPlateOuterDiameterMm * input.AnchorPlateOuterDiameterMm -
                               input.NominalDiameterMm * input.NominalDiameterMm) / 4;
            var bearingCapacity =
                input.ConcreteCompressiveStrengthMpa * bearingArea / 1000;
            AddVerification(
                scheme.Checks,
                "ANCHOR_PLATE_CONCRETE_BEARING",
                "单根锚栓下锚板混凝土净承压",
                tensileDemand,
                bearingCapacity,
                "kN",
                $"下锚板外径{input.AnchorPlateOuterDiameterMm:F0} mm，扣除锚栓孔后净承压面积{bearingArea:F0} mm²；不计局部承压提高系数，保守取Fl=fcAln={bearingCapacity:F2} kN。",
                load.GoverningCase,
                "GB/T 50010-2010（2024年版）局部受压；不计提高系数的保守下限");

            var cantileverProjection =
                (input.AnchorPlateOuterDiameterMm - input.NominalDiameterMm) / 2;
            var bearingStress = tensileDemand * 1000 / bearingArea;
            var stripMoment = bearingStress * cantileverProjection * cantileverProjection / 2;
            var requiredThickness = Math.Sqrt(
                6 * stripMoment / input.AnchorPlateSteelYieldStrengthMpa);
            AddVerification(
                scheme.Checks,
                "ANCHOR_PLATE_THICKNESS",
                "锚栓下锚板厚度",
                requiredThickness,
                input.AnchorPlateThicknessMm,
                "mm",
                $"按环板径向悬臂条带保守计算：q={bearingStress:F2} N/mm²、悬臂c={cantileverProjection:F1} mm，需厚度{requiredThickness:F1} mm，实配{input.AnchorPlateThicknessMm:F1} mm。",
                load.GoverningCase,
                "GB 50017板件抗弯；环形下锚板单位宽度悬臂保守模型");
        }
        else
        {
            scheme.Checks.Add(Pending(
                "ANCHOR_PLATE_DETAIL",
                "锚栓下锚板与局部承压",
                "请从锚栓详图补充下锚板外径、厚度、钢材强度及基础混凝土抗压强度；软件将自动计算净承压与板厚。",
                load.GoverningCase,
                "GB/T 50010-2010（2024年版）局部受压；GB 50017板件抗弯"));
        }

        var programModelReady = input.UseProgramCalculatedConcreteCapacity &&
                                input.ProgramConcreteModelSource.IsConfirmed &&
                                input.ConcreteMemberThicknessMm > 0 &&
                                input.MinimumAnchorEdgeDistanceMm > 0 &&
                                input.MinimumAnchorSpacingMm > 0 &&
                                input.EffectiveEmbedmentDepthMm > 0 &&
                                input.EffectiveEmbedmentDepthMm <= input.ConcreteMemberThicknessMm &&
                                input.ConcreteTensileStrengthMpa > 0 &&
                                input.ConcreteBreakoutCoefficient > 0 &&
                                input.PulloutBearingCoefficient > 0 &&
                                input.EdgeBreakoutCoefficient > 0;
        var concreteBreakoutCapacity = input.ConcreteBreakoutCapacityKn;
        var pulloutCapacity = input.PulloutCapacityKn;
        var edgeBreakoutCapacity = input.EdgeBreakoutCapacityKn;
        var concreteCapacitySource = input.ConcreteCapacitySource;
        if (input.UseProgramCalculatedConcreteCapacity)
        {
            if (!programModelReady)
            {
                scheme.Checks.Add(Pending(
                    "ANCHOR_CONCRETE_MODEL_INPUT",
                    "锚栓组混凝土破坏模型",
                    "已选择程序计算，但尚缺构件厚度、最小边距、最小间距、有效埋深、混凝土抗拉强度、三类模型系数或经确认的公式来源。系数保持0时软件不会套用未经确认的通用公式。",
                    load.GoverningCase,
                    "经审查的锚栓节点计算方法；项目明确的几何、系数和适用范围"));
            }
            else
            {
                // 这里的三个系数必须由项目采用的权威模型给出，并吸收相应的单位、
                // 群锚、边距和厚度修正。程序只执行已确认模型，不内置猜测系数。
                concreteBreakoutCapacity =
                    input.ConcreteBreakoutCoefficient *
                    Math.Sqrt(input.ConcreteTensileStrengthMpa) *
                    Math.Pow(input.EffectiveEmbedmentDepthMm, 1.5) / 1000;
                pulloutCapacity =
                    input.PulloutBearingCoefficient *
                    input.ConcreteCompressiveStrengthMpa *
                    Pi * input.NominalDiameterMm *
                    input.EffectiveEmbedmentDepthMm / 1000;
                edgeBreakoutCapacity =
                    input.EdgeBreakoutCoefficient *
                    Math.Sqrt(input.ConcreteTensileStrengthMpa) *
                    Math.Pow(input.MinimumAnchorEdgeDistanceMm, 1.5) / 1000;
                concreteCapacitySource = input.ProgramConcreteModelSource;
                scheme.Checks.Add(new FoundationCheckResult
                {
                    Code = "ANCHOR_GROUP_GEOMETRY",
                    Name = "锚栓组几何与模型适用性",
                    Status = CheckStatus.Result,
                    Demand = input.EffectiveEmbedmentDepthMm,
                    Unit = "mm",
                    GoverningCase = load.GoverningCase,
                    Explanation =
                        $"构件厚度h={input.ConcreteMemberThicknessMm:F0} mm、最小边距cmin={input.MinimumAnchorEdgeDistanceMm:F0} mm、" +
                        $"最小间距smin={input.MinimumAnchorSpacingMm:F0} mm、有效埋深hef={input.EffectiveEmbedmentDepthMm:F0} mm；" +
                        $"三个系数均来自已确认模型：{SourceText(input.ProgramConcreteModelSource)}。" +
                        "软件仅执行该项目模型，未自行补入群锚、开裂混凝土或边距修正系数。",
                    RuleReference = "项目确认的锚栓混凝土破坏模型及节点几何输入"
                });
            }
        }

        var externalCapacityReady = input.ConcreteCapacitySource.IsConfirmed &&
                                    input.ConcreteBreakoutCapacityKn > 0 &&
                                    input.PulloutCapacityKn > 0 &&
                                    input.EdgeBreakoutCapacityKn > 0;
        var concreteCapacityReady = programModelReady || externalCapacityReady;
        if (concreteCapacityReady)
        {
            var tensionConcreteCapacity = Math.Min(
                concreteBreakoutCapacity,
                pulloutCapacity);
            AddVerification(
                scheme.Checks,
                "ANCHOR_CONCRETE_TENSION",
                "锚栓混凝土锥体与拔出",
                tensileDemand,
                tensionConcreteCapacity,
                "kN",
                $"采用经确认节点计算的单根混凝土锥体承载力{concreteBreakoutCapacity:F2} kN与拔出承载力{pulloutCapacity:F2} kN中的较小值；来源：{SourceText(concreteCapacitySource)}。",
                load.GoverningCase,
                "经审查的锚栓节点计算；不得由软件按缺失边距或群锚参数猜测");
            AddVerification(
                scheme.Checks,
                "ANCHOR_CONCRETE_EDGE",
                "锚栓混凝土边缘破坏",
                shearDemand,
                edgeBreakoutCapacity,
                "kN",
                $"采用经确认节点计算的单根边缘破坏承载力{edgeBreakoutCapacity:F2} kN；来源：{SourceText(concreteCapacitySource)}。",
                load.GoverningCase,
                "经审查的锚栓节点计算；群锚与边距参数适用性门禁");
            var concreteInteraction =
                SafeRatio(tensileDemand, tensionConcreteCapacity) +
                SafeRatio(shearDemand, edgeBreakoutCapacity);
            AddVerification(
                scheme.Checks,
                "ANCHOR_CONCRETE_INTERACTION",
                "锚栓混凝土拉剪组合",
                concreteInteraction,
                1,
                "无量纲",
                $"保守线性组合Nt/Nc,Rd+V/Vc,Rd={concreteInteraction:F3}≤1.0。",
                load.GoverningCase,
                "经审查节点承载力；项目保守线性包络");
        }
        else
        {
            scheme.Checks.Add(Pending(
                "ANCHOR_CONCRETE_FAILURE",
                "锚栓混凝土锥体、拔出与边缘承载力",
                $"已完成锚栓钢材验算，埋深{input.EmbedmentDepthM:F3} m。请导入或填写完整节点计算给出的单根锥体、拔出和边缘破坏承载力；缺少边距、群锚和附加钢筋参数时软件不自动猜测。",
                load.GoverningCase,
                "GB/T 50010锚固与局部承压；经审查的塔型锚栓节点计算"));
        }
    }

    private static void AddSeparatedScopeItems(
        FoundationScheme scheme,
        GeotechnicalInput geotechnical)
    {
        var seismicKnown = geotechnical.SeismicIntensityDegree > 0 ||
                           geotechnical.DesignBasicGroundAccelerationG > 0 ||
                           !string.IsNullOrWhiteSpace(geotechnical.SiteClass);
        scheme.Checks.Add(seismicKnown
            ? SpecialReview(
                "SEISMIC_REVIEW",
                "抗震与场地效应",
                $"已记录：设防烈度{(geotechnical.SeismicIntensityDegree > 0 ? geotechnical.SeismicIntensityDegree + "度" : "未给") }、" +
                $"设计基本地震加速度{(geotechnical.DesignBasicGroundAccelerationG > 0 ? geotechnical.DesignBasicGroundAccelerationG.ToString("F2") + "g" : "未给")}、" +
                $"场地类别{(string.IsNullOrWhiteSpace(geotechnical.SiteClass) ? "未给" : geotechnical.SiteClass)}。本轮仅记录参数，抗震构造和地基抗震仍需专项复核。",
                "地震作用组合",
                "GB 55002-2021；GB/T 50011-2010（2024年版）")
            : SpecialReview(
                "SEISMIC_REVIEW",
                "抗震与场地效应",
                "地勘中尚未记录设防烈度、设计基本地震加速度和场地类别；当前抗震模块尚未开放，本项不作为可由用户补数字解决的待补参数，应在专项设计中复核。",
                "地震作用组合",
                "GB 55002-2021；GB/T 50011-2010（2024年版）"));

        scheme.Checks.Add(string.IsNullOrWhiteSpace(geotechnical.SpecialSoilRisks)
            ? SpecialReview(
                "SPECIAL_SOIL_REVIEW",
                "特殊土与不良地质作用",
                "尚未记录湿陷、液化、冻土、填土、腐蚀性或其他不良地质作用结论；该项属于地勘适用性与专项处理，不作为普通数值输入。",
                "地勘适用性",
                "GB 55003-2021；项目所在地专项地基标准")
            : SpecialReview(
                "SPECIAL_SOIL_REVIEW",
                "特殊土与不良地质作用",
                $"地勘记录：{geotechnical.SpecialSoilRisks}。该信息已进入设计边界，若存在风险须按对应专项标准处理。",
                "地勘适用性",
                "GB 55003-2021；项目所在地专项地基标准"));

        scheme.Checks.Add(Advisory(
            "CONSTRUCTION_DETAIL_REVIEW",
            "施工构造与现场复核",
            "护壁/降排水、钢筋锚固与接头、施工偏差、检测要求及塔脚二次灌浆应在施工图和专项方案中落实。",
            "施工与验收",
            "GB 51004-2015；GB 50202-2018；GB 50204-2015；YD/T 5132-2021"));
    }

    private static CrackResult CalculateRectangularCrackWidth(
        double momentKnM,
        double sectionWidthM,
        double sectionDepthM,
        double tensionSteelAreaMm2,
        double barDiameterMm,
        double coverMm,
        CrackDesignInput input)
    {
        var widthMm = sectionWidthM * 1000;
        var depthMm = sectionDepthM * 1000;
        var effectiveDepthMm = Math.Max(1, depthMm - coverMm - barDiameterMm / 2);
        var steelStress = momentKnM * 1_000_000 /
                          (0.87 * effectiveDepthMm * tensionSteelAreaMm2);
        var effectiveTensionArea = 0.5 * widthMm * depthMm;
        var rho = Math.Max(0.01, tensionSteelAreaMm2 / effectiveTensionArea);
        var psi = steelStress <= 1e-9
            ? 0.2
            : Math.Clamp(
                1.1 - 0.65 * input.ConcreteTensileStrengthStandardMpa /
                (rho * steelStress),
                0.2,
                1.0);
        var c = Math.Clamp(coverMm + barDiameterMm / 2, 20, 65);
        const double alphaCr = 1.9;
        var crackWidth = alphaCr * psi * steelStress /
            input.ReinforcementElasticModulusMpa *
            (1.9 * c + 0.08 * barDiameterMm / rho);
        return new CrackResult(Math.Max(0, crackWidth), Math.Max(0, steelStress));
    }

    private static double IntegrateInfluence(
        double startDepth,
        double endDepth,
        double baseLength,
        double baseWidth,
        bool circular)
    {
        const int segments = 80;
        var step = (endDepth - startDepth) / segments;
        var sum = 0d;
        for (var index = 0; index <= segments; index++)
        {
            var depth = startDepth + index * step;
            var value = circular
                ? CircularInfluence(depth, baseLength)
                : RectangularInfluence(depth, baseLength, baseWidth);
            var weight = index is 0 or segments
                ? 1
                : index % 2 == 0 ? 2 : 4;
            sum += weight * value;
        }
        return sum * step / 3;
    }

    private static double CircularInfluence(double depth, double diameter)
    {
        if (depth <= 1e-9)
        {
            return 1;
        }
        var radiusRatio = diameter / 2 / depth;
        return 1 - 1 / Math.Pow(1 + radiusRatio * radiusRatio, 1.5);
    }

    private static double RectangularInfluence(
        double depth,
        double length,
        double width)
    {
        if (depth <= 1e-9)
        {
            return 1;
        }
        var m = length / 2 / depth;
        var n = width / 2 / depth;
        var root = Math.Sqrt(1 + m * m + n * n);
        var corner = 1 / (2 * Pi) *
            (Math.Atan(m * n / root) +
             m * n / root *
             (1 / (1 + m * m) + 1 / (1 + n * n)));
        return Math.Clamp(4 * corner, 0, 1);
    }

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

    private static FoundationCheckResult CloneCheck(
        FoundationCheckResult source,
        CheckStatus status,
        double capacity,
        double utilization,
        string explanation,
        string ruleReference) => new()
        {
            Code = source.Code,
            Name = source.Name,
            Status = status,
            Demand = source.Demand,
            Capacity = capacity,
            Utilization = utilization,
            Unit = source.Unit,
            GoverningCase = source.GoverningCase,
            Explanation = explanation,
            RuleReference = ruleReference
        };

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

    private static FoundationCheckResult Advisory(
        string code,
        string name,
        string explanation,
        string governingCase,
        string ruleReference) =>
        new()
        {
            Code = code,
            Name = name,
            Status = CheckStatus.Advisory,
            Explanation = explanation,
            GoverningCase = governingCase,
            RuleReference = ruleReference
        };

    private static string SourceText(EngineeringParameterSource source) =>
        string.IsNullOrWhiteSpace(source.Display)
            ? source.SourceType.ToString()
            : source.Display;

    private static double SafeRatio(double numerator, double denominator)
    {
        if (Math.Abs(numerator) < 1e-12)
        {
            return 0;
        }
        return denominator > 0 ? numerator / denominator : double.PositiveInfinity;
    }

    private sealed record CrackResult(double WidthMm, double SteelStressMpa);
}
