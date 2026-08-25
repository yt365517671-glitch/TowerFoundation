using TowerFoundation.Calculation;
using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public sealed class FoundationAdjustmentAdvisor
{
    private readonly RectangularShortColumnFoundationCalculator _calculator;

    public FoundationAdjustmentAdvisor(RectangularShortColumnFoundationCalculator calculator)
    {
        _calculator = calculator;
    }

    public IReadOnlyList<FoundationAdjustmentAdvice> Analyze(
        FoundationScheme evaluatedScheme,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        var failedChecks = evaluatedScheme.Checks
            .Where(check => check.Status == CheckStatus.Fail)
            .ToList();

        if (failedChecks.Count == 0)
        {
            var completedScope = evaluatedScheme.FoundationType switch
            {
                FoundationType.Pile =>
                    "已完成单桩抗压、抗拔、水平承载力和桩身纵向最小配筋门禁",
                FoundationType.RigidShortPile =>
                    "已完成刚性判别、抗倾覆、最不利内力、圆形截面纵筋和箍筋计算",
                FoundationType.RigidRectangularShortPile =>
                    "已完成X/Y向刚性判别、抗倾覆、最不利内力、矩形截面双向偏压纵筋和双向受剪箍筋计算",
                _ => "已包含冲切、受剪、底板受弯和底筋验算"
            };
            return
            [
                new FoundationAdjustmentAdvice
                {
                    Priority = 1,
                    Title = "当前尺寸满足已实现的地基校核",
                    Action = "可以确认该尺寸，或与三种自动推荐比较工程量和余量。",
                    Reason = $"当前最大利用率为{evaluatedScheme.MaximumUtilization:P0}；{completedScope}，剩余专项见计算范围提示。",
                    IsBlocking = false
                }
            ];
        }

        var advice = new List<FoundationAdjustmentAdvice>();
        foreach (var check in failedChecks)
        {
            advice.Add(BuildTargetedAdvice(check, settings.DimensionStepM));
        }

        var nearestFeasible = FindNearestFeasible(
            evaluatedScheme.Geometry,
            appliedLoad,
            geotechnical,
            settings);

        if (nearestFeasible is not null)
        {
            var action = settings.FoundationType switch
            {
                FoundationType.Pile =>
                    $"每根独立灌注桩的桩径×埋深调整为{nearestFeasible.Geometry.PileDiameterM:F2} m×{nearestFeasible.Geometry.PileLengthM:F1} m；桩数和连梁布置保持不变。",
                FoundationType.RigidShortPile =>
                    $"刚性短柱桩直径调整为{nearestFeasible.Geometry.PileDiameterM:F2} m、埋深调整为{nearestFeasible.Geometry.PileLengthM:F1} m。",
                FoundationType.RigidRectangularShortPile =>
                    $"矩形刚性短柱桩截面调整为{nearestFeasible.Geometry.BaseLengthM:F2}×{nearestFeasible.Geometry.BaseWidthM:F2} m、埋深调整为{nearestFeasible.Geometry.PileLengthM:F1} m。",
                _ =>
                    $"底板长×宽×厚调整为 {nearestFeasible.Geometry.BaseLengthM:F1}×{nearestFeasible.Geometry.BaseWidthM:F1}×{nearestFeasible.Geometry.BaseThicknessM:F1} m。"
            };
            advice.Insert(
                0,
                new FoundationAdjustmentAdvice
                {
                    Priority = 0,
                    Title = "建议直接调整到最近可行尺寸",
                    Action = action,
                    Reason =
                        $"按当前{settings.DimensionStepM:F1} m步长搜索到的最近方案，" +
                        $"混凝土约{nearestFeasible.Quantities.ConcreteM3:F2} m³，" +
                        $"最大利用率{nearestFeasible.MaximumUtilization:P0}。",
                    IsBlocking = true
                });
        }
        else
        {
            advice.Insert(
                0,
                new FoundationAdjustmentAdvice
                {
                    Priority = 0,
                    Title = "当前搜索上限内没有找到可行尺寸",
                    Action = settings.FoundationType switch
                    {
                        FoundationType.Pile =>
                            "扩大独立灌注桩的桩径或桩长上限，并重新核对单塔腿控制力、土层侧阻/端阻、抗拔系数和水平承载力。",
                        FoundationType.RigidShortPile =>
                            "扩大刚性短柱桩直径或埋深上限，并重新核对荷载、土重度、内摩擦角和分层m值。",
                        FoundationType.RigidRectangularShortPile =>
                            "扩大矩形刚性短柱桩X/Y向边长或埋深上限，并重新核对两个方向荷载、土重度、内摩擦角和分层m值。",
                        _ =>
                            "扩大底板尺寸或厚度上限，并重新核对荷载、地勘参数和基础形式。"
                    },
                    Reason = "程序只在已设置的尺寸边界内搜索，不会越过用户给定的工程约束。",
                    IsBlocking = true
                });
        }

        return advice
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.Title)
            .ToList();
    }

    private FoundationScheme? FindNearestFeasible(
        FoundationGeometry current,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        if (settings.FoundationType == FoundationType.Pile)
        {
            return FindNearestFeasiblePile(
                current,
                appliedLoad,
                geotechnical,
                settings);
        }

        if (settings.FoundationType == FoundationType.RigidShortPile)
        {
            return FindNearestFeasibleRigidShortPile(
                current,
                appliedLoad,
                geotechnical,
                settings);
        }

        if (settings.FoundationType == FoundationType.RigidRectangularShortPile)
        {
            return FindNearestFeasibleRigidRectangularShortPile(
                current,
                appliedLoad,
                geotechnical,
                settings);
        }

        var candidates = new List<(FoundationScheme Scheme, double ChangeScore)>();
        var startLength = Math.Max(current.BaseLengthM, settings.MinimumBaseLengthM);
        var startWidth = Math.Max(current.BaseWidthM, settings.MinimumBaseWidthM);
        var startThickness = Math.Max(current.BaseThicknessM, settings.MinimumBaseThicknessM);

        foreach (var length in Range(startLength, settings.MaximumBaseLengthM, settings.DimensionStepM))
        {
            foreach (var width in Range(startWidth, settings.MaximumBaseWidthM, settings.DimensionStepM))
            {
                foreach (var thickness in Range(
                             startThickness,
                             settings.MaximumBaseThicknessM,
                             settings.DimensionStepM))
                {
                    var scheme = _calculator.Calculate(
                        new FoundationGeometry
                        {
                            BaseLengthM = length,
                            BaseWidthM = width,
                            BaseThicknessM = thickness,
                            PedestalLengthM = current.PedestalLengthM,
                            PedestalWidthM = current.PedestalWidthM,
                            PedestalHeightM = current.PedestalHeightM
                        },
                        appliedLoad,
                        geotechnical,
                        settings);

                    if (!scheme.IsFeasible)
                    {
                        continue;
                    }

                    var changeScore =
                        (length - current.BaseLengthM) / settings.DimensionStepM +
                        (width - current.BaseWidthM) / settings.DimensionStepM +
                        1.5 * (thickness - current.BaseThicknessM) / settings.DimensionStepM;
                    candidates.Add((scheme, changeScore));
                }
            }
        }

        return candidates
            .OrderBy(item => item.ChangeScore)
            .ThenBy(item => item.Scheme.Quantities.ConcreteM3)
            .ThenBy(item => item.Scheme.MaximumUtilization)
            .Select(item => item.Scheme)
            .FirstOrDefault();
    }

    private FoundationScheme? FindNearestFeasiblePile(
        FoundationGeometry current,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        var pile = settings.Pile;
        var candidates = new List<(FoundationScheme Scheme, double ChangeScore)>();
        var startDiameter = Math.Max(
            current.PileDiameterM,
            pile.MinimumPileDiameterM);
        var startLength = Math.Max(
            current.PileLengthM,
            pile.MinimumPileLengthM);
        foreach (var diameter in Range(
                     startDiameter,
                     pile.MaximumPileDiameterM,
                     pile.PileDiameterStepM))
        {
            foreach (var length in Range(
                         startLength,
                         pile.MaximumPileLengthM,
                         pile.PileLengthStepM))
            {
                var scheme = _calculator.Calculate(
                    new FoundationGeometry
                    {
                        PileDiameterM = diameter,
                        PileLengthM = length,
                        PedestalLengthM = diameter,
                        PedestalWidthM = diameter,
                        PedestalHeightM = pile.AboveGroundHeightM,
                        PileCount = pile.PileCount,
                        PileCenterSpacingM = pile.PileCenterSpacingM,
                        TieBeamCount = pile.TieBeamRequired
                            ? pile.PileCount
                            : 0,
                        TieBeamWidthM = pile.TieBeamWidthM,
                        TieBeamHeightM = pile.TieBeamHeightM
                    },
                    appliedLoad,
                    geotechnical,
                    settings);
                if (!scheme.IsFeasible)
                {
                    continue;
                }

                var changeScore =
                    (diameter - current.PileDiameterM) /
                    pile.PileDiameterStepM +
                    (length - current.PileLengthM) /
                    pile.PileLengthStepM;
                candidates.Add((scheme, changeScore));
            }
        }

        return candidates
            .OrderBy(item => item.ChangeScore)
            .ThenBy(item => item.Scheme.Quantities.ConcreteM3)
            .ThenBy(item => item.Scheme.MaximumUtilization)
            .Select(item => item.Scheme)
            .FirstOrDefault();
    }

    private FoundationScheme? FindNearestFeasibleRigidShortPile(
        FoundationGeometry current,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        var rigid = settings.RigidShortPile;
        var candidates = new List<(FoundationScheme Scheme, double ChangeScore)>();
        var startDiameter = Math.Max(current.PileDiameterM, rigid.MinimumDiameterM);
        var startDepth = Math.Max(current.PileLengthM, rigid.MinimumEmbeddedDepthM);
        foreach (var diameter in Range(
                     startDiameter,
                     rigid.MaximumDiameterM,
                     rigid.DiameterStepM))
        {
            foreach (var depth in Range(
                         startDepth,
                         rigid.MaximumEmbeddedDepthM,
                         rigid.EmbeddedDepthStepM))
            {
                var scheme = _calculator.Calculate(
                    new FoundationGeometry
                    {
                        PileDiameterM = diameter,
                        PileLengthM = depth,
                        PedestalLengthM = diameter,
                        PedestalWidthM = diameter,
                        PedestalHeightM = rigid.AboveGroundHeightM
                    },
                    appliedLoad,
                    geotechnical,
                    settings);
                if (!scheme.IsFeasible)
                {
                    continue;
                }
                var score =
                    (diameter - current.PileDiameterM) / rigid.DiameterStepM +
                    (depth - current.PileLengthM) / rigid.EmbeddedDepthStepM;
                candidates.Add((scheme, score));
            }
        }
        return candidates
            .OrderBy(item => item.ChangeScore)
            .ThenBy(item => item.Scheme.Quantities.ConcreteM3)
            .ThenBy(item => item.Scheme.MaximumUtilization)
            .Select(item => item.Scheme)
            .FirstOrDefault();
    }

    private FoundationScheme? FindNearestFeasibleRigidRectangularShortPile(
        FoundationGeometry current,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        var rigid = settings.RigidShortPile;
        var candidates = new List<(FoundationScheme Scheme, double ChangeScore)>();
        var startLength = Math.Max(
            current.BaseLengthM,
            rigid.MinimumRectangularLengthM);
        var startWidth = Math.Max(
            current.BaseWidthM,
            rigid.MinimumRectangularWidthM);
        var startDepth = Math.Max(
            current.PileLengthM,
            rigid.MinimumEmbeddedDepthM);
        foreach (var length in Range(
                     startLength,
                     rigid.MaximumRectangularLengthM,
                     rigid.RectangularLengthStepM))
        {
            foreach (var width in Range(
                         startWidth,
                         rigid.MaximumRectangularWidthM,
                         rigid.RectangularWidthStepM))
            {
                foreach (var depth in Range(
                             startDepth,
                             rigid.MaximumEmbeddedDepthM,
                             rigid.EmbeddedDepthStepM))
                {
                    var scheme = _calculator.Calculate(
                        new FoundationGeometry
                        {
                            BaseLengthM = length,
                            BaseWidthM = width,
                            PileLengthM = depth,
                            PedestalLengthM = length,
                            PedestalWidthM = width,
                            PedestalHeightM = rigid.AboveGroundHeightM
                        },
                        appliedLoad,
                        geotechnical,
                        settings);
                    if (!scheme.IsFeasible)
                    {
                        continue;
                    }
                    var score =
                        (length - current.BaseLengthM) /
                        rigid.RectangularLengthStepM +
                        (width - current.BaseWidthM) /
                        rigid.RectangularWidthStepM +
                        (depth - current.PileLengthM) /
                        rigid.EmbeddedDepthStepM;
                    candidates.Add((scheme, score));
                }
            }
        }
        return candidates
            .OrderBy(item => item.ChangeScore)
            .ThenBy(item => item.Scheme.Quantities.ConcreteM3)
            .ThenBy(item => item.Scheme.MaximumUtilization)
            .Select(item => item.Scheme)
            .FirstOrDefault();
    }

    private static FoundationAdjustmentAdvice BuildTargetedAdvice(
        FoundationCheckResult check,
        double dimensionStep)
    {
        return check.Code switch
        {
            "CONTACT" => new FoundationAdjustmentAdvice
            {
                Priority = 10,
                Title = "基底出现脱开",
                Action = $"优先沿控制弯矩方向增加底板平面尺寸，每次至少增加{dimensionStep:F1} m后复算。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "BEARING_AVERAGE" or "BEARING_MAX" => new FoundationAdjustmentAdvice
            {
                Priority = 20,
                Title = "地基承载力不足",
                Action = $"增加底板长或宽以扩大受压面积，每次至少增加{dimensionStep:F1} m；同时核对承载力取值。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "SLIDING" => new FoundationAdjustmentAdvice
            {
                Priority = 30,
                Title = "抗滑移不足",
                Action = $"优先增加底板厚度或平面尺寸以增加自重，每次至少增加{dimensionStep:F1} m；必要时评估抗剪键。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "PUNCHING_X" or "PUNCHING_Y" or "SHEAR_X" or "SHEAR_Y" => new FoundationAdjustmentAdvice
            {
                Priority = 35,
                Title = "底板冲切或受剪承载力不足",
                Action = $"优先增加底板厚度，每次至少增加{dimensionStep:F1} m；同时核对混凝土抗拉强度和保护层。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "BENDING_APPLICABILITY" => new FoundationAdjustmentAdvice
            {
                Priority = 40,
                Title = "底板简化受弯公式超出适用范围",
                Action = $"增加底板厚度或减小悬挑宽厚比；偏心过大时同步增加平面尺寸，每次至少增加{dimensionStep:F1} m。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "BOTTOM_REINFORCEMENT_X" or "BOTTOM_REINFORCEMENT_Y" => new FoundationAdjustmentAdvice
            {
                Priority = 45,
                Title = "底板配筋不足",
                Action = "优先减小钢筋间距或增大钢筋直径；若受弯计算控制，可同步增加底板厚度。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "PILE_COMPRESSION" => new FoundationAdjustmentAdvice
            {
                Priority = 20,
                Title = "单桩抗压承载力不足",
                Action = "优先增加桩长使桩端进入更好持力层；仍不足时增大单桩桩径，并复核成桩工艺和尺寸效应。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "PILE_UPLIFT" => new FoundationAdjustmentAdvice
            {
                Priority = 25,
                Title = "单桩抗拔承载力不足",
                Action = "优先增加有效桩长，复核各土层抗拔系数；必要时增大单桩桩径并复核钢筋锚固。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "PILE_HORIZONTAL" => new FoundationAdjustmentAdvice
            {
                Priority = 30,
                Title = "单桩水平承载力不足",
                Action = "增大桩径或埋深，并根据地勘m值、试桩成果或专项m法计算重新确认单桩水平承载力；不得用增加群桩效率代替。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "PILE_LONGITUDINAL_REINFORCEMENT" => new FoundationAdjustmentAdvice
            {
                Priority = 40,
                Title = "桩身纵筋不足",
                Action = "增加纵筋根数或直径，并复核钢筋净距、保护层和钢筋笼构造。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "RIGID_CLASSIFICATION" => new FoundationAdjustmentAdvice
            {
                Priority = 10,
                Title = "当前尺寸不属于刚性桩",
                Action = "优先减小计算埋深或增大桩径；若αh仍大于2.5，应改用弹性桩/常规桩基础模型。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "RIGID_RECT_CLASSIFICATION_X" or "RIGID_RECT_CLASSIFICATION_Y" => new FoundationAdjustmentAdvice
            {
                Priority = 10,
                Title = "矩形短柱桩当前方向不属于刚性桩",
                Action = "增大该方向截面惯性矩或调整埋深；若任一方向αh仍大于2.5，应改用弹性桩/常规桩基础模型。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "RIGID_OVERTURNING" => new FoundationAdjustmentAdvice
            {
                Priority = 20,
                Title = "刚性短柱桩抗倾覆不足",
                Action = "增加桩径或埋深，并核对主要影响深度内分层m值、土重度和内摩擦角。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "RIGID_RECT_OVERTURNING_X" or "RIGID_RECT_OVERTURNING_Y" => new FoundationAdjustmentAdvice
            {
                Priority = 20,
                Title = "矩形刚性短柱桩抗倾覆不足",
                Action = "优先增大控制方向边长及垂直荷载方向投影宽度，必要时增加埋深，并复核矩形推广公式适用性。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "RIGID_LONGITUDINAL_REINFORCEMENT" => new FoundationAdjustmentAdvice
            {
                Priority = 30,
                Title = "刚性短柱桩纵筋不足",
                Action = "增加纵筋根数或直径，并复核圆周净距、保护层和连接区锚固。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "RIGID_RECT_BIAXIAL_COMPRESSION" or "RIGID_RECT_LONGITUDINAL_REINFORCEMENT" => new FoundationAdjustmentAdvice
            {
                Priority = 30,
                Title = "矩形刚性短柱桩双向偏压或纵筋不足",
                Action = "增大控制方向截面边长，或增加周边纵筋根数/直径，并复核四角钢筋、净距、保护层及连接区锚固。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "RIGID_GROSS_SHEAR" or "RIGID_STIRRUP_REINFORCEMENT" => new FoundationAdjustmentAdvice
            {
                Priority = 35,
                Title = "刚性短柱桩受剪或箍筋不足",
                Action = "优先增大桩径；箍筋不足时同步增大直径或减小间距，并复核构造要求。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            "RIGID_RECT_GROSS_SHEAR_X" or
                "RIGID_RECT_GROSS_SHEAR_Y" or
                "RIGID_RECT_STIRRUP_REINFORCEMENT" => new FoundationAdjustmentAdvice
            {
                Priority = 35,
                Title = "矩形刚性短柱桩受剪或箍筋不足",
                Action = "增大控制方向有效高度；箍筋不足时增加有效肢数、增大直径或减小间距，并复核双向受剪构造。",
                Reason = check.Explanation,
                IsBlocking = true
            },
            _ => new FoundationAdjustmentAdvice
            {
                Priority = 90,
                Title = $"{check.Name}不满足",
                Action = "检查控制参数并增大相关基础尺寸后复算。",
                Reason = check.Explanation,
                IsBlocking = true
            }
        };
    }

    private static IEnumerable<double> Range(double start, double end, double step)
    {
        var normalizedStart = Math.Ceiling((start - 1e-9) / step) * step;
        var count = (int)Math.Floor((end - normalizedStart) / step + 1e-9);
        for (var index = 0; index <= count; index++)
        {
            yield return Math.Round(normalizedStart + index * step, 3);
        }
    }
}
