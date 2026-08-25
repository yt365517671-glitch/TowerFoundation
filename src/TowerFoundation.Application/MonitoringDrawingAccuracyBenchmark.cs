using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public sealed class MonitoringDrawingAccuracyReport
{
    public string Status { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset CompletedAt { get; init; }

    public IReadOnlyList<string> ApiFailures { get; init; } = [];

    public IReadOnlyList<MonitoringDrawingAccuracyDrawing> Drawings { get; init; } = [];

    public int TotalComparedFields { get; init; }

    public int CorrectFields { get; init; }

    public double? Accuracy { get; init; }

    public int CorrectMissingDeviceFieldCount { get; init; }

    public int ExpectedMissingDeviceFieldCount { get; init; }
}

public sealed class MonitoringDrawingAccuracyDrawing
{
    public string SourceFileName { get; init; } = string.Empty;

    public string ExpectedModel { get; init; } = string.Empty;

    public string RecognizedModel { get; init; } = string.Empty;

    public bool HasModelResult { get; init; }

    public IReadOnlyList<MonitoringDrawingAccuracyComparison> Comparisons { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class MonitoringDrawingAccuracyComparison
{
    public string Field { get; init; } = string.Empty;

    public double? Expected { get; init; }

    public double? Actual { get; init; }

    public string Unit { get; init; } = string.Empty;

    public bool Correct { get; init; }

    public double Confidence { get; init; }

    public bool Conflict { get; init; }

    public string Evidence { get; init; } = string.Empty;
}

public static class MonitoringDrawingAccuracyBenchmark
{
    public static MonitoringDrawingAccuracyReport Evaluate(
        IReadOnlyList<string> sourcePaths,
        MonitoringDrawingVisionBatchResult batch,
        string model,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        var expected = ExpectedCases;
        var drawings = new List<MonitoringDrawingAccuracyDrawing>();
        for (var index = 0; index < expected.Length; index++)
        {
            var sourceName = index < sourcePaths.Count
                ? Path.GetFileName(sourcePaths[index])
                : $"drawing-{index + 1}.pdf";
            var candidate = batch.Candidates.FirstOrDefault(item =>
                item.SourceFileName.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
            drawings.Add(EvaluateDrawing(sourceName, expected[index], candidate));
        }

        var comparisons = drawings.SelectMany(drawing => drawing.Comparisons).ToArray();
        var missingDeviceComparisons = comparisons.Where(item =>
            item.Field is MonitoringDrawingFieldNames.AttachmentProjectedArea or
                MonitoringDrawingFieldNames.AttachmentWeight).ToArray();
        var correct = comparisons.Count(item => item.Correct);
        return new MonitoringDrawingAccuracyReport
        {
            Status = batch.Candidates.Count == 0
                ? "failed"
                : batch.Failures.Count == 0 && batch.Candidates.Count >= expected.Length
                    ? "completed"
                    : "partial",
            Model = model,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ApiFailures = batch.Failures,
            Drawings = drawings,
            TotalComparedFields = comparisons.Length,
            CorrectFields = correct,
            Accuracy = batch.Candidates.Count == 0 || comparisons.Length == 0
                ? null
                : (double)correct / comparisons.Length,
            CorrectMissingDeviceFieldCount = missingDeviceComparisons.Count(item => item.Correct),
            ExpectedMissingDeviceFieldCount = missingDeviceComparisons.Length
        };
    }

    private static MonitoringDrawingAccuracyDrawing EvaluateDrawing(
        string sourceName,
        ExpectedDrawing expected,
        MonitoringDrawingCandidate? candidate)
    {
        var comparisons = new List<MonitoringDrawingAccuracyComparison>();
        Add(comparisons, candidate, MonitoringDrawingFieldNames.TitleHeight, expected.HeightM, "m", 0.02);
        Add(comparisons, candidate, MonitoringDrawingFieldNames.TitleArmLength, expected.ArmLengthM, "m", 0.02);
        Add(comparisons, candidate, MonitoringDrawingFieldNames.PoleHeight, expected.HeightM, "m", 0.02);
        Add(comparisons, candidate, MonitoringDrawingFieldNames.PoleTopDimension, expected.PoleTopM, "m", 0.001);
        Add(comparisons, candidate, MonitoringDrawingFieldNames.PoleBottomDimension, expected.PoleBottomM, "m", 0.001);
        Add(comparisons, candidate, MonitoringDrawingFieldNames.PoleWallThickness, expected.PoleThicknessM, "m", 0.0002);
        Add(comparisons, candidate, MonitoringDrawingFieldNames.ArmLength, expected.ArmLengthM, "m", 0.02);
        Add(comparisons, candidate, MonitoringDrawingFieldNames.ArmFarDimension, expected.ArmFarM, "m", 0.001);
        Add(comparisons, candidate, MonitoringDrawingFieldNames.ArmNearDimension, expected.ArmNearM, "m", 0.001);
        if (expected.Segments.Count == 0)
        {
            Add(comparisons, candidate, MonitoringDrawingFieldNames.ArmWallThickness,
                expected.ArmThicknessM, "m", 0.0002);
        }
        Add(comparisons, candidate, MonitoringDrawingFieldNames.AttachmentProjectedArea, null, "m²", 0);
        Add(comparisons, candidate, MonitoringDrawingFieldNames.AttachmentWeight, null, "kN", 0);

        for (var index = 0; index < expected.Segments.Count; index++)
        {
            var expectedSegment = expected.Segments[index];
            var actual = candidate?.ArmSegments.ElementAtOrDefault(index);
            AddSegment(comparisons, candidate, actual, index + 1, "length",
                expectedSegment.LengthM, actual?.LengthM, "m", 0.02);
            AddSegment(comparisons, candidate, actual, index + 1, "near_dimension",
                expectedSegment.NearM, actual?.NearDimensionM, "m", 0.001);
            AddSegment(comparisons, candidate, actual, index + 1, "far_dimension",
                expectedSegment.FarM, actual?.FarDimensionM, "m", 0.001);
            AddSegment(comparisons, candidate, actual, index + 1, "wall_thickness",
                expectedSegment.ThicknessM, actual?.WallThicknessM, "m", 0.0002);
        }

        return new MonitoringDrawingAccuracyDrawing
        {
            SourceFileName = sourceName,
            ExpectedModel = $"H{expected.HeightM:G}-L{expected.ArmLengthM:G}",
            RecognizedModel = candidate?.DrawingModel ?? string.Empty,
            HasModelResult = candidate is not null,
            Comparisons = comparisons,
            Warnings = candidate?.Warnings ?? []
        };
    }

    private static void Add(
        ICollection<MonitoringDrawingAccuracyComparison> comparisons,
        MonitoringDrawingCandidate? candidate,
        string fieldName,
        double? expected,
        string unit,
        double tolerance)
    {
        var field = candidate?.Fields.FirstOrDefault(item => item.FieldName == fieldName);
        var actual = field?.Value;
        comparisons.Add(new MonitoringDrawingAccuracyComparison
        {
            Field = fieldName,
            Expected = expected,
            Actual = actual,
            Unit = unit,
            Correct = candidate is not null && (expected.HasValue
                ? actual.HasValue && Math.Abs(actual.Value - expected.Value) <= tolerance
                : !actual.HasValue),
            Confidence = field?.Confidence ?? 0,
            Conflict = field?.HasConflict ?? false,
            Evidence = field?.RawAnnotation ?? string.Empty
        });
    }

    private static void AddSegment(
        ICollection<MonitoringDrawingAccuracyComparison> comparisons,
        MonitoringDrawingCandidate? candidate,
        MonitoringPoleArmSegment? actualSegment,
        int segmentNumber,
        string name,
        double expected,
        double? actual,
        string unit,
        double tolerance)
    {
        var field = candidate?.Fields.FirstOrDefault(item =>
            item.FieldName == MonitoringDrawingFieldNames.ArmSegments);
        comparisons.Add(new MonitoringDrawingAccuracyComparison
        {
            Field = $"arm_segment_{segmentNumber}_{name}",
            Expected = expected,
            Actual = actual,
            Unit = unit,
            Correct = actualSegment is not null && actual.HasValue &&
                      Math.Abs(actual.Value - expected) <= tolerance,
            Confidence = field?.Confidence ?? 0,
            Conflict = field?.HasConflict ?? false,
            Evidence = field?.RawAnnotation ?? string.Empty
        });
    }

    private static readonly ExpectedDrawing[] ExpectedCases =
    [
        new(6.5, 0.18, 0.24, 0.005, 3, 0.09, 0.16, 0.004, []),
        new(6.5, 0.18, 0.24, 0.005, 5, 0.09, 0.18, 0.004, []),
        new(6.5, 0.18, 0.24, 0.006, 7, 0.09, 0.18, 0.004, []),
        new(6.5, 0.20, 0.26, 0.006, 8, 0.09, 0.20, 0.004, []),
        new(6.5, 0.27, 0.33, 0.008, 10, 0.10, 0.24, 0.005, []),
        new(6.5, 0.28, 0.34, 0.008, 12, 0.11, 0.26, 0.006, []),
        new(6.5, 0.28, 0.34, 0.010, 14, 0.11, 0.28, null,
        [
            new ExpectedSegment(7, 0.28, 0.195, 0.006),
            new ExpectedSegment(7, 0.195, 0.11, 0.004)
        ])
    ];

    private sealed record ExpectedDrawing(
        double HeightM,
        double PoleTopM,
        double PoleBottomM,
        double PoleThicknessM,
        double ArmLengthM,
        double ArmFarM,
        double ArmNearM,
        double? ArmThicknessM,
        IReadOnlyList<ExpectedSegment> Segments);

    private sealed record ExpectedSegment(
        double LengthM,
        double NearM,
        double FarM,
        double ThicknessM);
}
