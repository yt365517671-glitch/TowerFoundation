using TowerFoundation.Domain;

namespace TowerFoundation.Calculation;

public sealed class RectangularShortColumnFoundationCalculator
{
    public FoundationScheme Calculate(
        FoundationGeometry geometry,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        geometry.FoundationUnitCount = Math.Max(1, appliedLoad.FoundationUnitCount);
        if (settings.FoundationType == FoundationType.Pile)
        {
            var scheme = new PileFoundationCalculator().Calculate(
                geometry,
                appliedLoad,
                geotechnical,
                settings);
            return SpecialtyVerificationCalculator.Apply(
                scheme,
                appliedLoad,
                geotechnical,
                settings,
                settings.SpecialtyDesign);
        }

        if (settings.FoundationType == FoundationType.RigidShortPile)
        {
            var scheme = new RigidShortPileFoundationCalculator().Calculate(
                geometry,
                appliedLoad,
                geotechnical,
                settings);
            return SpecialtyVerificationCalculator.Apply(
                scheme,
                appliedLoad,
                geotechnical,
                settings,
                settings.SpecialtyDesign);
        }

        if (settings.FoundationType == FoundationType.RigidRectangularShortPile)
        {
            var scheme = new RigidRectangularShortPileFoundationCalculator().Calculate(
                geometry,
                appliedLoad,
                geotechnical,
                settings);
            return SpecialtyVerificationCalculator.Apply(
                scheme,
                appliedLoad,
                geotechnical,
                settings,
                settings.SpecialtyDesign);
        }

        ValidateInputs(geometry, appliedLoad, geotechnical, settings);

        var pedestalPlanArea =
            settings.FoundationType == FoundationType.CircularShortColumn
                ? Math.PI *
                  geometry.PedestalLengthM *
                  geometry.PedestalLengthM /
                  4
                : geometry.PedestalLengthM * geometry.PedestalWidthM;
        var supportedPedestalCount = settings.FoundationType == FoundationType.Raft &&
                                     settings.Pile.PileCount is 3 or 4
            ? settings.Pile.PileCount
            : 1;
        var totalPedestalPlanArea = pedestalPlanArea * supportedPedestalCount;
        var slabVolume = geometry.BaseLengthM * geometry.BaseWidthM * geometry.BaseThicknessM;
        var pedestalVolume = totalPedestalPlanArea * geometry.PedestalHeightM;
        var concreteVolume = slabVolume + pedestalVolume;

        var soilCoverArea = Math.Max(
            0,
            geometry.BaseLengthM * geometry.BaseWidthM -
            totalPedestalPlanArea);
        var soilCoverVolume = soilCoverArea * geometry.PedestalHeightM;
        var grossSoilCoverWeight =
            soilCoverVolume * geotechnical.SoilUnitWeightKnPerM3;
        var grossFoundationWeight =
            concreteVolume * settings.ConcreteUnitWeightKnPerM3;
        var submergedPedestalHeight = Math.Clamp(
            geometry.PedestalHeightM - geotechnical.GroundwaterDepthM,
            0,
            geometry.PedestalHeightM);
        var submergedSlabHeight = Math.Clamp(
            geometry.EmbedmentDepthM -
            Math.Max(geotechnical.GroundwaterDepthM, geometry.PedestalHeightM),
            0,
            geometry.BaseThicknessM);
        var submergedConcreteVolume =
            totalPedestalPlanArea * submergedPedestalHeight +
            geometry.BaseLengthM *
            geometry.BaseWidthM *
            submergedSlabHeight;
        var submergedSoilVolume = soilCoverArea * submergedPedestalHeight;
        var concreteBuoyancy =
            submergedConcreteVolume * settings.WaterUnitWeightKnPerM3;
        var soilBuoyancy =
            submergedSoilVolume * settings.WaterUnitWeightKnPerM3;
        var foundationWeight = grossFoundationWeight - concreteBuoyancy;
        var soilCoverWeight = grossSoilCoverWeight - soilBuoyancy;
        var totalVertical = appliedLoad.VerticalKn + foundationWeight + soilCoverWeight;
        var correctedBearingCapacity = CalculateBearingCapacity(
            geometry,
            geotechnical);

        var baseMomentX = Math.Abs(appliedLoad.MomentXKnM) +
                          Math.Abs(appliedLoad.ShearYKn) * geometry.EmbedmentDepthM;
        var baseMomentY = Math.Abs(appliedLoad.MomentYKnM) +
                          Math.Abs(appliedLoad.ShearXKn) * geometry.EmbedmentDepthM;

        var pressure = CalculateBasePressure(
            geometry,
            totalVertical,
            baseMomentX,
            baseMomentY);

        var totalHorizontal = Math.Sqrt(
            Math.Pow(appliedLoad.ShearXKn, 2) +
            Math.Pow(appliedLoad.ShearYKn, 2));
        // GB 50135-2019 7.4.6 uses N + G for the no-embedment friction check.
        // Do not credit soil cover or passive earth pressure without a dedicated
        // embedded-foundation shear model and confirmed soil parameters.
        var slidingVertical = appliedLoad.VerticalKn + foundationWeight;
        var frictionResistance =
            geotechnical.BaseFrictionCoefficient * slidingVertical;
        var allowableHorizontal = frictionResistance / settings.RequiredSlidingSafetyFactor;
        var slidingUtilization = SafeRatio(totalHorizontal, allowableHorizontal);

        var checks = new List<FoundationCheckResult>
        {
            BuildCheck(
                "DIMENSION_LIMITS",
                "基础几何与搜索限值",
                1,
                GeometryWithinSettings(geometry, settings) ? 1 : 0,
                GeometryWithinSettings(geometry, settings) ? 0 : double.PositiveInfinity,
                "状态",
                $"底板{geometry.BaseLengthM:F2}×{geometry.BaseWidthM:F2}×{geometry.BaseThicknessM:F2} m；当前设计限值为长≥{settings.MinimumBaseLengthM:F2} m、宽≥{settings.MinimumBaseWidthM:F2} m、厚≥{settings.MinimumBaseThicknessM:F2} m。",
                appliedLoad.GoverningCase,
                "项目规则包几何门禁"),
            BuildCheck(
                "CONTACT",
                "基底脱开及抗倾覆稳定",
                pressure.ContactDemand,
                pressure.ContactCapacity,
                pressure.ContactUtilization,
                pressure.ContactUnit,
                pressure.Explanation,
                appliedLoad.GoverningCase,
                pressure.RuleReference),
            new()
            {
                Code = "BEARING_CAPACITY_CORRECTION",
                Name = "地基承载力宽深修正",
                Status = CheckStatus.Result,
                Demand = correctedBearingCapacity,
                Capacity = correctedBearingCapacity,
                Utilization = 0,
                Unit = "kPa",
                GoverningCase = appliedLoad.GoverningCase,
                Explanation = geotechnical.UseBearingCapacityCorrection
                    ? $"按fak={geotechnical.CharacteristicBearingCapacityKpa:F2} kPa、ηb={geotechnical.BearingCapacityWidthCorrectionFactor:F2}、ηd={geotechnical.BearingCapacityDepthCorrectionFactor:F2}，结合基础宽度和埋深计算fa={correctedBearingCapacity:F2} kPa。"
                    : $"采用用户从地勘报告确认的修正后承载力特征值fa={correctedBearingCapacity:F2} kPa。",
                RuleReference = geotechnical.UseBearingCapacityCorrection
                    ? "GB 50007-2011式(5.2.4)"
                    : "GB 50007-2011第5.2.1条；地勘人工确认"
            },
            BuildCheck(
                "BEARING_AVERAGE",
                "地基平均压力",
                pressure.AveragePressureKpa,
                correctedBearingCapacity,
                SafeRatio(pressure.AveragePressureKpa, correctedBearingCapacity),
                "kPa",
                $"平均压力pk={pressure.AveragePressureKpa:F2} kPa，修正后承载力特征值fa={correctedBearingCapacity:F2} kPa。",
                appliedLoad.GoverningCase,
                "GB 50007-2011第5.2.1条；GB 50135-2019第7.2.1条"),
            BuildCheck(
                "BEARING_MAX",
                "地基边缘最大压力",
                pressure.MaximumPressureKpa,
                1.2 * correctedBearingCapacity,
                SafeRatio(pressure.MaximumPressureKpa, 1.2 * correctedBearingCapacity),
                "kPa",
                $"最大压力pkmax={pressure.MaximumPressureKpa:F2} kPa，限值1.2fa={1.2 * correctedBearingCapacity:F2} kPa。",
                appliedLoad.GoverningCase,
                "GB 50007-2011式(5.2.1-2)；GB 50135-2019式(7.2.1-2)"),
            BuildCheck(
                "SLIDING",
                "抗滑移",
                totalHorizontal,
                allowableHorizontal,
                slidingUtilization,
                "kN",
                $"水平力Ph={totalHorizontal:F2} kN；按上部竖向力与基础有效自重计算摩擦抗力μ(N+G)={frictionResistance:F2} kN，未计覆土及被动土抗力；安全系数要求{settings.RequiredSlidingSafetyFactor:F2}，允许水平力{allowableHorizontal:F2} kN。",
                appliedLoad.GoverningCase,
                "GB 50135-2019式(7.4.6)"),
            new()
            {
                Code = "GROUNDWATER",
                Name = "地下水浮力修正",
                Status = CheckStatus.Result,
                Demand = concreteBuoyancy + soilBuoyancy,
                Capacity = grossFoundationWeight + grossSoilCoverWeight,
                Utilization = 0,
                Unit = "kN",
                GoverningCase = appliedLoad.GoverningCase,
                Explanation =
                    geotechnical.GroundwaterDepthM >= geometry.EmbedmentDepthM
                        ? $"地下水埋深{geotechnical.GroundwaterDepthM:F2} m不高于基础底面，浮力扣减为0。"
                        : $"地下水埋深{geotechnical.GroundwaterDepthM:F2} m；浸水混凝土{submergedConcreteVolume:F3} m³、浸水覆土{submergedSoilVolume:F3} m³，已按水重度{settings.WaterUnitWeightKnPerM3:F1} kN/m³扣减浮力{concreteBuoyancy + soilBuoyancy:F2} kN。",
                RuleReference = "GB 50135-2019第7.1.6条；有效重度法"
            }
        };
        if (settings.FoundationType == FoundationType.Raft && supportedPedestalCount > 1)
        {
            var legSpacingM = settings.Pile.PileCenterSpacingM;
            var edgeReserveM = settings.DimensionStepM;
            var requiredLengthM = geometry.PedestalLengthM + legSpacingM + 2 * edgeReserveM;
            var requiredWidthM = geometry.PedestalWidthM +
                                 (supportedPedestalCount == 3
                                     ? 2 * Math.Sqrt(3) * legSpacingM / 3
                                     : legSpacingM) +
                                 2 * edgeReserveM;
            var layoutUtilization = Math.Max(
                SafeRatio(requiredLengthM, geometry.BaseLengthM),
                SafeRatio(requiredWidthM, geometry.BaseWidthM));
            checks.Add(BuildCheck(
                "RAFT_TOWER_LEG_LAYOUT",
                "多塔脚筏板平面包络",
                Math.Max(requiredLengthM, requiredWidthM),
                Math.Min(geometry.BaseLengthM, geometry.BaseWidthM),
                layoutUtilization,
                "m",
                $"{supportedPedestalCount}个塔脚按中心距{legSpacingM:F2} m、相对塔中心定位；短柱外边缘两侧各保留不少于{edgeReserveM:F2} m方案净边后，筏板平面至少为{requiredLengthM:F2}×{requiredWidthM:F2} m，当前为{geometry.BaseLengthM:F2}×{geometry.BaseWidthM:F2} m。",
                appliedLoad.GoverningCase,
                "平剖一致性及几何包络门禁（2026-08-12）"));
            checks.Add(new FoundationCheckResult
            {
                Code = "RAFT_MULTI_LEG_LOCAL_ANALYSIS",
                Name = "多塔脚筏板局部受力整体分析",
                Status = CheckStatus.SpecialReview,
                Demand = 0,
                Capacity = 0,
                Utilization = 0,
                Unit = string.Empty,
                GoverningCase = appliedLoad.GoverningCase,
                Explanation =
                    "当前整体承载力、接触压力和工程量可用于方案比较；各塔脚反力分配、柱下局部冲切、柱带/跨中带配筋及不平衡弯矩传递必须采用塔架—筏板整体模型复核，未复核前不得直接出施工配筋图。",
                RuleReference = "GB 50007-2011第8.4节；GB 55008-2021第4.2节；专项计算门禁"
            });
        }
        checks.AddRange(RectangularFoundationStructuralCheckCalculator.Calculate(
            geometry,
            appliedLoad,
            geotechnical,
            settings,
            foundationWeight + soilCoverWeight));
        checks.Add(new FoundationCheckResult
        {
            Code = "REMAINING_SCOPE",
            Name = "剩余专项验算范围",
            Status = CheckStatus.SpecialReview,
            Demand = 0,
            Capacity = 0,
            Utilization = 0,
            Unit = string.Empty,
            GoverningCase = appliedLoad.GoverningCase,
            Explanation =
                BuildScopeExplanation(settings.FoundationType, supportedPedestalCount),
            RuleReference = "GB 55003-2021第4.2.5、4.2.6条；计算范围门禁（2026-08-02）"
        });

        var workingSpace = settings.ExcavationWorkingSpaceM;
        var excavationVolume =
            (geometry.BaseLengthM + 2 * workingSpace) *
            (geometry.BaseWidthM + 2 * workingSpace) *
            geometry.EmbedmentDepthM;
        var reinforcementDesigns = BuildBottomReinforcementDesigns(
            checks,
            geometry,
            settings);
        var calculatedReinforcement = reinforcementDesigns.Sum(item =>
            item.CalculatedWeightKg);

        var result = new FoundationScheme
        {
            FoundationType = settings.FoundationType,
            Geometry = geometry,
            Checks = checks,
            ReinforcementDesigns = reinforcementDesigns,
            Quantities = new QuantitySummary
            {
                ConcreteM3 = concreteVolume,
                ExcavationM3 = excavationVolume,
                BackfillM3 = Math.Max(0, excavationVolume - concreteVolume),
                EstimatedReinforcementKg = calculatedReinforcement
            }
        };
        result = SpecialtyVerificationCalculator.Apply(
            result,
            appliedLoad,
            geotechnical,
            settings,
            settings.SpecialtyDesign);
        return FoundationUnitQuantityScaler.Apply(result);
    }

    private static List<ReinforcementDesignResult> BuildBottomReinforcementDesigns(
        IReadOnlyList<FoundationCheckResult> checks,
        FoundationGeometry geometry,
        FoundationDesignSettings settings)
    {
        var results = new List<ReinforcementDesignResult>();
        foreach (var direction in new[] { "X", "Y" })
        {
            var check = checks.Single(item =>
                item.Code == $"BOTTOM_REINFORCEMENT_{direction}");
            var barLength = Math.Max(
                0,
                (direction == "X" ? geometry.BaseLengthM : geometry.BaseWidthM) -
                2 * settings.ConcreteCoverMm / 1000);
            var distributionWidth = Math.Max(
                0,
                (direction == "X" ? geometry.BaseWidthM : geometry.BaseLengthM) -
                2 * settings.ConcreteCoverMm / 1000);
            var barCount = check.Status == CheckStatus.NotEvaluated
                ? 0
                : (int)Math.Floor(
                    distributionWidth * 1000 /
                    settings.BottomBarSpacingMm) + 1;
            var totalLength = barCount * barLength;
            var unitWeight = settings.BottomBarDiameterMm *
                             settings.BottomBarDiameterMm / 162;

            results.Add(new ReinforcementDesignResult
            {
                Component = "基础底板底筋",
                Direction = $"{direction}向",
                BarSpecification =
                    $"Φ{settings.BottomBarDiameterMm:F0}@{settings.BottomBarSpacingMm:F0}",
                RequiredAreaMm2 = check.Demand,
                ProvidedAreaMm2 = check.Capacity,
                BarCount = barCount,
                BarDiameterMm = settings.BottomBarDiameterMm,
                BarSpacingMm = settings.BottomBarSpacingMm,
                SingleBarLengthM = barLength,
                TotalLengthM = totalLength,
                UnitWeightKgPerM = unitWeight,
                CalculatedWeightKg = totalLength * unitWeight,
                Status = check.Status,
                RuleReference = check.RuleReference
            });
        }

        return results;
    }

    private static string BuildScopeExplanation(FoundationType type, int supportedPedestalCount)
    {
        var implemented = type switch
        {
            FoundationType.CircularShortColumn =>
                "独立基础－圆形柱按圆形柱截面计入自重、覆土和浮力，并以柱直径进入柱边冲切、受剪、底板受弯与底筋验算。",
            FoundationType.Raft =>
                supportedPedestalCount > 1
                    ? "多塔脚共用筏板已计入整体承载力、脱开、抗滑及按当前等效模型得到的初步结构结果；塔脚反力分配与柱下局部结构结果须经整体模型专项复核。"
                    : "中央塔柱筏板已计入承载力、脱开、抗滑、冲切、受剪、筏板受弯与底筋验算。",
            _ =>
                "独立基础－矩形柱已计入承载力、脱开、抗滑、冲切、受剪、底板受弯与双向底筋验算。"
        };

        return implemented +
               "沉降、裂缝、短柱纵筋、锚栓、独立抗浮及特殊地基仍需相应分层地勘和构造输入。";
    }

    private static FoundationCheckResult BuildCheck(
        string code,
        string name,
        double demand,
        double capacity,
        double utilization,
        string unit,
        string explanation,
        string loadCase,
        string ruleReference)
    {
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

    private static BasePressureResult CalculateBasePressure(
        FoundationGeometry geometry,
        double totalVertical,
        double baseMomentX,
        double baseMomentY)
    {
        var length = geometry.BaseLengthM;
        var width = geometry.BaseWidthM;
        var area = length * width;
        var average = totalVertical / area;
        var pressureFromMomentX = 6 * baseMomentX / (length * Math.Pow(width, 2));
        var pressureFromMomentY = 6 * baseMomentY / (width * Math.Pow(length, 2));
        var linearMaximum = average + pressureFromMomentX + pressureFromMomentY;
        var linearMinimum = average - pressureFromMomentX - pressureFromMomentY;

        if (linearMinimum >= -1e-9)
        {
            return new BasePressureResult
            {
                AveragePressureKpa = average,
                MaximumPressureKpa = linearMaximum,
                ContactDemand = 0,
                ContactCapacity = 0.25,
                ContactUtilization = 0,
                ContactUnit = "底面积比例",
                Explanation = $"全截面受压，pkmin={Math.Max(0, linearMinimum):F2} kPa；按双向偏心线性压力公式计算。",
                RuleReference = "GB 50135-2019式(7.2.2-4)、式(7.2.2-5)"
            };
        }

        var eccentricityX = baseMomentX / totalVertical;
        var eccentricityY = baseMomentY / totalVertical;
        var distanceX = width / 2 - eccentricityX;
        var distanceY = length / 2 - eccentricityY;
        if (distanceX <= 0 || distanceY <= 0)
        {
            return new BasePressureResult
            {
                AveragePressureKpa = average,
                MaximumPressureKpa = double.PositiveInfinity,
                ContactDemand = 1,
                ContactCapacity = 0,
                ContactUtilization = double.PositiveInfinity,
                ContactUnit = "无量纲",
                Explanation = $"合力落在基础底面以外：ex={eccentricityX:F3} m，ey={eccentricityY:F3} m。",
                RuleReference = "GB 50135-2019第7.2.3条"
            };
        }

        const double tolerance = 1e-9;
        if (baseMomentX > tolerance && baseMomentY > tolerance)
        {
            var requiredProduct = 0.125 * area;
            var actualProduct = distanceX * distanceY;
            return new BasePressureResult
            {
                AveragePressureKpa = average,
                MaximumPressureKpa = totalVertical / (3 * actualProduct),
                ContactDemand = requiredProduct,
                ContactCapacity = actualProduct,
                ContactUtilization = SafeRatio(requiredProduct, actualProduct),
                ContactUnit = "m²",
                Explanation = $"双向偏心部分脱开：ax={distanceX:F3} m，ay={distanceY:F3} m；要求ax·ay≥0.125bl。",
                RuleReference = "GB 50135-2019式(7.2.3-3)、式(7.2.3-4)"
            };
        }

        if (baseMomentX > tolerance)
        {
            var required = 0.75 * width;
            var actual = 3 * distanceX;
            return new BasePressureResult
            {
                AveragePressureKpa = average,
                MaximumPressureKpa = 2 * totalVertical / (3 * length * distanceX),
                ContactDemand = required,
                ContactCapacity = actual,
                ContactUtilization = SafeRatio(required, actual),
                ContactUnit = "m",
                Explanation = $"X向偏心部分脱开：a={distanceX:F3} m；要求3a≥0.75b。",
                RuleReference = "GB 50135-2019式(7.2.3-1)、式(7.2.3-2)"
            };
        }

        var requiredLength = 0.75 * length;
        var actualLength = 3 * distanceY;
        return new BasePressureResult
        {
            AveragePressureKpa = average,
            MaximumPressureKpa = 2 * totalVertical / (3 * width * distanceY),
            ContactDemand = requiredLength,
            ContactCapacity = actualLength,
            ContactUtilization = SafeRatio(requiredLength, actualLength),
            ContactUnit = "m",
            Explanation = $"Y向偏心部分脱开：a={distanceY:F3} m；要求3a≥0.75l。",
            RuleReference = "GB 50135-2019式(7.2.3-1)、式(7.2.3-2)"
        };
    }

    private static double SafeRatio(double numerator, double denominator)
    {
        if (Math.Abs(numerator) < 1e-12)
        {
            return 0;
        }

        return denominator > 0 ? numerator / denominator : double.PositiveInfinity;
    }

    private static double CalculateBearingCapacity(
        FoundationGeometry geometry,
        GeotechnicalInput geotechnical)
    {
        if (!geotechnical.UseBearingCapacityCorrection)
        {
            return geotechnical.BearingCapacityKpa;
        }

        var effectiveWidth = Math.Clamp(
            Math.Min(geometry.BaseLengthM, geometry.BaseWidthM),
            3,
            6);
        var effectiveDepth = Math.Max(geometry.EmbedmentDepthM, 0.5);
        return geotechnical.CharacteristicBearingCapacityKpa +
               geotechnical.BearingCapacityWidthCorrectionFactor *
               geotechnical.SoilBelowBaseUnitWeightKnPerM3 *
               (effectiveWidth - 3) +
               geotechnical.BearingCapacityDepthCorrectionFactor *
               geotechnical.SoilAboveBaseAverageUnitWeightKnPerM3 *
               (effectiveDepth - 0.5);
    }

    private static bool GeometryWithinSettings(
        FoundationGeometry geometry,
        FoundationDesignSettings settings)
    {
        const double tolerance = 1e-9;
        return geometry.BaseLengthM + tolerance >= settings.MinimumBaseLengthM &&
               geometry.BaseWidthM + tolerance >= settings.MinimumBaseWidthM &&
               geometry.BaseThicknessM + tolerance >= settings.MinimumBaseThicknessM &&
               geometry.BaseLengthM <= settings.MaximumBaseLengthM + tolerance &&
               geometry.BaseWidthM <= settings.MaximumBaseWidthM + tolerance &&
               geometry.BaseThicknessM <= settings.MaximumBaseThicknessM + tolerance;
    }

    private static void ValidateInputs(
        FoundationGeometry geometry,
        FoundationLoad appliedLoad,
        GeotechnicalInput geotechnical,
        FoundationDesignSettings settings)
    {
        if (geometry.BaseLengthM <= 0 ||
            geometry.BaseWidthM <= 0 ||
            geometry.BaseThicknessM <= 0 ||
            geometry.PedestalLengthM <= 0 ||
            geometry.PedestalWidthM <= 0 ||
            geometry.PedestalHeightM < 0)
        {
            throw new ArgumentException("基础几何尺寸必须为正值。");
        }

        if (geometry.PedestalLengthM > geometry.BaseLengthM ||
            geometry.PedestalWidthM > geometry.BaseWidthM)
        {
            throw new ArgumentException("短柱尺寸不能超过基础底板尺寸。");
        }

        if (appliedLoad.VerticalKn < 0)
        {
            throw new ArgumentException("当前模块要求竖向压力采用向下为正。");
        }

        if (geotechnical.BearingCapacityKpa <= 0 ||
            geotechnical.SoilUnitWeightKnPerM3 <= 0 ||
            geotechnical.BaseFrictionCoefficient <= 0 ||
            geotechnical.GroundwaterDepthM < 0)
        {
            throw new ArgumentException("地勘参数必须为正值。");
        }

        if (geotechnical.UseBearingCapacityCorrection &&
            (geotechnical.CharacteristicBearingCapacityKpa <= 0 ||
             geotechnical.BearingCapacityWidthCorrectionFactor < 0 ||
             geotechnical.BearingCapacityDepthCorrectionFactor < 0 ||
             geotechnical.SoilBelowBaseUnitWeightKnPerM3 <= 0 ||
             geotechnical.SoilAboveBaseAverageUnitWeightKnPerM3 <= 0))
        {
            throw new ArgumentException("地基承载力宽深修正参数无效。");
        }

        if (settings.ConcreteUnitWeightKnPerM3 <= 0 ||
            settings.WaterUnitWeightKnPerM3 <= 0 ||
            settings.RequiredSlidingSafetyFactor <= 0)
        {
            throw new ArgumentException("基础设计设置无效。");
        }
    }

    private sealed class BasePressureResult
    {
        public double AveragePressureKpa { get; init; }

        public double MaximumPressureKpa { get; init; }

        public double ContactDemand { get; init; }

        public double ContactCapacity { get; init; }

        public double ContactUtilization { get; init; }

        public string ContactUnit { get; init; } = string.Empty;

        public string Explanation { get; init; } = string.Empty;

        public string RuleReference { get; init; } = string.Empty;
    }
}
