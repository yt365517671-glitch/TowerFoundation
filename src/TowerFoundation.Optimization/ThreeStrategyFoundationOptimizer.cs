using TowerFoundation.Calculation;
using TowerFoundation.Domain;
using System.Text;

namespace TowerFoundation.Optimization;

public sealed class ThreeStrategyFoundationOptimizer
{
    private readonly RectangularShortColumnFoundationCalculator _calculator;

    public ThreeStrategyFoundationOptimizer(RectangularShortColumnFoundationCalculator calculator)
    {
        _calculator = calculator;
    }

    public IReadOnlyList<FoundationScheme> Optimize(
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        ValidateSettings(settings);

        var evaluatedCandidates = GenerateCandidates(appliedLoad, geotechnical, settings)
            .ToList();
        var candidates = evaluatedCandidates
            .Where(candidate => candidate.IsFeasible)
            .ToList();

        if (candidates.Count == 0)
        {
            throw BuildNoFeasibleSchemeException(settings, evaluatedCandidates);
        }

        var selected = new List<FoundationScheme>();

        selected.Add(SelectCandidate(
            candidates,
            selected,
            OptimizationPreference.Economy,
            candidate =>
                candidate.Quantities.ConcreteM3 +
                0.10 * candidate.Quantities.ExcavationM3 +
                0.0005 * candidate.Quantities.EstimatedReinforcementKg));

        selected.Add(SelectCandidate(
            candidates,
            selected,
            OptimizationPreference.Constructability,
            candidate =>
                3.0 * ConstructionDepth(candidate) +
                0.15 * Math.Abs(candidate.Geometry.BaseLengthM - candidate.Geometry.BaseWidthM) +
                0.08 * candidate.Quantities.ConcreteM3));

        selected.Add(SelectCandidate(
            candidates,
            selected,
            OptimizationPreference.Robustness,
            candidate =>
                candidate.MaximumUtilization +
                0.008 * candidate.Quantities.ConcreteM3));

        ApplyPresentation(selected);
        MarkConvergedStrategies(selected);
        return selected;
    }

    private IEnumerable<FoundationScheme> GenerateCandidates(
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        if (settings.FoundationType == FoundationType.Pile)
        {
            foreach (var candidate in GeneratePileCandidates(
                         appliedLoad,
                         geotechnical,
                         settings))
            {
                yield return candidate;
            }

            yield break;
        }

        if (settings.FoundationType == FoundationType.RigidShortPile)
        {
            foreach (var candidate in GenerateRigidShortPileCandidates(
                         appliedLoad,
                         geotechnical,
                         settings))
            {
                yield return candidate;
            }

            yield break;
        }

        if (settings.FoundationType == FoundationType.RigidRectangularShortPile)
        {
            foreach (var candidate in GenerateRigidRectangularShortPileCandidates(
                         appliedLoad,
                         geotechnical,
                         settings))
            {
                yield return candidate;
            }

            yield break;
        }

        var pedestalLength = settings.FoundationType == FoundationType.CircularShortColumn
            ? settings.PedestalDiameterM
            : settings.PedestalLengthM;
        var pedestalWidth = settings.FoundationType == FoundationType.CircularShortColumn
            ? settings.PedestalDiameterM
            : settings.PedestalWidthM;
        var raftLegCount = settings.FoundationType == FoundationType.Raft
            ? settings.Pile.PileCount
            : 1;
        var raftLegSpacingM = settings.FoundationType == FoundationType.Raft && raftLegCount > 1
            ? settings.Pile.PileCenterSpacingM
            : 0;
        var raftLegSpanXM = raftLegCount > 1 ? raftLegSpacingM : 0;
        var raftLegSpanYM = raftLegCount switch
        {
            3 => 2 * Math.Sqrt(3) * raftLegSpacingM / 3,
            4 => raftLegSpacingM,
            _ => 0
        };
        var minimumLength = Math.Max(
            settings.MinimumBaseLengthM,
            pedestalLength + raftLegSpanXM + 2 * settings.DimensionStepM);
        var minimumWidth = Math.Max(
            settings.MinimumBaseWidthM,
            pedestalWidth + raftLegSpanYM + 2 * settings.DimensionStepM);

        foreach (var length in Range(
                     minimumLength,
                     settings.MaximumBaseLengthM,
                     settings.DimensionStepM))
        {
            foreach (var width in Range(
                         minimumWidth,
                         settings.MaximumBaseWidthM,
                         settings.DimensionStepM))
            {
                foreach (var thickness in Range(
                             settings.MinimumBaseThicknessM,
                             settings.MaximumBaseThicknessM,
                             settings.DimensionStepM))
                {
                    var scheme = IndependentFoundationTieBeamCalculator.Apply(
                        _calculator.Calculate(
                        new FoundationGeometry
                        {
                            FoundationUnitCount = Math.Max(1, appliedLoad.FoundationUnitCount),
                            BaseLengthM = length,
                            BaseWidthM = width,
                            BaseThicknessM = thickness,
                            PedestalLengthM = pedestalLength,
                            PedestalWidthM = pedestalWidth,
                            PedestalHeightM = settings.PedestalHeightM,
                            PileCount = raftLegCount,
                            PileCenterSpacingM = raftLegSpacingM
                        },
                        appliedLoad,
                        geotechnical,
                        settings),
                        appliedLoad.ResolveStructuralDesignLoad(settings),
                        geotechnical,
                        settings);

                    yield return scheme;
                }
            }
        }
    }

    private IEnumerable<FoundationScheme> GeneratePileCandidates(
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        var pile = settings.Pile;
        foreach (var diameter in Range(
                     pile.MinimumPileDiameterM,
                     pile.MaximumPileDiameterM,
                     pile.PileDiameterStepM))
        {
            foreach (var length in Range(
                         pile.MinimumPileLengthM,
                         pile.MaximumPileLengthM,
                         pile.PileLengthStepM))
            {
                yield return IndependentFoundationTieBeamCalculator.Apply(
                    _calculator.Calculate(
                    new FoundationGeometry
                    {
                        FoundationUnitCount = Math.Max(1, appliedLoad.FoundationUnitCount),
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
                    settings),
                    appliedLoad.ResolveStructuralDesignLoad(settings),
                    geotechnical,
                    settings);
            }
        }
    }

    private IEnumerable<FoundationScheme> GenerateRigidShortPileCandidates(
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        var rigid = settings.RigidShortPile;
        foreach (var diameter in Range(
                     rigid.MinimumDiameterM,
                     rigid.MaximumDiameterM,
                     rigid.DiameterStepM))
        {
            foreach (var embeddedDepth in Range(
                         rigid.MinimumEmbeddedDepthM,
                         rigid.MaximumEmbeddedDepthM,
                         rigid.EmbeddedDepthStepM))
            {
                yield return IndependentFoundationTieBeamCalculator.Apply(
                    _calculator.Calculate(
                        new FoundationGeometry
                        {
                            FoundationUnitCount = Math.Max(1, appliedLoad.FoundationUnitCount),
                            PileDiameterM = diameter,
                            PileLengthM = embeddedDepth,
                            PedestalLengthM = diameter,
                            PedestalWidthM = diameter,
                            PedestalHeightM = rigid.AboveGroundHeightM
                        },
                        appliedLoad,
                        geotechnical,
                        settings),
                    appliedLoad.ResolveStructuralDesignLoad(settings),
                    geotechnical,
                    settings);
            }
        }
    }

    private IEnumerable<FoundationScheme> GenerateRigidRectangularShortPileCandidates(
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        var rigid = settings.RigidShortPile;
        foreach (var length in Range(
                     rigid.MinimumRectangularLengthM,
                     rigid.MaximumRectangularLengthM,
                     rigid.RectangularLengthStepM))
        {
            foreach (var width in Range(
                         rigid.MinimumRectangularWidthM,
                         rigid.MaximumRectangularWidthM,
                         rigid.RectangularWidthStepM))
            {
                foreach (var embeddedDepth in Range(
                             rigid.MinimumEmbeddedDepthM,
                             rigid.MaximumEmbeddedDepthM,
                             rigid.EmbeddedDepthStepM))
                {
                    yield return IndependentFoundationTieBeamCalculator.Apply(
                        _calculator.Calculate(
                        new FoundationGeometry
                        {
                            FoundationUnitCount = Math.Max(1, appliedLoad.FoundationUnitCount),
                            BaseLengthM = length,
                            BaseWidthM = width,
                            PileLengthM = embeddedDepth,
                            PedestalLengthM = length,
                            PedestalWidthM = width,
                            PedestalHeightM = rigid.AboveGroundHeightM
                        },
                        appliedLoad,
                        geotechnical,
                        settings),
                        appliedLoad.ResolveStructuralDesignLoad(settings),
                        geotechnical,
                        settings);
                }
            }
        }
    }

    private static FoundationScheme SelectCandidate(
        IReadOnlyCollection<FoundationScheme> candidates,
        IReadOnlyCollection<FoundationScheme> alreadySelected,
        OptimizationPreference preference,
        Func<FoundationScheme, double> scoreSelector)
    {
        var ordered = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = scoreSelector(candidate)
            })
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Candidate.Quantities.ConcreteM3)
            .ThenBy(item => item.Candidate.MaximumUtilization)
            .ToList();

        var result = ordered.FirstOrDefault(item =>
                         alreadySelected.All(selected =>
                             !SameGeometry(selected.Geometry, item.Candidate.Geometry))) ??
                     ordered[0];

        var selected = CloneScheme(result.Candidate);
        selected.Preference = preference;
        selected.Score = result.Score;
        return selected;
    }

    private static FoundationScheme CloneScheme(FoundationScheme source) =>
        new()
        {
            FoundationType = source.FoundationType,
            Geometry = new FoundationGeometry
            {
                FoundationUnitCount = source.Geometry.FoundationUnitCount,
                BaseLengthM = source.Geometry.BaseLengthM,
                BaseWidthM = source.Geometry.BaseWidthM,
                BaseThicknessM = source.Geometry.BaseThicknessM,
                PedestalLengthM = source.Geometry.PedestalLengthM,
                PedestalWidthM = source.Geometry.PedestalWidthM,
                PedestalHeightM = source.Geometry.PedestalHeightM,
                PileDiameterM = source.Geometry.PileDiameterM,
                PileLengthM = source.Geometry.PileLengthM,
                PileCount = source.Geometry.PileCount,
                PileCenterSpacingM = source.Geometry.PileCenterSpacingM,
                TieBeamCount = source.Geometry.TieBeamCount,
                TieBeamWidthM = source.Geometry.TieBeamWidthM,
                TieBeamHeightM = source.Geometry.TieBeamHeightM
            },
            Checks = [.. source.Checks],
            ReinforcementDesigns = [.. source.ReinforcementDesigns],
            Quantities = source.Quantities
        };

    private static bool SameGeometry(FoundationGeometry left, FoundationGeometry right)
    {
        const double tolerance = 1e-9;
        return Math.Abs(left.BaseLengthM - right.BaseLengthM) < tolerance &&
               Math.Abs(left.BaseWidthM - right.BaseWidthM) < tolerance &&
               Math.Abs(left.BaseThicknessM - right.BaseThicknessM) < tolerance &&
               Math.Abs(left.PileDiameterM - right.PileDiameterM) < tolerance &&
               Math.Abs(left.PileLengthM - right.PileLengthM) < tolerance;
    }

    private static double ConstructionDepth(FoundationScheme scheme) =>
        scheme.FoundationType is
            FoundationType.Pile or
            FoundationType.RigidShortPile or
            FoundationType.RigidRectangularShortPile
            ? scheme.Geometry.PileLengthM
            : scheme.Geometry.EmbedmentDepthM;

    private static IEnumerable<double> Range(double start, double end, double step)
    {
        var count = (int)Math.Floor((end - start) / step + 1e-9);
        for (var index = 0; index <= count; index++)
        {
            yield return Math.Round(start + index * step, 3);
        }
    }

    private static void ApplyPresentation(IEnumerable<FoundationScheme> schemes)
    {
        foreach (var scheme in schemes)
        {
            (scheme.Name, scheme.Description) = scheme.Preference switch
            {
                OptimizationPreference.Economy =>
                    ("经济型", "优先降低混凝土、土方及材料估算量，在满足当前校核范围的前提下控制造价。"),
                OptimizationPreference.Constructability =>
                    ("施工型", "优先控制埋深和几何复杂度，便于开挖、支模和现场施工。"),
                OptimizationPreference.Robustness =>
                    ("稳健型", "优先降低控制验算利用率，为荷载和地勘参数变化保留更高余量。"),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }

    private static void MarkConvergedStrategies(IReadOnlyList<FoundationScheme> schemes)
    {
        foreach (var scheme in schemes)
        {
            if (schemes.Any(other =>
                    !ReferenceEquals(other, scheme) &&
                    SameGeometry(other.Geometry, scheme.Geometry)))
            {
                scheme.Description += " 当前不同策略在既定搜索步长下收敛到同一尺寸。";
            }
        }
    }

    private static void ValidateSettings(FoundationDesignSettings settings)
    {
        if (settings.DimensionStepM <= 0)
        {
            throw InvalidSearchRange(
                "基础尺寸步长设置错误",
                $"“基础尺寸步长”当前为{settings.DimensionStepM:F2} m，必须大于0。请在第5步“高级设计参数”中改为0.10 m或0.20 m后重新生成。");
        }

        ValidateRange("底板长范围（m）", settings.MinimumBaseLengthM, settings.MaximumBaseLengthM);
        ValidateRange("底板宽范围（m）", settings.MinimumBaseWidthM, settings.MaximumBaseWidthM);
        ValidateRange("底板厚范围（m）", settings.MinimumBaseThicknessM, settings.MaximumBaseThicknessM);

        if (settings.FoundationType == FoundationType.Pile)
        {
            var pile = settings.Pile;
            if (pile.PileDiameterStepM <= 0 ||
                pile.PileLengthStepM <= 0)
            {
                throw InvalidSearchRange(
                    "灌注桩搜索步长设置错误",
                    $"桩径步长为{pile.PileDiameterStepM:F2} m、桩长步长为{pile.PileLengthStepM:F2} m，两者都必须大于0。请在第5步“高级设计参数”中修改后重新生成。");
            }

            ValidateRange("桩径搜索范围（m）", pile.MinimumPileDiameterM, pile.MaximumPileDiameterM);
            ValidateRange("桩长搜索范围（m）", pile.MinimumPileLengthM, pile.MaximumPileLengthM);
        }

        if (settings.FoundationType == FoundationType.RigidShortPile)
        {
            var rigid = settings.RigidShortPile;
            if (rigid.DiameterStepM <= 0 ||
                rigid.EmbeddedDepthStepM <= 0)
            {
                throw InvalidSearchRange(
                    "圆形刚性短柱桩搜索步长设置错误",
                    $"直径步长为{rigid.DiameterStepM:F2} m、埋深步长为{rigid.EmbeddedDepthStepM:F2} m，两者都必须大于0。请在第5步“高级设计参数”中修改后重新生成。");
            }

            ValidateRange("圆形截面直径范围（m）", rigid.MinimumDiameterM, rigid.MaximumDiameterM);
            ValidateRange("埋深搜索范围（m）", rigid.MinimumEmbeddedDepthM, rigid.MaximumEmbeddedDepthM);
        }

        if (settings.FoundationType == FoundationType.RigidRectangularShortPile)
        {
            var rigid = settings.RigidShortPile;
            if (rigid.RectangularLengthStepM <= 0 ||
                rigid.RectangularWidthStepM <= 0 ||
                rigid.EmbeddedDepthStepM <= 0)
            {
                throw InvalidSearchRange(
                    "矩形刚性短柱桩搜索步长设置错误",
                    $"截面长、宽、埋深步长分别为{rigid.RectangularLengthStepM:F2}、{rigid.RectangularWidthStepM:F2}、{rigid.EmbeddedDepthStepM:F2} m，均必须大于0。请在第5步“高级设计参数”中修改后重新生成。");
            }

            ValidateRange("矩形X向边长范围（m）", rigid.MinimumRectangularLengthM, rigid.MaximumRectangularLengthM);
            ValidateRange("矩形Y向边长范围（m）", rigid.MinimumRectangularWidthM, rigid.MaximumRectangularWidthM);
            ValidateRange("埋深搜索范围（m）", rigid.MinimumEmbeddedDepthM, rigid.MaximumEmbeddedDepthM);
        }
    }

    private static FoundationOptimizationException BuildNoFeasibleSchemeException(
        FoundationDesignSettings settings,
        IReadOnlyList<FoundationScheme> evaluatedCandidates)
    {
        if (evaluatedCandidates.Count == 0)
        {
            return BuildEmptySearchException(settings);
        }

        var closest = evaluatedCandidates
            .OrderBy(candidate => BlockingChecks(candidate).Count)
            .ThenBy(CandidateFailureSeverity)
            .ThenByDescending(CandidateSize)
            .First();
        var blockers = BlockingChecks(closest)
            .OrderByDescending(check => check.Status == CheckStatus.Fail)
            .ThenByDescending(CheckSeverity)
            .ToList();
        var actions = BuildAdjustmentActions(settings, blockers);
        var controllingName = blockers.FirstOrDefault()?.Name ?? "尺寸搜索边界";

        var message = new StringBuilder()
            .AppendLine("搜索已完成，但当前范围内没有任何方案能够通过全部计算校核。")
            .AppendLine()
            .AppendLine($"最接近可行的尺寸：{closest.GeometrySummary}")
            .AppendLine("卡住的校核项：");

        foreach (var (check, index) in blockers.Take(3).Select((item, index) => (item, index)))
        {
            message.AppendLine($"{index + 1}. {FormatBlockingCheck(check)}");
        }

        message.AppendLine()
            .AppendLine("建议这样修改：");
        foreach (var (action, index) in actions.Select((item, index) => (item, index)))
        {
            message.AppendLine($"{index + 1}. {action}");
        }

        message.AppendLine()
            .Append("软件已展开第5步“高级设计参数”。修改上述数值后，点击“生成/重新生成三种方案”。");

        return new FoundationOptimizationException(
            $"方案被“{controllingName}”卡住",
            message.ToString(),
            $"未找到可行方案：{controllingName}未通过；已展开高级设计参数并给出具体修改值。");
    }

    private static FoundationOptimizationException BuildEmptySearchException(
        FoundationDesignSettings settings)
    {
        if (settings.FoundationType is not FoundationType.Pile and
            not FoundationType.RigidShortPile and
            not FoundationType.RigidRectangularShortPile)
        {
            var pedestalLength = settings.FoundationType == FoundationType.CircularShortColumn
                ? settings.PedestalDiameterM
                : settings.PedestalLengthM;
            var pedestalWidth = settings.FoundationType == FoundationType.CircularShortColumn
                ? settings.PedestalDiameterM
                : settings.PedestalWidthM;
            var requiredLength = Math.Max(
                settings.MinimumBaseLengthM,
                pedestalLength + 2 * settings.DimensionStepM);
            var requiredWidth = Math.Max(
                settings.MinimumBaseWidthM,
                pedestalWidth + 2 * settings.DimensionStepM);
            return InvalidSearchRange(
                "基础尺寸上限小于起始尺寸",
                $"短柱尺寸和构造余量要求底板至少为{requiredLength:F2}×{requiredWidth:F2} m，但当前“底板长范围（m）”和“底板宽范围（m）”右侧上限仅为{settings.MaximumBaseLengthM:F2}/{settings.MaximumBaseWidthM:F2} m。\n\n" +
                $"请在第5步“高级设计参数”中把“底板长范围（m）”右侧上限调至不少于{requiredLength:F2} m，把“底板宽范围（m）”右侧上限调至不少于{requiredWidth:F2} m，然后重新生成。");
        }

        return InvalidSearchRange(
            "尺寸搜索范围没有可计算点",
            "当前最小值、最大值和步长组合没有形成任何可计算尺寸。请在第5步“高级设计参数”中检查红框内的最小值、最大值和步长；最大值必须不小于最小值，步长必须大于0。");
    }

    private static List<FoundationCheckResult> BlockingChecks(FoundationScheme candidate) =>
        candidate.Checks
            .Where(check => check.Status is CheckStatus.Fail or CheckStatus.NotEvaluated)
            .ToList();

    private static double CandidateFailureSeverity(FoundationScheme candidate) =>
        BlockingChecks(candidate).Sum(check => Math.Min(1000, CheckSeverity(check)));

    private static double CheckSeverity(FoundationCheckResult check)
    {
        if (check.Status == CheckStatus.NotEvaluated)
        {
            return 50;
        }

        return double.IsFinite(check.Utilization)
            ? Math.Max(1, check.Utilization)
            : 100;
    }

    private static double CandidateSize(FoundationScheme candidate) =>
        candidate.Geometry.BaseLengthM +
        candidate.Geometry.BaseWidthM +
        candidate.Geometry.BaseThicknessM +
        candidate.Geometry.PileDiameterM +
        candidate.Geometry.PileLengthM;

    private static string FormatBlockingCheck(FoundationCheckResult check)
    {
        if (check.Status == CheckStatus.NotEvaluated)
        {
            return $"{check.Name}未能计算，因为前置适用条件没有通过。";
        }

        if (double.IsFinite(check.Demand) &&
            double.IsFinite(check.Capacity) &&
            check.Capacity > 0)
        {
            var utilization = double.IsFinite(check.Utilization)
                ? $"，利用率{check.Utilization:P0}"
                : string.Empty;
            return $"{check.Name}：计算值{check.Demand:F2}{check.Unit}，允许值{check.Capacity:F2}{check.Unit}{utilization}。";
        }

        var explanation = FirstSentence(check.Explanation);
        return string.IsNullOrWhiteSpace(explanation)
            ? $"{check.Name}未通过。"
            : $"{check.Name}未通过：{explanation}";
    }

    private static IReadOnlyList<string> BuildAdjustmentActions(
        FoundationDesignSettings settings,
        IReadOnlyCollection<FoundationCheckResult> blockers)
    {
        var actions = new List<string>();
        var codes = blockers.Select(check => check.Code).ToHashSet(StringComparer.Ordinal);
        var blockingByPrefix = new Func<string, bool>(prefix =>
            codes.Any(code => code.StartsWith(prefix, StringComparison.Ordinal)));

        void Add(string action)
        {
            if (!actions.Contains(action, StringComparer.Ordinal))
            {
                actions.Add(action);
            }
        }

        if (codes.Contains("TIE_BEAM_CLEAR_LENGTH"))
        {
            Add($"核对并填写实际塔脚/基础中心距；当前为{settings.Pile.PileCenterSpacingM:F2} m。中心距必须大于单个基础沿连系梁方向的尺寸，否则独立基础会重叠，应改用共同筏板或调整基础方案，不能靠增大底板上限解决。");
        }

        if (codes.Contains("TIE_BEAM_LAYOUT"))
        {
            var minimumHeight = Math.Max(0.40, settings.Pile.PileCenterSpacingM / 15.0);
            Add($"连系梁截面不得小于宽0.25 m、高{minimumHeight:F2} m（当前{settings.Pile.TieBeamWidthM:F2}×{settings.Pile.TieBeamHeightM:F2} m）；请按整体分析和构造要求调整后复算。");
        }

        if (settings.FoundationType is FoundationType.RectangularShortColumn or
            FoundationType.CircularShortColumn or FoundationType.Raft)
        {
            var needsPlanExpansion =
                codes.Contains("CONTACT") ||
                codes.Contains("BEARING_AVERAGE") ||
                codes.Contains("BEARING_MAX") ||
                codes.Contains("SLIDING") ||
                codes.Contains("HIGH_WATER_ANTIFLOTATION");
            var needsThickness =
                codes.Contains("BENDING_APPLICABILITY") ||
                blockingByPrefix("PUNCHING_") ||
                blockingByPrefix("SHEAR_") ||
                blockingByPrefix("BOTTOM_REINFORCEMENT_");

            if (needsPlanExpansion)
            {
                Add($"把“底板长范围（m）”右侧上限由{settings.MaximumBaseLengthM:F2} m调至" +
                    $"{NextBound(settings.MaximumBaseLengthM, settings.DimensionStepM, 0.40):F2} m。");
                Add($"把“底板宽范围（m）”右侧上限由{settings.MaximumBaseWidthM:F2} m调至" +
                    $"{NextBound(settings.MaximumBaseWidthM, settings.DimensionStepM, 0.40):F2} m。");
            }

            if (needsThickness)
            {
                Add($"把“底板厚范围（m）”右侧上限由{settings.MaximumBaseThicknessM:F2} m调至" +
                    $"{NextBound(settings.MaximumBaseThicknessM, settings.DimensionStepM, 0.40):F2} m。");
            }

            if (blockingByPrefix("BOTTOM_REINFORCEMENT_"))
            {
                var reinforcement = blockers.First(check =>
                    check.Code.StartsWith("BOTTOM_REINFORCEMENT_", StringComparison.Ordinal));
                Add(BuildBottomReinforcementAction(settings, reinforcement));
            }

            if (blockingByPrefix("PEDESTAL_"))
            {
                Add($"短柱截面或配筋不足：将短柱长/宽由{settings.PedestalLengthM:F2}/{settings.PedestalWidthM:F2} m各增加0.20 m，或在“短柱结构参数”中提高纵筋数量/直径后复算。");
            }

            if (actions.Count == 0)
            {
                Add($"先把“底板长范围（m）”右侧上限由{settings.MaximumBaseLengthM:F2} m调至" +
                    $"{NextBound(settings.MaximumBaseLengthM, settings.DimensionStepM, 0.40):F2} m。");
                Add($"再把“底板宽范围（m）”右侧上限由{settings.MaximumBaseWidthM:F2} m调至" +
                    $"{NextBound(settings.MaximumBaseWidthM, settings.DimensionStepM, 0.40):F2} m，然后重新生成。");
            }
        }
        else if (settings.FoundationType == FoundationType.Pile)
        {
            var pile = settings.Pile;
            var needsLength = codes.Contains("PILE_COMPRESSION") ||
                              codes.Contains("PILE_UPLIFT") ||
                              codes.Contains("PILE_LAYER_LENGTH");
            var needsDiameter = codes.Contains("PILE_COMPRESSION") ||
                                codes.Contains("PILE_UPLIFT") ||
                                blockingByPrefix("PILE_AXIAL_BENDING_") ||
                                codes.Contains("PILE_GROSS_SHEAR") ||
                                codes.Contains("PILE_CRACK_WIDTH");

            if (needsLength)
            {
                Add($"把“桩长搜索范围（m）”右侧上限由{pile.MaximumPileLengthM:F2} m调至" +
                    $"{NextBound(pile.MaximumPileLengthM, pile.PileLengthStepM, 2.00):F2} m；地勘分层累计厚度也必须覆盖新的桩长。");
            }

            if (needsDiameter)
            {
                Add($"把“桩径搜索范围（m）”右侧上限由{pile.MaximumPileDiameterM:F2} m调至" +
                    $"{NextBound(pile.MaximumPileDiameterM, pile.PileDiameterStepM, 0.20):F2} m。");
            }

            if (codes.Contains("PILE_HORIZONTAL"))
            {
                var check = blockers.First(item => item.Code == "PILE_HORIZONTAL");
                Add($"“单桩水平承载力确认值”当前为{pile.SinglePileHorizontalCapacityKn:F2} kN，至少需要达到{check.Demand:F2} kN；请依据地勘、试桩或m法专项结果填写，不能直接猜大。");
            }

            if (codes.Contains("PILE_LONGITUDINAL_REINFORCEMENT") ||
                codes.Contains("PILE_STRUCTURAL_LONGITUDINAL_REINFORCEMENT") ||
                codes.Contains("PILE_AXIAL_BENDING_INTERACTION") ||
                codes.Contains("PILE_CRACK_WIDTH"))
            {
                var check = blockers.FirstOrDefault(item =>
                    item.Code is "PILE_LONGITUDINAL_REINFORCEMENT" or
                        "PILE_STRUCTURAL_LONGITUDINAL_REINFORCEMENT");
                Add(BuildCircularLongitudinalReinforcementAction(
                    "灌注桩纵筋",
                    pile.PileMainBarCount,
                    pile.PileMainBarDiameterMm,
                    check?.Demand ?? 0));
            }

            if (codes.Contains("PILE_STIRRUP_REINFORCEMENT"))
            {
                Add(BuildStirrupAction(
                    "灌注桩箍筋",
                    pile.StirrupDiameterMm,
                    pile.StirrupSpacingMm,
                    blockers.First(item => item.Code == "PILE_STIRRUP_REINFORCEMENT")));
            }

            if (codes.Contains("PILE_LAYOUT"))
            {
                Add("按塔型修正桩数和连梁：单管塔1根桩；三管塔/增高架3根独立桩加3根连梁；角钢塔4根独立桩加4根连梁，均不设承台。");
            }

            if (actions.Count == 0)
            {
                Add($"先把“桩径搜索范围（m）”右侧上限由{pile.MaximumPileDiameterM:F2} m调至" +
                    $"{NextBound(pile.MaximumPileDiameterM, pile.PileDiameterStepM, 0.20):F2} m。");
                Add($"再把“桩长搜索范围（m）”右侧上限由{pile.MaximumPileLengthM:F2} m调至" +
                    $"{NextBound(pile.MaximumPileLengthM, pile.PileLengthStepM, 2.00):F2} m，然后重新生成。");
            }
        }
        else
        {
            BuildRigidShortPileActions(settings, blockers, codes, blockingByPrefix, Add);
            if (actions.Count == 0)
            {
                var rigid = settings.RigidShortPile;
                if (settings.FoundationType == FoundationType.RigidRectangularShortPile)
                {
                    Add($"把“矩形X向边长范围（m）”右侧上限由{rigid.MaximumRectangularLengthM:F2} m调至" +
                        $"{NextBound(rigid.MaximumRectangularLengthM, rigid.RectangularLengthStepM, 0.40):F2} m。");
                    Add($"把“矩形Y向边长范围（m）”右侧上限由{rigid.MaximumRectangularWidthM:F2} m调至" +
                        $"{NextBound(rigid.MaximumRectangularWidthM, rigid.RectangularWidthStepM, 0.40):F2} m，再重新生成。");
                }
                else
                {
                    Add($"把“圆形截面直径范围（m）”右侧上限由{rigid.MaximumDiameterM:F2} m调至" +
                        $"{NextBound(rigid.MaximumDiameterM, rigid.DiameterStepM, 0.40):F2} m，再重新生成。");
                }
            }
        }

        return actions.Take(4).ToList();
    }

    private static void BuildRigidShortPileActions(
        FoundationDesignSettings settings,
        IReadOnlyCollection<FoundationCheckResult> blockers,
        IReadOnlySet<string> codes,
        Func<string, bool> blockingByPrefix,
        Action<string> add)
    {
        var rigid = settings.RigidShortPile;
        var rectangular = settings.FoundationType == FoundationType.RigidRectangularShortPile;
        var classificationFailure = codes.Contains("RIGID_CLASSIFICATION") ||
                                    blockingByPrefix("RIGID_RECT_CLASSIFICATION_");
        var needsSection = classificationFailure ||
                           codes.Contains("RIGID_OVERTURNING") ||
                           blockingByPrefix("RIGID_RECT_OVERTURNING_") ||
                           blockingByPrefix("RIGID_RECT_BETA_") ||
                           codes.Contains("RIGID_RECT_BASIC_RESPONSE") ||
                           codes.Contains("RIGID_GROSS_SHEAR") ||
                           blockingByPrefix("RIGID_RECT_GROSS_SHEAR_") ||
                           codes.Contains("RIGID_RECT_BIAXIAL_COMPRESSION");
        var needsDepth = codes.Contains("RIGID_OVERTURNING") ||
                         blockingByPrefix("RIGID_RECT_OVERTURNING_") ||
                         blockingByPrefix("RIGID_RECT_BETA_") ||
                         codes.Contains("RIGID_RECT_BASIC_RESPONSE");

        if (rectangular && needsSection)
        {
            add($"把“矩形X向边长范围（m）”右侧上限由{rigid.MaximumRectangularLengthM:F2} m调至" +
                $"{NextBound(rigid.MaximumRectangularLengthM, rigid.RectangularLengthStepM, 0.40):F2} m。");
            add($"把“矩形Y向边长范围（m）”右侧上限由{rigid.MaximumRectangularWidthM:F2} m调至" +
                $"{NextBound(rigid.MaximumRectangularWidthM, rigid.RectangularWidthStepM, 0.40):F2} m。");
        }
        else if (needsSection)
        {
            add($"把“圆形截面直径范围（m）”右侧上限由{rigid.MaximumDiameterM:F2} m调至" +
                $"{NextBound(rigid.MaximumDiameterM, rigid.DiameterStepM, 0.40):F2} m。");
        }

        if (needsDepth && !classificationFailure)
        {
            add($"把“埋深搜索范围（m）”右侧上限由{rigid.MaximumEmbeddedDepthM:F2} m调至" +
                $"{NextBound(rigid.MaximumEmbeddedDepthM, rigid.EmbeddedDepthStepM, 2.00):F2} m。");
        }

        if (classificationFailure)
        {
            add("刚性判别αh仍大于2.50时不要继续增加埋深；应优先增大截面，若仍不满足则把基础形式改为“单桩灌注桩”。");
        }

        if (codes.Contains("RIGID_LONGITUDINAL_REINFORCEMENT") ||
            codes.Contains("RIGID_RECT_LONGITUDINAL_REINFORCEMENT") ||
            codes.Contains("RIGID_RECT_BIAXIAL_COMPRESSION"))
        {
            var check = blockers.FirstOrDefault(item =>
                item.Code is "RIGID_LONGITUDINAL_REINFORCEMENT" or
                    "RIGID_RECT_LONGITUDINAL_REINFORCEMENT");
            add(BuildCircularLongitudinalReinforcementAction(
                "刚性短柱桩纵筋",
                rigid.LongitudinalBarCount,
                rigid.LongitudinalBarDiameterMm,
                check?.Demand ?? 0));
        }

        if (codes.Contains("RIGID_STIRRUP_REINFORCEMENT") ||
            codes.Contains("RIGID_RECT_STIRRUP_REINFORCEMENT"))
        {
            var check = blockers.First(item =>
                item.Code is "RIGID_STIRRUP_REINFORCEMENT" or
                    "RIGID_RECT_STIRRUP_REINFORCEMENT");
            add(BuildStirrupAction(
                "刚性短柱桩箍筋",
                rigid.StirrupDiameterMm,
                rigid.StirrupSpacingMm,
                check));
        }
    }

    private static string BuildBottomReinforcementAction(
        FoundationDesignSettings settings,
        FoundationCheckResult check)
    {
        var utilization = double.IsFinite(check.Utilization)
            ? Math.Max(1, check.Utilization)
            : 1.25;
        var targetDiameter = NextEven(
            settings.BottomBarDiameterMm * Math.Sqrt(1.05 * utilization));
        var targetSpacing = Math.Max(
            80,
            Math.Floor(settings.BottomBarSpacingMm / (1.05 * utilization) / 10) * 10);
        return $"{check.Name}配筋不足：把底筋由Φ{settings.BottomBarDiameterMm:F0}@{settings.BottomBarSpacingMm:F0}调整为至少Φ{targetDiameter:F0}@{targetSpacing:F0}，并重新计算确认。";
    }

    private static string BuildCircularLongitudinalReinforcementAction(
        string label,
        int currentCount,
        double currentDiameterMm,
        double requiredAreaMm2)
    {
        var singleBarArea = Math.PI * currentDiameterMm * currentDiameterMm / 4;
        var requiredCount = requiredAreaMm2 > 0
            ? MakeEven((int)Math.Ceiling(1.05 * requiredAreaMm2 / singleBarArea))
            : MakeEven(currentCount + 4);
        requiredCount = Math.Max(requiredCount, currentCount + 2);
        return $"{label}不足：在配筋参数中把{currentCount}Φ{currentDiameterMm:F0}增加至至少{requiredCount}Φ{currentDiameterMm:F0}，或采用等面积以上的更大直径钢筋。";
    }

    private static string BuildStirrupAction(
        string label,
        double diameterMm,
        double spacingMm,
        FoundationCheckResult check)
    {
        var utilization = double.IsFinite(check.Utilization)
            ? Math.Max(1, check.Utilization)
            : 1.25;
        var targetSpacing = Math.Max(
            80,
            Math.Floor(spacingMm / (1.05 * utilization) / 10) * 10);
        return $"{label}不足：把Φ{diameterMm:F0}@{spacingMm:F0}加密至至少Φ{diameterMm:F0}@{targetSpacing:F0}；若计算仍不满足，再把箍筋直径增加2 mm。";
    }

    private static double NextBound(double current, double step, double minimumIncrement)
    {
        var safeStep = step > 0 ? step : minimumIncrement;
        var target = current + Math.Max(minimumIncrement, 2 * safeStep);
        return Math.Round(Math.Ceiling((target - 1e-9) / safeStep) * safeStep, 3);
    }

    private static double NextEven(double value) =>
        Math.Ceiling(value / 2) * 2;

    private static int MakeEven(int value) =>
        value % 2 == 0 ? value : value + 1;

    private static string FirstSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var index = text.IndexOf('。');
        return index >= 0 ? text[..(index + 1)] : text;
    }

    private static void ValidateRange(string uiLabel, double minimum, double maximum)
    {
        if (minimum <= maximum)
        {
            return;
        }

        throw InvalidSearchRange(
            $"{uiLabel}设置错误",
            $"“{uiLabel}”左侧下限为{minimum:F2} m，大于右侧上限{maximum:F2} m。请把右侧上限调到不小于{minimum:F2} m，或降低左侧下限后重新生成。");
    }

    private static FoundationOptimizationException InvalidSearchRange(
        string title,
        string message) =>
        new(
            title,
            message,
            message.Replace("\n", " ", StringComparison.Ordinal));
}
