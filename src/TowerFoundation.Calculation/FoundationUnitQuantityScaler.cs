using TowerFoundation.Domain;

namespace TowerFoundation.Calculation;

internal static class FoundationUnitQuantityScaler
{
    public static FoundationScheme Apply(FoundationScheme scheme)
    {
        var count = Math.Max(1, scheme.Geometry.FoundationUnitCount);
        if (count <= 1 || scheme.FoundationType == FoundationType.Pile)
        {
            return scheme;
        }

        var unitQuantities = scheme.Quantities;
        scheme.Quantities = new QuantitySummary
        {
            ConcreteM3 = unitQuantities.ConcreteM3 * count,
            ExcavationM3 = unitQuantities.ExcavationM3 * count,
            BackfillM3 = unitQuantities.BackfillM3 * count,
            EstimatedReinforcementKg = unitQuantities.EstimatedReinforcementKg * count
        };
        scheme.ReinforcementDesigns = scheme.ReinforcementDesigns
            .Select(item => new ReinforcementDesignResult
            {
                Component = $"{item.Component}（{count}个基础汇总）",
                Direction = item.Direction,
                BarSpecification = $"每个基础：{item.BarSpecification}",
                RequiredAreaMm2 = item.RequiredAreaMm2,
                ProvidedAreaMm2 = item.ProvidedAreaMm2,
                BarCount = item.BarCount * count,
                BarDiameterMm = item.BarDiameterMm,
                BarSpacingMm = item.BarSpacingMm,
                SingleBarLengthM = item.SingleBarLengthM,
                TotalLengthM = item.TotalLengthM * count,
                UnitWeightKgPerM = item.UnitWeightKgPerM,
                CalculatedWeightKg = item.CalculatedWeightKg * count,
                StirrupBodyPerimeterM = item.StirrupBodyPerimeterM,
                HookBendAllowanceM = item.HookBendAllowanceM,
                HookStraightAllowanceM = item.HookStraightAllowanceM,
                CuttingLengthExplanation = item.CuttingLengthExplanation,
                Status = item.Status,
                RuleReference = item.RuleReference
            })
            .ToList();
        return scheme;
    }
}
