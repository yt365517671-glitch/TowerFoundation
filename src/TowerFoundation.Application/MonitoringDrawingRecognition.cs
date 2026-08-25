using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public static class MonitoringDrawingFieldNames
{
    public const string TitleHeight = MonitoringDrawingFieldKeys.TitleHeight;
    public const string TitleArmLength = MonitoringDrawingFieldKeys.TitleArmLength;
    public const string PoleHeight = MonitoringDrawingFieldKeys.PoleHeight;
    public const string PoleBottomDimension = MonitoringDrawingFieldKeys.PoleBottomDimension;
    public const string PoleTopDimension = MonitoringDrawingFieldKeys.PoleTopDimension;
    public const string PoleWallThickness = MonitoringDrawingFieldKeys.PoleWallThickness;
    public const string ArmMountingHeight = MonitoringDrawingFieldKeys.ArmMountingHeight;
    public const string ArmLength = MonitoringDrawingFieldKeys.ArmLength;
    public const string ArmNearDimension = MonitoringDrawingFieldKeys.ArmNearDimension;
    public const string ArmFarDimension = MonitoringDrawingFieldKeys.ArmFarDimension;
    public const string ArmWallThickness = MonitoringDrawingFieldKeys.ArmWallThickness;
    public const string ArmCount = MonitoringDrawingFieldKeys.ArmCount;
    public const string AttachmentProjectedArea = MonitoringDrawingFieldKeys.AttachmentProjectedArea;
    public const string AttachmentWeight = MonitoringDrawingFieldKeys.AttachmentWeight;
    public const string ArmSegments = MonitoringDrawingFieldKeys.ArmSegments;
}

public sealed class MonitoringDrawingVisionBatchResult
{
    public IReadOnlyList<MonitoringDrawingCandidate> Candidates { get; init; } = [];

    public IReadOnlyList<string> Failures { get; init; } = [];
}

public interface IMonitoringDrawingVisionAiService
{
    Task<MonitoringDrawingVisionBatchResult> AnalyzePdfsAsync(
        IReadOnlyList<string> paths,
        IProgress<AiOperationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        VisionModelSwitchOptions? switchOptions = null);
}

public sealed class MonitoringDrawingApplyResult
{
    public int AppliedFieldCount { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = [];
}

public static class MonitoringDrawingCandidateRules
{
    public static void ValidateAndInitialize(MonitoringDrawingCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        foreach (var field in candidate.Fields)
        {
            field.Confidence = Math.Clamp(field.Confidence, 0, 1);
            field.IsSelected = field.IsHighConfidence;
        }

        CheckRange(candidate, MonitoringDrawingFieldNames.PoleHeight, 2, 30,
            "立杆高度超出2～30m合理范围，不得自动回填。");
        CheckPositive(candidate, MonitoringDrawingFieldNames.PoleBottomDimension);
        CheckPositive(candidate, MonitoringDrawingFieldNames.PoleTopDimension);
        CheckPositive(candidate, MonitoringDrawingFieldNames.PoleWallThickness);
        CheckPositive(candidate, MonitoringDrawingFieldNames.ArmLength);
        CheckPositive(candidate, MonitoringDrawingFieldNames.ArmNearDimension);
        CheckPositive(candidate, MonitoringDrawingFieldNames.ArmFarDimension);
        CheckPositive(candidate, MonitoringDrawingFieldNames.ArmWallThickness);

        var poleBottom = Value(candidate, MonitoringDrawingFieldNames.PoleBottomDimension);
        var poleTop = Value(candidate, MonitoringDrawingFieldNames.PoleTopDimension);
        if (poleBottom.HasValue && poleTop > poleBottom)
        {
            MarkUnsafe(candidate, MonitoringDrawingFieldNames.PoleTopDimension,
                "立杆上端尺寸大于下端尺寸，不得自动回填。");
        }

        var armNear = Value(candidate, MonitoringDrawingFieldNames.ArmNearDimension);
        var armFar = Value(candidate, MonitoringDrawingFieldNames.ArmFarDimension);
        if (armNear.HasValue && armFar > armNear)
        {
            MarkUnsafe(candidate, MonitoringDrawingFieldNames.ArmFarDimension,
                "横杆远端尺寸大于近端尺寸，不得自动回填。");
        }

        CrossCheckTitle(
            candidate,
            MonitoringDrawingFieldNames.TitleHeight,
            MonitoringDrawingFieldNames.PoleHeight,
            "标题H与立杆规格长度不一致");
        CrossCheckTitle(
            candidate,
            MonitoringDrawingFieldNames.TitleArmLength,
            MonitoringDrawingFieldNames.ArmLength,
            "标题L与横杆规格长度不一致");

        var segmentField = candidate.Fields.FirstOrDefault(
            field => field.FieldName == MonitoringDrawingFieldNames.ArmSegments);
        if (candidate.ArmSegments.Count > 0 && segmentField is not null)
        {
            var total = candidate.ArmSegments.Sum(segment => segment.LengthM);
            var armLength = Value(candidate, MonitoringDrawingFieldNames.ArmLength);
            var segmentArmNear = Value(candidate, MonitoringDrawingFieldNames.ArmNearDimension);
            var segmentArmFar = Value(candidate, MonitoringDrawingFieldNames.ArmFarDimension);
            var discontinuous = candidate.ArmSegments
                .Zip(candidate.ArmSegments.Skip(1), (first, second) =>
                    Math.Abs(first.FarDimensionM - second.NearDimensionM) > 0.001)
                .Any(value => value);
            if (candidate.ArmSegments.Any(segment =>
                    segment.LengthM <= 0 ||
                    segment.NearDimensionM <= 0 ||
                    segment.FarDimensionM <= 0 ||
                    segment.WallThicknessM <= 0 ||
                    segment.FarDimensionM > segment.NearDimensionM) ||
                discontinuous ||
                (armLength.HasValue && Math.Abs(total - armLength.Value) > 0.05) ||
                (segmentArmNear.HasValue && Math.Abs(candidate.ArmSegments[0].NearDimensionM - segmentArmNear.Value) > 0.001) ||
                (segmentArmFar.HasValue && Math.Abs(candidate.ArmSegments[^1].FarDimensionM - segmentArmFar.Value) > 0.001))
            {
                MarkUnsafe(candidate, MonitoringDrawingFieldNames.ArmSegments,
                    "横杆分段几何不完整或分段总长与横杆总长不一致。");
            }
        }

        foreach (var warning in candidate.Fields
                     .Where(field => !string.IsNullOrWhiteSpace(field.Warning))
                     .Select(field => $"{field.DisplayName}：{field.Warning}"))
        {
            if (!candidate.Warnings.Contains(warning, StringComparer.Ordinal))
            {
                candidate.Warnings.Add(warning);
            }
        }
    }

    private static void CrossCheckTitle(
        MonitoringDrawingCandidate candidate,
        string titleFieldName,
        string specificationFieldName,
        string warning)
    {
        var title = Value(candidate, titleFieldName);
        var specification = Value(candidate, specificationFieldName);
        if (title.HasValue && specification.HasValue &&
            Math.Abs(title.Value - specification.Value) > 0.20)
        {
            MarkUnsafe(candidate, specificationFieldName, warning + "，不得自动回填。");
        }
    }

    private static void CheckPositive(MonitoringDrawingCandidate candidate, string fieldName)
    {
        if (Value(candidate, fieldName) is <= 0)
        {
            MarkUnsafe(candidate, fieldName, "识别值必须大于0，不得自动回填。");
        }
    }

    private static void CheckRange(
        MonitoringDrawingCandidate candidate,
        string fieldName,
        double minimum,
        double maximum,
        string warning)
    {
        if (Value(candidate, fieldName) is { } value &&
            (value < minimum || value > maximum))
        {
            MarkUnsafe(candidate, fieldName, warning);
        }
    }

    private static double? Value(MonitoringDrawingCandidate candidate, string fieldName) =>
        candidate.Fields.FirstOrDefault(field => field.FieldName == fieldName)?.Value;

    private static void MarkUnsafe(
        MonitoringDrawingCandidate candidate,
        string fieldName,
        string warning)
    {
        var field = candidate.Fields.FirstOrDefault(item => item.FieldName == fieldName);
        if (field is null)
        {
            return;
        }

        field.HasConflict = true;
        field.Warning = string.IsNullOrWhiteSpace(field.Warning)
            ? warning
            : field.Warning + "；" + warning;
        field.IsSelected = false;
    }
}

public static class MonitoringDrawingCandidateApplicator
{
    public static MonitoringDrawingApplyResult Apply(
        ProjectModel project,
        MonitoringDrawingCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(candidate);
        var messages = new List<string>();
        var input = project.MonitoringPole;
        var snapshot = MonitoringPoleSnapshot.Capture(input);
        var appliedFields = new HashSet<string>(StringComparer.Ordinal);
        var appliedArmGeometry = false;
        var appliedSegments = false;

        foreach (var field in candidate.Fields.Where(field => field.IsSelected))
        {
            if (!field.Value.HasValue)
            {
                messages.Add($"{field.DisplayName}：图纸未给，保留原值。");
                continue;
            }
            if (!field.IsHighConfidence && !field.IsManuallyConfirmed)
            {
                messages.Add($"{field.DisplayName}：低置信或存在冲突，未人工确认，未回填。");
                continue;
            }

            if (ApplyField(input, candidate, field))
            {
                appliedFields.Add(field.FieldName);
                appliedArmGeometry |= field.FieldName is
                    MonitoringDrawingFieldNames.ArmLength or
                    MonitoringDrawingFieldNames.ArmNearDimension or
                    MonitoringDrawingFieldNames.ArmFarDimension or
                    MonitoringDrawingFieldNames.ArmWallThickness;
                appliedSegments |= field.FieldName == MonitoringDrawingFieldNames.ArmSegments;
            }
        }

        if (appliedArmGeometry && !appliedSegments && candidate.ArmSegments.Count == 0)
        {
            input.ArmSegments.Clear();
        }

        ReconcileDependentGeometry(input, snapshot, appliedFields, messages);
        input.ExplicitDrawingInputFields ??= [];
        input.ExplicitDrawingInputFields.UnionWith(appliedFields);
        if (appliedFields.Contains(MonitoringDrawingFieldNames.ArmSegments))
        {
            input.ExplicitDrawingInputFields.Add(MonitoringDrawingFieldNames.ArmWallThickness);
        }
        var applied = appliedFields.Count;
        if (applied > 0)
        {
            input.PoleSectionType = TubeSectionType.RegularOctagonDiagonalTube;
            input.ArmSectionType = TubeSectionType.RegularOctagonDiagonalTube;
            candidate.AppliedAt = DateTimeOffset.Now;
            project.ModifiedAt = DateTimeOffset.Now;
            project.AuditTrail.Add(new AuditRecord
            {
                Action = "采用监控杆施工图视觉候选",
                Details = $"{candidate.DisplayName}；采用{applied}个字段；来源页{candidate.PageNumber}；视觉模型{candidate.VisionModel}。"
            });
        }

        foreach (var missing in candidate.Fields.Where(field => field.IsMissing))
        {
            messages.Add($"{missing.DisplayName}：图纸未给，已列入第二次人工补录，不采用默认值。");
        }

        return new MonitoringDrawingApplyResult
        {
            AppliedFieldCount = applied,
            Messages = messages.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static void ReconcileDependentGeometry(
        MonitoringPoleInput input,
        MonitoringPoleSnapshot snapshot,
        ISet<string> appliedFields,
        ICollection<string> messages)
    {
        var hasPoleHeight = IsAvailable(input, appliedFields, MonitoringDrawingFieldNames.PoleHeight);
        var hasMountingHeight = IsAvailable(input, appliedFields, MonitoringDrawingFieldNames.ArmMountingHeight);
        if (hasPoleHeight && hasMountingHeight &&
            (input.ArmMountingHeightM <= 0 || input.ArmMountingHeightM > input.PoleHeightM))
        {
            var changedHeight = appliedFields.Contains(MonitoringDrawingFieldNames.PoleHeight);
            var changedMountingHeight = appliedFields.Contains(MonitoringDrawingFieldNames.ArmMountingHeight);
            var rejectedHeight = input.PoleHeightM;
            var retainedMountingHeight = input.ArmMountingHeightM;

            if (changedHeight)
            {
                RollBack(input, snapshot, appliedFields, MonitoringDrawingFieldNames.PoleHeight);
            }
            if (changedMountingHeight)
            {
                RollBack(input, snapshot, appliedFields, MonitoringDrawingFieldNames.ArmMountingHeight);
            }

            if (changedHeight && !changedMountingHeight)
            {
                messages.Add(
                    $"立杆高度未采用：候选值{rejectedHeight:G6}m小于保留的横杆安装高度{retainedMountingHeight:G6}m；请先人工确认横杆安装高度。");
            }
            else if (changedMountingHeight && !changedHeight)
            {
                messages.Add("横杆安装高度未采用：与当前立杆高度不一致，请人工确认后再填入。");
            }
            else if (changedHeight || changedMountingHeight)
            {
                messages.Add("立杆高度和横杆安装高度均未采用：两者组合不满足安装高度应大于0且不超过立杆高度，请人工确认。");
            }
        }

        ReconcileOrderedDimensions(
            input,
            snapshot,
            appliedFields,
            messages,
            MonitoringDrawingFieldNames.PoleTopDimension,
            MonitoringDrawingFieldNames.PoleBottomDimension,
            input.PoleTopDiameterM,
            input.PoleBottomDiameterM,
            "立杆上端尺寸不得大于下端尺寸");
        ReconcileOrderedDimensions(
            input,
            snapshot,
            appliedFields,
            messages,
            MonitoringDrawingFieldNames.ArmFarDimension,
            MonitoringDrawingFieldNames.ArmNearDimension,
            input.ArmFarDiameterM,
            input.ArmNearDiameterM,
            "横杆远端尺寸不得大于近端尺寸");

        if (IsAvailable(input, appliedFields, MonitoringDrawingFieldNames.ArmSegments) &&
            IsAvailable(input, appliedFields, MonitoringDrawingFieldNames.ArmLength) &&
            input.ArmSegments.Count > 0 &&
            Math.Abs(input.ArmSegments.Sum(segment => segment.LengthM) - input.ArmLengthM) > 0.02)
        {
            var changedSegments = appliedFields.Contains(MonitoringDrawingFieldNames.ArmSegments);
            var changedLength = appliedFields.Contains(MonitoringDrawingFieldNames.ArmLength);
            if (changedSegments)
            {
                RollBack(input, snapshot, appliedFields, MonitoringDrawingFieldNames.ArmSegments);
            }
            if (changedLength)
            {
                RollBack(input, snapshot, appliedFields, MonitoringDrawingFieldNames.ArmLength);
            }
            if (changedSegments || changedLength)
            {
                messages.Add("横杆长度或分段明细未采用：分段长度合计与横杆总长度不一致，请人工确认。");
            }
        }
    }

    private static void ReconcileOrderedDimensions(
        MonitoringPoleInput input,
        MonitoringPoleSnapshot snapshot,
        ISet<string> appliedFields,
        ICollection<string> messages,
        string smallerField,
        string largerField,
        double smallerValue,
        double largerValue,
        string rule)
    {
        if (!IsAvailable(input, appliedFields, smallerField) ||
            !IsAvailable(input, appliedFields, largerField) ||
            smallerValue <= largerValue)
        {
            return;
        }

        var changedSmaller = appliedFields.Contains(smallerField);
        var changedLarger = appliedFields.Contains(largerField);
        if (changedSmaller)
        {
            RollBack(input, snapshot, appliedFields, smallerField);
        }
        if (changedLarger)
        {
            RollBack(input, snapshot, appliedFields, largerField);
        }
        if (changedSmaller || changedLarger)
        {
            messages.Add($"相关尺寸未采用：{rule}，请人工确认。");
        }
    }

    private static bool IsAvailable(
        MonitoringPoleInput input,
        ISet<string> appliedFields,
        string fieldName)
    {
        input.ExplicitDrawingInputFields ??= [];
        return !input.RequireExplicitDrawingInputs ||
               appliedFields.Contains(fieldName) ||
               input.ExplicitDrawingInputFields.Contains(fieldName);
    }

    private static void RollBack(
        MonitoringPoleInput input,
        MonitoringPoleSnapshot snapshot,
        ISet<string> appliedFields,
        string fieldName)
    {
        switch (fieldName)
        {
            case MonitoringDrawingFieldNames.PoleHeight:
                input.PoleHeightM = snapshot.PoleHeightM;
                break;
            case MonitoringDrawingFieldNames.PoleBottomDimension:
                input.PoleBottomDiameterM = snapshot.PoleBottomDiameterM;
                break;
            case MonitoringDrawingFieldNames.PoleTopDimension:
                input.PoleTopDiameterM = snapshot.PoleTopDiameterM;
                break;
            case MonitoringDrawingFieldNames.ArmMountingHeight:
                input.ArmMountingHeightM = snapshot.ArmMountingHeightM;
                break;
            case MonitoringDrawingFieldNames.ArmLength:
                input.ArmLengthM = snapshot.ArmLengthM;
                break;
            case MonitoringDrawingFieldNames.ArmNearDimension:
                input.ArmNearDiameterM = snapshot.ArmNearDiameterM;
                break;
            case MonitoringDrawingFieldNames.ArmFarDimension:
                input.ArmFarDiameterM = snapshot.ArmFarDiameterM;
                break;
            case MonitoringDrawingFieldNames.ArmSegments:
                input.ArmSegments = snapshot.ArmSegments.Select(segment => new MonitoringPoleArmSegment
                {
                    LengthM = segment.LengthM,
                    NearDimensionM = segment.NearDimensionM,
                    FarDimensionM = segment.FarDimensionM,
                    WallThicknessM = segment.WallThicknessM
                }).ToList();
                input.ArmWallThicknessM = snapshot.ArmWallThicknessM;
                break;
        }
        appliedFields.Remove(fieldName);
    }

    private sealed record MonitoringPoleSnapshot(
        double PoleHeightM,
        double PoleBottomDiameterM,
        double PoleTopDiameterM,
        double ArmMountingHeightM,
        double ArmLengthM,
        double ArmNearDiameterM,
        double ArmFarDiameterM,
        double ArmWallThicknessM,
        IReadOnlyList<MonitoringPoleArmSegment> ArmSegments)
    {
        public static MonitoringPoleSnapshot Capture(MonitoringPoleInput input) => new(
            input.PoleHeightM,
            input.PoleBottomDiameterM,
            input.PoleTopDiameterM,
            input.ArmMountingHeightM,
            input.ArmLengthM,
            input.ArmNearDiameterM,
            input.ArmFarDiameterM,
            input.ArmWallThicknessM,
            input.ArmSegments.Select(segment => new MonitoringPoleArmSegment
            {
                LengthM = segment.LengthM,
                NearDimensionM = segment.NearDimensionM,
                FarDimensionM = segment.FarDimensionM,
                WallThicknessM = segment.WallThicknessM
            }).ToArray());
    }

    private static bool ApplyField(
        MonitoringPoleInput input,
        MonitoringDrawingCandidate candidate,
        MonitoringDrawingFieldCandidate field)
    {
        var value = field.Value!.Value;
        switch (field.FieldName)
        {
            case MonitoringDrawingFieldNames.PoleHeight:
                input.PoleHeightM = value;
                break;
            case MonitoringDrawingFieldNames.PoleBottomDimension:
                input.PoleBottomDiameterM = value;
                break;
            case MonitoringDrawingFieldNames.PoleTopDimension:
                input.PoleTopDiameterM = value;
                break;
            case MonitoringDrawingFieldNames.PoleWallThickness:
                input.PoleWallThicknessM = value;
                break;
            case MonitoringDrawingFieldNames.ArmMountingHeight:
                input.ArmMountingHeightM = value;
                break;
            case MonitoringDrawingFieldNames.ArmLength:
                input.ArmLengthM = value;
                break;
            case MonitoringDrawingFieldNames.ArmNearDimension:
                input.ArmNearDiameterM = value;
                break;
            case MonitoringDrawingFieldNames.ArmFarDimension:
                input.ArmFarDiameterM = value;
                break;
            case MonitoringDrawingFieldNames.ArmWallThickness:
                input.ArmWallThicknessM = value;
                break;
            case MonitoringDrawingFieldNames.ArmCount:
                input.ArmCount = Math.Max(1, (int)Math.Round(value));
                break;
            case MonitoringDrawingFieldNames.AttachmentProjectedArea:
                input.AttachmentProjectedAreaM2 = value;
                break;
            case MonitoringDrawingFieldNames.AttachmentWeight:
                input.AttachmentWeightKn = value;
                break;
            case MonitoringDrawingFieldNames.ArmSegments:
                input.ArmSegments = candidate.ArmSegments.Select(segment => new MonitoringPoleArmSegment
                {
                    LengthM = segment.LengthM,
                    NearDimensionM = segment.NearDimensionM,
                    FarDimensionM = segment.FarDimensionM,
                    WallThicknessM = segment.WallThicknessM
                }).ToList();
                if (input.ArmSegments.Count > 0)
                {
                    input.ArmWallThicknessM = input.ArmSegments[0].WallThicknessM;
                }
                break;
            default:
                return false;
        }

        return true;
    }
}
