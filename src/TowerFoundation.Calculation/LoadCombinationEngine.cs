using TowerFoundation.Domain;

namespace TowerFoundation.Calculation;

public sealed class LoadCombinationEngine
{
    public FoundationLoad Apply(
        FoundationLoad original,
        LoadCombinationDesignInput input,
        int foundationUnitCount,
        bool tieBeamsRequired)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(input);

        if (!input.UseDecomposedActions)
        {
            return EnsureTrace(original);
        }

        ValidateFactors(input);
        if (!input.PermanentAction.HasMeaningfulLoad &&
            !input.LeadingVariableAction.HasMeaningfulLoad)
        {
            throw new InvalidOperationException(
                "已启用作用分解组合，但永久作用和主导可变作用均为空。请录入作用效应，或关闭作用分解组合。" );
        }

        var source = input.Source.Display;
        var confirmed = input.Source.IsConfirmed;
        var standard = Combine(
            LoadCombinationKind.Standard,
            "正常使用极限状态标准组合",
            $"Sd=Gk+Q1k；来源：{source}",
            confirmed,
            (input.PermanentAction, 1.0),
            (input.LeadingVariableAction, 1.0));
        var basic = Combine(
            LoadCombinationKind.Basic,
            "承载能力极限状态基本组合",
            $"Sd={input.PermanentFactor:F3}Gk+{input.VariableFactor:F3}Q1k；来源：{source}",
            confirmed,
            (input.PermanentAction, input.PermanentFactor),
            (input.LeadingVariableAction, input.VariableFactor));
        var quasi = Combine(
            LoadCombinationKind.QuasiPermanent,
            "正常使用极限状态准永久组合",
            $"Sd=Gk+{input.QuasiPermanentVariableFactor:F3}Q1k；来源：{source}",
            confirmed,
            (input.PermanentAction, 1.0),
            (input.LeadingVariableAction, input.QuasiPermanentVariableFactor));

        var trace = new List<FoundationLoadCombination> { standard, basic, quasi };
        FoundationLoadCombination? seismic = null;
        if (input.SeismicAction.HasMeaningfulLoad)
        {
            seismic = Combine(
                LoadCombinationKind.Seismic,
                "地震设计状况组合",
                $"Sd={input.SeismicPermanentFactor:F3}Gk+{input.SeismicVariableCombinationFactor:F3}Q1k+{input.SeismicActionFactor:F3}Ek；来源：{source}",
                confirmed,
                (input.PermanentAction, input.SeismicPermanentFactor),
                (input.LeadingVariableAction, input.SeismicVariableCombinationFactor),
                (input.SeismicAction, input.SeismicActionFactor));
            trace.Add(seismic);
        }

        FoundationLoadCombination? accidental = null;
        if (input.AccidentalAction.HasMeaningfulLoad)
        {
            accidental = Combine(
                LoadCombinationKind.Accidental,
                "偶然设计状况组合",
                $"Sd={input.AccidentalPermanentFactor:F3}Gk+{input.AccidentalVariableCombinationFactor:F3}Q1k+{input.AccidentalActionFactor:F3}Ad；来源：{source}",
                confirmed,
                (input.PermanentAction, input.AccidentalPermanentFactor),
                (input.LeadingVariableAction, input.AccidentalVariableCombinationFactor),
                (input.AccidentalAction, input.AccidentalActionFactor));
            trace.Add(accidental);
        }

        var active = trace.FirstOrDefault(item =>
                         item.Kind == input.ActiveStructuralCombination &&
                         item.Kind is LoadCombinationKind.Basic or
                             LoadCombinationKind.Seismic or
                             LoadCombinationKind.Accidental) ?? basic;
        return new FoundationLoad
        {
            VerticalKn = standard.VerticalKn,
            ShearXKn = standard.ShearXKn,
            ShearYKn = standard.ShearYKn,
            MomentXKnM = standard.MomentXKnM,
            MomentYKnM = standard.MomentYKnM,
            TorsionKnM = standard.TorsionKnM,
            UsesIndividualPileReactions = standard.UsesIndividualPileReactions,
            IndividualPileCompressionKn = standard.IndividualPileCompressionKn,
            IndividualPileUpliftKn = standard.IndividualPileUpliftKn,
            IndividualPileHorizontalKn = standard.IndividualPileHorizontalKn,
            FoundationUnitCount = foundationUnitCount,
            TieBeamsRequired = tieBeamsRequired,
            GoverningCase = standard.GoverningCase,
            BasicCombination = basic,
            QuasiPermanentCombination = quasi,
            SeismicCombination = seismic,
            AccidentalCombination = accidental,
            ActiveStructuralCombination = active,
            CombinationTrace = trace
        };
    }

    public FoundationLoad EnsureTrace(FoundationLoad load)
    {
        if (load.CombinationTrace.Count > 0)
        {
            return load;
        }

        load.CombinationTrace.Add(ToCombination(
            load,
            LoadCombinationKind.Standard,
            load.GoverningCase,
            "来源文件或本机荷载计算给出的标准组合",
            true));
        foreach (var combination in new[]
                 {
                     load.BasicCombination,
                     load.QuasiPermanentCombination,
                     load.SeismicCombination,
                     load.AccidentalCombination
                 }.Where(item => item?.HasMeaningfulLoad == true))
        {
            load.CombinationTrace.Add(combination!);
        }

        load.ActiveStructuralCombination ??= load.BasicCombination;
        return load;
    }

    private static FoundationLoadCombination Combine(
        LoadCombinationKind kind,
        string name,
        string expression,
        bool confirmed,
        params (FoundationLoadCombination Action, double Factor)[] terms)
    {
        var result = new FoundationLoadCombination
        {
            Kind = kind,
            GoverningCase = name,
            Expression = expression,
            SourceDocument = "GB 50068-2018第8.2、8.3节及项目确认系数",
            IsConfirmed = confirmed,
            UsesIndividualPileReactions = terms.Any(item =>
                item.Action.UsesIndividualPileReactions)
        };
        foreach (var (action, factor) in terms)
        {
            result.VerticalKn += factor * action.VerticalKn;
            result.ShearXKn += factor * action.ShearXKn;
            result.ShearYKn += factor * action.ShearYKn;
            result.MomentXKnM += factor * action.MomentXKnM;
            result.MomentYKnM += factor * action.MomentYKnM;
            result.TorsionKnM += factor * action.TorsionKnM;
            result.IndividualPileCompressionKn +=
                factor * action.IndividualPileCompressionKn;
            result.IndividualPileUpliftKn +=
                factor * action.IndividualPileUpliftKn;
            result.IndividualPileHorizontalKn +=
                factor * action.IndividualPileHorizontalKn;
        }

        return result;
    }

    private static FoundationLoadCombination ToCombination(
        FoundationLoad load,
        LoadCombinationKind kind,
        string name,
        string expression,
        bool confirmed) => new()
        {
            Kind = kind,
            VerticalKn = load.VerticalKn,
            ShearXKn = load.ShearXKn,
            ShearYKn = load.ShearYKn,
            MomentXKnM = load.MomentXKnM,
            MomentYKnM = load.MomentYKnM,
            TorsionKnM = load.TorsionKnM,
            UsesIndividualPileReactions = load.UsesIndividualPileReactions,
            IndividualPileCompressionKn = load.IndividualPileCompressionKn,
            IndividualPileUpliftKn = load.IndividualPileUpliftKn,
            IndividualPileHorizontalKn = load.IndividualPileHorizontalKn,
            GoverningCase = name,
            Expression = expression,
            IsConfirmed = confirmed
        };

    private static void ValidateFactors(LoadCombinationDesignInput input)
    {
        var factors = new[]
        {
            input.PermanentFactor,
            input.VariableFactor,
            input.QuasiPermanentVariableFactor,
            input.SeismicPermanentFactor,
            input.SeismicActionFactor,
            input.SeismicVariableCombinationFactor,
            input.AccidentalPermanentFactor,
            input.AccidentalActionFactor,
            input.AccidentalVariableCombinationFactor
        };
        if (factors.Any(value =>
                value < 0 || double.IsNaN(value) || double.IsInfinity(value)))
        {
            throw new InvalidOperationException("荷载组合系数不得为负数、NaN或无穷大。" );
        }
    }
}
