namespace TowerFoundation.Calculation;

public sealed record RectangularStirrupCutLength(
    double BodyPerimeterM,
    double HookBendAllowanceM,
    double HookStraightAllowanceM,
    double TotalCutLengthM,
    string FormulaDescription);

public static class RebarCutLengthCalculator
{
    private const double HookBendAdjustmentDiameterFactor = 1.9;
    private const double MinimumSeismicHookStraightLengthM = 0.075;

    public static RectangularStirrupCutLength CalculateRectangularClosedStirrup(
        double overallLengthM,
        double overallWidthM,
        double concreteCoverMm,
        double stirrupDiameterMm,
        bool useSeismicDetailing = true,
        bool subjectToTorsion = false)
    {
        if (overallLengthM <= 0 ||
            overallWidthM <= 0 ||
            concreteCoverMm < 0 ||
            stirrupDiameterMm <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overallLengthM),
                "矩形箍筋外包尺寸、保护层和钢筋直径必须有效。");
        }

        var bodyLengthM = overallLengthM - 2 * concreteCoverMm / 1000;
        var bodyWidthM = overallWidthM - 2 * concreteCoverMm / 1000;
        if (bodyLengthM <= 0 || bodyWidthM <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(concreteCoverMm),
                "保护层厚度使矩形箍筋外包尺寸不再为正值。");
        }

        var diameterM = stirrupDiameterMm / 1000;
        var bodyPerimeterM = 2 * (bodyLengthM + bodyWidthM);
        var hookBendAllowanceM =
            2 * HookBendAdjustmentDiameterFactor * diameterM;
        var straightLengthPerEndM = useSeismicDetailing
            ? Math.Max(10 * diameterM, MinimumSeismicHookStraightLengthM)
            : subjectToTorsion
                ? 10 * diameterM
                : 5 * diameterM;
        var hookStraightAllowanceM = 2 * straightLengthPerEndM;
        var totalCutLengthM =
            bodyPerimeterM + hookBendAllowanceM + hookStraightAllowanceM;
        var straightFormula = useSeismicDetailing
            ? "max(10d,75mm)"
            : subjectToTorsion
                ? "10d"
                : "5d";
        var detailingDescription = useSeismicDetailing
            ? "抗震构造"
            : subjectToTorsion
                ? "非抗震但构件受扭"
                : "非抗震构造";

        return new RectangularStirrupCutLength(
            bodyPerimeterM,
            hookBendAllowanceM,
            hookStraightAllowanceM,
            totalCutLengthM,
            $"L=2[(b-2c)+(h-2c)]+2×1.9d+2×{straightFormula}=" +
            $"{bodyPerimeterM:F3}+{hookBendAllowanceM:F3}+{hookStraightAllowanceM:F3}=" +
            $"{totalCutLengthM:F3}m；按22G101-3第2-7页非焊接封闭箍135°弯钩及{detailingDescription}平直段计。"
        );
    }

    public static bool ShouldUseSeismicDetailing(int seismicIntensityDegree) =>
        seismicIntensityDegree <= 0 || seismicIntensityDegree >= 6;
}
