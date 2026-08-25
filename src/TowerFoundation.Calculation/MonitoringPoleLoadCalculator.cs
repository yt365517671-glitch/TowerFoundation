using TowerFoundation.Domain;

namespace TowerFoundation.Calculation;

public sealed class MonitoringPoleLoadCalculator
{
    public IReadOnlyList<ValidationIssue> Validate(MonitoringPoleInput input)
    {
        var issues = new List<ValidationIssue>();

        ValidateExplicitDrawingInputs(input, issues);

        RequirePositive(input.BasicWindPressureKpa, nameof(input.BasicWindPressureKpa), "基本风压必须大于0。", issues);
        RequirePositive(input.WindVibrationFactor, nameof(input.WindVibrationFactor), "风振系数必须大于0。", issues);
        RequirePositive(input.ShapeCoefficient, nameof(input.ShapeCoefficient), "体型系数必须大于0。", issues);
        RequirePositive(input.TerrainHeightFactor, nameof(input.TerrainHeightFactor), "风压高度变化系数必须大于0。", issues);
        RequirePositive(input.PoleHeightM, nameof(input.PoleHeightM), "立杆高度必须大于0。", issues);
        RequirePositive(input.PoleBottomDiameterM, nameof(input.PoleBottomDiameterM), "立杆下端直径必须大于0。", issues);
        RequirePositive(input.PoleTopDiameterM, nameof(input.PoleTopDiameterM), "立杆上端直径必须大于0。", issues);
        RequirePositive(input.PoleWallThicknessM, nameof(input.PoleWallThicknessM), "立杆壁厚必须大于0。", issues);
        RequirePositive(input.ArmLengthM, nameof(input.ArmLengthM), "横杆长度必须大于0。", issues);
        RequirePositive(input.ArmNearDiameterM, nameof(input.ArmNearDiameterM), "横杆近端直径必须大于0。", issues);
        RequirePositive(input.ArmFarDiameterM, nameof(input.ArmFarDiameterM), "横杆远端直径必须大于0。", issues);
        RequirePositive(input.ArmWallThicknessM, nameof(input.ArmWallThicknessM), "横杆壁厚必须大于0。", issues);

        if (input.PoleHeightM is < 2 or > 30)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.PoleHeightM),
                Message = "立杆高度应在2～30m范围内。"
            });
        }

        if (input.PoleTopDiameterM > input.PoleBottomDiameterM)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.PoleTopDiameterM),
                Message = "立杆上端尺寸不得大于下端尺寸。"
            });
        }

        if (input.ArmFarDiameterM > input.ArmNearDiameterM)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.ArmFarDiameterM),
                Message = "横杆远端尺寸不得大于近端尺寸。"
            });
        }

        if (input.ArmMountingHeightM <= 0 || input.ArmMountingHeightM > input.PoleHeightM)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.ArmMountingHeightM),
                Message = "横杆安装高度必须大于0且不超过立杆高度。"
            });
        }

        if (!HasPositiveInnerSection(
                input.PoleTopDiameterM,
                input.PoleWallThicknessM,
                input.PoleSectionType))
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.PoleWallThicknessM),
                Message = "立杆壁厚过大，内截面必须保持为正。"
            });
        }

        if (!HasPositiveInnerSection(
                input.ArmFarDiameterM,
                input.ArmWallThicknessM,
                input.ArmSectionType))
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.ArmWallThicknessM),
                Message = "横杆壁厚过大，内截面必须保持为正。"
            });
        }

        if (input.AttachmentProjectedAreaM2 < 0)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.AttachmentProjectedAreaM2),
                Message = "设备迎风面积不得小于0。"
            });
        }

        if (input.AttachmentWeightKn < 0)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.AttachmentWeightKn),
                Message = "设备重量不得小于0。"
            });
        }

        if (input.ArmSegments.Count > 0)
        {
            ValidateArmSegments(input, issues);
        }

        if (input.ArmCount < 1)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.ArmCount),
                Message = "横杆数量至少为1。"
            });
        }

        return issues;
    }

    private static void ValidateExplicitDrawingInputs(
        MonitoringPoleInput input,
        ICollection<ValidationIssue> issues)
    {
        if (!input.RequireExplicitDrawingInputs)
        {
            return;
        }

        input.ExplicitDrawingInputFields ??= [];
        var required = new (string FieldName, string PropertyName, string DisplayName)[]
        {
            (MonitoringDrawingFieldKeys.PoleHeight, nameof(input.PoleHeightM), "立杆高度"),
            (MonitoringDrawingFieldKeys.PoleBottomDimension, nameof(input.PoleBottomDiameterM), "立杆下端尺寸"),
            (MonitoringDrawingFieldKeys.PoleTopDimension, nameof(input.PoleTopDiameterM), "立杆上端尺寸"),
            (MonitoringDrawingFieldKeys.PoleWallThickness, nameof(input.PoleWallThicknessM), "立杆壁厚"),
            (MonitoringDrawingFieldKeys.ArmMountingHeight, nameof(input.ArmMountingHeightM), "横杆安装高度"),
            (MonitoringDrawingFieldKeys.ArmLength, nameof(input.ArmLengthM), "横杆长度"),
            (MonitoringDrawingFieldKeys.ArmNearDimension, nameof(input.ArmNearDiameterM), "横杆近端尺寸"),
            (MonitoringDrawingFieldKeys.ArmFarDimension, nameof(input.ArmFarDiameterM), "横杆远端尺寸"),
            (MonitoringDrawingFieldKeys.ArmWallThickness, nameof(input.ArmWallThicknessM), "横杆壁厚或分段壁厚"),
            (MonitoringDrawingFieldKeys.ArmCount, nameof(input.ArmCount), "横杆数量"),
            (MonitoringDrawingFieldKeys.AttachmentProjectedArea, nameof(input.AttachmentProjectedAreaM2), "设备迎风面积"),
            (MonitoringDrawingFieldKeys.AttachmentWeight, nameof(input.AttachmentWeightKn), "设备重量")
        };

        foreach (var item in required.Where(item =>
                     !input.ExplicitDrawingInputFields.Contains(item.FieldName)))
        {
            issues.Add(new ValidationIssue
            {
                Field = item.PropertyName,
                Message = $"{item.DisplayName}尚未由AI识别或人工填写，不得采用默认值继续计算。"
            });
        }
    }

    public MonitoringPoleLoadResult Calculate(
        MonitoringPoleInput input,
        FoundationDesignSettings? settings = null)
    {
        var issues = Validate(input);
        if (issues.Any(issue => issue.IsBlocking))
        {
            throw new ArgumentException(string.Join(" ", issues.Select(issue => issue.Message)));
        }

        // GB 50009-2012 formula 8.1.1-1: wk = beta_z * mu_s * mu_z * w0.
        // This module forms standard wind effects for foundation bearing checks;
        // load partial factors must not be folded into wk here.
        // GB 50135-2019 4.2.1 requires w0 >= 0.35 kN/m2 for tall structures.
        // Keep the guard in the kernel so imported/legacy files cannot bypass it.
        var adoptedBasicWindPressure = Math.Max(
            MonitoringPoleInput.MinimumBasicWindPressureKpa,
            input.BasicWindPressureKpa);
        var designWindPressure = adoptedBasicWindPressure *
                                 input.WindVibrationFactor *
                                 input.ShapeCoefficient *
                                 input.TerrainHeightFactor;

        var poleArea = CalculateProjectedArea(
            input.PoleHeightM,
            input.PoleBottomDiameterM,
            input.PoleTopDiameterM,
            input.PoleSectionType);
        var poleWindForce = designWindPressure * poleArea;

        // Exact first moment for a linearly tapered projected width.
        var poleWindMoment = designWindPressure *
                             Math.Pow(input.PoleHeightM, 2) *
                             (input.PoleBottomDiameterM / 2 +
                              (input.PoleTopDiameterM - input.PoleBottomDiameterM) / 3);

        var armSegments = GetEffectiveArmSegments(input);
        var singleArmArea = armSegments.Sum(segment => CalculateProjectedArea(
            segment.LengthM,
            segment.NearDimensionM,
            segment.FarDimensionM,
            input.ArmSectionType));
        var armWindForce = designWindPressure * singleArmArea * input.ArmCount;
        var armWindMoment = armWindForce * input.ArmMountingHeightM;

        var attachmentWindForce = designWindPressure * input.AttachmentProjectedAreaM2;
        var attachmentWindMoment = attachmentWindForce * input.ArmMountingHeightM;

        var offset = 0d;
        var armWindTorsion = 0d;
        var singleArmGravityMoment = 0d;
        var singleArmVolume = 0d;
        foreach (var segment in armSegments)
        {
            var projectedArea = CalculateProjectedArea(
                segment.LengthM,
                segment.NearDimensionM,
                segment.FarDimensionM,
                input.ArmSectionType);
            var windCentroid = CalculateLinearCentroid(
                segment.LengthM,
                GetProjectedWidth(segment.NearDimensionM, input.ArmSectionType),
                GetProjectedWidth(segment.FarDimensionM, input.ArmSectionType));
            armWindTorsion += designWindPressure * projectedArea *
                              (offset + windCentroid) * input.ArmCount;

            var segmentVolume = CalculateTaperedTubeVolume(
                segment.LengthM,
                segment.NearDimensionM,
                segment.FarDimensionM,
                segment.WallThicknessM,
                input.ArmSectionType);
            var nearSteelArea = CalculateHollowSectionArea(
                segment.NearDimensionM,
                segment.WallThicknessM,
                input.ArmSectionType);
            var farSteelArea = CalculateHollowSectionArea(
                segment.FarDimensionM,
                segment.WallThicknessM,
                input.ArmSectionType);
            var gravityCentroid = CalculateLinearCentroid(
                segment.LengthM,
                nearSteelArea,
                farSteelArea);
            singleArmVolume += segmentVolume;
            singleArmGravityMoment += segmentVolume * input.SteelUnitWeightKnPerM3 *
                                      (offset + gravityCentroid);
            offset += segment.LengthM;
        }
        var torsion = armWindTorsion + attachmentWindForce * input.ArmLengthM;

        var poleVolume = CalculateTaperedTubeVolume(
            input.PoleHeightM,
            input.PoleBottomDiameterM,
            input.PoleTopDiameterM,
            input.PoleWallThicknessM,
            input.PoleSectionType);
        var armVolume = singleArmVolume * input.ArmCount;

        var poleWeight = poleVolume * input.SteelUnitWeightKnPerM3;
        var armWeight = armVolume * input.SteelUnitWeightKnPerM3;
        var armGravityMoment = singleArmGravityMoment * input.ArmCount +
                               input.AttachmentWeightKn * input.ArmLengthM;

        var permanentLoadFactor =
            settings?.FoundationPermanentLoadFactor ?? 1.30;
        var windLoadFactor = settings?.StructuralDesignLoadFactor ?? 1.50;
        var standardVertical = poleWeight + armWeight + input.AttachmentWeightKn;
        var standardHorizontal =
            poleWindForce + armWindForce + attachmentWindForce;
        var standardMoment =
            poleWindMoment + armWindMoment + attachmentWindMoment;

        return new MonitoringPoleLoadResult
        {
            DesignWindPressureKpa = designWindPressure,
            PoleWindForceKn = poleWindForce,
            ArmWindForceKn = armWindForce,
            AttachmentWindForceKn = attachmentWindForce,
            PoleSelfWeightKn = poleWeight,
            ArmSelfWeightKn = armWeight,
            PoleSteelVolumeM3 = poleVolume,
            ArmSteelVolumeM3 = armVolume,
            ArmProjectedAreaM2 = singleArmArea * input.ArmCount,
            ArmGravityMomentKnM = armGravityMoment,
            ArmWindTorsionKnM = armWindTorsion,
            FoundationLoad = new FoundationLoad
            {
                VerticalKn = standardVertical,
                ShearXKn = standardHorizontal,
                ShearYKn = 0,
                MomentXKnM = armGravityMoment,
                MomentYKnM = standardMoment,
                TorsionKnM = torsion,
                GoverningCase = "监控杆基础端荷载标准组合",
                BasicCombination = new FoundationLoadCombination
                {
                    Kind = LoadCombinationKind.Basic,
                    VerticalKn = standardVertical * permanentLoadFactor,
                    ShearXKn = standardHorizontal * windLoadFactor,
                    ShearYKn = 0,
                    MomentXKnM = armGravityMoment * permanentLoadFactor,
                    MomentYKnM = standardMoment * windLoadFactor,
                    TorsionKnM = torsion * windLoadFactor,
                    GoverningCase =
                        $"监控杆承载能力极限状态基本组合（永久作用{permanentLoadFactor:F2}、风作用{windLoadFactor:F2}）",
                    Expression =
                        $"Sd={permanentLoadFactor:F2}Gk+{windLoadFactor:F2}Wk",
                    SourceDocument = "GB 50068-2018第8.2节；项目基础设计组合系数",
                    IsConfirmed = true
                },
                QuasiPermanentCombination = new FoundationLoadCombination
                {
                    Kind = LoadCombinationKind.QuasiPermanent,
                    VerticalKn = standardVertical,
                    MomentXKnM = armGravityMoment,
                    GoverningCase = "监控杆正常使用极限状态准永久组合",
                    Expression = "Sd=Gk+0.00Wk",
                    SourceDocument = "GB 50068-2018第8.3节；风作用准永久值系数按项目设置复核",
                    IsConfirmed = true
                }
            }
        };
    }

    public static double CalculateTaperedTubeVolume(
        double length,
        double outsideDimensionStart,
        double outsideDimensionEnd,
        double wallThickness,
        TubeSectionType sectionType)
    {
        if (length <= 0 || wallThickness <= 0)
        {
            return 0;
        }

        var startArea = CalculateHollowSectionArea(
            outsideDimensionStart,
            wallThickness,
            sectionType);
        var endArea = CalculateHollowSectionArea(
            outsideDimensionEnd,
            wallThickness,
            sectionType);
        return length * (startArea + endArea) / 2;
    }

    public static double CalculateProjectedArea(
        double length,
        double outsideDimensionStart,
        double outsideDimensionEnd,
        TubeSectionType sectionType) =>
        length * (GetProjectedWidth(outsideDimensionStart, sectionType) +
                  GetProjectedWidth(outsideDimensionEnd, sectionType)) / 2;

    private static double CalculateHollowSectionArea(
        double outsideDimension,
        double wallThickness,
        TubeSectionType sectionType)
    {
        var insideDimension = sectionType switch
        {
            TubeSectionType.CircularTube => outsideDimension - 2 * wallThickness,
            TubeSectionType.RegularOctagonDiagonalTube =>
                outsideDimension - 2 * wallThickness / Math.Cos(Math.PI / 8),
            _ => throw new ArgumentOutOfRangeException(nameof(sectionType))
        };
        if (insideDimension <= 0)
        {
            return 0;
        }

        return SolidSectionArea(outsideDimension, sectionType) -
               SolidSectionArea(insideDimension, sectionType);
    }

    private static double SolidSectionArea(
        double outsideDimension,
        TubeSectionType sectionType) => sectionType switch
        {
            TubeSectionType.CircularTube => Math.PI * outsideDimension * outsideDimension / 4,
            TubeSectionType.RegularOctagonDiagonalTube =>
                outsideDimension * outsideDimension / Math.Sqrt(2),
            _ => throw new ArgumentOutOfRangeException(nameof(sectionType))
        };

    private static double GetProjectedWidth(
        double outsideDimension,
        TubeSectionType sectionType) => sectionType switch
        {
            TubeSectionType.CircularTube => outsideDimension,
            // 对角尺寸的正八边形随风向的最大几何投影宽度等于对角尺寸。
            TubeSectionType.RegularOctagonDiagonalTube => outsideDimension,
            _ => throw new ArgumentOutOfRangeException(nameof(sectionType))
        };

    private static double CalculateLinearCentroid(
        double length,
        double valueAtStart,
        double valueAtEnd)
    {
        var denominator = 3 * (valueAtStart + valueAtEnd);
        return denominator <= 0
            ? length / 2
            : length * (valueAtStart + 2 * valueAtEnd) / denominator;
    }

    private static IReadOnlyList<MonitoringPoleArmSegment> GetEffectiveArmSegments(
        MonitoringPoleInput input) => input.ArmSegments.Count > 0
        ? input.ArmSegments
        :
        [
            new MonitoringPoleArmSegment
            {
                LengthM = input.ArmLengthM,
                NearDimensionM = input.ArmNearDiameterM,
                FarDimensionM = input.ArmFarDiameterM,
                WallThicknessM = input.ArmWallThicknessM
            }
        ];

    private static bool HasPositiveInnerSection(
        double outsideDimension,
        double wallThickness,
        TubeSectionType sectionType) => sectionType switch
        {
            TubeSectionType.CircularTube => outsideDimension > 2 * wallThickness,
            TubeSectionType.RegularOctagonDiagonalTube =>
                outsideDimension > 2 * wallThickness / Math.Cos(Math.PI / 8),
            _ => false
        };

    private static void ValidateArmSegments(
        MonitoringPoleInput input,
        ICollection<ValidationIssue> issues)
    {
        var totalLength = 0d;
        for (var index = 0; index < input.ArmSegments.Count; index++)
        {
            var segment = input.ArmSegments[index];
            var prefix = $"横杆第{index + 1}段";
            if (segment.LengthM <= 0 ||
                segment.NearDimensionM <= 0 ||
                segment.FarDimensionM <= 0 ||
                segment.WallThicknessM <= 0)
            {
                issues.Add(new ValidationIssue
                {
                    Field = nameof(input.ArmSegments),
                    Message = $"{prefix}的长度、端部尺寸和壁厚必须大于0。"
                });
                continue;
            }

            totalLength += segment.LengthM;
            if (segment.FarDimensionM > segment.NearDimensionM)
            {
                issues.Add(new ValidationIssue
                {
                    Field = nameof(input.ArmSegments),
                    Message = $"{prefix}远端尺寸不得大于近端尺寸。"
                });
            }
            if (!HasPositiveInnerSection(
                    segment.FarDimensionM,
                    segment.WallThicknessM,
                    input.ArmSectionType))
            {
                issues.Add(new ValidationIssue
                {
                    Field = nameof(input.ArmSegments),
                    Message = $"{prefix}壁厚过大，内截面必须保持为正。"
                });
            }
            if (index > 0 &&
                Math.Abs(input.ArmSegments[index - 1].FarDimensionM -
                         segment.NearDimensionM) > 0.001)
            {
                issues.Add(new ValidationIssue
                {
                    Field = nameof(input.ArmSegments),
                    Message = $"{prefix}近端尺寸与上一段远端尺寸不连续。"
                });
            }
        }

        if (Math.Abs(totalLength - input.ArmLengthM) > 0.001)
        {
            issues.Add(new ValidationIssue
            {
                Field = nameof(input.ArmSegments),
                Message = "横杆分段长度之和必须与横杆总长度一致。"
            });
        }
    }

    private static void RequirePositive(
        double value,
        string field,
        string message,
        ICollection<ValidationIssue> issues)
    {
        if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            issues.Add(new ValidationIssue
            {
                Field = field,
                Message = message
            });
        }
    }
}
