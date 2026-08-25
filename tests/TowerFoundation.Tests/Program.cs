using TowerFoundation.Application;
using TowerFoundation.Calculation;
using TowerFoundation.Domain;
using TowerFoundation.Infrastructure;
using TowerFoundation.Optimization;
using TowerFoundation.Licensing;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

var tests = new (string Name, Func<Task> Run)[]
{
    ("监控杆荷载计算", TestMonitoringPoleLoads),
    ("正八边形对角尺寸钢材体积", TestOctagonalTubeVolume),
    ("分段横杆迎风面积", TestSegmentedArmProjectedArea),
    ("分段横杆自重", TestSegmentedArmSelfWeight),
    ("分段横杆弯矩和扭矩", TestSegmentedArmMoments),
    ("监控杆图纸视觉JSON解析", TestMonitoringDrawingJsonParsing),
    ("监控杆规格证据本地语义纠偏", TestMonitoringDrawingSpecificationReconciliation),
    ("监控杆视觉切换逐次确认与规格优先", TestMonitoringDrawingForbiddenFallbackAndSpecificationPriority),
    ("监控杆视觉未经确认禁止换模型", TestMonitoringDrawingFallbackRequiresConsent),
    ("监控杆图纸单位换算", TestMonitoringDrawingUnitConversion),
    ("监控杆图纸缺失字段不覆盖原值", TestMonitoringDrawingMissingFieldPreservation),
    ("监控杆图纸低置信字段拦截", TestMonitoringDrawingLowConfidenceGate),
    ("监控杆部分候选回填保持荷载参数一致", TestMonitoringDrawingPartialCandidateConsistency),
    ("监控杆AI缺失项无默认值与二次补录门禁", TestMonitoringDrawingExplicitInputGate),
    ("监控杆视觉识别记录持久化与目录迁移", TestMonitoringDrawingRecognitionHistory),
    ("22G101-3矩形封闭箍筋下料", TestRectangularClosedStirrupCutLength),
    ("高耸结构基本风压0.35下限与塔桅免选址", TestWindMinimumAndTowerLocationIndependence),
    ("独立基础－矩形柱校核", TestFoundationChecks),
    ("规范偏心受压与允许脱开", TestNormativePartialContact),
    ("规范抗滑安全系数", TestNormativeSlidingSafetyFactor),
    ("三策略方案优化", TestThreeStrategyOptimization),
    ("方案搜索失败定量诊断", TestActionableOptimizationFailure),
    ("本机地勘分析记录保存与复用", TestGeotechnicalAnalysisHistory),
    ("离线设计流程", TestOfflineWorkflow),
    ("塔桅手工荷载流程", TestTowerManualLoadWorkflow),
    ("尺寸调整建议", TestAdjustmentAdvice),
    ("计算书CAD材料与工程量导出", TestPrototypeOutputPackage),
    ("项目文件往返保存", TestJsonRoundTrip),
    ("默认项目目录与项目列表", TestLocalProjectCatalog),
    ("设置密钥DPAPI保护", TestSettingsProtection),
    ("离线授权两级签名与机器绑定", TestLicenseSignatureHierarchy),
    ("客户授权期限与日期回退门禁", TestClientLicenseAssessment),
    ("根密钥DPAPI隔离与加密备份", TestRootKeyDpapiAndBackup),
    ("授权格式篡改与权限越界拦截", TestLicenseTamperAndPermissionGate),
    ("DeepSeek默认模型与白名单", TestDeepSeekModelDefaults),
    ("百炼视觉密钥保护与识别模型白名单", TestVisualAiSettingsAndModelWhitelist),
    ("视觉大模型直接观察PDF与参数安全解析", TestVisualPdfAnalysisPipeline),
    ("视觉请求失败自动拆页恢复", TestVisualBatchSplitRecovery),
    ("视觉单页重试与确认后快速模型兜底", TestVisualSinglePageModelFallback),
    ("全国省市县与规范风压来源", TestRegionWindCatalog),
    ("企业标准塔型荷载库与安全回填", TestEnterpriseTowerLoadCatalog),
    ("地下水浮力修正", TestGroundwaterBuoyancy),
    ("承载力宽深修正", TestBearingCapacityCorrection),
    ("冲切受剪弯矩与底筋", TestStructuralFoundationChecks),
    ("标准组合与基本组合双通道路由", TestDualCombinationRouting),
    ("第三轮五类荷载组合与采用轨迹", TestThirdRoundLoadCombinationEngine),
    ("双向偏心底板保守包络", TestBiaxialBendingEnvelope),
    ("本地PDF OCR", TestLocalPdfOcr),
    ("地勘Word正文表格提取", TestDocxExtraction),
    ("DeepSeek结构化参数解析", TestDeepSeekStructuredExtraction),
    ("DeepSeek塔脚锚栓详图证据提取", TestDeepSeekAnchorDrawingExtraction),
    ("DeepSeek冲突值与桩型错配拦截", TestDeepSeekConflictGuard),
    ("PDF OCR到DeepSeek关键参数回填闭环", TestPdfOcrAiPipeline),
    ("AI安全参数直接填入并展开fak", TestAiSafeValuesDirectFill),
    ("独立基础－圆形柱计算", TestCircularShortColumnFoundation),
    ("中央塔柱筏板基础计算", TestRaftFoundation),
    ("刚性短柱桩原计算书回归与导出", TestRigidShortPileFoundation),
    ("矩形刚性短柱桩双向计算与导出", TestRigidRectangularShortPileFoundation),
    ("单桩灌注桩抗压抗拔与水平承载力", TestPileFoundation),
    ("单桩灌注桩尺寸效应与抗拔公式回归", TestPileNormativeCorrections),
    ("计算结果与安全结论状态分流", TestCalculatedResultStatusSeparation),
    ("桩顶变形允许值与来源门禁", TestDeformationLimitSourceGate),
    ("分层沉降确定性验算", TestSettlementVerification),
    ("裂缝与锚栓确定性验算", TestCrackAndAnchorVerification),
    ("智能补齐适用性与状态分流", TestSpecialtyAutoFillAndStatusRouting),
    ("独立基础短柱配筋与高水位抗浮", TestPedestalAndHighWaterVerification),
    ("灌注桩m法变形与桩身结构验算", TestPileMMethodAndStructuralVerification),
    ("独立桩连梁内力门禁与配筋", TestTieBeamStructuralGate),
    ("多塔柱各类独立基础连系梁拓扑", TestIndependentFoundationTieBeamTopology),
    ("非桩独立基础连系梁内力门禁与CAD", TestIndependentFoundationTieBeamGateAndDxf),
    ("三四塔脚六类基础CAD组图", TestMultiLegFoundationDrawingSets),
    ("锚栓下锚板与确认承载力验算", TestAnchorPlateAndConcreteCapacity),
    ("第三轮锚栓程序模型来源门禁", TestAnchorProgramModelGate),
    ("第三轮桩基负摩阻与静载曲线沉降", TestPileNegativeFrictionAndLoadTestSettlement),
    ("特殊地基场景分流与冻深核对", TestSpecialGroundRiskRouting),
    ("缺参复核稿导出门禁", TestReviewDraftExportGate),
    ("六类基础v0.8.0金标准回归索引", TestGoldenBenchmarkPack),
    ("30组塔基全流程场景矩阵", TestThirtyTowerFoundationScenarios)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}");
        Console.WriteLine(exception);
    }
}

Console.WriteLine();
Console.WriteLine($"Tests: {tests.Length}, Passed: {tests.Length - failures.Count}, Failed: {failures.Count}");

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
}

return;

static Task TestMonitoringPoleLoads()
{
    var calculator = new MonitoringPoleLoadCalculator();
    var input = new MonitoringPoleInput();
    var result = calculator.Calculate(input);

    var expectedWindPressure = input.BasicWindPressureKpa *
                               input.WindVibrationFactor *
                               input.ShapeCoefficient *
                               input.TerrainHeightFactor;
    AssertClose(result.DesignWindPressureKpa, expectedWindPressure, 1e-10, "风荷载标准值公式应符合GB 50009式(8.1.1-1)。");
    Assert(result.FoundationLoad.ShearXKn > 0, "基础水平力应大于0。");
    Assert(result.FoundationLoad.MomentYKnM > result.FoundationLoad.ShearXKn, "基础弯矩应体现作用高度。");
    Assert(result.FoundationLoad.VerticalKn > input.AttachmentWeightKn, "竖向荷载应包含杆件自重。");
    Assert(result.FoundationLoad.TorsionKnM > 0, "单侧横杆应产生扭矩。");
    return Task.CompletedTask;
}

static Task TestOctagonalTubeVolume()
{
    const double length = 6.5;
    const double bottom = 0.24;
    const double top = 0.18;
    const double thickness = 0.005;
    var innerOffset = 2 * thickness / Math.Cos(Math.PI / 8);
    static double OctagonArea(double diagonal) => diagonal * diagonal / Math.Sqrt(2);
    var expected = length / 2 *
                   ((OctagonArea(bottom) - OctagonArea(bottom - innerOffset)) +
                    (OctagonArea(top) - OctagonArea(top - innerOffset)));
    var actual = MonitoringPoleLoadCalculator.CalculateTaperedTubeVolume(
        length,
        bottom,
        top,
        thickness,
        TubeSectionType.RegularOctagonDiagonalTube);
    AssertClose(actual, expected, 1e-12,
        "正八边形对角尺寸管必须按外、内正八边形面积差积分，不能套用圆管公式。" );

    var circular = MonitoringPoleLoadCalculator.CalculateTaperedTubeVolume(
        length,
        bottom,
        top,
        thickness,
        TubeSectionType.CircularTube);
    Assert(Math.Abs(actual - circular) > 1e-5,
        "相同名义尺寸下正八边形体积不应与圆管体积相同。" );
    return Task.CompletedTask;
}

static Task TestSegmentedArmProjectedArea()
{
    var input = CreateSegmentedOctagonalMonitoringPole();
    var result = new MonitoringPoleLoadCalculator().Calculate(input);
    var expected = 7 * (0.28 + 0.195) / 2 +
                   7 * (0.195 + 0.11) / 2;
    AssertClose(result.ArmProjectedAreaM2, expected, 1e-12,
        "14m横杆迎风面积必须按两段线性变截面逐段累计。" );
    return Task.CompletedTask;
}

static Task TestSegmentedArmSelfWeight()
{
    var input = CreateSegmentedOctagonalMonitoringPole();
    var first = MonitoringPoleLoadCalculator.CalculateTaperedTubeVolume(
        7, 0.28, 0.195, 0.006, TubeSectionType.RegularOctagonDiagonalTube);
    var second = MonitoringPoleLoadCalculator.CalculateTaperedTubeVolume(
        7, 0.195, 0.11, 0.004, TubeSectionType.RegularOctagonDiagonalTube);
    var result = new MonitoringPoleLoadCalculator().Calculate(input);
    AssertClose(result.ArmSteelVolumeM3, first + second, 1e-12,
        "14m横杆钢材量必须保留近端6mm、远端4mm两段。" );
    AssertClose(result.ArmSelfWeightKn, (first + second) * input.SteelUnitWeightKnPerM3, 1e-12,
        "分段横杆自重必须由逐段钢材体积确定。" );

    var averaged = MonitoringPoleLoadCalculator.CalculateTaperedTubeVolume(
        14, 0.28, 0.11, 0.005, TubeSectionType.RegularOctagonDiagonalTube);
    Assert(Math.Abs(result.ArmSteelVolumeM3 - averaged) > 1e-5,
        "分段横杆不得用5mm平均壁厚替代两段计算。" );
    return Task.CompletedTask;
}

static Task TestSegmentedArmMoments()
{
    var input = CreateSegmentedOctagonalMonitoringPole();
    input.BasicWindPressureKpa = 1;
    input.WindVibrationFactor = 1;
    input.ShapeCoefficient = 1;
    input.TerrainHeightFactor = 1;
    var result = new MonitoringPoleLoadCalculator().Calculate(input);

    static double Centroid(double length, double start, double end) =>
        length * (start + 2 * end) / (3 * (start + end));
    static double SteelArea(double diagonal, double thickness)
    {
        var inside = diagonal - 2 * thickness / Math.Cos(Math.PI / 8);
        return (diagonal * diagonal - inside * inside) / Math.Sqrt(2);
    }

    var windArea1 = 7 * (0.28 + 0.195) / 2;
    var windArea2 = 7 * (0.195 + 0.11) / 2;
    var expectedTorsion =
        windArea1 * Centroid(7, 0.28, 0.195) +
        windArea2 * (7 + Centroid(7, 0.195, 0.11));
    AssertClose(result.ArmWindTorsionKnM, expectedTorsion, 1e-12,
        "分段横杆风扭矩必须使用每段面积和全局形心。" );

    var a10 = SteelArea(0.28, 0.006);
    var a11 = SteelArea(0.195, 0.006);
    var a20 = SteelArea(0.195, 0.004);
    var a21 = SteelArea(0.11, 0.004);
    var v1 = 7 * (a10 + a11) / 2;
    var v2 = 7 * (a20 + a21) / 2;
    var expectedGravityMoment = input.SteelUnitWeightKnPerM3 *
                                (v1 * Centroid(7, a10, a11) +
                                 v2 * (7 + Centroid(7, a20, a21)));
    AssertClose(result.ArmGravityMomentKnM, expectedGravityMoment, 1e-12,
        "分段横杆重力弯矩必须逐段按钢材线密度形心累计。" );
    AssertClose(result.FoundationLoad.MomentXKnM, expectedGravityMoment, 1e-12,
        "横杆及设备偏心重力弯矩必须传入基础端荷载。" );
    return Task.CompletedTask;
}

static Task TestMonitoringDrawingJsonParsing()
{
    const string json = """
        {
          "drawing_model":"H6.5-L14",
          "fields":{
            "title_height":{"value":6.5,"unit":"m","raw_annotation":"H=6.5m","region":"标题栏","confidence":0.99,"conflict":false,"warning":""},
            "pole_height":{"value":6500,"unit":"mm","raw_annotation":"八角对角(280-340)×10×6500","region":"主视图","confidence":0.96,"conflict":false,"warning":""},
            "arm_length":{"value":14000,"unit":"mm","raw_annotation":"×14000","region":"横杆标注","confidence":0.97,"conflict":false,"warning":""},
            "attachment_projected_area":{"value":null,"unit":"m2","raw_annotation":"","region":"","confidence":0,"conflict":false,"warning":"图纸未给"},
            "attachment_weight":{"value":null,"unit":"kN","raw_annotation":"","region":"","confidence":0,"conflict":false,"warning":"图纸未给"}
          },
          "arm_segments":[
            {"length":{"value":7000,"unit":"mm"},"near_dimension":{"value":280,"unit":"mm"},"far_dimension":{"value":195,"unit":"mm"},"wall_thickness":{"value":6,"unit":"mm"},"raw_annotation":"近端δ=6","region":"横杆局部","confidence":0.93,"conflict":false,"warning":""},
            {"length":{"value":7000,"unit":"mm"},"near_dimension":{"value":195,"unit":"mm"},"far_dimension":{"value":110,"unit":"mm"},"wall_thickness":{"value":4,"unit":"mm"},"raw_annotation":"远端δ=4","region":"横杆局部","confidence":0.92,"conflict":false,"warning":""}
          ],
          "warnings":[]
        }
        """;
    var candidate = MonitoringDrawingVisionAiService.ParseCandidateResponse(
        json, "H6.5-L14.pdf", "abc", 1, "qwen3.7-plus");
    Assert(candidate.DrawingModel == "H6.5-L14" && candidate.ArmSegments.Count == 2,
        "视觉JSON应解析型号和14m分段集合。" );
    AssertClose(candidate.Fields.Single(field => field.FieldName == MonitoringDrawingFieldNames.PoleHeight).Value ?? 0,
        6.5, 1e-12, "视觉JSON解析应把6500mm立杆高度换算为6.5m。" );
    Assert(candidate.Fields.Single(field => field.FieldName == MonitoringDrawingFieldNames.AttachmentWeight).Value is null,
        "图纸未给的设备重量必须保持null。" );
    return Task.CompletedTask;
}

static Task TestMonitoringDrawingSpecificationReconciliation()
{
    const string json = """
        {
          "drawing_model":"H6.5-L3",
          "fields":{
            "pole_height":{"value":6.5,"unit":"m","raw_annotation":"八角对角(180-240)×5×6500","region":"主视图左侧标注","confidence":0.97,"conflict":false,"warning":""},
            "pole_bottom_dimension":{"value":240,"unit":"mm","raw_annotation":"","region":"","confidence":0.93,"conflict":false,"warning":""},
            "pole_top_dimension":{"value":120,"unit":"mm","raw_annotation":"","region":"","confidence":0.93,"conflict":false,"warning":""},
            "pole_wall_thickness":{"value":5,"unit":"mm","raw_annotation":"","region":"","confidence":0.94,"conflict":false,"warning":""},
            "arm_length":{"value":4,"unit":"m","raw_annotation":"八角对角(90-160)×4×3000","region":"横杆规格标注","confidence":0.96,"conflict":false,"warning":""},
            "arm_near_dimension":{"value":140,"unit":"mm","raw_annotation":"","region":"","confidence":0.90,"conflict":false,"warning":""},
            "arm_far_dimension":{"value":80,"unit":"mm","raw_annotation":"","region":"","confidence":0.90,"conflict":false,"warning":""},
            "arm_wall_thickness":{"value":4,"unit":"mm","raw_annotation":"","region":"","confidence":0.94,"conflict":false,"warning":""}
          },
          "arm_segments":[],
          "warnings":[]
        }
        """;

    var candidate = MonitoringDrawingVisionAiService.ParseCandidateResponse(
        json, "H6.5-L3.pdf", "evidence", 1, "vision-test");
    double Value(string name) => candidate.Fields.Single(field => field.FieldName == name).Value ?? 0;

    AssertClose(Value(MonitoringDrawingFieldNames.PoleTopDimension), 0.180, 1e-12,
        "立杆规格原始证据180-240必须按上端180、下端240的固定语义纠正结构化误值。" );
    AssertClose(Value(MonitoringDrawingFieldNames.PoleBottomDimension), 0.240, 1e-12,
        "立杆下端尺寸必须从同一原始规格证据解析。" );
    AssertClose(Value(MonitoringDrawingFieldNames.ArmLength), 3.0, 1e-12,
        "横杆规格原始证据末项3000必须纠正为3m横杆长度。" );
    AssertClose(Value(MonitoringDrawingFieldNames.ArmNearDimension), 0.160, 1e-12,
        "横杆规格90-160必须按远端90、近端160的固定语义解析。" );
    AssertClose(Value(MonitoringDrawingFieldNames.ArmFarDimension), 0.090, 1e-12,
        "横杆远端尺寸必须从同一原始规格证据解析。" );
    Assert(candidate.Fields.Any(field =>
            field.FieldName == MonitoringDrawingFieldNames.PoleTopDimension &&
            field.Warning.Contains("本地纠正", StringComparison.Ordinal)),
        "视觉结构化值与原始规格证据冲突时必须留下本地纠偏审计说明。" );
    return Task.CompletedTask;
}

static async Task TestMonitoringDrawingForbiddenFallbackAndSpecificationPriority()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.MonitoringDrawingFallback.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var pdfPath = Path.Combine(directory, "H6.5-L5.pdf");
    try
    {
        await File.WriteAllBytesAsync(pdfPath, BuildSimplePdf("H6.5 L5"));
        var settings = new ApplicationSettings
        {
            AiMode = AiOperatingMode.OnlinePreferred,
            HasVisionApiKey = true,
            VisionModel = "qwen3.7-plus",
            RequestTimeoutSeconds = 20
        };
        const string firstJson = """
            {"drawing_model":"H6.5-L5","fields":{
              "arm_length":{"value":5,"unit":"m","raw_annotation":"八角对角(90-180)×4×5000","region":"横杆规格","confidence":0.96,"conflict":false,"warning":""},
              "arm_near_dimension":{"value":180,"unit":"mm","raw_annotation":"","region":"","confidence":0.95,"conflict":false,"warning":""},
              "arm_far_dimension":{"value":90,"unit":"mm","raw_annotation":"","region":"","confidence":0.95,"conflict":false,"warning":""},
              "arm_wall_thickness":{"value":4,"unit":"mm","raw_annotation":"","region":"","confidence":0.95,"conflict":false,"warning":""}
            },"arm_segments":[],"warnings":[]}
            """;
        const string reviewJson = """
            {"drawing_model":"H6.5-L5","fields":{
              "arm_length":{"value":4.2,"unit":"m","raw_annotation":"1000+1000+1000+1000+200","region":"尺寸链","confidence":0.98,"conflict":true,"warning":"尺寸链与标题冲突"},
              "arm_near_dimension":{"value":180,"unit":"mm","raw_annotation":"90-180","region":"横杆规格","confidence":0.96,"conflict":false,"warning":""},
              "arm_far_dimension":{"value":90,"unit":"mm","raw_annotation":"90-180","region":"横杆规格","confidence":0.96,"conflict":false,"warning":""},
              "arm_wall_thickness":{"value":4,"unit":"mm","raw_annotation":"4","region":"横杆规格","confidence":0.96,"conflict":false,"warning":""}
            },"arm_segments":[],"warnings":[]}
            """;
        var requestCount = 0;
        var fallbackCount = 0;
        var selectedCount = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestCount++;
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("\"model\":\"qwen3.7-plus\"", StringComparison.Ordinal))
            {
                selectedCount++;
                return selectedCount == 1
                    ? BuildAiHttpResponse("{\"error\":{\"message\":\"forbidden\"}}", HttpStatusCode.Forbidden)
                    : BuildAiHttpResponse(reviewJson);
            }

            fallbackCount++;
            if (body.Contains("\"model\":\"qwen3.6-flash\"", StringComparison.Ordinal))
            {
                return BuildAiHttpResponse(
                    "{\"error\":{\"message\":\"fallback forbidden\"}}",
                    HttpStatusCode.Forbidden);
            }
            Assert(body.Contains("\"model\":\"qwen3-vl-flash\"", StringComparison.Ordinal),
                "首个备用模型仍无权限时必须继续切换第二备用视觉模型。" );
            return BuildAiHttpResponse(firstJson);
        });
        using var service = new MonitoringDrawingVisionAiService(
            new FakeSettingsService(settings),
            handler);
        var confirmations = new List<VisionModelSwitchRequest>();
        var result = await service.AnalyzePdfsAsync(
            [pdfPath],
            switchOptions: new VisionModelSwitchOptions
            {
                ConfirmAsync = (request, _) =>
                {
                    confirmations.Add(request);
                    return Task.FromResult(true);
                }
            });
        Assert(requestCount == 4 && fallbackCount == 2 && result.Failures.Count == 0,
            "主模型和首个备用模型403时应由第二备用模型完成首轮，再由主模型完成复核。" );
        Assert(confirmations.Count == 2 &&
               confirmations[0].CurrentModel == "qwen3.7-plus" &&
               confirmations[0].ProposedModel == "qwen3.6-flash" &&
               confirmations[1].CurrentModel == "qwen3.6-flash" &&
               confirmations[1].ProposedModel == "qwen3-vl-flash",
            "每一次更换视觉模型都必须先取得用户确认。" );
        var candidate = result.Candidates.Single();
        var armLength = candidate.Fields.Single(field =>
            field.FieldName == MonitoringDrawingFieldNames.ArmLength);
        AssertClose(armLength.Value ?? 0, 5, 1e-12,
            "第二遍尺寸链不得覆盖首轮完整横杆规格中的总长5000mm。" );
        Assert(!armLength.IsSelected &&
               armLength.Warning.Contains("保留首轮原始规格证据", StringComparison.Ordinal),
            "两遍证据冲突时应保留完整规格并要求人工确认。" );
        Assert(candidate.Warnings.Any(warning => warning.Contains("实际使用", StringComparison.Ordinal)),
            "备用模型的实际使用情况必须保留在候选警告。" );
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestMonitoringDrawingFallbackRequiresConsent()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.MonitoringDrawingConsent.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var pdfPath = Path.Combine(directory, "consent.pdf");
    try
    {
        await File.WriteAllBytesAsync(pdfPath, BuildSimplePdf("H6.5 L3"));
        var settings = new ApplicationSettings
        {
            AiMode = AiOperatingMode.OnlinePreferred,
            HasVisionApiKey = true,
            VisionModel = "qwen3.7-plus",
            RequestTimeoutSeconds = 20
        };
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            return BuildAiHttpResponse(
                "{\"error\":{\"message\":\"service unavailable\"}}",
                HttpStatusCode.ServiceUnavailable);
        });
        using var service = new MonitoringDrawingVisionAiService(
            new FakeSettingsService(settings),
            handler);
        var result = await service.AnalyzePdfsAsync([pdfPath]);
        Assert(requestCount == 1,
            "未提供用户确认回调时只能请求所选模型，不得静默请求备用模型。" );
        Assert(result.Candidates.Count == 0 &&
               result.Failures.Single().Contains("未获得用户确认", StringComparison.Ordinal),
            "拒绝自动换模应作为真实失败记录，不得伪造候选。" );
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static Task TestMonitoringDrawingUnitConversion()
{
    const string json = """
        {"drawing_model":"单位换算","fields":{
          "pole_bottom_dimension":{"value":340,"unit":"mm","raw_annotation":"340","region":"规格","confidence":0.9,"conflict":false,"warning":""},
          "attachment_projected_area":{"value":2000,"unit":"cm2","raw_annotation":"2000cm2","region":"设备表","confidence":0.9,"conflict":false,"warning":""},
          "attachment_weight":{"value":100,"unit":"kg","raw_annotation":"100kg","region":"设备表","confidence":0.9,"conflict":false,"warning":""}
        },"arm_segments":[],"warnings":[]}
        """;
    var candidate = MonitoringDrawingVisionAiService.ParseCandidateResponse(
        json, "units.pdf", "def", 1, "qwen3.7-plus");
    AssertClose(candidate.Fields.Single(field => field.FieldName == MonitoringDrawingFieldNames.PoleBottomDimension).Value ?? 0,
        0.34, 1e-12, "杆件mm尺寸应换算为m。" );
    AssertClose(candidate.Fields.Single(field => field.FieldName == MonitoringDrawingFieldNames.AttachmentProjectedArea).Value ?? 0,
        0.2, 1e-12, "设备cm2面积应换算为m2。" );
    AssertClose(candidate.Fields.Single(field => field.FieldName == MonitoringDrawingFieldNames.AttachmentWeight).Value ?? 0,
        0.980665, 1e-12, "设备kg质量应按标准重力加速度换算为kN。" );
    return Task.CompletedTask;
}

static Task TestMonitoringDrawingMissingFieldPreservation()
{
    var project = new ProjectModel();
    project.MonitoringPole.AttachmentProjectedAreaM2 = 0.42;
    project.MonitoringPole.AttachmentWeightKn = 0.31;
    var candidate = new MonitoringDrawingCandidate
    {
        Fields =
        [
            new MonitoringDrawingFieldCandidate
            {
                FieldName = MonitoringDrawingFieldNames.AttachmentProjectedArea,
                DisplayName = "设备迎风面积",
                Value = null,
                IsSelected = true,
                Confidence = 0.99
            },
            new MonitoringDrawingFieldCandidate
            {
                FieldName = MonitoringDrawingFieldNames.AttachmentWeight,
                DisplayName = "设备重量",
                Value = null,
                IsSelected = true,
                Confidence = 0.99
            }
        ]
    };
    var result = MonitoringDrawingCandidateApplicator.Apply(project, candidate);
    Assert(result.AppliedFieldCount == 0,
        "null候选不得计为已采用字段。" );
    AssertClose(project.MonitoringPole.AttachmentProjectedAreaM2, 0.42, 1e-12,
        "图纸未给设备迎风面积时必须保留原值。" );
    AssertClose(project.MonitoringPole.AttachmentWeightKn, 0.31, 1e-12,
        "图纸未给设备重量时必须保留原值。" );
    return Task.CompletedTask;
}

static Task TestMonitoringDrawingLowConfidenceGate()
{
    var project = new ProjectModel();
    project.MonitoringPole.PoleHeightM = 8;
    project.MonitoringPole.ArmMountingHeightM = 6;
    var field = new MonitoringDrawingFieldCandidate
    {
        FieldName = MonitoringDrawingFieldNames.PoleHeight,
        DisplayName = "立杆高度",
        Value = 6.5,
        Unit = "m",
        Confidence = 0.60,
        IsSelected = true
    };
    var candidate = new MonitoringDrawingCandidate { Fields = [field] };
    var blocked = MonitoringDrawingCandidateApplicator.Apply(project, candidate);
    Assert(blocked.AppliedFieldCount == 0 && Math.Abs(project.MonitoringPole.PoleHeightM - 8) < 1e-12,
        "低置信字段未人工确认时必须拦截。" );

    field.IsManuallyConfirmed = true;
    var confirmed = MonitoringDrawingCandidateApplicator.Apply(project, candidate);
    Assert(confirmed.AppliedFieldCount == 1 && Math.Abs(project.MonitoringPole.PoleHeightM - 6.5) < 1e-12,
        "低置信字段只有人工确认后才允许写入。" );
    return Task.CompletedTask;
}

static Task TestMonitoringDrawingPartialCandidateConsistency()
{
    var project = new ProjectModel();
    AssertClose(project.MonitoringPole.PoleHeightM, 8, 1e-12, "测试前立杆高度应为默认8m。");
    AssertClose(project.MonitoringPole.ArmMountingHeightM, 7, 1e-12, "测试前横杆安装高度应为默认7m。");

    static MonitoringDrawingFieldCandidate HighConfidenceField(
        string fieldName,
        string displayName,
        double value) => new()
    {
        FieldName = fieldName,
        DisplayName = displayName,
        Value = value,
        Unit = "m",
        Confidence = 0.96,
        IsSelected = true
    };

    var candidate = new MonitoringDrawingCandidate
    {
        DrawingModel = "H6.5-L3-partial",
        Fields =
        [
            HighConfidenceField(MonitoringDrawingFieldNames.PoleHeight, "立杆高度", 6.5),
            HighConfidenceField(MonitoringDrawingFieldNames.PoleBottomDimension, "立杆下端尺寸", 0.24),
            HighConfidenceField(MonitoringDrawingFieldNames.PoleWallThickness, "立杆壁厚", 0.005),
            new MonitoringDrawingFieldCandidate
            {
                FieldName = MonitoringDrawingFieldNames.ArmMountingHeight,
                DisplayName = "横杆安装高度",
                Value = null,
                Unit = "m",
                IsSelected = false
            }
        ]
    };

    var result = MonitoringDrawingCandidateApplicator.Apply(project, candidate);
    Assert(result.AppliedFieldCount == 2,
        "部分候选中与保留值冲突的立杆高度应被拦截，其余安全字段仍应采用。");
    AssertClose(project.MonitoringPole.PoleHeightM, 8, 1e-12,
        "候选立杆高度小于保留的横杆安装高度时不得写入并留下不可计算状态。");
    AssertClose(project.MonitoringPole.ArmMountingHeightM, 7, 1e-12,
        "图纸未给横杆安装高度时必须保留原值。");
    Assert(result.Messages.Any(message =>
            message.Contains("立杆高度未采用", StringComparison.Ordinal) &&
            message.Contains("横杆安装高度", StringComparison.Ordinal)),
        "采用结果应明确说明立杆高度与保留横杆安装高度冲突。");
    Assert(!new MonitoringPoleLoadCalculator().Validate(project.MonitoringPole).Any(issue =>
            issue.Message.Contains("横杆安装高度", StringComparison.Ordinal)),
        "部分候选采用后必须仍能通过横杆安装高度校验。");
    return Task.CompletedTask;
}

static Task TestMonitoringDrawingExplicitInputGate()
{
    var project = new ProjectModel();
    project.MonitoringPole.RequireExplicitDrawingInputs = true;
    project.MonitoringPole.ExplicitDrawingInputFields.Clear();
    var calculator = new MonitoringPoleLoadCalculator();
    var initialIssues = calculator.Validate(project.MonitoringPole);
    Assert(initialIssues.Count(issue =>
            issue.Message.Contains("不得采用默认值", StringComparison.Ordinal)) == 12,
        "新建监控杆项目的12个图纸参数均应保持未提供状态，不能把样例数值当成用户输入。" );

    var calculationBlocked = false;
    try
    {
        calculator.Calculate(project.MonitoringPole);
    }
    catch (ArgumentException exception)
    {
        calculationBlocked = exception.Message.Contains("横杆安装高度尚未", StringComparison.Ordinal) &&
                             exception.Message.Contains("设备重量尚未", StringComparison.Ordinal);
    }
    Assert(calculationBlocked, "未补齐AI缺失项时必须阻止进入正式荷载计算。" );

    var candidate = new MonitoringDrawingCandidate
    {
        DrawingModel = "H6.5-L3-explicit",
        Fields =
        [
            new MonitoringDrawingFieldCandidate
            {
                FieldName = MonitoringDrawingFieldNames.PoleHeight,
                DisplayName = "立杆高度",
                Value = 6.5,
                Unit = "m",
                Confidence = 0.98,
                IsSelected = true
            },
            new MonitoringDrawingFieldCandidate
            {
                FieldName = MonitoringDrawingFieldNames.ArmMountingHeight,
                DisplayName = "横杆安装高度",
                Value = null,
                Unit = "m"
            }
        ]
    };
    var applied = MonitoringDrawingCandidateApplicator.Apply(project, candidate);
    Assert(applied.AppliedFieldCount == 1 &&
           Math.Abs(project.MonitoringPole.PoleHeightM - 6.5) < 1e-12,
        "显式输入模式下，未识别的安装高度不得用隐藏样例值反向否决已可靠识别的立杆高度。" );
    Assert(project.MonitoringPole.ExplicitDrawingInputFields.Contains(
            MonitoringDrawingFieldNames.PoleHeight) &&
           !project.MonitoringPole.ExplicitDrawingInputFields.Contains(
               MonitoringDrawingFieldNames.ArmMountingHeight),
        "候选采用后只能标记实际写入字段，缺失字段必须留给二次补录。" );
    Assert(candidate.Fields[1].ValueDisplay == "图纸未给，待人工补录",
        "缺失字段应明确显示待人工补录，不再声称保留默认值。" );

    project.MonitoringPole.ArmMountingHeightM = 6.2;
    project.MonitoringPole.ExplicitDrawingInputFields.UnionWith(
    [
        MonitoringDrawingFieldNames.PoleBottomDimension,
        MonitoringDrawingFieldNames.PoleTopDimension,
        MonitoringDrawingFieldNames.PoleWallThickness,
        MonitoringDrawingFieldNames.ArmMountingHeight,
        MonitoringDrawingFieldNames.ArmLength,
        MonitoringDrawingFieldNames.ArmNearDimension,
        MonitoringDrawingFieldNames.ArmFarDimension,
        MonitoringDrawingFieldNames.ArmWallThickness,
        MonitoringDrawingFieldNames.ArmCount,
        MonitoringDrawingFieldNames.AttachmentProjectedArea,
        MonitoringDrawingFieldNames.AttachmentWeight
    ]);
    Assert(!calculator.Validate(project.MonitoringPole).Any(issue =>
            issue.Message.Contains("不得采用默认值", StringComparison.Ordinal)),
        "二次人工补录并标记来源后，应解除缺失字段门禁。" );
    calculator.Calculate(project.MonitoringPole);
    return Task.CompletedTask;
}

static Task TestMonitoringDrawingRecognitionHistory()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.MonitoringDrawingHistory.Tests",
        Guid.NewGuid().ToString("N"));
    var settingsDirectory = Path.Combine(root, "settings");
    var firstHistoryDirectory = Path.Combine(root, "history-a");
    var secondHistoryDirectory = Path.Combine(root, "history-b");
    Directory.CreateDirectory(settingsDirectory);

    try
    {
        var settingsService = new LocalApplicationSettingsService(settingsDirectory);
        var settings = settingsService.Load();
        settings.DefaultMonitoringDrawingHistoryDirectory = firstHistoryDirectory;
        settingsService.Save(settings);
        var history = new LocalMonitoringDrawingRecognitionHistoryService(settingsService);
        var candidate = new MonitoringDrawingCandidate
        {
            SourceFileName = "H6.5-L14.pdf",
            SourceFileSha256 = new string('a', 64),
            PageNumber = 1,
            DrawingModel = "H6.5-L14",
            VisionModel = "qwen3.7-plus",
            RecognizedAt = DateTimeOffset.UtcNow,
            EvidenceSummary = "八角对角(110-195-280)×(4+6)×14000",
            Warnings = ["设备迎风面积和重量图纸未给"],
            ArmSegments =
            [
                new MonitoringPoleArmSegment
                {
                    LengthM = 7,
                    NearDimensionM = 0.28,
                    FarDimensionM = 0.195,
                    WallThicknessM = 0.006
                },
                new MonitoringPoleArmSegment
                {
                    LengthM = 7,
                    NearDimensionM = 0.195,
                    FarDimensionM = 0.11,
                    WallThicknessM = 0.004
                }
            ],
            Fields =
            [
                new MonitoringDrawingFieldCandidate
                {
                    FieldName = MonitoringDrawingFieldNames.ArmLength,
                    DisplayName = "横杆长度",
                    Value = 14,
                    Unit = "m",
                    RawAnnotation = "×14000",
                    Region = "横杆规格标注",
                    PageNumber = 1,
                    Confidence = 0.98,
                    IsSelected = true
                }
            ]
        };

        history.Save([candidate]);
        var historyPath = Path.Combine(
            firstHistoryDirectory,
            "monitoring-drawing-recognition-history.json");
        Assert(File.Exists(historyPath), "视觉识别完成后必须立即生成独立本机记录文件。" );
        var savedText = File.ReadAllText(historyPath, Encoding.UTF8);
        Assert(savedText.Contains("H6.5-L14", StringComparison.Ordinal) &&
               savedText.Contains("RawAnnotation", StringComparison.Ordinal) &&
               savedText.Contains("WallThicknessM", StringComparison.Ordinal) &&
               !savedText.Contains("apiKey", StringComparison.OrdinalIgnoreCase),
            "记录必须保留模型、证据和分段横杆，且不得包含API密钥。" );

        var restored = new LocalMonitoringDrawingRecognitionHistoryService(settingsService)
            .FindBySourceHash(candidate.SourceFileSha256);
        Assert(restored.Count == 1 &&
               restored[0].Fields.Single().RawAnnotation == "×14000" &&
               restored[0].ArmSegments.Count == 2,
            "软件重启后必须能按原PDF哈希恢复完整候选和14m分段信息。" );

        history.MarkApplied(candidate.Id);
        Assert(history.Load().Single().AppliedAt.HasValue,
            "采用候选后应在独立识别记录中保存采用时间。" );

        settings = settingsService.Load();
        settings.DefaultMonitoringDrawingHistoryDirectory = secondHistoryDirectory;
        settingsService.Save(settings);
        var migrated = history.Load();
        Assert(migrated.Count == 1 &&
               File.Exists(Path.Combine(
                   secondHistoryDirectory,
                   "monitoring-drawing-recognition-history.json")),
            "修改默认记录目录后必须自动合并旧记录，不能因切换路径丢失。" );
        return Task.CompletedTask;
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static MonitoringPoleInput CreateSegmentedOctagonalMonitoringPole() => new()
{
    BasicWindPressureKpa = 0.55,
    WindVibrationFactor = 1,
    ShapeCoefficient = 1,
    TerrainHeightFactor = 1,
    PoleHeightM = 6.5,
    PoleBottomDiameterM = 0.34,
    PoleTopDiameterM = 0.28,
    PoleWallThicknessM = 0.010,
    PoleSectionType = TubeSectionType.RegularOctagonDiagonalTube,
    ArmMountingHeightM = 6.5,
    ArmLengthM = 14,
    ArmNearDiameterM = 0.28,
    ArmFarDiameterM = 0.11,
    ArmWallThicknessM = 0.006,
    ArmSectionType = TubeSectionType.RegularOctagonDiagonalTube,
    ArmCount = 1,
    AttachmentProjectedAreaM2 = 0,
    AttachmentWeightKn = 0,
    ArmSegments =
    [
        new MonitoringPoleArmSegment
        {
            LengthM = 7,
            NearDimensionM = 0.28,
            FarDimensionM = 0.195,
            WallThicknessM = 0.006
        },
        new MonitoringPoleArmSegment
        {
            LengthM = 7,
            NearDimensionM = 0.195,
            FarDimensionM = 0.11,
            WallThicknessM = 0.004
        }
    ]
};

static Task TestRectangularClosedStirrupCutLength()
{
    var seismic = RebarCutLengthCalculator.CalculateRectangularClosedStirrup(
        2.0,
        1.6,
        50,
        10,
        useSeismicDetailing: true);
    AssertClose(seismic.BodyPerimeterM, 6.80, 1e-9,
        "矩形封闭箍箍身应按构件外包尺寸扣两侧保护层计算。" );
    AssertClose(seismic.HookBendAllowanceM, 0.038, 1e-9,
        "两端135度弯钩量度增量应按2×1.9d计入。" );
    AssertClose(seismic.HookStraightAllowanceM, 0.20, 1e-9,
        "抗震Φ10封闭箍两端平直段应按2×max(10d,75mm)计入。" );
    Assert(seismic.FormulaDescription.Contains("22G101-3第2-7页", StringComparison.Ordinal) &&
           seismic.FormulaDescription.Contains("抗震构造", StringComparison.Ordinal),
        "下料公式必须显示22G101-3页码和采用的抗震构造分支。" );

    var nonSeismic = RebarCutLengthCalculator.CalculateRectangularClosedStirrup(
        2.0,
        1.6,
        50,
        10,
        useSeismicDetailing: false);
    AssertClose(nonSeismic.HookStraightAllowanceM, 0.10, 1e-9,
        "非抗震且不受扭时两端平直段应按2×5d计入。" );
    AssertClose(nonSeismic.TotalCutLengthM, 6.938, 1e-9,
        "非抗震矩形封闭箍下料长度应包含箍身、135度量度差和两端5d平直段。" );

    var torsional = RebarCutLengthCalculator.CalculateRectangularClosedStirrup(
        2.0,
        1.6,
        50,
        10,
        useSeismicDetailing: false,
        subjectToTorsion: true);
    AssertClose(torsional.HookStraightAllowanceM, 0.20, 1e-9,
        "非抗震基础构件受扭时两端平直段应按2×10d计入。" );
    Assert(RebarCutLengthCalculator.ShouldUseSeismicDetailing(0) &&
           RebarCutLengthCalculator.ShouldUseSeismicDetailing(6) &&
           !RebarCutLengthCalculator.ShouldUseSeismicDetailing(5),
        "设防烈度未知时应保守采用抗震构造，6度及以上采用抗震构造。" );
    return Task.CompletedTask;
}

static Task TestWindMinimumAndTowerLocationIndependence()
{
    var calculator = new MonitoringPoleLoadCalculator();
    var lowWind = new MonitoringPoleInput
    {
        BasicWindPressureKpa = 0.30,
        WindVibrationFactor = 1,
        ShapeCoefficient = 1,
        TerrainHeightFactor = 1
    };
    var result = calculator.Calculate(lowWind);
    AssertClose(
        result.DesignWindPressureKpa,
        0.35,
        1e-10,
        "监控杆基本风压低于0.35 kPa时必须按GB 50135-2019第4.2.1条采用0.35 kPa。");

    var tower = CreateConfirmedProject();
    tower.ProjectType = ProjectType.CommunicationTower;
    tower.Province = string.Empty;
    tower.City = string.Empty;
    tower.County = string.Empty;
    tower.TowerMast.TowerModel = "免选址塔桅测试";
    tower.TowerMast.VerticalKn = 100;
    tower.TowerMast.ShearXKn = 20;
    tower.TowerMast.MomentYKnM = 150;
    tower.TowerMast.IsConfirmed = true;
    var issues = BuildWorkflow().ValidateForDesign(tower);
    Assert(
        issues.All(issue => issue.Field != nameof(tower.City)),
        "通信塔桅基础端反力已包含风作用，不得因未选择城市而阻断设计。" );
    Assert(
        new ProjectReadinessService().Evaluate(tower) != ProjectStage.Created,
        "通信塔桅项目不应被地址完整性门禁退回创建阶段。" );

    var monitoring = CreateConfirmedProject();
    monitoring.City = string.Empty;
    Assert(
        BuildWorkflow().ValidateForDesign(monitoring)
            .Any(issue => issue.Field == nameof(monitoring.City) && issue.IsBlocking),
        "监控杆项目仍必须选择城市以取得基本风压。" );

    var gansuLocation = new ProjectModel
    {
        Province = "甘肃省",
        City = "兰州市",
        County = "城关区"
    };
    var seismicApplication = new LocationSeismicReferenceService()
        .ApplyIfAvailable(gansuLocation);
    Assert(seismicApplication.Applied &&
           gansuLocation.Geotechnical.SeismicIntensityDegree == 8 &&
           Math.Abs(gansuLocation.Geotechnical.DesignBasicGroundAccelerationG - 0.20) < 1e-9 &&
           gansuLocation.Geotechnical.DesignEarthquakeGroup == "第三组",
        "甘肃县区建设地点应自动补齐烈度、基本地震加速度和设计地震分组。" );
    Assert(string.IsNullOrWhiteSpace(gansuLocation.Geotechnical.SiteClass),
        "场地类别不得由建设地点数据库臆造，仍应来自地勘。" );

    var sanheLocation = new ProjectModel
    {
        Province = "河北省",
        City = "廊坊市",
        County = "三河市"
    };
    new LocationSeismicReferenceService().ApplyIfAvailable(sanheLocation);
    Assert(sanheLocation.Geotechnical.SeismicIntensityDegree == 8 &&
           sanheLocation.Geotechnical.DesignEarthquakeGroup == "第二组",
        "燕郊地勘所在三河市应能按建设地点匹配抗震参数。" );
    return Task.CompletedTask;
}

static Task TestFoundationChecks()
{
    var calculator = new RectangularShortColumnFoundationCalculator();
    var load = new FoundationLoad
    {
        VerticalKn = 5,
        ShearXKn = 10,
        MomentYKnM = 80,
        GoverningCase = "测试工况"
    };
    var geotechnical = new GeotechnicalInput
    {
        BearingCapacityKpa = 150,
        SoilUnitWeightKnPerM3 = 18,
        BaseFrictionCoefficient = 0.30,
        IsConfirmed = true
    };
    var settings = new FoundationDesignSettings();

    var small = calculator.Calculate(
        new FoundationGeometry
        {
            BaseLengthM = 1.6,
            BaseWidthM = 1.6,
            BaseThicknessM = 0.6,
            PedestalLengthM = 0.8,
            PedestalWidthM = 0.8,
            PedestalHeightM = 1.2
        },
        load,
        geotechnical,
        settings);
    var large = calculator.Calculate(
        new FoundationGeometry
        {
            BaseLengthM = 4.0,
            BaseWidthM = 3.0,
            BaseThicknessM = 1.0,
            PedestalLengthM = 0.8,
            PedestalWidthM = 0.8,
            PedestalHeightM = 1.2
        },
        load,
        geotechnical,
        settings);

    Assert(!small.IsFeasible, "过小基础应存在不满足项。");
    Assert(large.Checks.Single(check => check.Code == "BEARING_AVERAGE").Status == CheckStatus.Pass, "较大基础平均压力应通过承载力。");
    Assert(large.Checks.Single(check => check.Code == "BEARING_MAX").Status == CheckStatus.Pass, "较大基础最大压力应通过1.2fa限值。");
    AssertClose(large.Quantities.ConcreteM3, 12.768, 0.000001,
        "矩形柱金标准混凝土量发生漂移。");
    AssertClose(
        large.Checks.Single(check => check.Code == "BEARING_AVERAGE").Demand,
        47.4646666667,
        0.000001,
        "矩形柱金标准平均基底压力发生漂移。");
    Assert(large.Quantities.ConcreteM3 > small.Quantities.ConcreteM3, "较大基础混凝土量应更大。");
    return Task.CompletedTask;
}

static Task TestNormativePartialContact()
{
    var calculator = new RectangularShortColumnFoundationCalculator();
    var geometry = new FoundationGeometry
    {
        BaseLengthM = 4,
        BaseWidthM = 4,
        BaseThicknessM = 1,
        PedestalLengthM = 0.8,
        PedestalWidthM = 0.8,
        PedestalHeightM = 1.2
    };
    var geotechnical = new GeotechnicalInput
    {
        BearingCapacityKpa = 150,
        SoilUnitWeightKnPerM3 = 18,
        BaseFrictionCoefficient = 0.3,
        IsConfirmed = true
    };
    var settings = new FoundationDesignSettings();

    var permitted = calculator.Calculate(
        geometry,
        new FoundationLoad { VerticalKn = 100, MomentYKnM = 700, GoverningCase = "允许脱开" },
        geotechnical,
        settings);
    var excessive = calculator.Calculate(
        geometry,
        new FoundationLoad { VerticalKn = 100, MomentYKnM = 1300, GoverningCase = "过度脱开" },
        geotechnical,
        settings);

    var permittedContact = permitted.Checks.Single(check => check.Code == "CONTACT");
    Assert(permittedContact.Status == CheckStatus.Pass, "脱开面积不超过1/4时不应直接判失败。");
    Assert(permittedContact.Explanation.Contains("部分脱开"), "允许脱开应使用部分接触公式并保留说明。");
    Assert(excessive.Checks.Single(check => check.Code == "CONTACT").Status == CheckStatus.Fail, "超过允许脱开范围必须判失败。");
    AssertClose(
        permitted.Checks.Single(check => check.Code == "BEARING_MAX").Capacity,
        180,
        1e-9,
        "偏心荷载最大压力限值应为1.2fa。");
    return Task.CompletedTask;
}

static Task TestNormativeSlidingSafetyFactor()
{
    var calculator = new RectangularShortColumnFoundationCalculator();
    var scheme = calculator.Calculate(
        new FoundationGeometry
        {
            BaseLengthM = 4,
            BaseWidthM = 4,
            BaseThicknessM = 1,
            PedestalLengthM = 0.8,
            PedestalWidthM = 0.8,
            PedestalHeightM = 1.2
        },
        new FoundationLoad { ShearXKn = 180, GoverningCase = "抗滑测试" },
        new GeotechnicalInput
        {
            BearingCapacityKpa = 300,
            SoilUnitWeightKnPerM3 = 18,
            BaseFrictionCoefficient = 0.3,
            IsConfirmed = true
        },
        new FoundationDesignSettings { RequiredSlidingSafetyFactor = 1.5 });

    var sliding = scheme.Checks.Single(check => check.Code == "SLIDING");
    Assert(sliding.Status == CheckStatus.Fail, "摩擦抗力虽大于水平力但安全系数不足1.5时应判失败。");
    Assert(sliding.RuleReference.Contains("7.4.6"), "抗滑结果应保留规范公式编号。");
    return Task.CompletedTask;
}

static Task TestThreeStrategyOptimization()
{
    var poleCalculator = new MonitoringPoleLoadCalculator();
    var foundationCalculator = new RectangularShortColumnFoundationCalculator();
    var optimizer = new ThreeStrategyFoundationOptimizer(foundationCalculator);
    var load = poleCalculator.Calculate(new MonitoringPoleInput()).FoundationLoad;
    var geotechnical = new GeotechnicalInput { IsConfirmed = true };
    var settings = new FoundationDesignSettings();

    var schemes = optimizer.Optimize(load, geotechnical, settings);

    Assert(schemes.Count == 3, "应返回三种方案。");
    Assert(schemes.All(scheme => scheme.IsFeasible), "推荐方案必须全部可行。");
    Assert(schemes.Select(scheme => scheme.Preference).Distinct().Count() == 3, "应包含三种不同优化策略。");
    Assert(schemes.Select(scheme => scheme.Id).Distinct().Count() == 3, "每个方案必须有独立标识。");
    return Task.CompletedTask;
}

static Task TestActionableOptimizationFailure()
{
    var optimizer = new ThreeStrategyFoundationOptimizer(
        new RectangularShortColumnFoundationCalculator());
    var geotechnical = new GeotechnicalInput
    {
        BearingCapacityKpa = 80,
        SoilUnitWeightKnPerM3 = 18,
        BaseFrictionCoefficient = 0.25,
        GroundwaterDepthM = 10,
        IsConfirmed = true
    };

    var shallowSettings = new FoundationDesignSettings
    {
        MinimumBaseLengthM = 1.60,
        MaximumBaseLengthM = 1.60,
        MinimumBaseWidthM = 1.60,
        MaximumBaseWidthM = 1.60,
        MinimumBaseThicknessM = 0.60,
        MaximumBaseThicknessM = 0.60,
        DimensionStepM = 0.20
    };
    var shallowFailure = CaptureOptimizationFailure(() => optimizer.Optimize(
        new FoundationLoad
        {
            VerticalKn = 80,
            ShearXKn = 120,
            MomentYKnM = 2_500,
            GoverningCase = "定量诊断测试"
        },
        geotechnical,
        shallowSettings));

    Assert(shallowFailure.Message.Contains("卡住的校核项", StringComparison.Ordinal),
        "无可行方案提示应列出实际失败校核项。");
    Assert(shallowFailure.Message.Contains("“底板长范围（m）”右侧上限由1.60 m调至2.00 m", StringComparison.Ordinal) &&
           shallowFailure.Message.Contains("“底板宽范围（m）”右侧上限由1.60 m调至2.00 m", StringComparison.Ordinal),
        "浅基础失败提示应使用界面原字段名并给出右侧上限的当前值和建议值。");
    Assert(shallowFailure.Message.Contains("“底板厚范围（m）”右侧上限", StringComparison.Ordinal),
        "浅基础冲切、受剪或受弯适用性失败时应指出底板厚度上限。");

    var pileSettings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.Pile,
        Pile = new PileFoundationSettings
        {
            MinimumPileDiameterM = 0.80,
            MaximumPileDiameterM = 0.80,
            PileDiameterStepM = 0.20,
            MinimumPileLengthM = 8,
            MaximumPileLengthM = 8,
            PileLengthStepM = 2,
            SinglePileHorizontalCapacityKn = 10_000,
            PileMainBarCount = 100,
            IsConfirmed = true
        }
    };
    var pileFailure = CaptureOptimizationFailure(() => optimizer.Optimize(
        new FoundationLoad
        {
            VerticalKn = 5_000,
            GoverningCase = "灌注桩定量诊断测试"
        },
        geotechnical,
        pileSettings));

    Assert(pileFailure.Message.Contains("“桩长搜索范围（m）”右侧上限", StringComparison.Ordinal) &&
           pileFailure.Message.Contains("8.00 m", StringComparison.Ordinal) &&
           pileFailure.Message.Contains("12.00 m", StringComparison.Ordinal),
        "灌注桩抗压不足时应给出最大桩长的当前值和建议值。");
    Assert(pileFailure.Message.Contains("“桩径搜索范围（m）”右侧上限", StringComparison.Ordinal),
        "灌注桩承载力不足时应同时给出桩径上限建议。");

    var rigidSettings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.RigidRectangularShortPile
    };
    rigidSettings.RigidShortPile.IsConfirmed = true;
    rigidSettings.RigidShortPile.MinimumRectangularLengthM = 1.40;
    rigidSettings.RigidShortPile.MaximumRectangularLengthM = 2.40;
    rigidSettings.RigidShortPile.RectangularLengthStepM = 0.20;
    rigidSettings.RigidShortPile.MinimumRectangularWidthM = 1.40;
    rigidSettings.RigidShortPile.MaximumRectangularWidthM = 2.40;
    rigidSettings.RigidShortPile.RectangularWidthStepM = 0.20;
    rigidSettings.RigidShortPile.MinimumEmbeddedDepthM = 5;
    rigidSettings.RigidShortPile.MaximumEmbeddedDepthM = 10;
    rigidSettings.RigidShortPile.EmbeddedDepthStepM = 1;
    rigidSettings.RigidShortPile.LongitudinalBarCount = 36;
    rigidSettings.RigidShortPile.LongitudinalBarDiameterMm = 22;
    rigidSettings.RigidShortPile.SoilLayers =
    [
        new RigidShortPileSoilLayerInput
        {
            Name = "主要影响层",
            ThicknessM = 12,
            HorizontalResistanceCoefficientMnPerM4 = 12
        }
    ];
    var rigidFailure = CaptureOptimizationFailure(() => optimizer.Optimize(
        new FoundationLoad
        {
            VerticalKn = 82.60,
            ShearXKn = 60.40,
            MomentYKnM = 20_000,
            GoverningCase = "矩形刚性短柱桩界面字段诊断测试"
        },
        geotechnical,
        rigidSettings));
    Assert(
        rigidFailure.Message.Contains("“矩形X向边长范围（m）”右侧上限", StringComparison.Ordinal) &&
        rigidFailure.Message.Contains("“矩形Y向边长范围（m）”右侧上限", StringComparison.Ordinal) &&
        !rigidFailure.Message.Contains("最大矩形截面长/宽", StringComparison.Ordinal),
        "矩形刚性短柱桩失败提示必须与高级设计参数中的X、Y向字段逐字一致。" );

    var invalidRange = new FoundationDesignSettings
    {
        MinimumBaseThicknessM = 1.20,
        MaximumBaseThicknessM = 0.80
    };
    var rangeFailure = CaptureOptimizationFailure(() => optimizer.Optimize(
        new FoundationLoad { VerticalKn = 100 },
        geotechnical,
        invalidRange));
    Assert(rangeFailure.Message.Contains("“底板厚范围（m）”左侧下限", StringComparison.Ordinal) &&
           rangeFailure.Message.Contains("右侧上限", StringComparison.Ordinal) &&
           rangeFailure.Message.Contains("1.20 m", StringComparison.Ordinal),
        "尺寸范围错误应指出具体字段、当前值和最低修改值。");

    return Task.CompletedTask;

    static FoundationOptimizationException CaptureOptimizationFailure(Action action)
    {
        try
        {
            action();
        }
        catch (FoundationOptimizationException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("测试场景应触发可操作的方案搜索失败诊断。");
    }
}

static Task TestGeotechnicalAnalysisHistory()
{
    var dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.GeotechnicalHistory.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dataDirectory);
    try
    {
        var service = new LocalGeotechnicalAnalysisHistoryService(dataDirectory);
        var first = service.Save(new GeotechnicalAnalysisRecord
        {
            CreatedAt = new DateTimeOffset(2026, 8, 9, 8, 0, 0, TimeSpan.Zero),
            SourceFilePath = @"G:\地勘\甲项目.pdf",
            SourceName = "甲项目.pdf",
            AnalysisMethod = GeotechnicalAnalysisMethod.VisualPdfAi,
            FoundationType = FoundationType.RectangularShortColumn,
            ProviderDisplay = "百炼视觉",
            Model = "qwen3.7-plus",
            AiSourceType = ParameterSourceType.VisualAi,
            EvidencePaneTitle = "视觉模型逐页证据摘录",
            DocumentContent = "第18页：地基承载力特征值fak=150kPa。",
            AiResult = new GeotechnicalAiExtractionResult
            {
                BearingCapacityKpa = 150,
                EvidencePages = [18],
                Evidence = "第18页地基土参数表",
                Confidence = 0.92
            },
            PageCount = 40,
            ProcessedPageCount = 12
        });
        var second = service.Save(new GeotechnicalAnalysisRecord
        {
            CreatedAt = new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero),
            SourceFilePath = @"G:\地勘\乙项目.docx",
            SourceName = "乙项目.docx",
            AnalysisMethod = GeotechnicalAnalysisMethod.WordTextAi,
            FoundationType = FoundationType.Pile,
            ProviderDisplay = "DeepSeek",
            Model = "deepseek-v4-pro",
            DocumentContent = "桩基参数表",
            AiResult = new GeotechnicalAiExtractionResult
            {
                GroundwaterDepthM = 5,
                Confidence = 0.85
            }
        });

        var loaded = service.Load();
        Assert(loaded.Count == 2 && loaded[0].Id == second.Id && loaded[1].Id == first.Id,
            "地勘分析记录应按时间倒序保存并可跨会话读取。" );
        Assert(loaded[1].AiResult?.BearingCapacityKpa == 150 &&
               loaded[1].DocumentContent.Contains("fak=150", StringComparison.Ordinal) &&
               loaded[1].DisplayText.Contains("视觉AI", StringComparison.Ordinal),
            "历史记录必须保留结构化候选、证据原文和可读分析方式。" );

        service.MarkApplied(first.Id);
        var applied = service.Load().Single(item => item.Id == first.Id);
        Assert(applied.WasApplied && applied.UsageCount == 1 && applied.LastUsedAt is not null,
            "引用历史地勘结果后应记录采用状态和使用次数。" );

        for (var index = 0; index < 45; index++)
        {
            service.Save(new GeotechnicalAnalysisRecord
            {
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(index),
                SourceName = $"容量测试-{index}.pdf",
                AnalysisMethod = GeotechnicalAnalysisMethod.PdfOcrOnly,
                FoundationType = FoundationType.RectangularShortColumn
            });
        }
        var fullEvidence = new string('证', 210_000);
        var fullEvidenceRecord = service.Save(new GeotechnicalAnalysisRecord
        {
            CreatedAt = DateTimeOffset.UtcNow.AddHours(2),
            SourceName = "完整证据保存测试.pdf",
            AnalysisMethod = GeotechnicalAnalysisMethod.VisualPdfAi,
            FoundationType = FoundationType.RectangularShortColumn,
            DocumentContent = fullEvidence
        });
        var unlimitedHistory = service.Load();
        Assert(unlimitedHistory.Count == 48,
            "本机地勘历史不得按数量自动清理，除非用户主动删除，否则应一直保留。" );
        Assert(unlimitedHistory.Single(item => item.Id == fullEvidenceRecord.Id)
                   .DocumentContent.Length == fullEvidence.Length,
            "地勘分析原文和证据不得按固定长度静默截断。" );
        Assert(service.Delete(first.Id) && service.Load().All(item => item.Id != first.Id),
            "地勘历史应支持用户明确选择后主动删除单条记录。" );
        Assert(!service.Delete(first.Id),
            "重复删除不存在的地勘记录应安全返回，不得误删其他记录。" );

        File.WriteAllText(
            Path.Combine(dataDirectory, "geotechnical-analysis-history.json"),
            "{损坏的JSON",
            Encoding.UTF8);
        Assert(service.Load().Count == 0,
            "历史文件损坏时应安全回到空记录，不能阻断基础计算。" );
    }
    finally
    {
        Directory.Delete(dataDirectory, recursive: true);
    }

    return Task.CompletedTask;
}

static Task TestOfflineWorkflow()
{
    var service = BuildWorkflow();
    var project = CreateConfirmedProject();

    var issues = service.ValidateForDesign(project);
    Assert(issues.Count == 0, "完整手工输入项目不应存在阻断问题。");

    var schemes = service.GenerateSchemes(project);
    service.SelectScheme(project, schemes[0].Id);

    Assert(project.Stage == ProjectStage.SchemeSelected, "存在待补参数或专项复核时，选择可行尺寸后仍应保持复核稿状态。");
    Assert(!schemes[0].IsFormalVerificationComplete, "缺少专项允许值时不得标记为全部验算完成。");
    Assert(project.AuditTrail.Count >= 3, "离线流程应保留审计记录。");
    return Task.CompletedTask;
}

static Task TestTowerManualLoadWorkflow()
{
    var service = BuildWorkflow();
    var project = CreateConfirmedProject();
    project.Name = "三管塔基础测试项目";
    project.ProjectType = ProjectType.CommunicationTower;
    project.TowerMast = new TowerMastInput
    {
        TowerModel = "测试三管塔",
        StructureType = TowerStructureType.ThreeTube,
        LoadSourceType = TowerLoadSourceType.Manual,
        HeightM = 35,
        UsesIndividualPileReactions = true,
        IndividualPileCompressionKn = 180,
        IndividualPileUpliftKn = 150,
        IndividualPileHorizontalKn = 24,
        IsConfirmed = true
    };

    var issues = service.ValidateForDesign(project);
    Assert(issues.Count == 0, "完整塔桅手工荷载项目不应存在阻断问题。");

    var schemes = service.GenerateSchemes(project);
    Assert(schemes.Count == 3, "塔桅流程应生成三种方案。");
    Assert(project.FoundationLoad.FoundationUnitCount == 3, "三管塔独立基础应形成3个基础单元。");
    AssertClose(project.FoundationLoad.VerticalKn, 180, 1e-9, "单塔脚压力应传递到每个基础单元。");
    AssertClose(project.FoundationLoad.IndividualPileUpliftKn, 150, 1e-9, "单塔脚上拔包络不得丢失。");
    AssertClose(project.FoundationLoad.MomentYKnM, 0, 1e-9, "图集未给单塔脚弯矩时不得套用整塔弯矩。");
    Assert(project.AuditTrail.Any(item => item.Action.Contains("塔桅")), "塔桅流程应记录荷载来源。");
    return Task.CompletedTask;
}

static Task TestAdjustmentAdvice()
{
    var service = BuildWorkflow();
    var project = CreateConfirmedProject();
    service.GenerateSchemes(project);

    var small = service.EvaluateCustomScheme(
        project,
        new FoundationGeometry
        {
            BaseLengthM = 0.8,
            BaseWidthM = 0.8,
            BaseThicknessM = 0.3,
            PedestalLengthM = 0.8,
            PedestalWidthM = 0.8,
            PedestalHeightM = 1.2
        });
    var advice = service.GetAdjustmentAdvice(project, small);

    Assert(!small.IsFeasible, "刻意减小的基础应不满足。");
    Assert(advice.Count > 0, "不满足方案应返回调整建议。");
    Assert(advice.Any(item => item.Title.Contains("最近可行尺寸")), "调整建议应包含定量的最近可行尺寸。");
    return Task.CompletedTask;
}

static async Task TestPrototypeOutputPackage()
{
    var workflow = BuildWorkflow();
    var project = CreateConfirmedProject();
    var schemes = workflow.GenerateSchemes(project);
    workflow.SelectScheme(project, schemes[0].Id);

    var parentDirectory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.Output.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(parentDirectory);

    try
    {
        var outputService = new PrototypeOutputPackageService();
        var result = await outputService.ExportPrototypePackageAsync(
            project,
            parentDirectory);
        CopyOutputSampleIfRequested(result, "shallow");

        var hasBundledCadFonts = typeof(PrototypeOutputPackageService).Assembly
            .GetManifestResourceNames()
            .Contains(
                "TowerFoundation.Infrastructure.CadFonts.simplex.shx",
                StringComparer.Ordinal) &&
            typeof(PrototypeOutputPackageService).Assembly
                .GetManifestResourceNames()
                .Contains(
                    "TowerFoundation.Infrastructure.CadFonts.gbcbig.shx",
                    StringComparer.Ordinal);
        Assert(
            result.Files.Count == (hasBundledCadFonts ? 9 : 7),
            "成果包应包含Word计算书、材料表、工程量、配筋DXF、下料表、DWG转换脚本和manifest；仅在资源获准随包分发时附带两种SHX字体。" );
        Assert(result.Files.All(File.Exists), "成果包文件应全部存在。");
        if (hasBundledCadFonts)
        {
            Assert(result.Files.Any(path => path.EndsWith("simplex.shx", StringComparison.OrdinalIgnoreCase)) &&
                   result.Files.Any(path => path.EndsWith("gbcbig.shx", StringComparison.OrdinalIgnoreCase)),
                "CAD成果目录必须随图提供_TCH_DIM组合字体所需的simplex.shx与gbcbig.shx。" );
            Assert(new FileInfo(result.Files.Single(path => path.EndsWith("simplex.shx", StringComparison.OrdinalIgnoreCase))).Length == 17776 &&
                   new FileInfo(result.Files.Single(path => path.EndsWith("gbcbig.shx", StringComparison.OrdinalIgnoreCase))).Length == 902841,
                "导出的SHX字体必须与用户指定并内置的组合字体资源一致。" );
        }
        else
        {
            Assert(
                !result.Files.Any(path => path.EndsWith(".shx", StringComparison.OrdinalIgnoreCase)),
                "未获再分发许可时，公开源码成果包不得伪造或夹带SHX字体。" );
        }
        var calculationBook = result.Files.Single(path => path.EndsWith(".docx"));
        using (var archive = ZipFile.OpenRead(calculationBook))
        {
            var documentEntry = archive.GetEntry("word/document.xml") ??
                                throw new InvalidOperationException("Word计算书缺少document.xml。");
            using var reader = new StreamReader(documentEntry.Open(), Encoding.UTF8);
            var documentXml = await reader.ReadToEndAsync();
            _ = System.Xml.Linq.XDocument.Parse(documentXml);
            Assert(documentXml.Contains("配筋计算结果与实配"), "Word计算书必须包含配筋计算与实配章节。");
            Assert(
                documentXml.Contains("标准组合：用于地基承载力", StringComparison.Ordinal) &&
                documentXml.Contains("基本组合：用于基础高度", StringComparison.Ordinal) &&
                documentXml.Contains("监控杆承载能力极限状态基本组合", StringComparison.Ordinal),
                "Word计算书必须分别列出标准组合与基本组合及其验算用途。" );
            Assert(documentXml.Contains("荷载组合生成与采用轨迹", StringComparison.Ordinal),
                "Word计算书必须列出荷载组合表达式、来源和确认状态。" );
            Assert(documentXml.Contains("结构安全详细验算（计算校核明细）"), "Word计算书必须包含逐项结构安全验算过程。");
            Assert(documentXml.Contains("抗拔/抗浮验算及荷载适用性"), "浅基础计算书必须说明抗拔验算及荷载适用性。");
            Assert(documentXml.Contains("pmax,min = pavg"), "浅基础计算书必须包含基底压力及抗倾覆公式。");
            Assert(documentXml.Contains("Hk = √(Vx²+Vy²)"), "浅基础计算书必须包含抗滑抗剪公式。");
            Assert(documentXml.Contains("As,calc = M·10⁶/(0.9fyh0)"), "浅基础计算书必须包含底板配筋公式和过程。");
            Assert(documentXml.Contains("数值代入与计算"), "Word计算书必须逐项给出数值代入过程。");
        }
        var dxfRaw = await File.ReadAllTextAsync(result.Files.Single(path => path.EndsWith(".dxf")));
        var dxf = DecodeDxfUnicodeEscapes(dxfRaw);
        var dxfPairs = ParseDxfPairs(dxfRaw);
        Assert(
            dxfPairs.Any(pair => pair is (1, "AC1009")) &&
            !dxfPairs.Any(pair => pair is (1, "AC1027")) &&
            !dxfPairs.Any(pair => pair.Code == 370) &&
            !dxfPairs.Any(pair => pair.Code == 74) &&
            dxfPairs.Any(pair => pair is (2, "BLOCKS")) &&
            dxfPairs[^1] is (0, "EOF") &&
            dxfRaw.All(character => character <= 0x7F),
            "DXF必须按统一的AutoCAD R12 ASCII契约输出，不能再以AC1027文件头混用R12实体语法。" );
        var textEntityCount = dxfPairs.Count(pair => pair is (0, "TEXT"));
        Assert(textEntityCount > 0 &&
               dxfPairs.Count(pair => pair is (7, "_TCH_DIM")) == textEntityCount &&
               !dxf.Contains("CALCULATION NOTES", StringComparison.OrdinalIgnoreCase) &&
               !dxf.Contains("CHECKS:", StringComparison.OrdinalIgnoreCase),
            "所有可见文字必须采用_TCH_DIM组合字体，图内说明不得夹带英文标题。" );
        Assert(dxf.Contains("REBAR_BOTTOM_X", StringComparison.Ordinal),
            "浅基础DXF必须包含X向底筋图层。");
        Assert(dxf.Contains("$ACADVER", StringComparison.Ordinal) &&
               dxf.Contains("CENTER2", StringComparison.Ordinal) &&
               dxf.Contains("AXIS", StringComparison.Ordinal) &&
               dxf.Contains("DIMENSION", StringComparison.Ordinal) &&
               dxf.Contains("SECTION_MARK", StringComparison.Ordinal) &&
               dxf.Contains("BLINDING", StringComparison.Ordinal) &&
               dxf.Contains("CONCRETE_HATCH", StringComparison.Ordinal) &&
               dxf.Contains("MATERIAL_SCHEDULE", StringComparison.Ordinal) &&
               dxf.Contains("_TCH_DIM", StringComparison.Ordinal) &&
               dxf.Contains("simplex.shx", StringComparison.OrdinalIgnoreCase) &&
               dxf.Contains("gbcbig.shx", StringComparison.OrdinalIgnoreCase) &&
               dxfPairs.Any(pair => pair is (0, "POLYLINE")) &&
               dxfPairs.Any(pair => pair.Code == 40 && pair.Value == "0.024") &&
               dxf.Contains("DJj01", StringComparison.Ordinal) &&
               dxf.Contains("B：X", StringComparison.Ordinal) &&
               dxf.Contains("REBAR_REVEAL_BOUNDARY", StringComparison.Ordinal) &&
               dxf.Contains("独立基础采用平面集中标注", StringComparison.Ordinal) &&
               dxf.Contains("独立基础平面图", StringComparison.Ordinal) &&
               dxf.Contains("1-1剖面图", StringComparison.Ordinal) &&
               dxf.Contains("主要材料及工程量表", StringComparison.Ordinal) &&
               dxf.Contains("混凝土", StringComparison.Ordinal) &&
               dxf.Contains("已计算钢筋", StringComparison.Ordinal) &&
               dxf.Contains("基坑开挖", StringComparison.Ordinal) &&
               dxf.Contains("回填土", StringComparison.Ordinal) &&
               dxf.Contains("垫层/锚栓/附加筋", StringComparison.Ordinal) &&
               dxf.Contains("未计量", StringComparison.Ordinal) &&
               dxf.Contains("制图表达参考22G101-3", StringComparison.Ordinal),
            "浅基础DXF必须形成22G101-3式轴网、尺寸、剖切、垫层、剖面线、钢筋表和主要材料工程量表。" );
        Assert(
            (await File.ReadAllTextAsync(result.Files.Single(path => path.Contains("配筋及材料表"))))
            .Contains("未计量内容"),
            "材料表必须明确列出尚未完成的钢筋计量范围。");
        Assert(
            (await File.ReadAllTextAsync(result.Files.Single(path => path.Contains("工程量"))))
            .Contains("不采用经验含钢量"),
            "工程量表不得再用经验含钢量冒充配筋计算值。");
        Assert(
            (await File.ReadAllTextAsync(result.Files.Single(path => path.Contains("钢筋下料表"))))
            .Contains("弯钩、锚固、搭接", StringComparison.Ordinal),
            "钢筋下料表必须明确计算长度与最终施工下料的边界。" );
        Assert(
            (await File.ReadAllTextAsync(result.Files.Single(path => path.EndsWith(".scr"))))
            .Contains("_.SAVEAS", StringComparison.Ordinal),
            "成果包必须提供本地CAD将DXF转存DWG的脚本。" );
        using (var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                   result.Files.Single(path => path.EndsWith("manifest.json")))))
        {
            Assert(manifest.RootElement.GetProperty("schemaVersion").GetInt32() == 2 &&
                   manifest.RootElement.TryGetProperty("drawing", out _) &&
                   manifest.RootElement.TryGetProperty("combinations", out _),
                "成果清单必须记录企业图签设置和荷载组合追溯。" );
        }
        var keepDirectory = Environment.GetEnvironmentVariable(
            "TOWER_FOUNDATION_EXPORT_SAMPLE");
        if (!string.IsNullOrWhiteSpace(keepDirectory))
        {
            Directory.CreateDirectory(keepDirectory);
            foreach (var file in result.Files)
            {
                File.Copy(
                    file,
                    Path.Combine(keepDirectory, Path.GetFileName(file)),
                    overwrite: true);
            }
        }
    }
    finally
    {
        if (Directory.Exists(parentDirectory))
        {
            Directory.Delete(parentDirectory, recursive: true);
        }
    }
}

static async Task TestJsonRoundTrip()
{
    var repository = new JsonProjectRepository();
    var project = CreateConfirmedProject();
    BuildWorkflow().GenerateSchemes(project);
    project.MonitoringDrawingCandidates.Add(new MonitoringDrawingCandidate
    {
        SourceFileName = "H6.5-L14.pdf",
        SourceFileSha256 = new string('b', 64),
        DrawingModel = "H6.5-L14",
        VisionModel = "qwen3.7-plus",
        EvidenceSummary = "八角对角(110-195-280)×(4+6)×14000，第1页",
        Fields =
        [
            new MonitoringDrawingFieldCandidate
            {
                FieldName = MonitoringDrawingFieldNames.ArmLength,
                DisplayName = "横杆长度",
                Value = 14,
                Unit = "m",
                Confidence = 0.96,
                RawAnnotation = "×14000",
                PageNumber = 1
            }
        ],
        ArmSegments =
        [
            new MonitoringPoleArmSegment
            {
                LengthM = 7,
                NearDimensionM = 0.28,
                FarDimensionM = 0.195,
                WallThicknessM = 0.006
            },
            new MonitoringPoleArmSegment
            {
                LengthM = 7,
                NearDimensionM = 0.195,
                FarDimensionM = 0.11,
                WallThicknessM = 0.004
            }
        ]
    });

    var directory = Path.Combine(Path.GetTempPath(), "TowerFoundation.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "roundtrip.tfdproj.json");

    try
    {
        await repository.SaveAsync(project, path);
        var loaded = await repository.LoadAsync(path);

        Assert(loaded.Id == project.Id, "项目标识应保持一致。");
        Assert(loaded.Schemes.Count == 3, "方案列表应完整保存。");
        Assert(loaded.Geotechnical.IsConfirmed, "地勘确认状态应完整保存。");
        Assert(loaded.SchemaVersion >= 5 &&
               loaded.MonitoringDrawingCandidates.Count == 1 &&
               loaded.MonitoringDrawingCandidates[0].Fields[0].RawAnnotation == "×14000" &&
               loaded.MonitoringDrawingCandidates[0].ArmSegments.Count == 2,
            "监控杆视觉候选、证据、置信度和分段信息必须随项目保存，避免重复识别。" );
        Assert(
            loaded.FoundationLoad.HasExplicitBasicCombination &&
            loaded.FoundationLoad.BasicCombination!.MomentYKnM >
            loaded.FoundationLoad.MomentYKnM,
            "项目往返保存必须完整保留基本组合，不能退化为只有标准组合。");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static async Task TestLocalProjectCatalog()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.Catalog.Tests",
        Guid.NewGuid().ToString("N"));
    var repository = new JsonProjectRepository();
    var catalog = new LocalProjectCatalogService(repository, directory);
    var project = CreateConfirmedProject();
    project.Name = "燕郊通信塔桅基础";
    project.ProjectType = ProjectType.CommunicationTower;
    project.Province = "河北省";
    project.City = "廊坊市";
    project.County = "三河市";

    try
    {
        var firstPath = catalog.CreateDefaultProjectPath(project.Name);
        Assert(
            Path.GetDirectoryName(firstPath) == Path.GetFullPath(directory),
            "普通保存应自动使用软件默认项目目录。");
        Assert(
            Path.GetFileName(firstPath) == "燕郊通信塔桅基础.tjproj",
            "默认文件名应使用项目名称。");

        await repository.SaveAsync(project, firstPath);
        var secondPath = catalog.CreateDefaultProjectPath(project.Name);
        Assert(
            Path.GetFileName(secondPath) == "燕郊通信塔桅基础 (2).tjproj",
            "同名新项目不得静默覆盖已有项目。");

        var entries = await catalog.ListAsync();
        Assert(entries.Count == 1, "项目目录应列出已保存项目。");
        Assert(entries[0].ProjectName == project.Name, "项目列表应读取项目名称。");
        Assert(entries[0].ProjectType == ProjectType.CommunicationTower, "项目列表应读取工程类型。");
        Assert(
            entries[0].Location == "塔脚反力输入 · 无需城市风压",
            "通信塔桅项目列表应提示采用塔脚反力且无需城市风压。" );
        Assert(entries[0].IsReadable, "有效项目应允许双击打开。");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static Task TestSettingsProtection()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.Settings.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    const string secret = "test-key-local-dpapi-1234";
    var projectDirectory = Path.Combine(directory, "custom-projects");
    var exportDirectory = Path.Combine(directory, "custom-exports");
    var historyDirectory = Path.Combine(directory, "custom-geotechnical-history");
    var monitoringHistoryDirectory = Path.Combine(directory, "custom-monitoring-history");

    try
    {
        var service = new LocalApplicationSettingsService(directory);
        service.Save(
            new ApplicationSettings
            {
                AiMode = AiOperatingMode.OnlinePreferred,
                DeepSeekModel = "deepseek-v4-flash",
                DefaultProjectDirectory = projectDirectory,
                DefaultExportDirectory = exportDirectory,
                DefaultGeotechnicalHistoryDirectory = historyDirectory,
                DefaultMonitoringDrawingHistoryDirectory = monitoringHistoryDirectory
            },
            secret);

        var settingsText = File.ReadAllText(Path.Combine(directory, "settings.json"));
        Assert(!settingsText.Contains(secret, StringComparison.Ordinal), "设置文件不得保存明文 API 密钥。");
        Assert(service.GetApiKey() == secret, "当前 Windows 用户应能通过 DPAPI 取回 API 密钥。");

        var loaded = service.Load();
        Assert(loaded.HasApiKey, "读取设置时应识别已保存密钥。");
        Assert(loaded.ApiKeyLastFour == "1234", "界面只应显示密钥末四位。");
        Assert(loaded.AiMode == AiOperatingMode.OnlinePreferred, "默认联网模式应保存为 AI 在线优先。");
        Assert(
            loaded.DefaultProjectDirectory == Path.GetFullPath(projectDirectory) &&
            loaded.DefaultExportDirectory == Path.GetFullPath(exportDirectory) &&
            loaded.DefaultGeotechnicalHistoryDirectory == Path.GetFullPath(historyDirectory) &&
            loaded.DefaultMonitoringDrawingHistoryDirectory == Path.GetFullPath(monitoringHistoryDirectory),
            "项目、成果、地勘分析记录和监控杆识别记录的默认保存位置必须持久化。" );

        var dynamicHistory = new LocalGeotechnicalAnalysisHistoryService(service);
        var persistentRecord = dynamicHistory.Save(new GeotechnicalAnalysisRecord
        {
            CreatedAt = DateTimeOffset.UtcNow,
            SourceName = "设置目录迁移验证.pdf",
            AnalysisMethod = GeotechnicalAnalysisMethod.VisualPdfAi,
            FoundationType = FoundationType.RectangularShortColumn
        });
        var changedHistoryDirectory = Path.Combine(directory, "changed-geotechnical-history");
        loaded.DefaultGeotechnicalHistoryDirectory = changedHistoryDirectory;
        service.Save(loaded);
        Assert(dynamicHistory.Load().Any(item => item.Id == persistentRecord.Id) &&
               File.Exists(Path.Combine(changedHistoryDirectory, "geotechnical-analysis-history.json")),
            "修改地勘记录目录后应自动合并已有记录，新旧记录不能因切换路径丢失。" );

        var dynamicCatalog = new LocalProjectCatalogService(
            new JsonProjectRepository(),
            service);
        Assert(
            dynamicCatalog.ProjectDirectory == Path.GetFullPath(projectDirectory),
            "项目目录服务必须动态读取设置中的默认项目位置。" );
        var changedProjectDirectory = Path.Combine(directory, "changed-projects");
        loaded.DefaultProjectDirectory = changedProjectDirectory;
        service.Save(loaded);
        Assert(
            dynamicCatalog.ProjectDirectory == Path.GetFullPath(changedProjectDirectory),
            "设置变更后，打开和普通保存必须立即切换到新项目目录。" );
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static Task TestLicenseSignatureHierarchy()
{
    const string rootMachine = "TJSM-AAAAA-AAAAA-AAAAA-AAAAA-AAAAA-A";
    const string customerMachine = "TJSM-BBBBB-BBBBB-BBBBB-BBBBB-BBBBB-B";
    var root = LicenseCryptography.GenerateKeyPair();
    var issuer = LicenseCryptography.GenerateKeyPair();
    try
    {
        var rootPublic = ToBase64Url(root.PublicKey);
        var request = LicenseCryptography.CreateIssuerRequest(
            "测试签发员", rootMachine, issuer.PrivateKey, "ISSUER01",
            new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero));
        var certificate = LicenseCryptography.IssueIssuerCertificate(
            request.Token, root.PrivateKey, 366, canIssuePermanent: false,
            new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero));
        var license = LicenseCryptography.IssueCustomerLicense(
            customerMachine, "示例客户", new DateOnly(2026, 8, 13),
            new DateOnly(2027, 8, 13), certificate.Token, issuer.PrivateKey,
            rootPublic);
        var verified = LicenseCryptography.VerifyCustomerLicense(
            license.Token, customerMachine, rootPublic);
        Assert(verified.CustomerName == "示例客户" &&
               verified.ExpiresOn == new DateOnly(2027, 8, 13),
            "根证书、签发员证书和机器绑定客户授权应形成可验证的两级签名链。");

        AssertThrows<LicenseException>(() =>
            LicenseCryptography.VerifyCustomerLicense(
                license.Token,
                "TJSM-CCCCC-CCCCC-CCCCC-CCCCC-CCCCC-C",
                rootPublic),
            "客户授权必须拒绝其他电脑的机器码。");
    }
    finally
    {
        CryptographicOperations.ZeroMemory(root.PrivateKey);
        CryptographicOperations.ZeroMemory(issuer.PrivateKey);
    }
    return Task.CompletedTask;
}

static Task TestClientLicenseAssessment()
{
    var directory = Path.Combine(Path.GetTempPath(), "TowerFoundation.License.Client", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    const string machine = "TJSM-DDDDD-DDDDD-DDDDD-DDDDD-DDDDD-D";
    var root = LicenseCryptography.GenerateKeyPair();
    var issuer = LicenseCryptography.GenerateKeyPair();
    try
    {
        var rootPublic = ToBase64Url(root.PublicKey);
        var request = LicenseCryptography.CreateIssuerRequest("期限签发员", machine, issuer.PrivateKey);
        var certificate = LicenseCryptography.IssueIssuerCertificate(request.Token, root.PrivateKey, 90);
        var token = LicenseCryptography.IssueCustomerLicense(machine, "期限客户",
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            certificate.Token, issuer.PrivateKey, rootPublic).Token;
        var manager = new ClientLicenseManager(new ClientLicenseStore(directory), machine, rootPublic);
        Assert(manager.Assess(new DateOnly(2026, 8, 13)).Status == ClientLicenseStatus.Missing,
            "没有授权文件时必须进入未授权预览状态。");
        Assert(manager.Activate(token, new DateOnly(2026, 8, 13)).IsUsable,
            "合法期限授权应能激活正式功能。");
        Assert(manager.Assess(new DateOnly(2026, 9, 10)).Status == ClientLicenseStatus.Grace,
            "到期后15天内应明确标为宽限期。");
        Assert(manager.Assess(new DateOnly(2026, 9, 20)).Status == ClientLicenseStatus.Expired,
            "超过宽限期必须停止正式功能。");
        _ = manager.Assess(new DateOnly(2026, 8, 20));
        Assert(manager.Assess(new DateOnly(2026, 8, 17)).Status == ClientLicenseStatus.ClockRollback,
            "授权状态必须拦截系统日期明显回退。");
    }
    finally
    {
        CryptographicOperations.ZeroMemory(root.PrivateKey);
        CryptographicOperations.ZeroMemory(issuer.PrivateKey);
        Directory.Delete(directory, true);
    }
    return Task.CompletedTask;
}

static Task TestRootKeyDpapiAndBackup()
{
    var directory = Path.Combine(Path.GetTempPath(), "TowerFoundation.License.Root", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var rootPath = Path.Combine(directory, "root-authority.json");
    var backupPath = Path.Combine(directory, "root-backup.tjzroot");
    try
    {
        var store = new RootKeyStore(rootPath, trustedRootPublicKey: string.Empty);
        var publicKey = store.Create();
        var text = File.ReadAllText(rootPath);
        var originalKey = store.LoadPrivateKey();
        var plaintextBase64 = Convert.ToBase64String(originalKey);
        CryptographicOperations.ZeroMemory(originalKey);
        Assert(!text.Contains(plaintextBase64, StringComparison.Ordinal) &&
               text.Contains("encryptedPrivateKey", StringComparison.Ordinal) &&
               text.Contains("windows-dpapi-current-user", StringComparison.Ordinal),
            "根私钥存储必须使用当前用户DPAPI密文，不能落盘明文私钥字段。");
        store.ExportBackup(backupPath, "root-backup-password-2026");
        var restoredPath = Path.Combine(directory, "restored-root.json");
        var restored = new RootKeyStore(restoredPath, publicKey);
        Assert(restored.ImportBackup(backupPath, "root-backup-password-2026") == publicKey,
            "正确密码应恢复同一根公钥对应的私钥。");
        var key = restored.LoadPrivateKey();
        CryptographicOperations.ZeroMemory(key);
        AssertThrows<LicenseException>(() =>
            new RootKeyStore(Path.Combine(directory, "wrong-root.json"), publicKey)
                .ImportBackup(backupPath, "wrong-password"),
            "错误密码不得恢复根密钥。");
    }
    finally { Directory.Delete(directory, true); }
    return Task.CompletedTask;
}

static Task TestLicenseTamperAndPermissionGate()
{
    const string machine = "TJSM-EEEEE-EEEEE-EEEEE-EEEEE-EEEEE-E";
    var root = LicenseCryptography.GenerateKeyPair();
    var issuer = LicenseCryptography.GenerateKeyPair();
    try
    {
        var rootPublic = ToBase64Url(root.PublicKey);
        var request = LicenseCryptography.CreateIssuerRequest("受限签发员", machine, issuer.PrivateKey);
        var certificate = LicenseCryptography.IssueIssuerCertificate(
            request.Token, root.PrivateKey, maximumCustomerDays: 30, canIssuePermanent: false);
        AssertThrows<LicenseException>(() => LicenseCryptography.IssueCustomerLicense(
                machine, "越界客户", new DateOnly(2026, 8, 13), new DateOnly(2026, 10, 1),
                certificate.Token, issuer.PrivateKey, rootPublic),
            "签发员不得生成超过根证书期限权限的客户授权。");
        AssertThrows<LicenseException>(() => LicenseCryptography.IssueCustomerLicense(
                machine, "永久客户", new DateOnly(2026, 8, 13), null,
                certificate.Token, issuer.PrivateKey, rootPublic),
            "没有永久权限的签发员不得生成永久客户授权。");
        var license = LicenseCryptography.IssueCustomerLicense(machine, "正常客户",
            new DateOnly(2026, 8, 13), new DateOnly(2026, 9, 1),
            certificate.Token, issuer.PrivateKey, rootPublic);
        var tampered = license.Token[..^1] + (license.Token[^1] == 'A' ? 'B' : 'A');
        AssertThrows<LicenseException>(() =>
            LicenseCryptography.VerifyCustomerLicense(tampered, machine, rootPublic),
            "授权码任何字符被篡改都必须验签失败。");
    }
    finally
    {
        CryptographicOperations.ZeroMemory(root.PrivateKey);
        CryptographicOperations.ZeroMemory(issuer.PrivateKey);
    }
    return Task.CompletedTask;
}

static string ToBase64Url(byte[] value) => Convert.ToBase64String(value)
    .TrimEnd('=').Replace('+', '-').Replace('/', '_');

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException(message);
}

static Task TestDeepSeekModelDefaults()
{
    Assert(
        new ApplicationSettings().DeepSeekModel == "deepseek-v4-pro",
        "DeepSeek默认模型应为deepseek-v4-pro。");

    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.Model.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var service = new LocalApplicationSettingsService(directory);
        service.Save(new ApplicationSettings { DeepSeekModel = "unsupported-model" });
        Assert(
            service.Load().DeepSeekModel == "deepseek-v4-pro",
            "不在Pro/Flash白名单中的模型必须回退为deepseek-v4-pro。");

        service.Save(new ApplicationSettings { DeepSeekModel = "deepseek-v4-flash" });
        Assert(
            service.Load().DeepSeekModel == "deepseek-v4-flash",
            "用户应可下拉切换为deepseek-v4-flash。");
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static Task TestVisualAiSettingsAndModelWhitelist()
{
    Assert(
        VisualAiModelCatalog.SupportedModels.SequenceEqual(
            new[]
            {
                "qwen3.7-plus",
                "qwen3.7-plus-2026-05-26",
                "qwen3.6-plus",
                "qwen3.6-flash",
                "qwen3-vl-plus",
                "qwen3-vl-flash"
            }),
        "设置中只能保留可直接识图的视觉理解模型。" );
    Assert(
        VisualAiModelCatalog.SupportedModels.All(model =>
            !model.Contains("qwen-image", StringComparison.OrdinalIgnoreCase) &&
            !model.Contains("wan", StringComparison.OrdinalIgnoreCase)),
        "图片生成和视频生成模型不得出现在地勘识别模型列表中。" );

    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.VisionSettings.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    const string secret = "test-key-fake-vision-7890";
    var csvPath = Path.Combine(directory, "business-space.csv");
    try
    {
        File.WriteAllText(
            Path.Combine(directory, "settings.json"),
            "{\"Settings\":{\"SchemaVersion\":3,\"VisionPagesPerBatch\":4}}",
            new UTF8Encoding(false));
        File.WriteAllText(
            csvPath,
            "id,6569497\napiKey," + secret +
            "\nopenAiCompatible,https://dashscope.aliyuncs.com/compatible-mode/v1\n",
            new UTF8Encoding(false));
        var service = new LocalApplicationSettingsService(directory);
        Assert(service.Load().VisionPagesPerBatch == 2 && service.Load().SchemaVersion == 6,
            "旧版每批4页设置应迁移为每批2页策略，并升级到含监控杆识别记录目录的schema 6。" );
        var imported = service.ImportVisualApiFromCsv(csvPath);
        Assert(imported.Imported, "应能读取业务空间转置CSV并导入视觉API配置。" );
        Assert(service.GetVisionApiKey() == secret, "当前Windows用户应能通过DPAPI取回视觉密钥。" );
        var settingsText = File.ReadAllText(Path.Combine(directory, "settings.json"));
        Assert(!settingsText.Contains(secret, StringComparison.Ordinal), "视觉密钥不得以明文写入设置文件。" );
        Assert(service.Load().VisionModel == VisualAiModelCatalog.DefaultModel,
            "导入业务空间后默认应采用qwen3.7-plus视觉理解模型。" );

        var settings = service.Load();
        settings.VisionModel = "qwen-image-3.0";
        service.Save(settings);
        Assert(service.Load().VisionModel == VisualAiModelCatalog.DefaultModel,
            "不适用于识别的图片生成模型必须在存储层回退，不得保留。" );
        settings = service.Load();
        settings.VisionModel = "qwen3-vl-flash";
        service.Save(settings);
        Assert(service.Load().VisionModel == "qwen3-vl-flash",
            "用户应可切换到白名单内的视觉理解模型。" );
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestVisualPdfAnalysisPipeline()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.VisualPdf.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var pdfPath = Path.Combine(directory, "vision-sample.pdf");
    try
    {
        await File.WriteAllBytesAsync(pdfPath, BuildSimplePdf("Geotechnical table visual test"));
        var settings = new ApplicationSettings
        {
            AiMode = AiOperatingMode.OnlinePreferred,
            HasVisionApiKey = true,
            VisionModel = "qwen3.7-plus",
            OcrStartPage = 1,
            OcrEndPage = 1,
            VisionPagesPerBatch = 1
        };
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestCount++;
            var requestJson = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert(requestJson.Contains("\"model\":\"qwen3.7-plus\"", StringComparison.Ordinal),
                "视觉请求必须使用设置中选定的识别模型。" );
            Assert(!requestJson.Contains("qwen-image", StringComparison.OrdinalIgnoreCase) &&
                   !requestJson.Contains("wan2.7", StringComparison.OrdinalIgnoreCase),
                "视觉请求中不得出现图片或视频生成模型。" );
            if (requestCount == 1)
            {
                Assert(requestJson.Contains("data:image/jpeg;base64,", StringComparison.Ordinal),
                    "首轮必须把PDF页渲染为轻量高清图片发送给视觉模型，而不是只发送OCR文字。" );
            }

            var aiContent = requestCount == 1
                ? """
{"pages":[{"pdf_page":1,"section":"参数表","context_facts":["河北省廊坊市三河市"],"groundwater_facts":["地下水埋深4.5m"],"seismic_facts":[],"soil_layer_facts":[{"name":"粉土","unit_weight_kn_per_m3":21,"fak_kpa":110}],"pile_parameter_facts":[],"foundation_recommendations":[],"special_risk_facts":[],"verbatim_evidence":["粉土 重度21 fak 110 地下水4.5"],"uncertain":[]}],"cross_page_conflicts":[]}
"""
                : """
{"project_name":"视觉地勘测试","site_location":"河北省廊坊市三河市","province":"河北省","city":"廊坊市","county":"三河市","bearing_capacity_kpa":null,"characteristic_bearing_capacity_kpa":110,"soil_unit_weight_kn_per_m3":21,"groundwater_depth_m":4.5,"groundwater_depth_candidates_m":[4.5],"soil_description":"粉土","pile_parameters_safe_to_apply":false,"pile_soil_layers":[],"pile_parameter_options":[],"critical_warnings":[],"evidence":"PDF第1页参数表","evidence_pages":[1],"confidence":0.95}
""";
            var response = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = aiContent } } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        });
        using var service = new VisualGeotechnicalAiService(
            new FakeSettingsService(settings),
            handler);
        var result = await service.AnalyzePdfAsync(
            pdfPath,
            GeotechnicalDocumentImportService.BuildFoundationSpecificRequirements(
                FoundationType.RectangularShortColumn),
            switchOptions: new VisionModelSwitchOptions
            {
                ConfirmAsync = (_, _) => Task.FromResult(true)
            });
        Assert(requestCount == 2, "单页视觉分析应执行逐页证据读取和跨页工程复核两轮请求。" );
        Assert(result.PageCount == 1 && result.ProcessedPageCount == 1,
            "视觉分析必须保留PDF总页数和实际处理页数。" );
        AssertClose(result.AiResult.CharacteristicBearingCapacityKpa ?? 0, 110, 1e-9,
            "视觉结果应通过本机解析器安全提取fak。" );
        AssertClose(result.AiResult.GroundwaterDepthM ?? 0, 4.5, 1e-9,
            "视觉结果应安全提取唯一地下水埋深。" );
        Assert(result.AiResult.EvidencePages.SequenceEqual(new[] { 1 }),
            "视觉结果必须保留固定PDF页码证据。" );
        var project = new ProjectModel();
        var importService = new GeotechnicalDocumentImportService(
            new FakeSettingsService(settings),
            new FakeDeepSeekService(result.AiResult),
            new FakeWordTextExtractor(),
            new FakePdfOcrService(string.Empty));
        var applied = importService.ApplyAiCandidates(
            project,
            new GeotechnicalDocumentImportResult
            {
                Document = new DocumentTextExtractionResult
                {
                    SourceName = "vision-sample.pdf（视觉直读）",
                    Content = result.EvidenceText
                },
                AiResult = result.AiResult,
                AiProviderDisplay = "百炼视觉 qwen3.7-plus",
                AiSourceType = ParameterSourceType.VisualAi,
                EvidencePaneTitle = "视觉模型逐页证据摘录"
            });
        Assert(project.Geotechnical.CharacteristicBearingCapacityKpa == 110 &&
               project.Geotechnical.SourceType == ParameterSourceType.VisualAi &&
               applied.AssignedFields.Count > 0,
            "用户在复核窗采用后，视觉候选值应直接填入地勘表单并保留视觉AI来源。" );
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestVisualBatchSplitRecovery()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.VisualSplit.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var pdfPath = Path.Combine(directory, "two-pages.pdf");
    try
    {
        await File.WriteAllBytesAsync(
            pdfPath,
            BuildSimplePdfPages("Page one fak 110", "Page two groundwater 4.5"));
        var settings = new ApplicationSettings
        {
            AiMode = AiOperatingMode.OnlinePreferred,
            HasVisionApiKey = true,
            VisionModel = "qwen3.7-plus",
            VisionPagesPerBatch = 2,
            OcrStartPage = 1,
            OcrEndPage = 2
        };
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestCount++;
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var imageCount = body.Split(
                "data:image/jpeg;base64,",
                StringSplitOptions.None).Length - 1;
            if (imageCount == 2)
            {
                return BuildAiHttpResponse(
                    "{\"error\":{\"message\":\"gateway timeout\"}}",
                    HttpStatusCode.GatewayTimeout);
            }

            if (imageCount == 1)
            {
                var page = JsonContainsText(body, "pdf_page只能写1") ? 1 : 2;
                var fact = page == 1
                    ? "{\"layer\":\"粉土\",\"fak\":110,\"evidence\":\"fak 110\"}"
                    : "{\"depth_m\":4.5,\"evidence\":\"地下水4.5m\"}";
                var json = page == 1
                    ? $"{{\"pages\":[{{\"pdf_page\":1,\"soil_layers\":[{fact}]}}],\"cross_page_conflicts\":[]}}"
                    : $"{{\"pages\":[{{\"pdf_page\":2,\"groundwater\":[{fact}]}}],\"cross_page_conflicts\":[]}}";
                return BuildAiHttpResponse(json);
            }

            return BuildAiHttpResponse("""
{"characteristic_bearing_capacity_kpa":110,"groundwater_depth_m":4.5,"groundwater_depth_candidates_m":[4.5],"soil_description":"粉土","pile_parameters_safe_to_apply":false,"pile_soil_layers":[],"pile_parameter_options":[],"evidence":"PDF第1、2页","evidence_pages":[1,2],"critical_warnings":[],"confidence":0.9}
""");
        });
        using var service = new VisualGeotechnicalAiService(
            new FakeSettingsService(settings),
            handler);
        var result = await service.AnalyzePdfAsync(
            pdfPath,
            GeotechnicalDocumentImportService.BuildFoundationSpecificRequirements(
                FoundationType.RectangularShortColumn));
        Assert(requestCount == 4,
            "两页批次失败后应自动拆成两个单页，再执行一次跨页汇总。" );
        Assert(result.Warnings.Any(item => item.Contains("自动拆", StringComparison.Ordinal)),
            "自动拆页恢复必须写入可追溯警告。" );
        AssertClose(result.AiResult.CharacteristicBearingCapacityKpa ?? 0, 110, 1e-9,
            "拆页恢复后仍应正确汇总fak。" );
        AssertClose(result.AiResult.GroundwaterDepthM ?? 0, 4.5, 1e-9,
            "拆页恢复后仍应正确汇总地下水埋深。" );
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestVisualSinglePageModelFallback()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.VisualFallback.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var pdfPath = Path.Combine(directory, "one-page.pdf");
    try
    {
        await File.WriteAllBytesAsync(pdfPath, BuildSimplePdf("fak 120"));
        var settings = new ApplicationSettings
        {
            AiMode = AiOperatingMode.OnlinePreferred,
            HasVisionApiKey = true,
            VisionModel = "qwen3.7-plus",
            VisionPagesPerBatch = 1,
            OcrStartPage = 1,
            OcrEndPage = 1
        };
        var requestCount = 0;
        var selectedModelImageAttempts = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestCount++;
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var hasImage = body.Contains("data:image/jpeg;base64,", StringComparison.Ordinal);
            var usesSelected = body.Contains("\"model\":\"qwen3.7-plus\"", StringComparison.Ordinal);
            if (hasImage && usesSelected)
            {
                selectedModelImageAttempts++;
                return BuildAiHttpResponse(
                    "{\"error\":{\"message\":\"service unavailable\"}}",
                    HttpStatusCode.ServiceUnavailable);
            }

            if (hasImage)
            {
                Assert(body.Contains("\"model\":\"qwen3.6-flash\"", StringComparison.Ordinal),
                    "主模型单页重试失败后应改用快速视觉理解模型。" );
                return BuildAiHttpResponse("""
{"pages":[{"pdf_page":1,"soil_layers":[{"layer":"粉土","fak":120,"evidence":"fak 120"}]}],"cross_page_conflicts":[]}
""");
            }

            return BuildAiHttpResponse("""
{"characteristic_bearing_capacity_kpa":120,"soil_description":"粉土","pile_parameters_safe_to_apply":false,"pile_soil_layers":[],"pile_parameter_options":[],"evidence":"PDF第1页","evidence_pages":[1],"critical_warnings":[],"confidence":0.88}
""");
        });
        using var service = new VisualGeotechnicalAiService(
            new FakeSettingsService(settings),
            handler);
        var switchConfirmations = 0;
        var result = await service.AnalyzePdfAsync(
            pdfPath,
            GeotechnicalDocumentImportService.BuildFoundationSpecificRequirements(
                FoundationType.RectangularShortColumn),
            switchOptions: new VisionModelSwitchOptions
            {
                ConfirmAsync = (request, _) =>
                {
                    switchConfirmations++;
                    Assert(request.CurrentModel == "qwen3.7-plus" &&
                           request.ProposedModel == "qwen3.6-flash",
                        "地勘视觉换模确认必须显示当前模型与建议模型。" );
                    return Task.FromResult(true);
                }
            });
        Assert(selectedModelImageAttempts == 2 && requestCount == 4,
            "单页应先重试主模型两次，再用快速模型读取，最后由主模型汇总。" );
        Assert(switchConfirmations == 1,
            "地勘视觉单页切换备用模型前必须且只应确认一次。" );
        Assert(result.EvidenceText.Contains("qwen3.6-flash", StringComparison.Ordinal) &&
               result.Warnings.Any(item => item.Contains("备用", StringComparison.Ordinal)),
            "备用模型的实际使用情况必须保留在证据和警告中。" );
        AssertClose(result.AiResult.CharacteristicBearingCapacityKpa ?? 0, 120, 1e-9,
            "模型兜底后仍应完成参数解析。" );
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static Task TestRegionWindCatalog()
{
    var catalog = new EmbeddedRegionWindCatalog();
    Assert(catalog.Provinces.Count == 34, "内置行政区应覆盖34个省级地区。");
    Assert(catalog.Provinces[0].Name == "甘肃省", "省级地址列表应将甘肃省固定置顶。");
    Assert(
        catalog.GetCities(620000).Any(item => item.Name == "兰州市"),
        "甘肃省城市列表应包含兰州市。");
    Assert(
        catalog.GetCounties(620100).Any(item => item.Name == "城关区"),
        "兰州市县区列表应包含城关区。");

    var beijing = catalog.Lookup("北京市", "北京市", "东城区");
    Assert(
        beijing.SourceKind == WindPressureSourceKind.ParentCityReference,
        "东城区应明确采用北京市台站参考值，而不是伪称县区直接值。");
    AssertClose(
        beijing.FiftyYearKpa ?? 0,
        0.45,
        1e-9,
        "北京市50年重现期基本风压应取表E.5的0.45 kPa。");

    var lanzhou = catalog.Lookup("甘肃省", "兰州市", "城关区");
    AssertClose(
        lanzhou.FiftyYearKpa ?? 0,
        0.30,
        1e-9,
        "兰州市50年重现期基本风压应取表E.5的0.30 kPa。");
    Assert(
        lanzhou.Explanation.Contains("确认"),
        "县区引用城市台站时必须提示人工确认。");
    return Task.CompletedTask;
}

static Task TestEnterpriseTowerLoadCatalog()
{
    var catalog = new EmbeddedTowerLoadCatalog();
    var service = new EnterpriseTowerLoadService(catalog);
    if (catalog.Records.Count == 0 && catalog.LegacyRecords.Count == 0)
    {
        Assert(!catalog.Status.IsCompleteForNewDesign, "空的公开占位库不得标记为可用于新建设计。");
        Assert(service.Filter(null, null, null).Count == 0, "公开占位库不得返回虚构塔型荷载。");
        Assert(catalog.Status.StatusMessage.Contains("手工荷载", StringComparison.Ordinal),
            "公开占位库必须明确提示改用手工荷载输入。" );
        return Task.CompletedTask;
    }

    Assert(catalog.Records.Count == 446, "V2.0三个part1应提取446条可用于基础设计的塔脚反力记录。");
    Assert(catalog.LegacyRecords.Count == 353, "历史库应继续保留353条记录用于旧项目来源追溯。");
    Assert(
        catalog.Status.IsCompleteForNewDesign &&
        catalog.Status.NoticeNumber == "中国铁塔〔2025〕244号" &&
        catalog.Status.StandardNumbers.SequenceEqual(
            ["Q/ZTT 1023-2025", "Q/ZTT 1032-2025"]),
        "现行库必须保存版本状态和两套V2.0图集编号。");
    Assert(
        service.GetSourceTitles().Count == 3 &&
        service.GetTowerTypes(null).Count == 10 &&
        service.Filter(null, null, null).Count == 446,
        "图集来源、塔型分类和具体型号筛选必须返回现行V2.0数据。");
    var heightWindMatches = service.Filter(
        null,
        "支架式单管塔",
        "20 6F",
        towerHeightM: 20,
        windPressureKpa: 0.45);
    Assert(
        heightWindMatches.Count > 0 &&
        heightWindMatches.All(item =>
            EnterpriseTowerLoadService.ParseHeight(item) == 20 &&
            EnterpriseTowerLoadService.ParseWindPressure(item) == 0.45 &&
            item.TowerCode.Contains("6F", StringComparison.OrdinalIgnoreCase)),
        "塔型库必须支持塔高、风压和空格分隔的多关键词联合筛选。" );
    Assert(
        service.GetTowerHeights(null, "支架式单管塔").Contains(20) &&
        service.GetWindPressures(null, "支架式单管塔").Contains(0.45),
        "塔高和设计风压筛选项必须从现行图集记录动态生成。" );
    Assert(
        catalog.LegacyRecords.Count(item => item.UsableForAutomaticOverallLoad) == 310 &&
        catalog.LegacyRecords.Count(item => item.UsableForAutomaticSingleLegLoad) == 109,
        "历史库内容必须原样保留，避免旧项目打开后来源记录丢失。");

    var singleTube = service.Filter(
            "通信铁塔标准图集（第一分册）",
            "支架式单管塔",
            "DGT(Z)-20-0.35-2ZJ-6F")
        .Single();
    AssertClose(singleTube.OverallBaseReaction!.Standard!.AxialKn, 17.7, 1e-9, "单管塔标准组合轴力错误。");
    AssertClose(singleTube.OverallBaseReaction.Standard.ShearKn, 10.1, 1e-9, "单管塔标准组合剪力错误。");
    AssertClose(singleTube.OverallBaseReaction.Standard.MomentKnM, 145.0, 1e-9, "单管塔标准组合弯矩错误。");

    var threeTube = service.Filter(
            "通信铁塔标准图集（第二分册）",
            "双斜杆三管塔",
            "3GT(SX)-20-0.45-1NPT-6F")
        .Single();
    AssertClose(threeTube.OverallBaseReaction!.Standard!.AxialKn, 31.6, 1e-9, "三管塔整塔轴力错误。");
    AssertClose(
        threeTube.SingleLegReaction!.Standard!.CompressionControl!.CompressionKn,
        300.0,
        1e-9,
        "三管塔单塔腿压力错误。");
    AssertClose(
        threeTube.SingleLegReaction.Standard.TensionControl!.TensionKn,
        280.1,
        1e-9,
        "三管塔单塔腿拔力错误。");

    var project = new ProjectModel
    {
        ProjectType = ProjectType.CommunicationTower,
        Geotechnical = new GeotechnicalInput { IsConfirmed = true }
    };
    project.FoundationSettings.FoundationType = FoundationType.RectangularShortColumn;
    service.ApplyOverallStandardLoad(project, singleTube.Id);
    Assert(project.TowerMast.TowerModel == singleTube.TowerCode, "采用图集后应回填塔型编号。");
    AssertClose(project.TowerMast.VerticalKn, 17.7, 1e-9, "采用图集后应回填整塔轴力。");
    AssertClose(project.TowerMast.BasicVerticalKn, 23.0, 1e-9, "采用图集后应同步回填整塔基本组合轴力。");
    AssertClose(project.TowerMast.BasicMomentYKnM, 217.5, 1e-9, "采用图集后应同步回填整塔基本组合弯矩。");

    project.FoundationSettings.FoundationType = FoundationType.Pile;
    service.ApplyOverallStandardLoad(project, threeTube.Id);
    Assert(project.TowerMast.UsesIndividualPileReactions, "三管塔灌注桩应采用单塔腿反力。");
    Assert(project.FoundationSettings.Pile.PileCount == 3, "三管塔应形成3根单桩及连梁布置。");
    AssertClose(project.TowerMast.IndividualPileCompressionKn, 300.0, 1e-9, "单桩压力回填错误。");
    AssertClose(project.TowerMast.BasicIndividualPileCompressionKn, 448.6, 1e-9, "单桩基本组合压力回填错误。");
    AssertClose(project.TowerMast.BasicIndividualPileUpliftKn, 422.6, 1e-9, "单桩基本组合上拔力回填错误。");

    project.FoundationSettings.FoundationType = FoundationType.RectangularShortColumn;
    service.ApplyOverallStandardLoad(project, threeTube.Id);
    Assert(project.TowerMast.UsesIndividualPileReactions, "三管塔独立浅基础也必须采用一个塔脚反力。");
    AssertClose(project.TowerMast.IndividualPileCompressionKn, 300.0, 1e-9, "独立浅基础单塔脚压力回填错误。");
    AssertClose(project.TowerMast.BasicIndividualPileCompressionKn, 448.6, 1e-9, "独立浅基础基本组合单塔脚压力回填错误。");
    project.TowerMast.IsConfirmed = true;
    Assert(
        BuildWorkflow().ValidateForDesign(project).All(issue =>
            !issue.Message.Contains("当前基本组合不完整", StringComparison.Ordinal)),
        "多塔脚独立浅基础已回填一个塔脚基本组合时，不得按旧灌注桩专用条件误报缺失。" );

    project.FoundationSettings.FoundationType = FoundationType.RigidRectangularShortPile;
    service.ApplyOverallStandardLoad(project, threeTube.Id);
    Assert(project.TowerMast.UsesIndividualPileReactions, "三管塔刚性短柱桩也必须采用一个塔脚反力。");

    project.FoundationSettings.FoundationType = FoundationType.Raft;
    service.ApplyOverallStandardLoad(project, threeTube.Id);
    Assert(!project.TowerMast.UsesIndividualPileReactions, "三管塔共同筏板应采用整塔基础端反力。");
    AssertClose(project.TowerMast.MomentYKnM, threeTube.OverallBaseReaction.Standard.MomentKnM, 1e-9, "共同筏板整塔弯矩回填错误。");

    var record = catalog.FindById("camouflage-p9-r1") ??
                 throw new InvalidOperationException("历史来源记录缺失。");
    Assert(
        !catalog.IsCurrentRecord(record.Id) &&
        record.CanApplyOverallStandardLoad,
        "旧记录应可追溯但必须明确不属于现行库。");

    var historyRecordBlocked = false;
    try
    {
        service.ApplyOverallStandardLoad(project, record.Id);
    }
    catch (InvalidOperationException exception)
    {
        historyRecordBlocked =
            exception.Message.Contains("历史来源反力", StringComparison.Ordinal) &&
            exception.Message.Contains("当前企业塔型库", StringComparison.Ordinal);
    }

    Assert(historyRecordBlocked, "历史来源记录必须阻止新项目直接重新采用。");
    return Task.CompletedTask;
}

static Task TestGroundwaterBuoyancy()
{
    var calculator = new RectangularShortColumnFoundationCalculator();
    var geometry = new FoundationGeometry
    {
        BaseLengthM = 4,
        BaseWidthM = 4,
        BaseThicknessM = 1,
        PedestalLengthM = 0.8,
        PedestalWidthM = 0.8,
        PedestalHeightM = 1.2
    };
    var settings = new FoundationDesignSettings();
    var dry = calculator.Calculate(
        geometry,
        new FoundationLoad { VerticalKn = 20, GoverningCase = "地下水测试" },
        new GeotechnicalInput
        {
            BearingCapacityKpa = 300,
            SoilUnitWeightKnPerM3 = 18,
            BaseFrictionCoefficient = 0.3,
            GroundwaterDepthM = 5,
            IsConfirmed = true
        },
        settings);
    var wet = calculator.Calculate(
        geometry,
        new FoundationLoad { VerticalKn = 20, GoverningCase = "地下水测试" },
        new GeotechnicalInput
        {
            BearingCapacityKpa = 300,
            SoilUnitWeightKnPerM3 = 18,
            BaseFrictionCoefficient = 0.3,
            GroundwaterDepthM = 0.5,
            IsConfirmed = true
        },
        settings);

    var dryPressure = dry.Checks.Single(item => item.Code == "BEARING_AVERAGE").Demand;
    var wetPressure = wet.Checks.Single(item => item.Code == "BEARING_AVERAGE").Demand;
    var waterCheck = wet.Checks.Single(item => item.Code == "GROUNDWATER");
    Assert(wetPressure < dryPressure, "地下水浮力扣减后基底平均压力应降低。");
    Assert(waterCheck.Demand > 0, "地下水高于基础底面时应形成非零浮力扣减。");
    Assert(waterCheck.Status == CheckStatus.Result, "浮力扣减是计算过程结果，不应伪装为安全性通过项。");
    return Task.CompletedTask;
}

static Task TestBearingCapacityCorrection()
{
    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        new FoundationGeometry
        {
            BaseLengthM = 4,
            BaseWidthM = 4,
            BaseThicknessM = 1,
            PedestalLengthM = 0.8,
            PedestalWidthM = 0.8,
            PedestalHeightM = 1.2
        },
        new FoundationLoad { VerticalKn = 100, GoverningCase = "承载力修正测试" },
        new GeotechnicalInput
        {
            BearingCapacityKpa = 150,
            UseBearingCapacityCorrection = true,
            CharacteristicBearingCapacityKpa = 150,
            BearingCapacityWidthCorrectionFactor = 0.3,
            BearingCapacityDepthCorrectionFactor = 1.5,
            SoilBelowBaseUnitWeightKnPerM3 = 18,
            SoilAboveBaseAverageUnitWeightKnPerM3 = 18,
            SoilUnitWeightKnPerM3 = 18,
            BaseFrictionCoefficient = 0.3,
            GroundwaterDepthM = 5,
            IsConfirmed = true
        },
        new FoundationDesignSettings());

    var correction = scheme.Checks.Single(
        item => item.Code == "BEARING_CAPACITY_CORRECTION");
    var bearing = scheme.Checks.Single(item => item.Code == "BEARING_AVERAGE");
    AssertClose(
        correction.Demand,
        201.3,
        1e-9,
        "承载力宽深修正应按式(5.2.4)计算。");
    AssertClose(
        bearing.Capacity,
        201.3,
        1e-9,
        "修正后的fa应进入承载力验算。");
    Assert(
        correction.RuleReference.Contains("5.2.4"),
        "承载力修正结果应保留公式编号。");
    return Task.CompletedTask;
}

static Task TestStructuralFoundationChecks()
{
    var calculator = new RectangularShortColumnFoundationCalculator();
    var geometry = new FoundationGeometry
    {
        BaseLengthM = 4,
        BaseWidthM = 4,
        BaseThicknessM = 1,
        PedestalLengthM = 0.8,
        PedestalWidthM = 0.8,
        PedestalHeightM = 1.2
    };
    var load = new FoundationLoad
    {
        VerticalKn = 100,
        ShearXKn = 10,
        MomentYKnM = 80,
        GoverningCase = "结构规则测试"
    };
    var geotechnical = new GeotechnicalInput
    {
        BearingCapacityKpa = 300,
        SoilUnitWeightKnPerM3 = 18,
        BaseFrictionCoefficient = 0.3,
        GroundwaterDepthM = 5,
        IsConfirmed = true
    };

    var normal = calculator.Calculate(
        geometry,
        load,
        geotechnical,
        new FoundationDesignSettings());
    Assert(
        normal.Checks.Any(item => item.Code == "PUNCHING_X"),
        "冲切锥位于基础底面内时应执行X向冲切验算。");
    Assert(
        normal.Checks.Single(item => item.Code == "BENDING_APPLICABILITY").Status ==
        CheckStatus.Pass,
        "满足宽厚比和偏心条件时应开放底板简化受弯公式。");
    Assert(
        normal.Checks.Single(item => item.Code == "BOTTOM_REINFORCEMENT_X").Status ==
        CheckStatus.Pass,
        "默认底筋应形成可追溯的X向配筋结果。");
    Assert(
        normal.ReinforcementDesigns.Count == 2 &&
        normal.ReinforcementDesigns.All(item =>
            item.BarCount > 0 &&
            item.CalculatedWeightKg > 0 &&
            item.ProvidedAreaMm2 >= item.RequiredAreaMm2),
        "浅基础必须形成X/Y向结构化配筋、长度和重量结果。");
    Assert(
        Math.Abs(
            normal.Quantities.EstimatedReinforcementKg -
            normal.ReinforcementDesigns.Sum(item => item.CalculatedWeightKg)) < 1e-9,
        "钢筋工程量必须严格来自结构化配筋结果，不得使用经验含钢量。");

    var underReinforced = calculator.Calculate(
        geometry,
        load,
        geotechnical,
        new FoundationDesignSettings
        {
            BottomBarDiameterMm = 8,
            BottomBarSpacingMm = 500
        });
    Assert(
        underReinforced.Checks.Any(item =>
            item.Code.StartsWith("BOTTOM_REINFORCEMENT_", StringComparison.Ordinal) &&
            item.Status == CheckStatus.Fail),
        "底筋面积不足时必须判失败。");
    Assert(
        underReinforced.Checks.Any(item =>
            item.Code.StartsWith("PUNCHING_", StringComparison.Ordinal) &&
            item.RuleReference.Contains("8.2.8")),
        "冲切验算应保留GB 50007第8.2.8条引用。");
    return Task.CompletedTask;
}

static Task TestDualCombinationRouting()
{
    var calculator = new RectangularShortColumnFoundationCalculator();
    var geometry = new FoundationGeometry
    {
        BaseLengthM = 4,
        BaseWidthM = 4,
        BaseThicknessM = 1,
        PedestalLengthM = 0.8,
        PedestalWidthM = 0.8,
        PedestalHeightM = 1.2
    };
    var load = new FoundationLoad
    {
        VerticalKn = 100,
        ShearXKn = 10,
        MomentYKnM = 80,
        GoverningCase = "地基承载力标准组合",
        BasicCombination = new FoundationLoadCombination
        {
            VerticalKn = 130,
            ShearXKn = 18,
            MomentYKnM = 150,
            GoverningCase = "来源文件基本组合"
        }
    };
    var geotechnical = new GeotechnicalInput
    {
        BearingCapacityKpa = 300,
        SoilUnitWeightKnPerM3 = 18,
        BaseFrictionCoefficient = 0.3,
        GroundwaterDepthM = 5,
        IsConfirmed = true
    };
    var normalSettings = new FoundationDesignSettings
    {
        StructuralDesignLoadFactor = 1.5
    };
    var exaggeratedFallbackSettings = new FoundationDesignSettings
    {
        StructuralDesignLoadFactor = 9.0
    };

    var normal = calculator.Calculate(
        geometry,
        load,
        geotechnical,
        normalSettings);
    var exaggeratedFallback = calculator.Calculate(
        geometry,
        load,
        geotechnical,
        exaggeratedFallbackSettings);

    AssertClose(
        normal.Checks.Single(item => item.Code == "BEARING_AVERAGE").Demand,
        exaggeratedFallback.Checks.Single(item => item.Code == "BEARING_AVERAGE").Demand,
        1e-9,
        "地基承载力必须只采用标准组合，不能受结构基本组合回退系数影响。");
    AssertClose(
        normal.Checks.Single(item => item.Code == "BOTTOM_REINFORCEMENT_X").Demand,
        exaggeratedFallback.Checks.Single(item => item.Code == "BOTTOM_REINFORCEMENT_X").Demand,
        1e-9,
        "来源已提供明确基本组合时，结构验算不得再乘标准组合推导系数。");
    var combinationCheck = normal.Checks.Single(
        item => item.Code == "STRUCTURAL_COMBINATION");
    Assert(
        combinationCheck.GoverningCase == "来源文件基本组合" &&
        combinationCheck.Explanation.Contains("明确给出的基本组合", StringComparison.Ordinal),
        "结构验算必须记录明确基本组合的控制工况和来源类型。");

    var fallbackLoad = new FoundationLoad
    {
        VerticalKn = 100,
        ShearXKn = 10,
        MomentYKnM = 80,
        GoverningCase = "旧项目标准组合"
    };
    var resolvedFallback = fallbackLoad.ResolveStructuralDesignLoad(
        exaggeratedFallbackSettings);
    AssertClose(resolvedFallback.VerticalKn, 900, 1e-9, "旧项目缺少基本组合时应保留显式系数推导回退。");
    Assert(
        resolvedFallback.GoverningCase.Contains("未提供基本组合", StringComparison.Ordinal),
        "旧项目回退必须在控制工况中醒目标识，不能伪装成来源基本组合。");
    return Task.CompletedTask;
}

static Task TestBiaxialBendingEnvelope()
{
    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        new FoundationGeometry
        {
            BaseLengthM = 5,
            BaseWidthM = 5,
            BaseThicknessM = 1.2,
            PedestalLengthM = 1,
            PedestalWidthM = 1,
            PedestalHeightM = 1.2
        },
        new FoundationLoad
        {
            VerticalKn = 300,
            ShearXKn = 10,
            ShearYKn = 8,
            MomentXKnM = 100,
            MomentYKnM = 140,
            GoverningCase = "双向偏心回归"
        },
        new GeotechnicalInput
        {
            BearingCapacityKpa = 300,
            SoilUnitWeightKnPerM3 = 18,
            BaseFrictionCoefficient = 0.35,
            GroundwaterDepthM = 8
        },
        new FoundationDesignSettings
        {
            MaximumBaseLengthM = 8,
            MaximumBaseWidthM = 8,
            MaximumBaseThicknessM = 2.5,
            BottomBarDiameterMm = 20,
            BottomBarSpacingMm = 100
        });

    var applicability = scheme.Checks.Single(item => item.Code == "BENDING_APPLICABILITY");
    Assert(applicability.Status == CheckStatus.Pass, "全接触双向偏心基础应执行保守条带包络，不应漏算底板。" );
    Assert(applicability.Explanation.Contains("双向偏心", StringComparison.Ordinal), "双向偏心处理方法必须写入可追溯说明。" );
    Assert(
        scheme.Checks.Where(item => item.Code.StartsWith("BOTTOM_REINFORCEMENT_", StringComparison.Ordinal))
            .All(item => item.Explanation.Contains("正交方向最不利边缘压力", StringComparison.Ordinal)),
        "双向偏心的两个配筋方向都必须叠加正交方向不利压力包络。" );
    return Task.CompletedTask;
}

static async Task TestLocalPdfOcr()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.Ocr.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "ocr-sample.pdf");

    try
    {
        await File.WriteAllBytesAsync(
            path,
            BuildSimplePdf(
                "Bearing capacity 180 kPa, native PDF text extraction verification, " +
                "foundation soil parameter source evidence."));
        var result = await new LocalPdfOcrService(
            Path.Combine(directory, "tessdata")).ExtractRangeAsync(path, 1, 1);
        Assert(result.PageCount == 1, "本地OCR应识别单页PDF。");
        Assert(result.ProcessedPageCount == 1, "本地OCR应完成单页处理。");
        Assert(result.Content.Contains("180"), "OCR结果应包含样例数值180。");
        Assert(
            result.Content.Contains("第 1 页", StringComparison.Ordinal),
            "按页识别结果必须保留原PDF页码证据标记。");
        Assert(
            result.ExtractionMode == PdfTextExtractionMode.NativeTextLayer,
            "文字型PDF应优先读取原生文字层，不应重复OCR。");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestDocxExtraction()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.Docx.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "地勘样例.docx");

    try
    {
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("word/document.xml");
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync("""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
    <w:p><w:r><w:t>建议以圆砾层作为持力层。</w:t></w:r></w:p>
    <w:tbl>
      <w:tr><w:tc><w:p><w:r><w:t>承载力特征值</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>180 kPa</w:t></w:r></w:p></w:tc></w:tr>
    </w:tbl>
  </w:body>
</w:document>
""");
        }

        var result = await new DocxTextExtractor().ExtractAsync(path);
        Assert(result.Content.Contains("圆砾层"), "应提取 Word 正文段落。");
        Assert(result.Content.Contains("承载力特征值") && result.Content.Contains("180 kPa"), "应提取 Word 表格单元格。");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestDeepSeekStructuredExtraction()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.DeepSeek.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);

    try
    {
        var settings = new LocalApplicationSettingsService(directory);
        settings.Save(new ApplicationSettings(), "sk-fake-test-key");
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestCount++;
            Assert(request.RequestUri?.AbsoluteUri == "https://api.deepseek.com/chat/completions", "应调用官方 chat completions 地址。");
            Assert(request.Headers.Authorization?.Scheme == "Bearer", "应使用 Bearer API 密钥认证。");

            var aiContent = requestCount == 1
                ? """
{"context":{},"groundwater_mentions":[{"depth_m":4.5,"evidence":"第3页"}],"soil_layers":[{"name":"圆砾层","fak_kpa":180,"compression_modulus_mpa":12}],"pile_parameter_sets":[{"pile_method":"旋挖灌注桩","layer_name":"圆砾层","compression_modulus_mpa":12,"qsik_limit_standard_kpa":60,"qpk_limit_standard_kpa":1300,"uplift_coefficient":0.7}],"foundation_recommendations":[],"seismic":{},"special_risks":[],"data_conflicts":[]}
"""
                : """
{"project_name":"燕郊西柳河屯村东头","site_location":"河北省廊坊市三河市燕郊镇西柳河屯村东头","province":"河北省","city":"廊坊市","county":"三河市","bearing_capacity_kpa":null,"characteristic_bearing_capacity_kpa":180,"soil_unit_weight_kn_per_m3":19,"compression_modulus_mpa":12,"base_friction_coefficient":0.32,"groundwater_depth_m":4.5,"groundwater_depth_candidates_m":[4.5],"soil_description":"圆砾层","pile_parameters_safe_to_apply":true,"pile_soil_layers":[{"name":"圆砾层","thickness_m":8,"side_resistance_kpa":60,"tip_resistance_kpa":1300,"uplift_coefficient":0.7}],"pile_parameter_options":[{"pile_method":"旋挖灌注桩","layer_name":"圆砾层","thickness_m":8,"soil_unit_weight_kn_per_m3":19,"characteristic_bearing_capacity_kpa":180,"compression_modulus_mpa":12,"side_resistance_limit_standard_kpa":60,"tip_resistance_limit_standard_kpa":1300,"uplift_coefficient":0.7,"evidence":"表6.2"}],"single_pile_horizontal_capacity_kn":120,"evidence":"表6.2","confidence":0.92}
""";
            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new { message = new { content = aiContent } }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        });
        using var service = new DeepSeekService(settings, handler);
        var result = await service.ExtractGeotechnicalParametersAsync(
            "报告表6.2：圆砾层厚8m，fak=180kPa，重度19kN/m3，基底摩擦系数0.32，" +
            "地下水埋深4.5m、压缩模量Es=12MPa；旋挖灌注桩qsik=60kPa、qpk=1300kPa、抗拔系数0.7，" +
            "单桩水平承载力120kN。");

        Assert(requestCount == 2, "地勘AI解析应执行证据摘录与工程复核两轮请求。");
        Assert(
            result.Province == "河北省" &&
            result.City == "廊坊市" &&
            result.County == "三河市",
            "地勘AI应把建设地点拆分为省、市、县级行政区，供界面回填和抗震地点表匹配。" );
        AssertClose(result.CharacteristicBearingCapacityKpa ?? 0, 180, 1e-9, "应将fak解析为地基承载力特征值。");
        Assert(result.BearingCapacityKpa is null, "只有fak时不得臆造宽深修正后的fa。");
        AssertClose(result.BaseFrictionCoefficient ?? 0, 0.32, 1e-9, "应解析基底摩擦系数候选值。");
        Assert(
            result.PileParameterOptions.Single().CompressionModulusMpa == 12,
            "沉降所需分层压缩模量Es必须保留在结构化土层候选中。" );
        Assert(
            result.PileSoilLayers.Count == 1 &&
            result.PileSoilLayers[0].TipResistanceKpa == 1300,
            "应解析桩基础分层侧阻、端阻和抗拔候选值。");
        AssertClose(
            result.SinglePileHorizontalCapacityKn ?? 0,
            120,
            1e-9,
            "应解析报告明确给出的单桩水平承载力候选值。");
        Assert(result.Evidence == "表6.2", "应保留 AI 返回的证据字段。");
        AssertClose(result.Confidence, 0.92, 1e-9, "应保留 AI 提取置信度。");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestDeepSeekAnchorDrawingExtraction()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.DeepSeek.Anchor.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var settings = new LocalApplicationSettingsService(directory);
        settings.Save(new ApplicationSettings(), "sk-fake-anchor-key");
        var handler = new StubHttpMessageHandler(_ =>
        {
            var aiContent = """
{"bolt_count":12,"nominal_diameter_mm":36,"bolt_circle_diameter_mm":1200,"embedment_depth_mm":1500,"tensile_strength_design_mpa":180,"shear_strength_design_mpa":140,"thread_stress_area_factor":0.78,"material_grade":"Q355","evidence":"塔脚详图：12-M36，锚栓圆直径1200，埋深1500","warnings":[],"confidence":0.94}
""";
            var content = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = aiContent } } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        });
        using var service = new DeepSeekService(settings, handler);
        var result = await service.ExtractAnchorBoltParametersAsync(
            "塔脚详图：12-M36，锚栓圆直径1200mm，埋深1500mm；材质Q355；" +
            "抗拉设计值180MPa，抗剪设计值140MPa，螺纹有效面积系数0.78。");

        Assert(result.BoltCount == 12, "应从12-M36中区分锚栓数量。" );
        AssertClose(result.NominalDiameterMm ?? 0, 36, 1e-9, "应从12-M36中区分公称直径。" );
        AssertClose(result.BoltCircleDiameterMm ?? 0, 1200, 1e-9, "应提取锚栓圆直径。" );
        AssertClose(result.EmbedmentDepthMm ?? 0, 1500, 1e-9, "应提取锚栓埋深。" );
        Assert(result.MaterialGrade == "Q355" && result.Evidence.Contains("12-M36", StringComparison.Ordinal),
            "应保留材料牌号和原图证据。" );
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestDeepSeekConflictGuard()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.DeepSeek.Conflict.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);

    try
    {
        var settings = new LocalApplicationSettingsService(directory);
        settings.Save(new ApplicationSettings(), "sk-fake-test-key");
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            var aiContent = requestCount == 1
                ? """
{"context":{},"groundwater_mentions":[{"depth_m":7,"evidence":"2.5节"},{"depth_m":2.7,"evidence":"结论"}],"soil_layers":[{"name":"粉土","unit_weight_kn_per_m3":21,"fak_kpa":110,"horizontal_resistance_coefficient_mn_per_m4":14}],"pile_parameter_sets":[{"pile_method":"混凝土预制桩","layer_name":"粉土","qsik_limit_standard_kpa":35,"qpk_limit_standard_kpa":950,"uplift_coefficient":null},{"pile_method":"泥浆护壁钻孔桩","layer_name":"粉土","qsik_limit_standard_kpa":38,"qpk_limit_standard_kpa":400,"uplift_coefficient":null}],"foundation_recommendations":[{"foundation_type":"桩基础","pile_method":"人工挖孔桩"}],"seismic":{},"special_risks":[],"data_conflicts":[{"field":"groundwater_depth_m","values":[7,2.7]}]}
"""
                : """
{"bearing_capacity_kpa":null,"characteristic_bearing_capacity_kpa":110,"soil_unit_weight_kn_per_m3":21,"cohesion_kpa":15,"internal_friction_angle_degree":22,"compression_modulus_mpa":7,"groundwater_depth_m":7,"groundwater_depth_candidates_m":[7,2.7],"soil_description":"粉土","recommended_foundation_type":"人工挖孔桩","pile_parameters_safe_to_apply":false,"pile_soil_layers":[],"pile_parameter_options":[{"pile_method":"混凝土预制桩","layer_name":"粉土","top_depth_m":0,"bottom_depth_m":12,"thickness_m":12,"soil_unit_weight_kn_per_m3":21,"characteristic_bearing_capacity_kpa":110,"horizontal_resistance_coefficient_mn_per_m4":14,"side_resistance_limit_standard_kpa":35,"tip_resistance_limit_standard_kpa":950,"uplift_coefficient":null,"evidence":"表3.1"},{"pile_method":"泥浆护壁钻孔桩","layer_name":"粉土","top_depth_m":0,"bottom_depth_m":12,"thickness_m":12,"soil_unit_weight_kn_per_m3":21,"characteristic_bearing_capacity_kpa":110,"horizontal_resistance_coefficient_mn_per_m4":14,"side_resistance_limit_standard_kpa":38,"tip_resistance_limit_standard_kpa":400,"uplift_coefficient":null,"evidence":"表3.1"}],"critical_warnings":["地下水埋深冲突","人工挖孔桩与参数表桩型不对应"],"evidence":"2.5节、结论、表3.1","confidence":0.72}
""";
            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new { message = new { content = aiContent } }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        });
        using var service = new DeepSeekService(settings, handler);
        var result = await service.ExtractGeotechnicalParametersAsync(
            "粉土0~12m，重度21，黏聚力15，内摩擦角22，压缩模量7，fak=110，m=14。混凝土预制桩qsik=35、qpk=950；" +
            "泥浆护壁钻孔桩qsik=38、qpk=400；抗拔系数/。2.5节地下水7.0m，结论2.7m，" +
            "建议采用桩基础、人工挖孔桩。");

        Assert(requestCount == 2, "冲突场景也应完成两轮交叉核对。");
        Assert(result.GroundwaterDepthM is null, "地下水埋深冲突时不得自动采用任一值。");
        Assert(
            result.GroundwaterDepthCandidatesM.SequenceEqual(new[] { 2.7, 7d }),
            "地下水冲突候选值应全部保留并排序。");
        Assert(result.PileSoilLayers.Count == 0, "推荐桩型与参数列不匹配且抗拔系数缺失时不得自动回填计算层。");
        Assert(result.PileParameterOptions.Count == 2, "不同成桩方法的参数列应分别保留供人工选择。");
        Assert(
            result.SoilUnitWeightKnPerM3 == 21 &&
            result.CohesionKpa == 15 &&
            result.InternalFrictionAngleDegree == 22 &&
            result.CompressionModulusMpa == 7,
            "主要持力层的重度、黏聚力、内摩擦角和压缩模量应分别提取。");
        Assert(
            result.PileParameterOptions.Any(item =>
                item.HorizontalResistanceCoefficientMnPerM4 == 14 &&
                item.SideResistanceLimitStandardKpa == 35 &&
                item.TipResistanceLimitStandardKpa == 950),
            "m、qsik、qpk必须按表头正确分列，不能把m=14当成桩侧阻力。");
        Assert(
            result.PileParameterOptions.All(item => item.UpliftCoefficient is null),
            "报告以斜杠表示未提供抗拔系数时不得臆造默认值。");
        Assert(result.CriticalWarnings.Count >= 2, "冲突值和桩型错配必须形成显式警告。");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestPdfOcrAiPipeline()
{
    var settings = new FakeSettingsService(new ApplicationSettings
    {
        AiMode = AiOperatingMode.OnlinePreferred,
        DeepSeekModel = "deepseek-v4-pro",
        HasApiKey = true
    });
    var ocr = new FakePdfOcrService(
        "地基承载力特征值 fak=180 kPa，基底摩擦系数0.32，地下水埋深4.5m。");
    var ai = new FakeDeepSeekService(new GeotechnicalAiExtractionResult
    {
        CharacteristicBearingCapacityKpa = 180,
        BaseFrictionCoefficient = 0.32,
        GroundwaterDepthM = 4.5,
        SoilDescription = "圆砾层",
        RecommendedFoundationType = "扩展基础",
        Evidence = "地勘报告第6.2节",
        PileSoilLayers =
        [
            new PileSoilLayerCandidate
            {
                Name = "圆砾层",
                ThicknessM = 8,
                SideResistanceKpa = 60,
                TipResistanceKpa = 1300,
                UpliftCoefficient = 0.70
            }
        ],
        PileParameterOptions =
        [
            new PileParameterSetCandidate
            {
                PileMethod = "旋挖灌注桩",
                LayerName = "圆砾层",
                ThicknessM = 8,
                CompressionModulusMpa = 12,
                Evidence = "表6.2"
            }
        ],
        SinglePileHorizontalCapacityKn = 120,
        Confidence = 0.93
    });
    var service = new GeotechnicalDocumentImportService(
        settings,
        ai,
        new FakeWordTextExtractor(),
        ocr);

    var import = await service.ImportPdfAsync(
        "ignored.pdf",
        FoundationType.Pile);
    var project = CreateConfirmedProject();
    project.FoundationSettings.FoundationType = FoundationType.Pile;
    var application = service.ApplyAiCandidates(project, import);

    Assert(import.UsedAi, "在线优先且已配置密钥时，PDF OCR 后必须继续调用 DeepSeek。");
    Assert(
        ai.LastDocumentText?.Contains("fak=180", StringComparison.Ordinal) == true,
        "DeepSeek 必须收到本地 OCR 的实际文本。");
    Assert(
        ai.LastDocumentText?.Contains("当前已选基础形式：桩基础", StringComparison.Ordinal) == true,
        "AI 分析前必须收到已经前置选择的基础形式和对应提取要求。");
    AssertClose(
        project.Geotechnical.CharacteristicBearingCapacityKpa,
        180,
        1e-9,
        "AI 提取的 fak 应回填到地勘候选参数。");
    AssertClose(
        project.Geotechnical.BaseFrictionCoefficient,
        0.32,
        1e-9,
        "AI 提取的基底摩擦系数应回填。");
    Assert(
        project.Geotechnical.SourceType == ParameterSourceType.DeepSeek &&
        !project.Geotechnical.IsConfirmed,
        "AI 回填值必须标记为 DeepSeek 候选且保持未确认。");
    Assert(
        application.AssignedFields.Count >= 3,
        "闭环结果应明确列出已回填的关键字段。");
    Assert(
        project.FoundationSettings.Pile.SoilLayers.Count == 1 &&
        project.FoundationSettings.Pile.SoilLayers[0].SideResistanceKpa == 60 &&
        project.FoundationSettings.Pile.SinglePileHorizontalCapacityKn == 120 &&
        !project.FoundationSettings.Pile.IsConfirmed,
        "选择桩基础时，AI 必须回填分层侧阻、端阻和水平承载力候选，并保持未确认。");
    Assert(
        project.FoundationSettings.SpecialtyDesign.Settlement.SoilLayers.Count == 1 &&
        project.FoundationSettings.SpecialtyDesign.Settlement.SoilLayers[0].CompressionModulusMpa == 12 &&
        !project.FoundationSettings.SpecialtyDesign.Settlement.Source.IsConfirmed,
        "AI应把分层厚度和Es直接填入沉降候选表，但必须保持待人工确认。" );
}

static Task TestAiSafeValuesDirectFill()
{
    var settings = new FakeSettingsService(new ApplicationSettings());
    var aiResult = new GeotechnicalAiExtractionResult
    {
        ProjectName = "燕郊西柳河屯村东头",
        SiteLocation = "河北省廊坊市三河市燕郊镇西柳河屯村东头",
        CharacteristicBearingCapacityKpa = 110,
        SoilUnitWeightKnPerM3 = 21,
        GroundwaterDepthCandidatesM = [2.7, 7],
        CriticalWarnings = ["地下水埋深冲突"],
        Evidence = "表3.1、2.5节、结论",
        Confidence = 0.90
    };
    var service = new GeotechnicalDocumentImportService(
        settings,
        new FakeDeepSeekService(aiResult),
        new FakeWordTextExtractor(),
        new FakePdfOcrService("sample"));
    var project = new ProjectModel
    {
        ProjectType = ProjectType.CommunicationTower
    };
    project.FoundationSettings.FoundationType = FoundationType.RectangularShortColumn;
    var import = new GeotechnicalDocumentImportResult
    {
        Document = new DocumentTextExtractionResult
        {
            SourceName = "地勘.pdf",
            Content = "fak=110，γ=21，地下水2.7m/7.0m"
        },
        AiResult = aiResult
    };

    var application = service.ApplyAiCandidates(project, import);

    Assert(project.Name == "燕郊西柳河屯村东头", "默认项目名称应由AI识别结果直接替换。");
    AssertClose(project.Geotechnical.CharacteristicBearingCapacityKpa, 110, 1e-9, "fak应直接填入对应字段。");
    AssertClose(project.Geotechnical.BearingCapacityKpa, 110, 1e-9, "仅识别到fak时，fa可见框不应继续显示无关示例值。");
    Assert(project.Geotechnical.UseBearingCapacityCorrection, "仅识别到fak时应自动展开并启用宽深修正区。");
    AssertClose(project.Geotechnical.SoilUnitWeightKnPerM3, 21, 1e-9, "报告土重度应直接填入。");
    AssertClose(project.Geotechnical.GroundwaterDepthM, 5, 1e-9, "地下水冲突时不得擅自选择候选值。");
    AssertClose(project.Geotechnical.BaseFrictionCoefficient, 0.30, 1e-9, "报告未给摩擦系数时不得臆造或覆盖人工值。");
    Assert(!project.Geotechnical.IsConfirmed, "AI直接填入后仍应由用户最终确认。");
    Assert(application.Summary.StartsWith("已直接填入", StringComparison.Ordinal), "结果摘要应明确参数已直接写入输入框。");
    return Task.CompletedTask;
}

static async Task TestRigidShortPileFoundation()
{
    var settings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.RigidShortPile,
        StructuralDesignLoadFactor = 1.4,
        ReinforcementYieldStrengthMpa = 300,
        ConcreteTensileStrengthMpa = 1.43,
        ConcreteCoverMm = 50
    };
    settings.RigidShortPile.IsConfirmed = true;
    settings.RigidShortPile.AboveGroundHeightM = 0.30;
    settings.RigidShortPile.LateralResistanceWidthCoefficient = 0.65;
    settings.RigidShortPile.VerticalReactionEccentricityCoefficient = 0.33;
    settings.RigidShortPile.ConcreteElasticModulusMpa = 30_000;
    settings.RigidShortPile.ConcreteCompressiveStrengthMpa = 14.3;
    settings.RigidShortPile.LongitudinalBarCount = 36;
    settings.RigidShortPile.LongitudinalBarDiameterMm = 22;
    settings.RigidShortPile.StirrupDiameterMm = 10;
    settings.RigidShortPile.StirrupSpacingMm = 150;
    settings.RigidShortPile.SoilLayers =
    [
        new RigidShortPileSoilLayerInput
        {
            Name = "第1层",
            ThicknessM = 1,
            HorizontalResistanceCoefficientMnPerM4 = 0
        },
        new RigidShortPileSoilLayerInput
        {
            Name = "第2层",
            ThicknessM = 1,
            HorizontalResistanceCoefficientMnPerM4 = 12
        },
        new RigidShortPileSoilLayerInput
        {
            Name = "第3层",
            ThicknessM = 6,
            HorizontalResistanceCoefficientMnPerM4 = 12
        }
    ];
    var geometry = new FoundationGeometry
    {
        PileDiameterM = 1.8,
        PileLengthM = 8,
        PedestalHeightM = 0.30
    };
    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry,
        new FoundationLoad
        {
            VerticalKn = 129.2,
            ShearXKn = 90.6,
            MomentYKnM = 1927.5,
            GoverningCase = "单管塔规程计算刚性桩(yy).xls原算例"
        },
        new GeotechnicalInput
        {
            SoilUnitWeightKnPerM3 = 16,
            InternalFrictionAngleDegree = 5,
            GroundwaterDepthM = 9.7
        },
        settings);

    Assert(scheme.FoundationType == FoundationType.RigidShortPile,
        "计算结果必须保留刚性短柱桩基础类型。" );
    var overturning = scheme.Checks.Single(item => item.Code == "RIGID_OVERTURNING");
    AssertClose(overturning.Demand, 1954.68, 0.02,
        "原计算书地面处控制弯矩回归不一致。" );
    AssertClose(overturning.Capacity, 1941.02, 0.08,
        "精确求解β后的抗倾覆承载力Mu/2回归不一致；不得沿用原表未收敛的单变量求解值。" );
    Assert(overturning.Status == CheckStatus.Fail,
        "原表β未收敛导致1.8m×8m误判为通过；精确求根后必须保守判为不满足并要求调大。" );

    var classification = scheme.Checks.Single(item => item.Code == "RIGID_CLASSIFICATION");
    AssertClose(classification.Demand, 1.943, 0.005,
        "JGJ 94第5.7节参数核对后的刚性判别αh回归不一致。" );
    Assert(classification.Status == CheckStatus.Pass,
        "原计算书算例必须满足αh≤2.5刚性判别。" );
    AssertClose(
        scheme.Checks.Single(item => item.Code == "RIGID_TOP_DISPLACEMENT").Demand,
        0.013848,
        0.00001,
        "桩顶水平位移回归不一致。" );
    AssertClose(
        scheme.Checks.Single(item => item.Code == "RIGID_TOP_ROTATION").Demand,
        0.002533,
        0.00001,
        "桩顶转角回归不一致。" );
    Assert(
        scheme.ReinforcementDesigns.Count == 2 &&
        scheme.ReinforcementDesigns.Any(item =>
            item.Component == "刚性短柱桩纵筋" &&
            item.RequiredAreaMm2 > 7_000 &&
            item.ProvidedAreaMm2 > item.RequiredAreaMm2) &&
        scheme.ReinforcementDesigns.Any(item =>
            item.Component == "刚性短柱桩箍筋" &&
            item.CalculatedWeightKg > 0),
        "刚性短柱桩必须形成圆形偏心受压纵筋和箍筋结构化结果。" );

    var outputDirectory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.RigidShortPileOutput.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outputDirectory);
    try
    {
        scheme.Name = "刚性短柱桩原计算书回归方案";
        var project = CreateConfirmedProject();
        project.Name = "刚性短柱桩回归项目";
        project.ProjectType = ProjectType.CommunicationTower;
        project.FoundationSettings = settings;
        project.FoundationLoad = new FoundationLoad
        {
            VerticalKn = 129.2,
            ShearXKn = 90.6,
            MomentYKnM = 1927.5,
            GoverningCase = "原计算书"
        };
        project.Schemes.Add(scheme);
        project.SelectedSchemeId = scheme.Id;
        var output = await new PrototypeOutputPackageService()
            .ExportPrototypePackageAsync(project, outputDirectory);
        var dxf = DecodeDxfUnicodeEscapes(await File.ReadAllTextAsync(
            output.Files.Single(path => path.EndsWith("04_基础配筋图.dxf"))));
        Assert(
            dxf.Contains("RIGID_SHORT_PILE", StringComparison.Ordinal) &&
            dxf.Contains("RIGID_LONGITUDINAL_REBAR", StringComparison.Ordinal) &&
            dxf.Contains("RIGID_STIRRUP", StringComparison.Ordinal),
            "刚性短柱桩DXF必须包含桩身、纵筋和箍筋专用图层。" );
        Assert(output.Files.Any(path => path.EndsWith("01_基础计算书.docx")) &&
               output.Files.Any(path => path.EndsWith("02_配筋及材料表.csv")) &&
               output.Files.Any(path => path.EndsWith("03_工程量.csv")),
            "刚性短柱桩必须导出计算书、材料表和工程量。" );
        var documentXml = await ReadCalculationDocumentXmlAsync(output);
        Assert(documentXml.Contains("Mkd = √(Mx²+My²) + V·hp", StringComparison.Ordinal) &&
               documentXml.Contains("α = (m·b0/EI)^(1/5)", StringComparison.Ordinal) &&
               documentXml.Contains("Asv/s ≥", StringComparison.Ordinal),
            "圆形刚性短柱桩计算书必须包含抗倾覆、刚性判别和箍筋计算公式。" );
        CopyOutputSampleIfRequested(output, "rigid-round");
    }
    finally
    {
        Directory.Delete(outputDirectory, recursive: true);
    }
}

static async Task TestRigidRectangularShortPileFoundation()
{
    var settings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.RigidRectangularShortPile,
        StructuralDesignLoadFactor = 1.4,
        FoundationPermanentLoadFactor = 1.3,
        ReinforcementYieldStrengthMpa = 360,
        ConcreteTensileStrengthMpa = 1.43,
        ConcreteCoverMm = 50
    };
    settings.RigidShortPile.IsConfirmed = true;
    settings.RigidShortPile.AboveGroundHeightM = 0.30;
    settings.RigidShortPile.LateralResistanceWidthCoefficient = 0.65;
    settings.RigidShortPile.VerticalReactionEccentricityCoefficient = 0.33;
    settings.RigidShortPile.ConcreteElasticModulusMpa = 30_000;
    settings.RigidShortPile.ConcreteCompressiveStrengthMpa = 14.3;
    settings.RigidShortPile.LongitudinalBarCount = 36;
    settings.RigidShortPile.LongitudinalBarDiameterMm = 22;
    settings.RigidShortPile.StirrupDiameterMm = 10;
    settings.RigidShortPile.StirrupSpacingMm = 150;
    settings.RigidShortPile.StirrupLegCount = 2;
    settings.RigidShortPile.SoilLayers =
    [
        new RigidShortPileSoilLayerInput
        {
            Name = "表层土",
            ThicknessM = 1,
            HorizontalResistanceCoefficientMnPerM4 = 8
        },
        new RigidShortPileSoilLayerInput
        {
            Name = "粉质黏土",
            ThicknessM = 3,
            HorizontalResistanceCoefficientMnPerM4 = 20
        },
        new RigidShortPileSoilLayerInput
        {
            Name = "密实层",
            ThicknessM = 4,
            HorizontalResistanceCoefficientMnPerM4 = 35
        }
    ];
    var geometry = new FoundationGeometry
    {
        BaseLengthM = 2.0,
        BaseWidthM = 1.6,
        PileLengthM = 6.0,
        PedestalLengthM = 2.0,
        PedestalWidthM = 1.6,
        PedestalHeightM = 0.30
    };
    var load = new FoundationLoad
    {
        VerticalKn = 300,
        ShearXKn = 20,
        ShearYKn = 15,
        MomentXKnM = 100,
        MomentYKnM = 160,
        GoverningCase = "矩形刚性短柱桩双向标准组合"
    };
    var geotechnical = new GeotechnicalInput
    {
        SoilUnitWeightKnPerM3 = 18,
        InternalFrictionAngleDegree = 15,
        GroundwaterDepthM = 10
    };
    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry,
        load,
        geotechnical,
        settings);

    Assert(scheme.FoundationType == FoundationType.RigidRectangularShortPile,
        "矩形分支必须保留独立的基础类型，不能回落成圆形刚性短柱桩。" );
    Assert(scheme.Checks.Any(item => item.Code == "RIGID_RECT_OVERTURNING_X") &&
           scheme.Checks.Any(item => item.Code == "RIGID_RECT_OVERTURNING_Y") &&
           scheme.Checks.Any(item => item.Code == "RIGID_RECT_CLASSIFICATION_X") &&
           scheme.Checks.Any(item => item.Code == "RIGID_RECT_CLASSIFICATION_Y"),
        "矩形刚性短柱桩必须分别形成X、Y向抗倾覆和刚性判别。" );
    Assert(scheme.Checks.Single(item => item.Code == "RIGID_RECT_CLASSIFICATION_X")
               .Explanation.Contains("b0=2.600", StringComparison.Ordinal) &&
           scheme.Checks.Single(item => item.Code == "RIGID_RECT_CLASSIFICATION_Y")
               .Explanation.Contains("b0=3.000", StringComparison.Ordinal),
        "JGJ 94方形桩b0应按两个方向的垂直投影边宽分别计算。" );
    Assert(scheme.Checks.Any(item =>
               item.Code == "RIGID_RECT_BIAXIAL_COMPRESSION" &&
               item.RuleReference.Contains("6.2.21", StringComparison.Ordinal)) &&
           scheme.ReinforcementDesigns.Any(item =>
               item.Component == "刚性短柱桩－矩形纵筋" &&
               item.CalculatedWeightKg > 0) &&
           scheme.ReinforcementDesigns.Any(item =>
               item.Component == "刚性短柱桩－矩形箍筋" &&
               item.CalculatedWeightKg > 0),
        "矩形分支必须形成GB 50010双向偏压纵筋和矩形闭合箍结构化结果。" );
    var rectangularStirrup = scheme.ReinforcementDesigns.Single(item =>
        item.Component == "刚性短柱桩－矩形箍筋");
    AssertClose(rectangularStirrup.StirrupBodyPerimeterM, 6.80, 1e-9,
        "矩形箍筋箍身周长应按构件外包尺寸扣除两侧保护层计算。" );
    AssertClose(rectangularStirrup.HookBendAllowanceM, 0.038, 1e-9,
        "Φ10矩形箍筋两端135度弯钩弯曲增量应按2×1.9d计入。" );
    AssertClose(rectangularStirrup.HookStraightAllowanceM, 0.20, 1e-9,
        "Φ10矩形箍筋两端弯后平直段应按2×max(10d,75mm)计入。" );
    AssertClose(rectangularStirrup.SingleBarLengthM, 7.038, 1e-9,
        "矩形箍筋单根下料长度必须包含箍身、弯钩量度差和两端弯后平直段。" );
    AssertClose(scheme.Quantities.ConcreteM3, 20.16, 0.001,
        "矩形桩身混凝土体积应按截面长×宽×总高度计算。" );

    var outputDirectory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.RigidRectangularShortPileOutput.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outputDirectory);
    try
    {
        scheme.Name = "矩形刚性短柱桩回归方案";
        var project = CreateConfirmedProject();
        project.Name = "矩形刚性短柱桩回归项目";
        project.ProjectType = ProjectType.CommunicationTower;
        project.FoundationSettings = settings;
        project.FoundationLoad = load;
        project.Geotechnical = geotechnical;
        project.Schemes.Add(scheme);
        project.SelectedSchemeId = scheme.Id;
        var output = await new PrototypeOutputPackageService()
            .ExportPrototypePackageAsync(project, outputDirectory);
        var dxf = DecodeDxfUnicodeEscapes(await File.ReadAllTextAsync(
            output.Files.Single(path => path.EndsWith("04_基础配筋图.dxf"))));
        Assert(
            dxf.Contains("RIGID_RECT_SHORT_PILE", StringComparison.Ordinal) &&
            dxf.Contains("RIGID_RECT_LONGITUDINAL_REBAR", StringComparison.Ordinal) &&
            dxf.Contains("RIGID_RECT_STIRRUP", StringComparison.Ordinal),
            "矩形刚性短柱桩DXF必须包含矩形桩身、周边纵筋和闭合箍专用图层。" );
        Assert(output.Files.Any(path => path.EndsWith("01_基础计算书.docx")) &&
               output.Files.Any(path => path.EndsWith("02_配筋及材料表.csv")) &&
               output.Files.Any(path => path.EndsWith("03_工程量.csv")),
            "矩形刚性短柱桩必须导出计算书、材料表、工程量和DXF。" );
        var documentXml = await ReadCalculationDocumentXmlAsync(output);
        Assert(documentXml.Contains("沿i方向按受力边长", StringComparison.Ordinal) &&
               documentXml.Contains("1/Nu ≈ 1/Nux + 1/Nuy - 1/Nu0", StringComparison.Ordinal) &&
               documentXml.Contains("Asv/s按X、Y方向", StringComparison.Ordinal) &&
               documentXml.Contains("max(10d,75mm)", StringComparison.Ordinal),
            "矩形刚性短柱桩计算书必须包含双向抗倾覆、双向偏压、双向箍筋及箍筋下料公式。" );
        var cuttingSchedule = await File.ReadAllTextAsync(
            output.Files.Single(path => path.EndsWith("05_钢筋下料表.csv")));
        Assert(cuttingSchedule.Contains("已计入矩形箍筋135°弯钩量度差和两端弯后平直段", StringComparison.Ordinal),
            "矩形箍筋下料表必须明确弯钩量度差和两端弯后平直段已经计量。" );
        var materialSchedule = await File.ReadAllTextAsync(
            output.Files.Single(path => path.EndsWith("02_配筋及材料表.csv")));
        Assert(materialSchedule.Contains("135度弯钩量度增量(m)", StringComparison.Ordinal) &&
               materialSchedule.Contains("两端弯后平直段增量(m)", StringComparison.Ordinal) &&
               materialSchedule.Contains("22G101-3第2-7页", StringComparison.Ordinal),
            "材料表必须分列矩形箍筋弯钩量度增量、平直段增量并追溯22G101-3。" );
        CopyOutputSampleIfRequested(output, "rigid-rectangle");
    }
    finally
    {
        Directory.Delete(outputDirectory, recursive: true);
    }
}

static async Task TestCircularShortColumnFoundation()
{
    var settings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.CircularShortColumn,
        PedestalDiameterM = 0.80
    };
    var geometry = new FoundationGeometry
    {
        BaseLengthM = 3.0,
        BaseWidthM = 3.0,
        BaseThicknessM = 0.8,
        PedestalLengthM = 0.8,
        PedestalWidthM = 0.8,
        PedestalHeightM = 1.2
    };
    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry,
        new FoundationLoad
        {
            VerticalKn = 80,
            ShearXKn = 5,
            MomentYKnM = 20,
        GoverningCase = "独立基础－圆形柱算例"
        },
        new GeotechnicalInput
        {
            BearingCapacityKpa = 180,
            SoilUnitWeightKnPerM3 = 18,
            BaseFrictionCoefficient = 0.35,
            GroundwaterDepthM = 5
        },
        settings);

    var expectedConcrete =
        3.0 * 3.0 * 0.8 +
        Math.PI * 0.8 * 0.8 / 4 * 1.2;
    Assert(
        scheme.FoundationType == FoundationType.CircularShortColumn,
        "计算结果必须保留独立基础－圆形柱类型。");
    AssertClose(
        scheme.Quantities.ConcreteM3,
        expectedConcrete,
        1e-9,
        "独立基础－圆形柱的柱体混凝土量必须按圆面积计算。");
    Assert(
        scheme.Checks.Any(item => item.Code.StartsWith("PUNCHING_", StringComparison.Ordinal)),
        "独立基础－圆形柱必须执行冲切验算。");

    var outputDirectory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.CircularFoundationCad.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outputDirectory);
    try
    {
        scheme.Name = "圆形短柱独立基础CAD算例";
        var project = CreateConfirmedProject();
        project.FoundationSettings = settings;
        project.Schemes.Add(scheme);
        project.SelectedSchemeId = scheme.Id;
        var output = await new PrototypeOutputPackageService()
            .ExportPrototypePackageAsync(project, outputDirectory);
        var dxf = DecodeDxfUnicodeEscapes(await File.ReadAllTextAsync(
            output.Files.Single(path => path.EndsWith("04_基础配筋图.dxf"))));
        Assert(dxf.Contains("DJj01", StringComparison.Ordinal) &&
               dxf.Contains("DZ01 圆形直径800", StringComparison.Ordinal) &&
               dxf.Contains("REBAR_REVEAL_BOUNDARY", StringComparison.Ordinal),
            "圆形短柱独立基础DXF必须使用DJj/DZ集中标注并局部揭示底筋。" );
        CopyOutputSampleIfRequested(output, "circular");
    }
    finally
    {
        Directory.Delete(outputDirectory, recursive: true);
    }
}

static async Task TestRaftFoundation()
{
    var settings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.Raft,
        PedestalLengthM = 1.8,
        PedestalWidthM = 1.8,
        PedestalHeightM = 1.2
    };
    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        new FoundationGeometry
        {
            BaseLengthM = 5.0,
            BaseWidthM = 5.0,
            BaseThicknessM = 0.8,
            PedestalLengthM = 1.8,
            PedestalWidthM = 1.8,
            PedestalHeightM = 1.2
        },
        new FoundationLoad
        {
            VerticalKn = 120,
            ShearXKn = 10,
            MomentYKnM = 80,
            GoverningCase = "中央塔柱筏板算例"
        },
        new GeotechnicalInput
        {
            BearingCapacityKpa = 160,
            SoilUnitWeightKnPerM3 = 18,
            BaseFrictionCoefficient = 0.30,
            GroundwaterDepthM = 5
        },
        settings);

    Assert(
        scheme.FoundationType == FoundationType.Raft,
        "计算结果必须保留中央塔柱筏板基础类型。");
    Assert(
        scheme.Checks.Any(item => item.Code == "BEARING_AVERAGE") &&
        scheme.Checks.Any(item => item.Code.StartsWith("BOTTOM_REINFORCEMENT_", StringComparison.Ordinal)),
        "筏板基础必须执行地基承载力和双向底筋验算。");
    Assert(
        scheme.Quantities.ConcreteM3 > 20,
        "筏板基础工程量应包含大底板和中央塔柱。");
    AssertClose(scheme.Quantities.ConcreteM3, 23.888, 0.000001,
        "筏板金标准混凝土量发生漂移。");

    var multiLegSettings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.Raft,
        PedestalLengthM = 0.8,
        PedestalWidthM = 0.8,
        PedestalHeightM = 1.0,
        MaximumBaseLengthM = 8.0,
        MaximumBaseWidthM = 8.0
    };
    multiLegSettings.Pile.PileCount = 3;
    multiLegSettings.Pile.PileCenterSpacingM = 5.5;
    var multiLegScheme = new RectangularShortColumnFoundationCalculator().Calculate(
        new FoundationGeometry
        {
            BaseLengthM = 7.6,
            BaseWidthM = 7.6,
            BaseThicknessM = 0.9,
            PedestalLengthM = 0.8,
            PedestalWidthM = 0.8,
            PedestalHeightM = 1.0,
            PileCount = 3,
            PileCenterSpacingM = 5.5
        },
        new FoundationLoad
        {
            VerticalKn = 360,
            ShearXKn = 20,
            MomentYKnM = 120,
            GoverningCase = "三塔脚共用筏板算例"
        },
        new GeotechnicalInput
        {
            BearingCapacityKpa = 160,
            SoilUnitWeightKnPerM3 = 18,
            BaseFrictionCoefficient = 0.30,
            GroundwaterDepthM = 5
        },
        multiLegSettings);
    Assert(multiLegScheme.Checks.Any(item =>
               item.Code == "RAFT_TOWER_LEG_LAYOUT" && item.Status == CheckStatus.Pass) &&
           multiLegScheme.Checks.Any(item =>
               item.Code == "RAFT_MULTI_LEG_LOCAL_ANALYSIS" && item.Status == CheckStatus.SpecialReview),
        "三塔脚共用筏板必须校验实际根开包络，并把柱下局部冲切及配筋保留为整体分析专项门禁。" );
    AssertClose(multiLegScheme.Quantities.ConcreteM3, 53.904, 0.000001,
        "三塔脚共用筏板混凝土量必须包含三根实际短柱，不能仍按虚构中央短柱计量。");

    var outputDirectory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.RaftCad.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outputDirectory);
    try
    {
        scheme.Name = "中央塔柱筏板CAD算例";
        var project = CreateConfirmedProject();
        project.FoundationSettings = settings;
        project.Schemes.Add(scheme);
        project.SelectedSchemeId = scheme.Id;
        var output = await new PrototypeOutputPackageService()
            .ExportPrototypePackageAsync(project, outputDirectory);
        var dxf = DecodeDxfUnicodeEscapes(await File.ReadAllTextAsync(
            output.Files.Single(path => path.EndsWith("04_基础配筋图.dxf"))));
        Assert(dxf.Contains("BPB01", StringComparison.Ordinal) &&
               dxf.Contains("筏板基础平面图", StringComparison.Ordinal) &&
               dxf.Contains("T：筏板顶筋待结构计算确认", StringComparison.Ordinal) &&
               dxf.Contains("基础底标高：待项目标高确认", StringComparison.Ordinal),
            "筏板DXF必须使用独立编号、成套平剖视图，并对未确认标高保持待确认。" );
        CopyOutputSampleIfRequested(output, "raft");
    }
    finally
    {
        Directory.Delete(outputDirectory, recursive: true);
    }
}

static async Task TestPileFoundation()
{
    var settings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.Pile,
        PedestalLengthM = 0.8,
        PedestalWidthM = 0.8,
        PedestalHeightM = 1.2
    };
    settings.Pile.IsConfirmed = true;
    settings.Pile.AboveGroundHeightM = 0.3;

    var load = new FoundationLoad
    {
        VerticalKn = 800,
        ShearXKn = 50,
        MomentXKnM = 200,
        MomentYKnM = 200,
        GoverningCase = "单根灌注桩算例",
        BasicCombination = new FoundationLoadCombination
        {
            VerticalKn = 900,
            ShearXKn = 65,
            MomentXKnM = 260,
            MomentYKnM = 260,
            GoverningCase = "单根灌注桩结构基本组合"
        }
    };
    var geotechnical = new GeotechnicalInput
    {
        SoilUnitWeightKnPerM3 = 18,
        GroundwaterDepthM = 5
    };

    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        new FoundationGeometry
        {
            PedestalLengthM = 1.0,
            PedestalWidthM = 1.0,
            PedestalHeightM = 0.3,
            PileDiameterM = 1.0,
            PileLengthM = 12
        },
        load,
        geotechnical,
        settings);

    Assert(
        scheme.FoundationType == FoundationType.Pile,
        "计算结果必须为单桩灌注桩基础。");
    Assert(
        scheme.Checks.Single(item => item.Code == "PILE_COMPRESSION").Status ==
        CheckStatus.Pass,
        "算例必须通过单桩竖向抗压承载力验算。");
    Assert(
        scheme.Checks.Single(item => item.Code == "PILE_UPLIFT").Status ==
        CheckStatus.Pass,
        "算例必须通过单桩抗拔承载力验算。");
    Assert(
        scheme.Checks.Single(item => item.Code == "PILE_HORIZONTAL").Status ==
        CheckStatus.Pass,
        "算例必须通过用户确认的单桩水平承载力门禁。");
    Assert(
        scheme.Checks.Any(item => item.Code == "PILE_M_METHOD_CLASSIFICATION") &&
        scheme.Checks.Any(item => item.Code == "PILE_INTERNAL_FORCE_ENVELOPE") &&
        scheme.Checks.Any(item => item.Code == "PILE_STRUCTURAL_COMBINATION" && item.Status == CheckStatus.Pass),
        "参数与基本组合齐全时，必须形成m法换算深度、桩身内力包络和结构组合验算。" );
    Assert(
        scheme.ReinforcementDesigns.Any(item =>
            item.Component.Contains("桩身纵筋", StringComparison.Ordinal) &&
            item.CalculatedWeightKg > 0) &&
        scheme.ReinforcementDesigns.Any(item =>
            item.Component.Contains("桩身箍筋", StringComparison.Ordinal) &&
            item.CalculatedWeightKg > 0),
        "单桩灌注桩必须形成纵筋和箍筋的结构化面积、长度和重量结果。" );
    Assert(
        scheme.Quantities.BackfillM3 == 0 &&
        scheme.Geometry.BaseLengthM == 0 &&
        scheme.Geometry.BaseWidthM == 0,
        "单桩灌注桩不得生成承台体积、承台尺寸或基坑回填量。");
    AssertClose(scheme.Quantities.ConcreteM3, 9.6603974103, 0.000001,
        "单桩灌注桩金标准混凝土量发生漂移。");

    var outputDirectory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.PileOutput.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outputDirectory);
    try
    {
        scheme.Name = "单桩灌注桩类型化输出算例";
        var project = CreateConfirmedProject();
        project.ProjectType = ProjectType.CommunicationTower;
        project.FoundationSettings = settings;
        project.FoundationLoad = load;
        project.Geotechnical = geotechnical;
        project.Schemes.Add(scheme);
        project.SelectedSchemeId = scheme.Id;
        var output = await new PrototypeOutputPackageService()
            .ExportPrototypePackageAsync(project, outputDirectory);
        var dxf = DecodeDxfUnicodeEscapes(await File.ReadAllTextAsync(
            output.Files.Single(path => path.EndsWith("04_基础配筋图.dxf"))));
        Assert(
            dxf.Contains("CAST_IN_PLACE_PILE", StringComparison.Ordinal) &&
            dxf.Contains("PILE_LONGITUDINAL_REBAR", StringComparison.Ordinal) &&
            dxf.Contains("不设承台", StringComparison.Ordinal),
            "单管塔灌注桩 DXF 必须只包含一根桩及其纵筋，并明确不含承台。 ");
        var documentXml = await ReadCalculationDocumentXmlAsync(output);
        Assert(documentXml.Contains("单桩竖向抗压承载力", StringComparison.Ordinal) &&
               documentXml.Contains("R = [uΣ(ψsi·qsik·li) + ψp·qpk·Ap]/K", StringComparison.Ordinal) &&
               documentXml.Contains("Rt = uΣ(λi·qsik·li)/K + Gp,eff", StringComparison.Ordinal) &&
               documentXml.Contains("单桩水平抗剪承载力", StringComparison.Ordinal),
            "灌注桩计算书必须包含抗压、抗拔和水平抗剪计算公式。" );
        CopyOutputSampleIfRequested(output, "pile");

        settings.Pile.PileCount = 3;
        settings.Pile.TieBeamRequired = true;
        settings.Pile.PileCenterSpacingM = 3;
        settings.Pile.TieBeamWidthM = 0.4;
        settings.Pile.TieBeamHeightM = 0.6;
        var threePileScheme = new RectangularShortColumnFoundationCalculator().Calculate(
            new FoundationGeometry
            {
                PedestalLengthM = 1,
                PedestalWidthM = 1,
                PedestalHeightM = 0.3,
                PileDiameterM = 1,
                PileLengthM = 12,
                PileCount = 3,
                PileCenterSpacingM = 3,
                TieBeamCount = 3,
                TieBeamWidthM = 0.4,
                TieBeamHeightM = 0.6
            },
            new FoundationLoad
            {
                UsesIndividualPileReactions = true,
                IndividualPileCompressionKn = 800,
                IndividualPileUpliftKn = 300,
                IndividualPileHorizontalKn = 50,
                FoundationUnitCount = 3,
                TieBeamsRequired = true,
                GoverningCase = "三管塔单塔腿标准组合"
            },
            new GeotechnicalInput
            {
                SoilUnitWeightKnPerM3 = 18,
                GroundwaterDepthM = 5
            },
            settings);
        Assert(
            threePileScheme.Geometry.PileCount == 3 &&
            threePileScheme.Geometry.TieBeamCount == 3 &&
            threePileScheme.ReinforcementDesigns[0].BarCount ==
            3 * settings.Pile.PileMainBarCount &&
            threePileScheme.Quantities.ConcreteM3 > 3 * scheme.Quantities.ConcreteM3,
            "三管塔必须形成3根分别验算的独立灌注桩、3根连梁及三倍桩身纵筋工程量。" );
        threePileScheme.Name = "三管塔三桩连梁输出算例";
        var threePileProject = CreateConfirmedProject();
        threePileProject.ProjectType = ProjectType.CommunicationTower;
        threePileProject.Schemes.Add(threePileScheme);
        threePileProject.SelectedSchemeId = threePileScheme.Id;
        var threePileOutput = await new PrototypeOutputPackageService()
            .ExportPrototypePackageAsync(threePileProject, outputDirectory);
        var threePileDxf = DecodeDxfUnicodeEscapes(await File.ReadAllTextAsync(
            threePileOutput.Files.Single(path => path.EndsWith("04_基础配筋图.dxf"))));
        Assert(
            threePileDxf.Contains("TIE_BEAM", StringComparison.Ordinal) &&
            threePileDxf.Contains("GZH01共3根", StringComparison.Ordinal) &&
            threePileDxf.Contains("不设承台", StringComparison.Ordinal),
            "三管塔DXF必须绘制3根独立桩和3根连梁，并明确无承台。" );

        settings.Pile.PileCount = 4;
        settings.Pile.PileCenterSpacingM = 4;
        var fourPileScheme = new RectangularShortColumnFoundationCalculator().Calculate(
            new FoundationGeometry
            {
                PedestalLengthM = 1,
                PedestalWidthM = 1,
                PedestalHeightM = 0.3,
                PileDiameterM = 1,
                PileLengthM = 12,
                PileCount = 4,
                PileCenterSpacingM = 4,
                TieBeamCount = 4,
                TieBeamWidthM = 0.4,
                TieBeamHeightM = 0.6
            },
            new FoundationLoad
            {
                UsesIndividualPileReactions = true,
                IndividualPileCompressionKn = 750,
                IndividualPileUpliftKn = 260,
                IndividualPileHorizontalKn = 45,
                FoundationUnitCount = 4,
                TieBeamsRequired = true,
                GoverningCase = "角钢塔单塔腿标准组合"
            },
            new GeotechnicalInput
            {
                SoilUnitWeightKnPerM3 = 18,
                GroundwaterDepthM = 5
            },
            settings);
        Assert(
            fourPileScheme.Geometry.PileCount == 4 &&
            fourPileScheme.Geometry.TieBeamCount == 4 &&
            fourPileScheme.ReinforcementDesigns[0].BarCount ==
            4 * settings.Pile.PileMainBarCount,
            "角钢塔必须形成4根分别验算的独立灌注桩、4根周边连梁及四倍桩身纵筋工程量。" );
        fourPileScheme.Name = "角钢塔四桩连梁输出算例";
        var fourPileProject = CreateConfirmedProject();
        fourPileProject.ProjectType = ProjectType.CommunicationTower;
        fourPileProject.Schemes.Add(fourPileScheme);
        fourPileProject.SelectedSchemeId = fourPileScheme.Id;
        var fourPileOutput = await new PrototypeOutputPackageService()
            .ExportPrototypePackageAsync(fourPileProject, outputDirectory);
        var fourPileDxf = DecodeDxfUnicodeEscapes(await File.ReadAllTextAsync(
            fourPileOutput.Files.Single(path => path.EndsWith("04_基础配筋图.dxf"))));
        Assert(
            fourPileDxf.Contains("TIE_BEAM", StringComparison.Ordinal) &&
            fourPileDxf.Contains("GZH01共4根", StringComparison.Ordinal) &&
            fourPileDxf.Contains("连系梁共4根；不设承台", StringComparison.Ordinal),
            "角钢塔DXF必须绘制4根独立桩和4根周边连梁，并明确无承台。" );
    }
    finally
    {
        Directory.Delete(outputDirectory, recursive: true);
    }
}

static Task TestPileNormativeCorrections()
{
    var geometry = new FoundationGeometry
    {
        PedestalLengthM = 1.2,
        PedestalWidthM = 1.2,
        PedestalHeightM = 0.3,
        PileDiameterM = 1.2,
        PileLengthM = 12
    };
    var load = new FoundationLoad
    {
        VerticalKn = 100,
        GoverningCase = "单桩规范修正回归"
    };
    var geotechnical = new GeotechnicalInput
    {
        SoilUnitWeightKnPerM3 = 18,
        GroundwaterDepthM = 5
    };
    var calculator = new RectangularShortColumnFoundationCalculator();

    var claySettings = BuildPileRegressionSettings(isSandOrGravel: false, useConfirmedForces: true);
    var sandSettings = BuildPileRegressionSettings(isSandOrGravel: true, useConfirmedForces: true);
    var clay = calculator.Calculate(geometry, load, geotechnical, claySettings);
    var sand = calculator.Calculate(geometry, load, geotechnical, sandSettings);
    var clayCompression = clay.Checks.Single(item => item.Code == "PILE_COMPRESSION");
    var sandCompression = sand.Checks.Single(item => item.Code == "PILE_COMPRESSION");
    Assert(
        sandCompression.Capacity < clayCompression.Capacity,
        "大直径桩在砂土/碎石层中的1/3指数应比黏性土/粉土指数产生更大的尺寸效应折减。" );

    var uplift = clay.Checks.Single(item => item.Code == "PILE_UPLIFT");
    AssertClose(
        uplift.Demand,
        80,
        1e-9,
        "JGJ 94式(5.4.5-2)已在承载力侧采用Tuk/2，不得再对Nk重复乘1.2。" );
    Assert(
        uplift.RuleReference.Contains("5.4.5-2", StringComparison.Ordinal),
        "抗拔结果必须保留JGJ 94式(5.4.5-2)引用。" );

    var automaticSettings = BuildPileRegressionSettings(isSandOrGravel: false, useConfirmedForces: false);
    var automatic = calculator.Calculate(geometry, load, geotechnical, automaticSettings);
    var compression = automatic.Checks.Single(item => item.Code == "PILE_COMPRESSION");
    AssertClose(
        compression.Demand,
        100,
        1e-9,
        "单桩灌注桩必须直接采用塔脚竖向力，不得做群桩轴力分配或叠加虚构承台自重。" );
    Assert(
        compression.Explanation.Contains("Nk≤R", StringComparison.Ordinal) &&
        automatic.Checks.All(item =>
            item.Code is not "PILE_LOAD_DISTRIBUTION" and
            not "PILE_COMPRESSION_AVERAGE"),
        "单桩应按Nk≤R校核，结果中不得残留群桩平均压力或轴力分配项目。" );
    return Task.CompletedTask;
}

static FoundationDesignSettings BuildPileRegressionSettings(
    bool isSandOrGravel,
    bool useConfirmedForces)
{
    var settings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.Pile,
        PedestalLengthM = 0.8,
        PedestalWidthM = 0.8,
        PedestalHeightM = 1.2,
        MaximumBaseLengthM = 8,
        MaximumBaseWidthM = 8
    };
    settings.Pile.IsConfirmed = true;
    settings.Pile.UseUserConfirmedPileHeadForces = useConfirmedForces;
    settings.Pile.MaximumPileCompressionKn = 100;
    settings.Pile.MaximumPileUpliftKn = 80;
    settings.Pile.AboveGroundHeightM = 0.3;
    settings.Pile.SoilLayers =
    [
        new PileSoilLayerInput
        {
            Name = isSandOrGravel ? "砂土层" : "粉质黏土层",
            ThicknessM = 12,
            SideResistanceKpa = 60,
            TipResistanceKpa = 1300,
            UpliftCoefficient = 0.70,
            IsSandOrGravel = isSandOrGravel
        }
    ];
    return settings;
}

static Task TestCalculatedResultStatusSeparation()
{
    var (geometry, load, geotechnical, settings) =
        BuildRigidRectangularSpecialtyCase();
    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry,
        load,
        geotechnical,
        settings);

    Assert(
        scheme.Checks.Single(item => item.Code == "RIGID_RECT_INTERNAL_FORCE").Status ==
        CheckStatus.Result,
        "最不利内力是计算过程结果，不得显示为待完善或安全性通过。" );
    Assert(
        scheme.Checks.Single(item => item.Code == "RIGID_RECT_DISPLACEMENT_X").Status ==
        CheckStatus.SpecialReview,
        "未确认允许位移时，应保留计算值并自动转专业核对。" );
    Assert(
        scheme.CalculatedResults.Any(item => item.Code == "RIGID_RECT_SERVICE_MOMENT_X") &&
        scheme.ScopeAndInputItems.Any(item => item.Code == "RIGID_RECT_DISPLACEMENT_X") &&
        scheme.VerificationChecks.All(item => item.Status is CheckStatus.Pass or CheckStatus.Fail),
        "结果、待补参数和安全性验算必须分栏。" );
    Assert(!scheme.IsFormalVerificationComplete, "待补允许值时不得形成全部验算完成结论。" );
    return Task.CompletedTask;
}

static Task TestDeformationLimitSourceGate()
{
    var (geometry, load, geotechnical, settings) =
        BuildRigidRectangularSpecialtyCase();
    var baseline = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry,
        load,
        geotechnical,
        settings);
    var displacement = baseline.Checks
        .Where(item => item.Code.StartsWith("RIGID_RECT_DISPLACEMENT_", StringComparison.Ordinal))
        .Max(item => item.Demand);
    var rotation = baseline.Checks
        .Where(item => item.Code.StartsWith("RIGID_RECT_ROTATION_", StringComparison.Ordinal))
        .Max(item => item.Demand);
    settings.SpecialtyDesign.Deformation.AllowableTopDisplacementMm =
        displacement * 1000 * 1.5;
    settings.SpecialtyDesign.Deformation.AllowableTopRotationRad = rotation * 1.5;
    settings.SpecialtyDesign.Deformation.Source = new EngineeringParameterSource
    {
        SourceType = ParameterSourceType.Manual,
        SourceDocument = "塔脚连接技术条件",
        SourceLocation = "第3.2条",
        IsConfirmed = true
    };
    var passing = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry,
        load,
        geotechnical,
        settings);
    Assert(
        passing.Checks.Where(item =>
                item.Code.StartsWith("RIGID_RECT_DISPLACEMENT_", StringComparison.Ordinal) ||
                item.Code.StartsWith("RIGID_RECT_ROTATION_", StringComparison.Ordinal))
            .All(item => item.Status == CheckStatus.Pass),
        "已确认来源且允许值足够时，变形验算应形成通过结论。" );

    settings.SpecialtyDesign.Deformation.AllowableTopDisplacementMm =
        displacement * 1000 * 0.5;
    var failing = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry,
        load,
        geotechnical,
        settings);
    Assert(
        failing.Checks.Any(item =>
            item.Code.StartsWith("RIGID_RECT_DISPLACEMENT_", StringComparison.Ordinal) &&
            item.Status == CheckStatus.Fail),
        "位移超过已确认允许值时必须明确不通过。" );
    return Task.CompletedTask;
}

static Task TestSettlementVerification()
{
    var settings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.RectangularShortColumn
    };
    settings.SpecialtyDesign.Settlement = new SettlementDesignInput
    {
        AllowableSettlementMm = 1000,
        ExperienceCoefficient = 1,
        SoilLayers =
        [
            new SettlementSoilLayerInput { Name = "粉质黏土", ThicknessM = 2, CompressionModulusMpa = 8 },
            new SettlementSoilLayerInput { Name = "密实粉土", ThicknessM = 4, CompressionModulusMpa = 15 }
        ],
        Source = new EngineeringParameterSource
        {
            SourceDocument = "地勘报告",
            SourceLocation = "表6.2",
            IsConfirmed = true
        }
    };
    var geometry = new FoundationGeometry
    {
        BaseLengthM = 3,
        BaseWidthM = 3,
        BaseThicknessM = 0.8,
        PedestalLengthM = 0.8,
        PedestalWidthM = 0.8,
        PedestalHeightM = 1.2
    };
    var load = new FoundationLoad
    {
        VerticalKn = 100,
        ShearXKn = 5,
        MomentYKnM = 20,
        GoverningCase = "沉降标准组合"
    };
    var geotechnical = new GeotechnicalInput
    {
        BearingCapacityKpa = 180,
        SoilUnitWeightKnPerM3 = 18,
        BaseFrictionCoefficient = 0.35,
        GroundwaterDepthM = 10
    };
    var passing = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry,
        load,
        geotechnical,
        settings);
    var settlement = passing.Checks.Single(item => item.Code == "SETTLEMENT");
    Assert(settlement.Status == CheckStatus.Pass && settlement.Demand > 0,
        "有效分层、Es和允许值齐全时必须形成可追溯沉降计算。" );
    Assert(settlement.RuleReference.Contains("5.3.5", StringComparison.Ordinal),
        "浅基础沉降必须保留GB 50007第5.3.5条依据。" );

    settings.SpecialtyDesign.Settlement.AllowableSettlementMm = settlement.Demand / 2;
    var failing = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry,
        load,
        geotechnical,
        settings);
    Assert(failing.Checks.Single(item => item.Code == "SETTLEMENT").Status == CheckStatus.Fail,
        "沉降超过允许值时必须明确不通过。" );
    return Task.CompletedTask;
}

static Task TestCrackAndAnchorVerification()
{
    var (geometry, load, geotechnical, settings) =
        BuildRigidRectangularSpecialtyCase();
    settings.SpecialtyDesign.Crack.EnvironmentCategory = "二a类";
    settings.SpecialtyDesign.Crack.MaximumCrackWidthMm = 0.20;
    settings.SpecialtyDesign.Crack.Source.IsConfirmed = true;
    settings.SpecialtyDesign.AnchorBolts = new AnchorBoltDesignInput
    {
        ConnectionType = AnchorConnectionType.AnchorBoltCage,
        BoltCount = 8,
        NominalDiameterMm = 36,
        BoltCircleDiameterM = 1.2,
        TensileStrengthDesignMpa = 180,
        ShearStrengthDesignMpa = 140,
        ThreadStressAreaFactor = 0.78,
        EmbedmentDepthM = 1.2,
        Source = new EngineeringParameterSource
        {
            SourceDocument = "塔脚锚栓详图",
            SourceLocation = "M36-8，D=1200",
            IsConfirmed = true
        }
    };
    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry,
        load,
        geotechnical,
        settings);
    Assert(
        scheme.Checks.Any(item => item.Code == "CRACK_WIDTH" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail),
        "矩形刚性短柱桩在裂缝参数齐全时应执行GB 50010确定性裂缝验算。" );
    Assert(
        scheme.Checks.Any(item => item.Code == "ANCHOR_STEEL_TENSION") &&
        scheme.Checks.Any(item => item.Code == "ANCHOR_STEEL_SHEAR") &&
        scheme.Checks.Any(item => item.Code == "ANCHOR_STEEL_INTERACTION"),
        "锚栓参数齐全时应形成受拉、受剪和拉剪组合三项钢材验算。" );
    Assert(
        scheme.Checks.Any(item => item.Code == "ANCHOR_CONCRETE_FAILURE" &&
            item.Status == CheckStatus.PendingInput) &&
        scheme.Checks.Any(item => item.Code == "ANCHOR_PLATE_DETAIL" &&
            item.Status == CheckStatus.PendingInput),
        "无完整节点边距和下锚板信息时，混凝土锥体破坏不得假装通过。" );
    return Task.CompletedTask;
}

static Task TestSpecialtyAutoFillAndStatusRouting()
{
    var geometry = new FoundationGeometry
    {
        BaseLengthM = 3,
        BaseWidthM = 3,
        BaseThicknessM = 0.8,
        PedestalLengthM = 0.8,
        PedestalWidthM = 0.8,
        PedestalHeightM = 1.2
    };
    var load = new FoundationLoad
    {
        VerticalKn = 80,
        ShearXKn = 8,
        MomentYKnM = 40,
        GoverningCase = "专项状态分流测试"
    };
    var geotechnical = new GeotechnicalInput
    {
        BearingCapacityKpa = 180,
        SoilUnitWeightKnPerM3 = 18,
        BaseFrictionCoefficient = 0.35,
        GroundwaterDepthM = 10
    };
    var settings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.RectangularShortColumn
    };
    var calculator = new RectangularShortColumnFoundationCalculator();
    var scheme = calculator.Calculate(geometry, load, geotechnical, settings);
    Assert(scheme.Checks.Any(item => item.Code == "SETTLEMENT" && item.Status == CheckStatus.SpecialReview),
        "浅基础缺少原始沉降分层时应自动转专业核对，不要求普通用户猜数。" );
    Assert(scheme.Checks.Any(item => item.Code == "CRACK_SECTION_METHOD" && item.Status == CheckStatus.SpecialReview),
        "不支持的裂缝截面方法应归入专项复核，不能要求用户填写无效参数。" );
    Assert(scheme.Checks.Any(item => item.Code == "ANCHOR_CONNECTION_TYPE" && item.Status == CheckStatus.SpecialReview),
        "未确定塔脚连接方式时应转专业核对，自动补齐会采用地脚锚栓工作默认。" );
    Assert(scheme.Checks.Any(item => item.Code == "SEISMIC_REVIEW" && item.Status == CheckStatus.SpecialReview) &&
           scheme.Checks.Any(item => item.Code == "SPECIAL_SOIL_REVIEW" && item.Status == CheckStatus.SpecialReview),
        "抗震基本参数和特殊土地勘结论缺失时必须归入专业核对，不能静默假定。" );
    Assert(scheme.DeliveryReminders.Any(item => item.Code == "CONSTRUCTION_DETAIL_REVIEW") &&
           scheme.ScopeAndInputItems.All(item => item.Code != "CONSTRUCTION_DETAIL_REVIEW"),
        "施工构造应作为交付提醒单独展示，不得计入待补参数。" );

    var project = new ProjectModel
    {
        Geotechnical = geotechnical,
        FoundationSettings = settings
    };
    var autoFill = new SpecialtyAutoFillService().ApplyRecommendedDefaults(project);
    Assert(autoFill.FilledCategoryCount >= 2 &&
           settings.SpecialtyDesign.Settlement.AllowableSettlementMm == 400 &&
           settings.SpecialtyDesign.Settlement.ExperienceCoefficient == 1,
        "一键补齐应按高耸结构高度填入明确标记来源的沉降允许值。" );
    Assert(settings.SpecialtyDesign.Settlement.SoilLayers.All(layer =>
            layer.ThicknessM == 0 && layer.CompressionModulusMpa == 0),
        "软件不得为地勘缺失的沉降土层厚度或Es编造默认值。" );
    Assert(project.Geotechnical.SpecialSoilRisks.Contains("需在专项复核", StringComparison.Ordinal),
        "地勘未说明特殊土时应明确标记需专项复核，不能默认为无风险。" );

    var aiBackfillProject = new ProjectModel
    {
        Geotechnical = new GeotechnicalInput
        {
            CompressionModulusMpa = 11.5,
            GroundwaterDepthM = 3.2,
            SpecialSoilRisks = "场地不具湿陷性，不存在液化，可不考虑冻胀。",
            Evidence = "地勘第18页、第32页",
            SourceType = ParameterSourceType.DeepSeek,
            IsConfirmed = true
        },
        FoundationSettings = new FoundationDesignSettings
        {
            FoundationType = FoundationType.RectangularShortColumn
        }
    };
    new SpecialtyAutoFillService().ApplyRecommendedDefaults(aiBackfillProject);
    AssertClose(
        aiBackfillProject.FoundationSettings.SpecialtyDesign.Settlement.SoilLayers[0].CompressionModulusMpa,
        11.5,
        1e-9,
        "智能补齐应复用地勘AI已经提取的压缩模量。" );
    AssertClose(
        aiBackfillProject.FoundationSettings.SpecialtyDesign.Hydrogeology.DesignHighGroundwaterDepthM,
        3.2,
        1e-9,
        "智能补齐应把已确认地下水埋深带入抗浮工作候选。" );
    Assert(
        aiBackfillProject.FoundationSettings.SpecialtyDesign.SpecialGround.CollapsibleLoess == EngineeringRiskState.NotPresent &&
        aiBackfillProject.FoundationSettings.SpecialtyDesign.SpecialGround.Liquefaction == EngineeringRiskState.NotPresent &&
        aiBackfillProject.FoundationSettings.SpecialtyDesign.SpecialGround.FrostHeave == EngineeringRiskState.NotPresent,
        "智能补齐只应把地勘原文明确的无风险结论转换为场景状态。" );

    var pileAutoFillProject = new ProjectModel
    {
        FoundationSettings = new FoundationDesignSettings
        {
            FoundationType = FoundationType.Pile
        }
    };
    new SpecialtyAutoFillService().ApplyRecommendedDefaults(pileAutoFillProject);
    Assert(
        pileAutoFillProject.FoundationSettings.Pile.SettlementMethod == PileSettlementMethod.MindlinReviewEstimate,
        "没有试桩资料时应自动进入不判通过的Mindlin量级复核，避免要求普通用户填写不存在的Q-s曲线。" );

    settings.SpecialtyDesign.AnchorBolts.ConnectionType = AnchorConnectionType.DirectEmbedded;
    var directEmbedded = calculator.Calculate(geometry, load, geotechnical, settings);
    Assert(directEmbedded.Checks.Any(item => item.Code == "ANCHOR_NOT_APPLICABLE" && item.Status == CheckStatus.Advisory) &&
           directEmbedded.ScopeAndInputItems.All(item => !item.Code.StartsWith("ANCHOR", StringComparison.Ordinal)),
        "选择直埋连接后应取消锚栓待补项，只保留施工提醒。" );
    return Task.CompletedTask;
}

static async Task TestPedestalAndHighWaterVerification()
{
    var settings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.RectangularShortColumn,
        PedestalLengthM = 1.0,
        PedestalWidthM = 1.0,
        PedestalHeightM = 1.2
    };
    settings.SpecialtyDesign.PedestalStructure = new PedestalStructuralDesignInput
    {
        ConcreteCompressiveStrengthMpa = 14.3,
        LongitudinalBarDiameterMm = 20,
        LongitudinalBarCount = 16,
        MinimumLongitudinalReinforcementRatio = 0.005,
        StirrupDiameterMm = 10,
        StirrupSpacingMm = 150,
        StirrupLegCount = 4,
        Source = ConfirmedSource("短柱配筋确认表", "P-01")
    };
    settings.SpecialtyDesign.Hydrogeology = new HydrogeologyDesignInput
    {
        DesignHighGroundwaterDepthM = 0.20,
        AntiFlotationSafetyFactor = 1.05,
        Source = ConfirmedSource("地勘报告", "设计最高水位章节")
    };
    MarkNoSpecialGroundRisk(settings);
    settings.SpecialtyDesign.AnchorBolts.ConnectionType = AnchorConnectionType.DirectEmbedded;

    var geometry = new FoundationGeometry
    {
        BaseLengthM = 3.5,
        BaseWidthM = 3.5,
        BaseThicknessM = 0.9,
        PedestalLengthM = 1.0,
        PedestalWidthM = 1.0,
        PedestalHeightM = 1.2
    };
    var load = new FoundationLoad
    {
        VerticalKn = 100,
        ShearXKn = 12,
        ShearYKn = 8,
        MomentXKnM = 35,
        MomentYKnM = 55,
        GoverningCase = "短柱标准组合",
        BasicCombination = new FoundationLoadCombination
        {
            VerticalKn = 135,
            ShearXKn = 18,
            ShearYKn = 12,
            MomentXKnM = 52,
            MomentYKnM = 82,
            GoverningCase = "短柱基本组合"
        }
    };
    var geotechnical = new GeotechnicalInput
    {
        BearingCapacityKpa = 180,
        SoilUnitWeightKnPerM3 = 18,
        BaseFrictionCoefficient = 0.35,
        GroundwaterDepthM = 2,
        IsConfirmed = true
    };
    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry, load, geotechnical, settings);

    Assert(scheme.Checks.Any(item => item.Code == "PEDESTAL_LONGITUDINAL_REINFORCEMENT" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail) &&
           scheme.Checks.Any(item => item.Code == "PEDESTAL_AXIAL_BENDING_INTERACTION" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail) &&
           scheme.Checks.Any(item => item.Code == "PEDESTAL_STIRRUP_REINFORCEMENT" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail),
        "短柱参数确认后必须形成纵筋、轴力-双向弯矩包络和箍筋确定性验算。" );
    Assert(scheme.ReinforcementDesigns.Any(item => item.Component.Contains("短柱纵筋", StringComparison.Ordinal)) &&
           scheme.ReinforcementDesigns.Any(item => item.Component.Contains("短柱箍筋", StringComparison.Ordinal)),
        "独立基础材料表必须纳入短柱纵筋与箍筋。" );
    var pedestalStirrup = scheme.ReinforcementDesigns.Single(item =>
        item.Component.Contains("矩形短柱箍筋", StringComparison.Ordinal));
    Assert(pedestalStirrup.HookBendAllowanceM > 0 &&
           pedestalStirrup.HookStraightAllowanceM > 0 &&
           pedestalStirrup.SingleBarLengthM > pedestalStirrup.StirrupBodyPerimeterM,
        "独立基础矩形短柱箍筋必须自动计入135度弯钩量度差和两端弯后平直段。" );
    Assert(scheme.Checks.Any(item => item.Code == "HIGH_WATER_ANTIFLOTATION" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail &&
            item.RuleReference.Contains("5.4.3", StringComparison.Ordinal)),
        "设计最高水位确认后必须按GB 50007第5.4.3条形成抗浮稳定结论。" );

    settings.SpecialtyDesign.Hydrogeology.Source.IsConfirmed = false;
    var pending = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry, load, geotechnical, settings);
    Assert(pending.Checks.Any(item => item.Code == "HIGH_WATER_ANTIFLOTATION" &&
            item.Status == CheckStatus.PendingInput),
        "设计最高水位来源未确认时不得用常年地下水位替代。" );
    settings.SpecialtyDesign.Hydrogeology.Source.IsConfirmed = true;

    var outputDirectory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.SecondRound.ShallowOutput.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outputDirectory);
    try
    {
        scheme.Name = "第二轮短柱与抗浮输出算例";
        var project = CreateConfirmedProject();
        project.FoundationSettings = settings;
        project.Geotechnical = geotechnical;
        project.FoundationLoad = load;
        project.Schemes.Add(scheme);
        project.SelectedSchemeId = scheme.Id;
        var output = await new PrototypeOutputPackageService()
            .ExportPrototypePackageAsync(project, outputDirectory);
        var dxf = DecodeDxfUnicodeEscapes(await File.ReadAllTextAsync(
            output.Files.Single(path => path.EndsWith("04_基础配筋图.dxf"))));
        Assert(dxf.Contains("DRAWING_FRAME", StringComparison.Ordinal) &&
               dxf.Contains("TITLE_BLOCK", StringComparison.Ordinal) &&
               dxf.Contains("REBAR_SCHEDULE", StringComparison.Ordinal) &&
               dxf.Contains("MATERIAL_SCHEDULE", StringComparison.Ordinal) &&
               dxf.Contains("PEDESTAL_MAIN_REBAR", StringComparison.Ordinal),
            "浅基础DXF必须包含图框、标题栏、钢筋表、材料工程量表和短柱纵筋图层。" );
        var documentXml = await ReadCalculationDocumentXmlAsync(output);
        Assert(documentXml.Contains("N/N0 + Mx/Mrx + My/Mry", StringComparison.Ordinal) &&
               documentXml.Contains("Gk/Nw,k ≥ Kw", StringComparison.Ordinal),
            "第二轮计算书必须输出短柱轴弯包络和设计最高水位抗浮公式。" );
    }
    finally
    {
        Directory.Delete(outputDirectory, recursive: true);
    }
}

static Task TestPileMMethodAndStructuralVerification()
{
    var (geometry, load, geotechnical, settings) = BuildVerifiedPileStructuralCase(1);
    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry, load, geotechnical, settings);

    var alpha = scheme.Checks.Single(item => item.Code == "PILE_M_METHOD_CLASSIFICATION");
    var displacement = scheme.Checks.Single(item => item.Code == "PILE_TOP_DISPLACEMENT");
    var rotation = scheme.Checks.Single(item => item.Code == "PILE_TOP_ROTATION");
    Assert(alpha.Status == CheckStatus.Result && alpha.Demand > 0 && double.IsFinite(alpha.Demand),
        "灌注桩必须形成有限的m法换算深度αh。" );
    Assert(displacement.Status == CheckStatus.Pass && displacement.Demand >= 0 && double.IsFinite(displacement.Demand) &&
           rotation.Status == CheckStatus.Pass && double.IsFinite(rotation.Demand),
        "确认允许值后，桩顶位移与转角必须由m法结果进入确定性比较。" );
    Assert(scheme.Checks.Any(item => item.Code == "PILE_AXIAL_BENDING_INTERACTION" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail) &&
           scheme.Checks.Any(item => item.Code == "PILE_STRUCTURAL_LONGITUDINAL_REINFORCEMENT" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail) &&
           scheme.Checks.Any(item => item.Code == "PILE_STIRRUP_REINFORCEMENT" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail) &&
           scheme.Checks.Any(item => item.Code == "PILE_CRACK_WIDTH" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail),
        "桩身必须形成轴弯、纵筋、箍筋和裂缝验算，不得只验算竖向承载力。" );
    Assert(scheme.Checks.Any(item => item.Code == "SETTLEMENT_PILE_METHOD" && item.Status == CheckStatus.Pass),
        "已确认的试验或专项计算沉降值必须与允许值比较。" );
    return Task.CompletedTask;
}

static async Task TestTieBeamStructuralGate()
{
    var (geometry, load, geotechnical, settings) = BuildVerifiedPileStructuralCase(3);
    settings.Pile.UseUserConfirmedTieBeamForces = false;
    var pending = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry, load, geotechnical, settings);
    Assert(pending.Checks.Any(item => item.Code == "TIE_BEAM_FORCE_INPUT" &&
            item.Status == CheckStatus.PendingInput),
        "三桩连梁没有整体分析内力时必须保持待补，不能把塔脚反力平均成连梁内力。" );

    settings.Pile.UseUserConfirmedTieBeamForces = true;
    settings.Pile.TieBeamAxialTensionKn = 80;
    settings.Pile.TieBeamMomentKnM = 45;
    settings.Pile.TieBeamShearKn = 35;
    var verified = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry, load, geotechnical, settings);
    Assert(verified.Checks.Any(item => item.Code == "TIE_BEAM_LONGITUDINAL_REINFORCEMENT" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail) &&
           verified.Checks.Any(item => item.Code == "TIE_BEAM_MAIN_BAR_COUNT_DETAILING" &&
            item.Status == CheckStatus.Pass) &&
           verified.Checks.Any(item => item.Code == "TIE_BEAM_MAIN_BAR_DIAMETER_DETAILING" &&
            item.Status == CheckStatus.Pass) &&
           verified.Checks.Any(item => item.Code == "TIE_BEAM_GROSS_SHEAR" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail) &&
           verified.Checks.Any(item => item.Code == "TIE_BEAM_STIRRUP_REINFORCEMENT" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail),
        "经确认连梁内力后必须形成纵筋、受剪上限与箍筋验算。" );
    Assert(verified.ReinforcementDesigns.Any(item => item.Component.Contains("连梁纵筋", StringComparison.Ordinal)) &&
           verified.ReinforcementDesigns.Any(item => item.Component.Contains("连梁箍筋", StringComparison.Ordinal)),
        "连梁计算结果必须进入配筋与材料表。" );
    var tieBeamStirrup = verified.ReinforcementDesigns.Single(item =>
        item.Component.Contains("连梁箍筋", StringComparison.Ordinal));
    Assert(tieBeamStirrup.HookBendAllowanceM > 0 &&
           tieBeamStirrup.HookStraightAllowanceM > 0 &&
           tieBeamStirrup.SingleBarLengthM > tieBeamStirrup.StirrupBodyPerimeterM,
        "独立桩连梁矩形箍筋必须计入135度弯钩量度差和两端弯后平直段。" );

    var outputDirectory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.SecondRound.PileOutput.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outputDirectory);
    try
    {
        verified.Name = "第二轮三桩连梁输出算例";
        var project = CreateConfirmedProject();
        project.ProjectType = ProjectType.CommunicationTower;
        project.FoundationSettings = settings;
        project.Geotechnical = geotechnical;
        project.FoundationLoad = load;
        project.Schemes.Add(verified);
        project.SelectedSchemeId = verified.Id;
        var output = await new PrototypeOutputPackageService()
            .ExportPrototypePackageAsync(project, outputDirectory);
        var dxf = DecodeDxfUnicodeEscapes(await File.ReadAllTextAsync(
            output.Files.Single(path => path.EndsWith("04_基础配筋图.dxf"))));
        Assert(dxf.Contains("PILE_STIRRUP", StringComparison.Ordinal) &&
               dxf.Contains("连系梁纵筋", StringComparison.Ordinal) &&
               dxf.Contains("REBAR_SCHEDULE", StringComparison.Ordinal) &&
               dxf.Contains("MATERIAL_SCHEDULE", StringComparison.Ordinal) &&
               dxf.Contains("桩位及基础连系梁平面图", StringComparison.Ordinal) &&
               dxf.Contains("2-2桩身断面", StringComparison.Ordinal) &&
               dxf.Contains("基础连系梁JLL01纵剖及1-1断面", StringComparison.Ordinal),
            "独立桩DXF必须包含桩位图、代表桩纵剖/断面、连梁详图、箍筋、钢筋表及材料工程量表。" );
        var documentXml = await ReadCalculationDocumentXmlAsync(output);
        CopyOutputSampleIfRequested(output, "second-round-three-pile-tie-beam");
        var decodedDocumentText = System.Net.WebUtility.HtmlDecode(documentXml);
        Assert(decodedDocumentText.Contains("EI·y''''(z) + m·b0·z·y(z) = 0", StringComparison.Ordinal),
            "第二轮计算书必须输出灌注桩m法微分方程。" );
        Assert(decodedDocumentText.Contains("As,req=max(Nt/fy + M/(0.9fyh0)", StringComparison.Ordinal),
            "第二轮计算书必须输出连梁配筋公式。" );
    }
    finally
    {
        Directory.Delete(outputDirectory, recursive: true);
    }
}

static Task TestIndependentFoundationTieBeamTopology()
{
    var separateTypes = new[]
    {
        FoundationType.RectangularShortColumn,
        FoundationType.CircularShortColumn,
        FoundationType.RigidShortPile,
        FoundationType.RigidRectangularShortPile,
        FoundationType.Pile
    };
    foreach (var foundationType in separateTypes)
    {
        Assert(PileLayoutRules.RequiresTieBeams(TowerStructureType.ThreeTube, foundationType),
            $"三管塔采用{foundationType}时必须设置连系梁。" );
        Assert(PileLayoutRules.GetFoundationUnitCount(TowerStructureType.ThreeTube, foundationType) == 3,
            $"三管塔采用{foundationType}时必须形成3个基础节点。" );
        Assert(PileLayoutRules.RequiresTieBeams(TowerStructureType.AngleSteel, foundationType),
            $"角钢塔采用{foundationType}时必须设置连系梁。" );
        Assert(PileLayoutRules.GetFoundationUnitCount(TowerStructureType.AngleSteel, foundationType) == 4,
            $"角钢塔采用{foundationType}时必须形成4个基础节点。" );
    }

    Assert(!PileLayoutRules.RequiresTieBeams(TowerStructureType.AngleSteel, FoundationType.Raft) &&
           PileLayoutRules.GetFoundationUnitCount(TowerStructureType.AngleSteel, FoundationType.Raft) == 1,
        "共用整体筏板已经连接全部塔柱，不得重复生成独立连系梁。" );

    var heighteningFrame = new TowerMastInput
    {
        StructureType = TowerStructureType.HeighteningFrame,
        FoundationLegCount = 4
    };
    Assert(PileLayoutRules.GetFoundationUnitCount(
               heighteningFrame,
               FoundationType.RectangularShortColumn) == 4 &&
           PileLayoutRules.DescribeFoundationLayout(
               heighteningFrame,
               FoundationType.RectangularShortColumn).Contains("4根周边连系梁", StringComparison.Ordinal),
        "增高架必须允许按实际四塔柱拓扑生成4根闭合周边连系梁，不能永久写死为三柱。" );
    return Task.CompletedTask;
}

static async Task TestIndependentFoundationTieBeamGateAndDxf()
{
    var settings = new FoundationDesignSettings();
    settings.Pile.PileCenterSpacingM = 6.0;
    settings.Pile.TieBeamWidthM = 0.40;
    settings.Pile.TieBeamHeightM = 0.60;
    settings.Pile.UseUserConfirmedTieBeamForces = false;
    var load = new FoundationLoad
    {
        VerticalKn = 320,
        ShearXKn = 40,
        FoundationUnitCount = 3,
        TieBeamsRequired = true,
        GoverningCase = "三塔柱独立基础连系梁测试"
    };
    var geotechnical = new GeotechnicalInput { SeismicIntensityDegree = 8 };
    var pending = IndependentFoundationTieBeamCalculator.Apply(
        new FoundationScheme
        {
            Name = "三塔柱矩形独立基础",
            FoundationType = FoundationType.RectangularShortColumn,
            Geometry = new FoundationGeometry
            {
                FoundationUnitCount = 3,
                BaseLengthM = 2.0,
                BaseWidthM = 2.0,
                BaseThicknessM = 0.7,
                PedestalLengthM = 0.8,
                PedestalWidthM = 0.8,
                PedestalHeightM = 1.0
            },
            Checks =
            [
                new FoundationCheckResult
                {
                    Code = "BASE_SAMPLE",
                    Name = "基础样例校核",
                    Status = CheckStatus.Pass,
                    Demand = 1,
                    Capacity = 2,
                    Utilization = 0.5
                }
            ],
            Quantities = new QuantitySummary { ConcreteM3 = 8.0 }
        },
        load,
        geotechnical,
        settings);

    Assert(pending.Geometry.TieBeamCount == 3 &&
           pending.Checks.Any(item => item.Code == "TIE_BEAM_LAYOUT" && item.Status == CheckStatus.Pass) &&
           pending.Checks.Any(item => item.Code == "TIE_BEAM_FORCE_INPUT" && item.Status == CheckStatus.PendingInput),
        "三塔柱非桩独立基础必须生成3根闭合连系梁；未确认整体内力时必须保持待补。" );
    Assert(pending.Quantities.ConcreteM3 > 8.0 &&
           !pending.ReinforcementDesigns.Any(item => item.Component.Contains("连系梁", StringComparison.Ordinal)),
        "连系梁混凝土应进入工程量，但未确认内力前不得虚构连系梁配筋。" );

    var outputDirectory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.IndependentTieBeam.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outputDirectory);
    try
    {
        var project = CreateConfirmedProject();
        project.ProjectType = ProjectType.CommunicationTower;
        project.TowerMast.StructureType = TowerStructureType.ThreeTube;
        project.TowerMast.FoundationLegCount = 3;
        project.FoundationSettings = settings;
        project.FoundationLoad = load;
        project.Schemes.Add(pending);
        project.SelectedSchemeId = pending.Id;
        var output = await new PrototypeOutputPackageService()
            .ExportPrototypePackageAsync(project, outputDirectory);
        var dxf = DecodeDxfUnicodeEscapes(await File.ReadAllTextAsync(
            output.Files.Single(path => path.EndsWith("04_基础配筋图.dxf"))));
        var beamOutlineCount = System.Text.RegularExpressions.Regex.Matches(
            dxf,
            @"\r?\n8\r?\nTIE_BEAM\r?\n").Count;
        Assert(beamOutlineCount >= 6 &&
               dxf.Contains("独立基础单元3个；闭合周边连系梁3根", StringComparison.Ordinal) &&
               dxf.Contains("JLL01内力待补；未假定配筋", StringComparison.Ordinal) &&
               dxf.Contains("基础连系梁JLL01纵剖及1-1断面", StringComparison.Ordinal) &&
               dxf.Contains("JLL01整体分析内力待确认，不生成假配筋", StringComparison.Ordinal),
            "非桩三塔柱DXF必须绘出闭合连系梁平面及连系梁纵剖/断面，并明确未确认内力时不生成配筋。" );
    }
    finally
    {
        Directory.Delete(outputDirectory, recursive: true);
    }

    settings.Pile.UseUserConfirmedTieBeamForces = true;
    settings.Pile.TieBeamAxialTensionKn = 80;
    settings.Pile.TieBeamMomentKnM = 45;
    settings.Pile.TieBeamShearKn = 35;
    load.FoundationUnitCount = 4;
    var verified = IndependentFoundationTieBeamCalculator.Apply(
        new FoundationScheme
        {
            Name = "四塔柱圆形刚性短柱桩",
            FoundationType = FoundationType.RigidShortPile,
            Geometry = new FoundationGeometry
            {
                FoundationUnitCount = 4,
                PileDiameterM = 1.8,
                PileLengthM = 5.0,
                PedestalLengthM = 1.8,
                PedestalWidthM = 1.8,
                PedestalHeightM = 0.3
            },
            Checks =
            [
                new FoundationCheckResult
                {
                    Code = "RIGID_SAMPLE",
                    Name = "刚性基础样例校核",
                    Status = CheckStatus.Pass,
                    Demand = 1,
                    Capacity = 2,
                    Utilization = 0.5
                }
            ]
        },
        load,
        geotechnical,
        settings);
    Assert(verified.Geometry.TieBeamCount == 4 &&
           verified.ReinforcementDesigns.Any(item => item.Component.Contains("连系梁纵筋", StringComparison.Ordinal)) &&
           verified.ReinforcementDesigns.Any(item => item.Component.Contains("连系梁箍筋", StringComparison.Ordinal)) &&
           verified.Quantities.EstimatedReinforcementKg > 0,
        "四塔柱非桩独立基础确认内力后必须完成4根周边连系梁配筋并汇总钢筋量。" );

    settings.Pile.TieBeamMainBarCount = 2;
    settings.Pile.TieBeamMainBarDiameterMm = 10;
    var detailingRejected = IndependentFoundationTieBeamCalculator.Apply(
        new FoundationScheme
        {
            Name = "连系梁构造下限拦截",
            FoundationType = FoundationType.RigidShortPile,
            Geometry = new FoundationGeometry
            {
                FoundationUnitCount = 4,
                PileDiameterM = 1.8,
                PileLengthM = 5.0,
                PedestalLengthM = 1.8,
                PedestalWidthM = 1.8,
                PedestalHeightM = 0.3
            }
        },
        load,
        geotechnical,
        settings);
    Assert(detailingRejected.Checks.Any(item =>
               item.Code == "TIE_BEAM_MAIN_BAR_COUNT_DETAILING" && item.Status == CheckStatus.Fail) &&
           detailingRejected.Checks.Any(item =>
               item.Code == "TIE_BEAM_MAIN_BAR_DIAMETER_DETAILING" && item.Status == CheckStatus.Fail) &&
           detailingRejected.ReinforcementDesigns.Any(item =>
               item.Component.Contains("连系梁纵筋", StringComparison.Ordinal) && item.Status == CheckStatus.Fail),
        "连系梁纵筋少于上下各2根或直径小于12mm时，必须拦截而不是仅按总面积判通过。" );
}

static async Task TestMultiLegFoundationDrawingSets()
{
    var foundationTypes = new[]
    {
        FoundationType.RectangularShortColumn,
        FoundationType.CircularShortColumn,
        FoundationType.Raft,
        FoundationType.RigidShortPile,
        FoundationType.RigidRectangularShortPile,
        FoundationType.Pile
    };
    var sampleNames = new Dictionary<FoundationType, string>
    {
        [FoundationType.RectangularShortColumn] = "rectangular-independent",
        [FoundationType.CircularShortColumn] = "circular-independent",
        [FoundationType.Raft] = "shared-raft",
        [FoundationType.RigidShortPile] = "rigid-round",
        [FoundationType.RigidRectangularShortPile] = "rigid-rectangle",
        [FoundationType.Pile] = "cast-in-place-pile"
    };

    foreach (var legCount in new[] { 3, 4 })
    {
        foreach (var foundationType in foundationTypes)
        {
            var outputDirectory = Path.Combine(
                Path.GetTempPath(),
                "TowerFoundation.MultiLegDrawing.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputDirectory);
            try
            {
                var project = CreateConfirmedProject();
                project.Name = legCount == 3
                    ? "三管塔及三塔脚增高架基础组图"
                    : "角钢塔及四塔脚增高架基础组图";
                project.ProjectType = ProjectType.CommunicationTower;
                project.TowerMast.StructureType = legCount == 3
                    ? TowerStructureType.ThreeTube
                    : TowerStructureType.AngleSteel;
                project.TowerMast.FoundationLegCount = legCount;
                project.FoundationSettings.FoundationType = foundationType;
                var scheme = BuildMultiLegDrawingScheme(foundationType, legCount);
                project.Schemes.Add(scheme);
                project.SelectedSchemeId = scheme.Id;

                var output = await new PrototypeOutputPackageService()
                    .ExportPrototypePackageAsync(project, outputDirectory);
                var dxf = DecodeDxfUnicodeEscapes(await File.ReadAllTextAsync(
                    output.Files.Single(path => path.EndsWith("04_基础配筋图.dxf"))));
                var markerEntityCount = System.Text.RegularExpressions.Regex.Matches(
                    dxf,
                    @"\r?\n8\r?\nTOWER_LEG_MARK\r?\n").Count;
                Assert(markerEntityCount >= legCount * 2 &&
                       Enumerable.Range(1, legCount).All(number =>
                           dxf.Contains($"塔脚{number}", StringComparison.Ordinal)),
                    $"{legCount}塔脚{foundationType}必须逐一绘出并编号全部塔脚。" );
                Assert(dxf.Contains("MATERIAL_SCHEDULE", StringComparison.Ordinal) &&
                       dxf.Contains("主要材料及工程量表", StringComparison.Ordinal) &&
                       dxf.Contains("混凝土", StringComparison.Ordinal) &&
                       dxf.Contains("已计算钢筋", StringComparison.Ordinal) &&
                       dxf.Contains("其他材料", StringComparison.Ordinal) &&
                       dxf.Contains("未计量", StringComparison.Ordinal),
                    $"{legCount}塔脚{foundationType}的CAD图内必须直接绘出主要材料及工程量表。" );

                if (foundationType == FoundationType.Raft)
                {
                    var pedestalSectionEntityCount = System.Text.RegularExpressions.Regex.Matches(
                        dxf,
                        @"\r?\n8\r?\nPEDESTAL_SECTION\r?\n").Count;
                    Assert(dxf.Contains($"{legCount}塔脚共用筏板基础平面图", StringComparison.Ordinal) &&
                           dxf.Contains("1-1剖面通过塔脚1、2", StringComparison.Ordinal) &&
                           dxf.Contains("1-1塔脚列筏板剖面图", StringComparison.Ordinal) &&
                           dxf.Contains("塔脚1", StringComparison.Ordinal) &&
                           dxf.Contains("塔脚2", StringComparison.Ordinal) &&
                           pedestalSectionEntityCount >= 8 &&
                           !dxf.Contains("3200  800  3200", StringComparison.Ordinal) &&
                           !dxf.Contains("禁止作为施工图", StringComparison.Ordinal) &&
                           dxf.Contains(
                               legCount == 3
                                   ? "底边塔脚中心距 5500"
                                   : "塔脚横向中心距 5500",
                               StringComparison.Ordinal) &&
                           dxf.Contains(
                               legCount == 3
                                   ? "三角形高 4763"
                                   : "塔脚纵向中心距 5500",
                               StringComparison.Ordinal) &&
                           dxf.Contains($"共用整体筏板承托{legCount}个塔脚", StringComparison.Ordinal) &&
                           dxf.Contains("筏板本身形成整体连接，不另设独立连系梁", StringComparison.Ordinal) &&
                           dxf.Contains($"共用整体筏板基础（{legCount}塔脚）", StringComparison.Ordinal),
                        $"{legCount}塔脚筏板必须按实际根开绘制全部塔脚，使1-1剖面穿过塔脚1、2并显示两根实际短柱。" );
                }
                else
                {
                    var beamOutlineCount = System.Text.RegularExpressions.Regex.Matches(
                        dxf,
                        @"\r?\n8\r?\nTIE_BEAM\r?\n").Count;
                    Assert(beamOutlineCount >= legCount * 2 &&
                           dxf.Contains($"JLL01-{legCount}", StringComparison.Ordinal) &&
                           dxf.Contains(
                               legCount == 3 ? "三角形布置" : "四角布置",
                               StringComparison.Ordinal),
                        $"{legCount}塔脚{foundationType}必须绘出闭合连系梁、逐梁编号和正确平面拓扑。" );
                }

                if (foundationType == FoundationType.Pile)
                {
                    Assert(dxf.Contains("GZH01", StringComparison.Ordinal),
                        "多塔脚灌注桩组图必须采用GZH集中标注。" );
                }
                if (foundationType is FoundationType.RigidShortPile or FoundationType.RigidRectangularShortPile)
                {
                    Assert(dxf.Contains(
                            foundationType == FoundationType.RigidShortPile
                                ? "RIGID_LONGITUDINAL_REBAR"
                                : "RIGID_RECT_LONGITUDINAL_REBAR",
                            StringComparison.Ordinal),
                        "多塔脚刚性短柱桩不得只绘空轮廓，必须包含已确认的钢筋笼。" );
                }

                CopyOutputSampleIfRequested(
                    output,
                    $"multi-{legCount}-{sampleNames[foundationType]}");
            }
            finally
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }
}

static FoundationScheme BuildMultiLegDrawingScheme(FoundationType foundationType, int legCount)
{
    var isRaft = foundationType == FoundationType.Raft;
    var isPile = foundationType == FoundationType.Pile;
    var isRigidRound = foundationType == FoundationType.RigidShortPile;
    var isRigidRectangle = foundationType == FoundationType.RigidRectangularShortPile;
    var scheme = new FoundationScheme
    {
        Name = $"{legCount}塔脚{foundationType}CAD组图算例",
        FoundationType = foundationType,
        Geometry = new FoundationGeometry
        {
            FoundationUnitCount = isRaft ? 1 : legCount,
            BaseLengthM = isRaft ? 7.6 : isRigidRectangle ? 1.8 : 2.2,
            BaseWidthM = isRaft ? 7.6 : isRigidRectangle ? 1.6 : 2.2,
            BaseThicknessM = isRaft ? 0.9 : 0.7,
            PedestalLengthM = isPile ? 1.2 : isRigidRound ? 1.8 : 0.8,
            PedestalWidthM = isPile ? 1.2 : isRigidRound ? 1.8 : 0.8,
            PedestalHeightM = isRigidRound || isRigidRectangle ? 0.3 : 1.0,
            PileDiameterM = isPile ? 1.2 : isRigidRound ? 1.8 : 0,
            PileLengthM = isPile ? 12.0 : isRigidRound || isRigidRectangle ? 6.0 : 0,
            PileCount = isPile ? legCount : 1,
            PileCenterSpacingM = 5.5,
            TieBeamCount = isRaft ? 0 : legCount,
            TieBeamWidthM = 0.45,
            TieBeamHeightM = 0.70
        },
        Checks =
        [
            new FoundationCheckResult
            {
                Code = "MULTI_LEG_DRAWING_SAMPLE",
                Name = "多塔脚基础组图样例",
                Status = CheckStatus.Pass,
                Demand = 1,
                Capacity = 2,
                Utilization = 0.5,
                Explanation = "用于三、四塔脚基础CAD平面拓扑与详图回归。"
            }
        ],
        Quantities = new QuantitySummary { ConcreteM3 = 20 }
    };

    if (foundationType is FoundationType.RectangularShortColumn or
        FoundationType.CircularShortColumn or FoundationType.Raft)
    {
        scheme.ReinforcementDesigns.AddRange(
        [
            new ReinforcementDesignResult { Component = "基础底板底筋", Direction = "X向", BarSpecification = "Φ16@150", BarDiameterMm = 16, BarSpacingMm = 150, BarCount = 20 * (isRaft ? 1 : legCount), Status = CheckStatus.Pass },
            new ReinforcementDesignResult { Component = "基础底板底筋", Direction = "Y向", BarSpecification = "Φ16@150", BarDiameterMm = 16, BarSpacingMm = 150, BarCount = 20 * (isRaft ? 1 : legCount), Status = CheckStatus.Pass }
        ]);
        if (!isRaft)
        {
            scheme.ReinforcementDesigns.AddRange(
            [
                new ReinforcementDesignResult { Component = "独立基础短柱纵筋", Direction = "截面周边", BarSpecification = "16Φ20", BarDiameterMm = 20, BarCount = 16 * legCount, Status = CheckStatus.Pass },
                new ReinforcementDesignResult { Component = "独立基础短柱箍筋", Direction = "闭合箍", BarSpecification = "Φ10@150", BarDiameterMm = 10, BarSpacingMm = 150, BarCount = 30 * legCount, Status = CheckStatus.Pass }
            ]);
        }
    }
    else if (isPile)
    {
        scheme.ReinforcementDesigns.AddRange(
        [
            new ReinforcementDesignResult { Component = "灌注桩桩身纵筋", Direction = "周向均布", BarSpecification = "每桩16Φ22", BarDiameterMm = 22, BarCount = 16 * legCount, Status = CheckStatus.Pass },
            new ReinforcementDesignResult { Component = "灌注桩桩身箍筋", Direction = "螺旋箍", BarSpecification = "Φ10@150", BarDiameterMm = 10, BarSpacingMm = 150, BarCount = 80 * legCount, Status = CheckStatus.Pass }
        ]);
    }
    else
    {
        var prefix = isRigidRound ? "刚性短柱桩" : "矩形刚性短柱桩";
        scheme.ReinforcementDesigns.AddRange(
        [
            new ReinforcementDesignResult { Component = $"{prefix}纵筋", Direction = "截面周边", BarSpecification = "每基础16Φ22", BarDiameterMm = 22, BarCount = 16 * legCount, Status = CheckStatus.Pass },
            new ReinforcementDesignResult { Component = $"{prefix}箍筋", Direction = "闭合箍", BarSpecification = "Φ10@150", BarDiameterMm = 10, BarSpacingMm = 150, BarCount = 42 * legCount, Status = CheckStatus.Pass }
        ]);
    }

    if (!isRaft)
    {
        var beamPrefix = isPile ? "独立桩连梁" : "多塔柱基础连系梁";
        scheme.ReinforcementDesigns.AddRange(
        [
            new ReinforcementDesignResult { Component = $"{beamPrefix}纵筋", Direction = $"{legCount}根闭合周边连系梁", BarSpecification = "每梁8Φ20", BarDiameterMm = 20, BarCount = 8 * legCount, Status = CheckStatus.Pass },
            new ReinforcementDesignResult { Component = $"{beamPrefix}箍筋", Direction = $"{legCount}根闭合周边连系梁", BarSpecification = "Φ10@150", BarDiameterMm = 10, BarSpacingMm = 150, BarCount = 40 * legCount, Status = CheckStatus.Pass }
        ]);
    }
    return scheme;
}

static Task TestAnchorPlateAndConcreteCapacity()
{
    var (geometry, load, geotechnical, settings) = BuildRigidRectangularSpecialtyCase();
    settings.SpecialtyDesign.AnchorBolts = new AnchorBoltDesignInput
    {
        ConnectionType = AnchorConnectionType.AnchorBoltCage,
        BoltCount = 12,
        NominalDiameterMm = 42,
        BoltCircleDiameterM = 1.45,
        TensileStrengthDesignMpa = 200,
        ShearStrengthDesignMpa = 155,
        ThreadStressAreaFactor = 0.78,
        EmbedmentDepthM = 1.50,
        AnchorPlateOuterDiameterMm = 160,
        AnchorPlateThicknessMm = 60,
        AnchorPlateSteelYieldStrengthMpa = 235,
        ConcreteCompressiveStrengthMpa = 14.3,
        ConcreteBreakoutCapacityKn = 800,
        PulloutCapacityKn = 650,
        EdgeBreakoutCapacityKn = 500,
        Source = ConfirmedSource("塔脚锚栓详图", "A-08"),
        ConcreteCapacitySource = ConfirmedSource("经审查节点计算书", "锚栓组承载力表")
    };
    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry, load, geotechnical, settings);
    Assert(scheme.Checks.Any(item => item.Code == "ANCHOR_PLATE_CONCRETE_BEARING" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail) &&
           scheme.Checks.Any(item => item.Code == "ANCHOR_PLATE_THICKNESS" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail),
        "下锚板尺寸齐全时必须验算混凝土净承压和板厚。" );
    Assert(scheme.Checks.Any(item => item.Code == "ANCHOR_CONCRETE_TENSION" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail) &&
           scheme.Checks.Any(item => item.Code == "ANCHOR_CONCRETE_EDGE" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail) &&
           scheme.Checks.Any(item => item.Code == "ANCHOR_CONCRETE_INTERACTION" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail),
        "导入经审查节点承载力后必须形成锥体/拔出、边缘和拉剪组合验算。" );
    Assert(scheme.Checks.All(item => item.Code != "ANCHOR_CONCRETE_FAILURE" &&
            item.Code != "ANCHOR_PLATE_DETAIL"),
        "节点资料齐全后不应继续显示同类待补项。" );
    return Task.CompletedTask;
}

static Task TestThirdRoundLoadCombinationEngine()
{
    var input = new LoadCombinationDesignInput
    {
        UseDecomposedActions = true,
        ActiveStructuralCombination = LoadCombinationKind.Seismic,
        PermanentAction = new FoundationLoadCombination
        {
            VerticalKn = 100,
            GoverningCase = "永久作用标准值"
        },
        LeadingVariableAction = new FoundationLoadCombination
        {
            ShearXKn = 10,
            MomentYKnM = 50,
            GoverningCase = "风作用标准值"
        },
        SeismicAction = new FoundationLoadCombination
        {
            ShearXKn = 20,
            MomentYKnM = 100,
            GoverningCase = "地震作用标准值"
        },
        AccidentalAction = new FoundationLoadCombination
        {
            VerticalKn = -20,
            GoverningCase = "偶然作用代表值"
        },
        Source = ConfirmedSource("项目荷载组合表", "GB 50068第8.2、8.3节及项目系数")
    };
    var result = new LoadCombinationEngine().Apply(
        new FoundationLoad(),
        input,
        1,
        false);
    Assert(result.CombinationTrace.Count == 5 &&
           result.CombinationTrace.Select(item => item.Kind).Distinct().Count() == 5,
        "作用分解后必须形成标准、基本、准永久、地震和偶然五类组合轨迹。" );
    AssertClose(result.VerticalKn, 100, 1e-9, "标准组合永久作用不一致。" );
    AssertClose(result.ShearXKn, 10, 1e-9, "标准组合风作用不一致。" );
    AssertClose(result.BasicCombination!.VerticalKn, 130, 1e-9, "基本组合永久作用分项不一致。" );
    AssertClose(result.BasicCombination.ShearXKn, 15, 1e-9, "基本组合可变作用分项不一致。" );
    AssertClose(result.QuasiPermanentCombination!.ShearXKn, 0, 1e-9,
        "准永久组合应按明确的准永久系数计算。" );
    Assert(result.ActiveStructuralCombination?.Kind == LoadCombinationKind.Seismic &&
           result.ResolveStructuralDesignLoad(new FoundationDesignSettings()).GoverningCase.Contains("地震", StringComparison.Ordinal),
        "选择地震设计状况后，结构验算必须采用地震组合而非固定基本组合。" );
    Assert(result.CombinationTrace.All(item =>
            item.IsConfirmed &&
            !string.IsNullOrWhiteSpace(item.Expression) &&
            !string.IsNullOrWhiteSpace(item.SourceDocument)),
        "每类组合必须保留表达式、来源和确认状态。" );
    return Task.CompletedTask;
}

static Task TestAnchorProgramModelGate()
{
    var (geometry, load, geotechnical, settings) = BuildRigidRectangularSpecialtyCase();
    var anchor = new AnchorBoltDesignInput
    {
        ConnectionType = AnchorConnectionType.AnchorBoltCage,
        BoltCount = 12,
        NominalDiameterMm = 42,
        BoltCircleDiameterM = 1.45,
        TensileStrengthDesignMpa = 200,
        ShearStrengthDesignMpa = 155,
        ThreadStressAreaFactor = 0.78,
        EmbedmentDepthM = 1.50,
        UseProgramCalculatedConcreteCapacity = true,
        ConcreteMemberThicknessMm = 1800,
        MinimumAnchorEdgeDistanceMm = 420,
        MinimumAnchorSpacingMm = 260,
        EffectiveEmbedmentDepthMm = 1200,
        ConcreteTensileStrengthMpa = 1.43,
        ConcreteBreakoutCoefficient = 1.8,
        PulloutBearingCoefficient = 0.8,
        EdgeBreakoutCoefficient = 2.0,
        Source = ConfirmedSource("塔脚锚栓详图", "A-08")
    };
    settings.SpecialtyDesign.AnchorBolts = anchor;
    var pending = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry, load, geotechnical, settings);
    Assert(pending.Checks.Any(item => item.Code == "ANCHOR_CONCRETE_MODEL_INPUT" &&
            item.Status == CheckStatus.PendingInput),
        "程序锚栓模型未确认公式来源时必须保持待补。" );

    anchor.ProgramConcreteModelSource = ConfirmedSource(
        "经审查锚栓节点计算方法",
        "混凝土锥体、拔出和边缘模型系数表");
    var verified = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry, load, geotechnical, settings);
    Assert(verified.Checks.Any(item => item.Code == "ANCHOR_GROUP_GEOMETRY" &&
            item.Status == CheckStatus.Result) &&
           verified.Checks.Any(item => item.Code == "ANCHOR_CONCRETE_TENSION" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail) &&
           verified.Checks.Any(item => item.Code == "ANCHOR_CONCRETE_EDGE" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail),
        "模型来源和全部几何系数确认后，程序应形成几何追溯及混凝土破坏验算。" );
    Assert(verified.Checks.All(item => item.Code != "ANCHOR_CONCRETE_FAILURE"),
        "程序模型完整时不应继续显示同类混凝土承载力待补项。" );
    return Task.CompletedTask;
}

static Task TestPileNegativeFrictionAndLoadTestSettlement()
{
    var (geometry, load, geotechnical, settings) = BuildVerifiedPileStructuralCase(1);
    settings.Pile.SettlementMethod = PileSettlementMethod.StaticLoadTestCurve;
    settings.Pile.UseConfirmedServiceSettlement = false;
    settings.Pile.SettlementSource = ConfirmedSource("单桩静载试验报告", "试桩SZ-01 Q-s曲线");
    settings.Pile.StaticLoadTestCurve =
    [
        new PileLoadTestPoint { LoadKn = 0, SettlementMm = 0 },
        new PileLoadTestPoint { LoadKn = 300, SettlementMm = 5 },
        new PileLoadTestPoint { LoadKn = 600, SettlementMm = 15 }
    ];
    settings.Pile.UseNegativeSkinFriction = true;
    settings.Pile.NegativeSkinFrictionSource = ConfirmedSource(
        "地勘报告",
        "第6.4节负摩阻区段");
    settings.Pile.NegativeSkinFrictionLayers =
    [
        new NegativeSkinFrictionLayerInput
        {
            Name = "欠固结填土",
            ThicknessM = 2,
            UnitNegativeSkinFrictionKpa = 10
        }
    ];

    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry, load, geotechnical, settings);
    var negative = scheme.Checks.Single(item => item.Code == "PILE_NEGATIVE_SKIN_FRICTION");
    var compression = scheme.Checks.Single(item => item.Code == "PILE_COMPRESSION");
    var expectedDrag = Math.PI * geometry.PileDiameterM * 2 * 10;
    Assert(negative.Status == CheckStatus.Result,
        "确认负摩阻分层后必须形成可追溯下拉荷载结果。" );
    AssertClose(negative.Demand, expectedDrag, 1e-9, "负摩阻分层累计值不一致。" );
    AssertClose(compression.Demand, load.VerticalKn + expectedDrag, 1e-9,
        "负摩阻下拉荷载必须叠加到单桩抗压需求。" );
    var settlement = scheme.Checks.Single(item => item.Code == "SETTLEMENT_PILE_METHOD");
    Assert(settlement.Status == CheckStatus.Pass,
        "静载Q-s曲线覆盖服务荷载时应形成沉降比较结论。" );
    AssertClose(settlement.Demand, 11.6666666667, 0.000001,
        "静载Q-s曲线分段线性内插值不一致。" );

    settings.Pile.StaticLoadTestCurve =
    [
        new PileLoadTestPoint { LoadKn = 0, SettlementMm = 0 },
        new PileLoadTestPoint { LoadKn = 400, SettlementMm = 8 }
    ];
    var outsideRange = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry, load, geotechnical, settings);
    Assert(outsideRange.Checks.Any(item => item.Code == "SETTLEMENT_PILE_METHOD" &&
            item.Status == CheckStatus.PendingInput &&
            item.Explanation.Contains("禁止外推", StringComparison.Ordinal)),
        "服务荷载超过静载曲线范围时必须禁止外推并保持待补。" );
    return Task.CompletedTask;
}

static Task TestSpecialGroundRiskRouting()
{
    var settings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.RectangularShortColumn
    };
    settings.SpecialtyDesign.AnchorBolts.ConnectionType = AnchorConnectionType.DirectEmbedded;
    settings.SpecialtyDesign.SpecialGround = new SpecialGroundDesignInput
    {
        CollapsibleLoess = EngineeringRiskState.NotPresent,
        Liquefaction = EngineeringRiskState.NotPresent,
        FrostHeave = EngineeringRiskState.PresentTreatmentConfirmed,
        DesignFrostDepthM = 1.20,
        TreatmentDescription = "基底置于设计冻深以下并采取防冻胀构造",
        Source = ConfirmedSource("地勘报告与专项设计", "冻胀评价")
    };
    var geotechnical = new GeotechnicalInput
    {
        BearingCapacityKpa = 180,
        SoilUnitWeightKnPerM3 = 18,
        BaseFrictionCoefficient = 0.35,
        SeismicIntensityDegree = 8,
        DesignBasicGroundAccelerationG = 0.20,
        DesignEarthquakeGroup = "第二组",
        SiteClass = "Ⅱ类"
    };
    var geometry = new FoundationGeometry
    {
        BaseLengthM = 3,
        BaseWidthM = 3,
        BaseThicknessM = 0.8,
        PedestalLengthM = 0.8,
        PedestalWidthM = 0.8,
        PedestalHeightM = 1.3
    };
    var load = new FoundationLoad
    {
        VerticalKn = 80,
        ShearXKn = 8,
        MomentYKnM = 40,
        GoverningCase = "风作用标准组合"
    };
    var scheme = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry, load, geotechnical, settings);
    Assert(scheme.Checks.Any(item => item.Code == "SPECIAL_SOIL_REVIEW" &&
            item.Status == CheckStatus.SpecialReview) &&
           scheme.Checks.Any(item => item.Code == "FROST_EMBEDMENT" &&
            item.Status is CheckStatus.Pass or CheckStatus.Fail),
        "存在且已有处理的冻胀风险必须保留专项复核，并单独核对埋深与设计冻深。" );
    Assert(scheme.Checks.Any(item => item.Code == "SEISMIC_REVIEW" &&
            item.Status == CheckStatus.SpecialReview),
        "抗震参数齐全但没有可追溯地震作用组合时仍不得以风组合代替。" );

    settings.SpecialtyDesign.SpecialGround.Source.IsConfirmed = false;
    var pending = new RectangularShortColumnFoundationCalculator().Calculate(
        geometry, load, geotechnical, settings);
    Assert(pending.Checks.Any(item => item.Code == "SPECIAL_SOIL_REVIEW" &&
            item.Status == CheckStatus.SpecialReview),
        "特殊地基来源未确认时应转专业核对，不要求普通用户猜结论。" );
    return Task.CompletedTask;
}

static (
    FoundationGeometry Geometry,
    FoundationLoad Load,
    GeotechnicalInput Geotechnical,
    FoundationDesignSettings Settings) BuildVerifiedPileStructuralCase(int pileCount)
{
    var settings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.Pile,
        ConcreteCoverMm = 60,
        ReinforcementYieldStrengthMpa = 360
    };
    settings.Pile.IsConfirmed = true;
    settings.Pile.PileCount = pileCount;
    settings.Pile.TieBeamRequired = pileCount > 1;
    settings.Pile.PileCenterSpacingM = 3.5;
    settings.Pile.TieBeamWidthM = 0.45;
    settings.Pile.TieBeamHeightM = 0.70;
    settings.Pile.CapacityReductionFactor = 2.0;
    settings.Pile.SinglePileHorizontalCapacityKn = 300;
    settings.Pile.HorizontalResistanceCoefficientMnPerM4 = 18;
    settings.Pile.ConcreteElasticModulusMpa = 30_000;
    settings.Pile.ConcreteCompressiveStrengthMpa = 14.3;
    settings.Pile.PileMainBarDiameterMm = 28;
    settings.Pile.PileMainBarCount = 24;
    settings.Pile.StirrupDiameterMm = 12;
    settings.Pile.StirrupSpacingMm = 120;
    settings.Pile.StirrupLegCount = 4;
    settings.Pile.TieBeamMainBarDiameterMm = 22;
    settings.Pile.TieBeamMainBarCount = 8;
    settings.Pile.TieBeamStirrupDiameterMm = 10;
    settings.Pile.TieBeamStirrupSpacingMm = 120;
    settings.Pile.TieBeamStirrupLegCount = 4;
    settings.Pile.UseConfirmedServiceSettlement = true;
    settings.Pile.ServiceSettlementFromTestOrSpecialCalculationMm = 8;
    settings.Pile.SoilLayers =
    [
        new PileSoilLayerInput
        {
            Name = "粉质黏土",
            ThicknessM = 6,
            SideResistanceKpa = 70,
            TipResistanceKpa = 900,
            UpliftCoefficient = 0.75
        },
        new PileSoilLayerInput
        {
            Name = "密实砂层",
            ThicknessM = 10,
            SideResistanceKpa = 110,
            TipResistanceKpa = 2200,
            UpliftCoefficient = 0.65,
            IsSandOrGravel = true
        }
    ];
    settings.SpecialtyDesign.Deformation = new DeformationLimitInput
    {
        AllowableTopDisplacementMm = 100,
        AllowableTopRotationRad = 0.10,
        Source = ConfirmedSource("塔型连接技术条件", "允许变形")
    };
    settings.SpecialtyDesign.Settlement = new SettlementDesignInput
    {
        AllowableSettlementMm = 20,
        ExperienceCoefficient = 1,
        Source = ConfirmedSource("静载试验或专项计算", "服务沉降")
    };
    settings.SpecialtyDesign.Crack.EnvironmentCategory = "二a类";
    settings.SpecialtyDesign.Crack.MaximumCrackWidthMm = 0.30;
    settings.SpecialtyDesign.Crack.Source = ConfirmedSource("混凝土耐久性条件", "裂缝限值");
    settings.SpecialtyDesign.AnchorBolts.ConnectionType = AnchorConnectionType.DirectEmbedded;
    MarkNoSpecialGroundRisk(settings);

    var geometry = new FoundationGeometry
    {
        PileDiameterM = 1.2,
        PileLengthM = 14,
        PedestalLengthM = 1.2,
        PedestalWidthM = 1.2,
        PedestalHeightM = 0.3,
        PileCount = pileCount,
        PileCenterSpacingM = 3.5,
        TieBeamCount = pileCount > 1 ? pileCount : 0,
        TieBeamWidthM = 0.45,
        TieBeamHeightM = 0.70
    };
    var standard = new FoundationLoad
    {
        VerticalKn = pileCount > 1 ? 0 : 500,
        ShearXKn = pileCount > 1 ? 0 : 55,
        MomentYKnM = pileCount > 1 ? 0 : 120,
        UsesIndividualPileReactions = pileCount > 1,
        IndividualPileCompressionKn = pileCount > 1 ? 450 : 0,
        IndividualPileUpliftKn = pileCount > 1 ? 180 : 0,
        IndividualPileHorizontalKn = pileCount > 1 ? 45 : 0,
        FoundationUnitCount = pileCount,
        TieBeamsRequired = pileCount > 1,
        GoverningCase = "桩基标准组合"
    };
    standard.BasicCombination = new FoundationLoadCombination
    {
        VerticalKn = pileCount > 1 ? 0 : 650,
        ShearXKn = pileCount > 1 ? 0 : 75,
        MomentYKnM = pileCount > 1 ? 0 : 165,
        UsesIndividualPileReactions = pileCount > 1,
        IndividualPileCompressionKn = pileCount > 1 ? 600 : 0,
        IndividualPileUpliftKn = pileCount > 1 ? 240 : 0,
        IndividualPileHorizontalKn = pileCount > 1 ? 60 : 0,
        GoverningCase = "桩身结构基本组合"
    };
    var geotechnical = new GeotechnicalInput
    {
        SoilUnitWeightKnPerM3 = 18,
        GroundwaterDepthM = 4,
        IsConfirmed = true
    };
    return (geometry, standard, geotechnical, settings);
}

static EngineeringParameterSource ConfirmedSource(string document, string location) => new()
{
    SourceType = ParameterSourceType.Manual,
    SourceDocument = document,
    SourceLocation = location,
    IsConfirmed = true
};

static void MarkNoSpecialGroundRisk(FoundationDesignSettings settings)
{
    settings.SpecialtyDesign.SpecialGround = new SpecialGroundDesignInput
    {
        CollapsibleLoess = EngineeringRiskState.NotPresent,
        Liquefaction = EngineeringRiskState.NotPresent,
        FrostHeave = EngineeringRiskState.NotPresent,
        Source = ConfirmedSource("地勘报告", "特殊土评价")
    };
}

static async Task TestReviewDraftExportGate()
{
    var project = CreateConfirmedProject();
    var workflow = BuildWorkflow();
    var scheme = workflow.GenerateSchemes(project)[0];
    project.SelectedSchemeId = scheme.Id;
    var outputDirectory = Path.Combine(
        Path.GetTempPath(),
        "TowerFoundation.ReviewDraft.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outputDirectory);
    try
    {
        var output = await new PrototypeOutputPackageService()
            .ExportPrototypePackageAsync(project, outputDirectory);
        var manifestPath = output.Files.Single(path => path.EndsWith("manifest.json"));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert(
            manifest.RootElement.GetProperty("status").GetString() ==
            "REVIEW_DRAFT_PENDING_INPUTS",
            "缺少关键专项参数时，成果清单必须标记为待补参数复核稿。" );
        var documentXml = await ReadCalculationDocumentXmlAsync(output);
        Assert(
            documentXml.Contains("本文件为复核稿，不得标记为全部通过", StringComparison.Ordinal) &&
            documentXml.Contains("自动处理与专业核对清单", StringComparison.Ordinal) &&
            documentXml.Contains("计算过程结果（不单独判定通过）", StringComparison.Ordinal),
            "复核稿计算书必须显式分列安全验算、计算结果和待补项目。" );
    }
    finally
    {
        Directory.Delete(outputDirectory, recursive: true);
    }
}

static (
    FoundationGeometry Geometry,
    FoundationLoad Load,
    GeotechnicalInput Geotechnical,
    FoundationDesignSettings Settings) BuildRigidRectangularSpecialtyCase()
{
    var settings = new FoundationDesignSettings
    {
        FoundationType = FoundationType.RigidRectangularShortPile,
        StructuralDesignLoadFactor = 1.5,
        FoundationPermanentLoadFactor = 1.3,
        StructureImportanceFactor = 1.0
    };
    settings.RigidShortPile.IsConfirmed = true;
    settings.RigidShortPile.AboveGroundHeightM = 0.30;
    settings.RigidShortPile.LongitudinalBarCount = 36;
    settings.RigidShortPile.LongitudinalBarDiameterMm = 22;
    settings.RigidShortPile.StirrupDiameterMm = 10;
    settings.RigidShortPile.StirrupSpacingMm = 150;
    settings.RigidShortPile.StirrupLegCount = 2;
    settings.RigidShortPile.SoilLayers =
    [
        new RigidShortPileSoilLayerInput { Name = "表层土", ThicknessM = 1, HorizontalResistanceCoefficientMnPerM4 = 8 },
        new RigidShortPileSoilLayerInput { Name = "粉质黏土", ThicknessM = 3, HorizontalResistanceCoefficientMnPerM4 = 20 },
        new RigidShortPileSoilLayerInput { Name = "密实层", ThicknessM = 4, HorizontalResistanceCoefficientMnPerM4 = 35 }
    ];
    var geometry = new FoundationGeometry
    {
        BaseLengthM = 2.0,
        BaseWidthM = 1.6,
        PileLengthM = 6.0,
        PedestalLengthM = 2.0,
        PedestalWidthM = 1.6,
        PedestalHeightM = 0.30
    };
    var load = new FoundationLoad
    {
        VerticalKn = 300,
        ShearXKn = 20,
        ShearYKn = 15,
        MomentXKnM = 100,
        MomentYKnM = 160,
        GoverningCase = "矩形刚性短柱桩双向标准组合",
        BasicCombination = new FoundationLoadCombination
        {
            VerticalKn = 390,
            ShearXKn = 28,
            ShearYKn = 21,
            MomentXKnM = 140,
            MomentYKnM = 224,
            GoverningCase = "矩形刚性短柱桩基本组合"
        }
    };
    var geotechnical = new GeotechnicalInput
    {
        SoilUnitWeightKnPerM3 = 18,
        InternalFrictionAngleDegree = 15,
        GroundwaterDepthM = 10,
        SeismicIntensityDegree = 8,
        SiteClass = "Ⅱ类",
        SpecialSoilRisks = "未发现湿陷、液化和不良地质作用"
    };
    return (geometry, load, geotechnical, settings);
}

static async Task TestThirtyTowerFoundationScenarios()
{
    const int scenarioCount = 30;
    var catalog = new EmbeddedTowerLoadCatalog();
    if (catalog.LegacyRecords.Count == 0)
    {
        Assert(!catalog.Status.IsCompleteForNewDesign, "公开占位库不得伪装为完整企业荷载库。");
        Assert(catalog.Status.StatusMessage.Contains("手工荷载", StringComparison.Ordinal),
            "缺少私有企业荷载数据时必须提示使用手工荷载输入。" );
        return;
    }

    var records = catalog.LegacyRecords
        .Where(item =>
            item.CanApplyOverallStandardLoad &&
            (PileLayoutRules.GetPileCount(
                EnterpriseTowerLoadService.InferStructureType(item.TowerType)) <= 1 ||
             item.CanApplySingleLegStandardLoad))
        .OrderBy(item => item.OverallBaseReaction!.Standard!.MomentKnM)
        .ThenBy(item => item.TowerCode, StringComparer.Ordinal)
        .ToList();
    Assert(records.Count >= scenarioCount, "历史回归样本中可用的塔脚反力记录不足30条。" );

    var workflow = BuildWorkflow();
    var reportRows = new List<Dictionary<string, object?>>();
    var failures = new List<string>();
    var foundationTypes = new[]
    {
        FoundationType.RectangularShortColumn,
        FoundationType.CircularShortColumn,
        FoundationType.Raft,
        FoundationType.RigidShortPile,
        FoundationType.Pile
    };
    var bearingValues = new[] { 90d, 110d, 130d, 150d, 180d, 220d, 260d, 320d };
    var groundwaterDepths = new[] { 0.3d, 0.8d, 1.5d, 2.5d, 5d, 8d };
    var frictionValues = new[] { 0.25d, 0.30d, 0.35d, 0.40d, 0.45d };

    for (var index = 0; index < scenarioCount; index++)
    {
        var recordIndex = (int)Math.Round(index * (records.Count - 1d) / (scenarioCount - 1));
        var record = records[recordIndex];
        var foundationType = foundationTypes[index % foundationTypes.Length];
        var project = new ProjectModel
        {
            Name = $"塔基矩阵-{index + 1:00}",
            ProjectType = ProjectType.CommunicationTower,
            Province = string.Empty,
            City = string.Empty,
            County = string.Empty,
            Geotechnical = new GeotechnicalInput
            {
                BearingCapacityKpa = bearingValues[index % bearingValues.Length],
                CharacteristicBearingCapacityKpa = bearingValues[index % bearingValues.Length],
                UseBearingCapacityCorrection = index % 3 == 0,
                BearingCapacityWidthCorrectionFactor = index % 3 == 0 ? 0.3 : 0,
                BearingCapacityDepthCorrectionFactor = index % 3 == 0 ? 1.2 : 0,
                SoilBelowBaseUnitWeightKnPerM3 = 18 + index % 3,
                SoilAboveBaseAverageUnitWeightKnPerM3 = 17 + index % 4,
                SoilUnitWeightKnPerM3 = 17 + index % 5,
                InternalFrictionAngleDegree = 5 + index % 4 * 2,
                BaseFrictionCoefficient = frictionValues[index % frictionValues.Length],
                GroundwaterDepthM = groundwaterDepths[index % groundwaterDepths.Length],
                SoilDescription = "30组塔基计算矩阵模拟地勘参数",
                IsConfirmed = true
            },
            FoundationSettings = BuildScenarioSettings(foundationType, index, record)
        };

        try
        {
            ApplyLegacyReactionAsManualRegressionInput(project, record);
            project.TowerMast.IsConfirmed = true;
            var schemes = workflow.GenerateSchemes(project);
            ValidateScenarioResult(project, record, foundationType, schemes);
            var economy = schemes.Single(item => item.Preference == OptimizationPreference.Economy);
            var scenarioStatus = schemes
                .SelectMany(item => item.Checks)
                .Any(item => item.Status == CheckStatus.Warning)
                ? "PASS_WITH_SCOPE_WARNINGS"
                : "PASS";
            reportRows.Add(BuildScenarioReportRow(
                index + 1,
                scenarioStatus,
                project,
                record,
                foundationType,
                economy,
                null));
        }
        catch (Exception exception)
        {
            var failure = $"场景{index + 1:00} {foundationType} {record.TowerCode}: {exception.Message}";
            failures.Add(failure);
            reportRows.Add(BuildScenarioReportRow(
                index + 1,
                "FAIL",
                project,
                record,
                foundationType,
                null,
                exception.Message));
        }
    }

    var outputDirectory = Path.Combine(
        Directory.GetCurrentDirectory(),
        "output",
        "validation");
    Directory.CreateDirectory(outputDirectory);
    var jsonPath = Path.Combine(outputDirectory, "tower-foundation-30-scenario-results.json");
    var csvPath = Path.Combine(outputDirectory, "tower-foundation-30-scenario-results.csv");
    await File.WriteAllTextAsync(
        jsonPath,
        JsonSerializer.Serialize(
            reportRows,
            new JsonSerializerOptions { WriteIndented = true }),
        new UTF8Encoding(false));
    await File.WriteAllTextAsync(
        csvPath,
        BuildScenarioCsv(reportRows),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    Assert(
        failures.Count == 0,
        "30组场景存在失败：" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    Assert(reportRows.Count == scenarioCount, "场景报告必须完整记录30组输入与结果。" );
}

static async Task TestGoldenBenchmarkPack()
{
    var path = Path.Combine(
        Directory.GetCurrentDirectory(),
        "benchmarks",
        "golden-cases-v0.8.0.json");
    Assert(File.Exists(path), "缺少六类基础金标准回归包。" );
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
    var root = document.RootElement;
    Assert(root.GetProperty("productVersion").GetString() == "0.8.0",
        "金标准包版本必须与本轮版本一致。" );
    var cases = root.GetProperty("cases").EnumerateArray().ToList();
    Assert(cases.Count == 6, "金标准包必须恰好覆盖六类基础。" );
    var expectedTypes = Enum.GetNames<FoundationType>().OrderBy(value => value).ToArray();
    var actualTypes = cases
        .Select(item => item.GetProperty("foundationType").GetString() ?? string.Empty)
        .OrderBy(value => value)
        .ToArray();
    Assert(expectedTypes.SequenceEqual(actualTypes), "金标准包未完整覆盖六种FoundationType。" );
    Assert(cases.Select(item => item.GetProperty("caseId").GetString()).Distinct().Count() == 6,
        "金标准caseId不得重复。" );
    Assert(cases.All(item =>
            item.GetProperty("absoluteTolerance").GetDouble() > 0 &&
            item.GetProperty("lockedMetrics").EnumerateObject().Any()),
        "每个金标准案例必须包含正容差和至少一个锁定指标。" );

    var reportDirectory = Path.Combine(
        Directory.GetCurrentDirectory(),
        "output",
        "validation");
    Directory.CreateDirectory(reportDirectory);
    var report = new StringBuilder()
        .AppendLine("# 塔基智设 v0.8.0 六类基础金标准回归")
        .AppendLine()
        .AppendLine("- 状态：回归索引完整；各 runnerTestName 由同一自动测试套件逐项执行。")
        .AppendLine("- 边界：本报告证明程序版本内数值未静默漂移，不等同于第三方审图或试验验证完成。")
        .AppendLine()
        .AppendLine("| 案例 | 基础类型 | 自动测试 | 容差 |")
        .AppendLine("|---|---|---|---:|");
    foreach (var item in cases)
    {
        report.AppendLine(
            $"| {item.GetProperty("caseId").GetString()} | " +
            $"{item.GetProperty("foundationType").GetString()} | " +
            $"{item.GetProperty("runnerTestName").GetString()} | " +
            $"{item.GetProperty("absoluteTolerance").GetDouble():G} |");
    }
    await File.WriteAllTextAsync(
        Path.Combine(reportDirectory, "golden-benchmark-v0.8.0.md"),
        report.ToString(),
        new UTF8Encoding(false));
}

static void ApplyLegacyReactionAsManualRegressionInput(
    ProjectModel project,
    TowerLoadCatalogRecord record)
{
    var tower = project.TowerMast;
    tower.LoadSourceType = TowerLoadSourceType.Manual;
    tower.TowerModel = $"历史数值回归样本-{record.TowerCode}";
    tower.StructureType = EnterpriseTowerLoadService.InferStructureType(record.TowerType);
    tower.HeightM = EnterpriseTowerLoadService.ParseHeight(record) ?? tower.HeightM;
    tower.LoadCaseName = "已废止图集数值，仅用于计算内核回归测试，不作为现行设计依据";
    var useSingleLeg = PileLayoutRules.RequiresSingleLegReactions(
        tower.StructureType,
        project.FoundationSettings.FoundationType);
    tower.UsesIndividualPileReactions = useSingleLeg;
    if (useSingleLeg)
    {
        var standard = record.SingleLegReaction!.Standard!;
        tower.IndividualPileCompressionKn = standard.CompressionControl!.CompressionKn;
        tower.IndividualPileUpliftKn = standard.TensionControl!.TensionKn;
        tower.IndividualPileHorizontalKn = Math.Max(
            standard.CompressionControl.ShearKn,
            standard.TensionControl.ShearKn);
        tower.VerticalKn = tower.IndividualPileCompressionKn;
        tower.ShearXKn = tower.IndividualPileHorizontalKn;
        tower.ShearYKn = 0;
        tower.MomentXKnM = 0;
        tower.MomentYKnM = 0;
        tower.TorsionKnM = 0;
    }
    else
    {
        var standard = record.OverallBaseReaction!.Standard!;
        tower.IndividualPileCompressionKn = 0;
        tower.IndividualPileUpliftKn = 0;
        tower.IndividualPileHorizontalKn = 0;
        tower.VerticalKn = standard.AxialKn;
        tower.ShearXKn = standard.ShearKn;
        tower.ShearYKn = 0;
        tower.MomentXKnM = 0;
        tower.MomentYKnM = standard.MomentKnM;
        tower.TorsionKnM = 0;
    }

    PileLayoutRules.Synchronize(project);
}

static FoundationDesignSettings BuildScenarioSettings(
    FoundationType foundationType,
    int index,
    TowerLoadCatalogRecord record)
{
    var reaction = record.OverallBaseReaction!.Standard!;
    var highLoad = reaction.MomentKnM > 3000 || reaction.ShearKn > 120;
    var settings = new FoundationDesignSettings
    {
        FoundationType = foundationType,
        PedestalLengthM = foundationType == FoundationType.Raft ? 1.8 : 0.8 + 0.2 * (index % 3),
        PedestalWidthM = foundationType == FoundationType.Raft ? 1.8 : 0.8 + 0.2 * (index % 3),
        PedestalDiameterM = 0.8 + 0.2 * (index % 3),
        PedestalHeightM = 1.0 + 0.2 * (index % 4),
        MinimumBaseLengthM = 1.6,
        MaximumBaseLengthM = 12,
        MinimumBaseWidthM = 1.6,
        MaximumBaseWidthM = 12,
        MinimumBaseThicknessM = 0.6,
        MaximumBaseThicknessM = 2.6,
        DimensionStepM = 0.5,
        RequiredSlidingSafetyFactor = 1.5,
        BottomBarDiameterMm = highLoad ? 28 : 22,
        BottomBarSpacingMm = 100,
        ConcreteCoverMm = 50,
        MinimumReinforcementRatio = 0.0015
    };
    settings.Pile.PileCenterSpacingM = 6.0;
    settings.Pile.TieBeamWidthM = 0.40;
    settings.Pile.TieBeamHeightM = 0.60;
    if (foundationType == FoundationType.RigidShortPile)
    {
        var rigid = settings.RigidShortPile;
        rigid.MinimumDiameterM = 1.8;
        rigid.MaximumDiameterM = 3.6;
        rigid.DiameterStepM = 0.2;
        rigid.MinimumEmbeddedDepthM = 5;
        rigid.MaximumEmbeddedDepthM = 12;
        rigid.EmbeddedDepthStepM = 1;
        rigid.AboveGroundHeightM = 0.3;
        rigid.LateralResistanceWidthCoefficient = 0.65;
        rigid.VerticalReactionEccentricityCoefficient = 0.33;
        rigid.ConcreteElasticModulusMpa = 30_000;
        rigid.ConcreteCompressiveStrengthMpa = 14.3;
        rigid.LongitudinalBarDiameterMm = 32;
        rigid.LongitudinalBarCount = 72;
        rigid.MinimumLongitudinalReinforcementRatio = 0.005;
        rigid.StirrupDiameterMm = 14;
        rigid.StirrupSpacingMm = 100;
        rigid.StirrupLegCount = 4;
        rigid.IsConfirmed = true;
        rigid.SoilLayers =
        [
            new RigidShortPileSoilLayerInput
            {
                Name = "填土",
                ThicknessM = 1,
                HorizontalResistanceCoefficientMnPerM4 = 0
            },
            new RigidShortPileSoilLayerInput
            {
                Name = "粉质黏土",
                ThicknessM = 5,
                HorizontalResistanceCoefficientMnPerM4 = 12 + index % 3 * 3
            },
            new RigidShortPileSoilLayerInput
            {
                Name = "密实土层",
                ThicknessM = 10,
                HorizontalResistanceCoefficientMnPerM4 = 24 + index % 3 * 4
            }
        ];
        return settings;
    }

    if (foundationType != FoundationType.Pile)
    {
        return settings;
    }

    var pile = settings.Pile;
    pile.MinimumPileDiameterM = 0.8;
    pile.MaximumPileDiameterM = 2.0;
    pile.PileDiameterStepM = 0.2;
    pile.MinimumPileLengthM = 10;
    pile.MaximumPileLengthM = 24;
    pile.PileLengthStepM = 2;
    pile.AboveGroundHeightM = 0.3;
    pile.CapacityReductionFactor = 2;
    pile.SinglePileHorizontalCapacityKn = highLoad ? 1000 : 600;
    pile.PileMainBarDiameterMm = highLoad ? 36 : 28;
    pile.PileMainBarCount = highLoad ? 40 : 28;
    pile.MinimumLongitudinalReinforcementRatio = 0.005;
    pile.HorizontalResistanceCoefficientMnPerM4 = 20;
    pile.StirrupDiameterMm = highLoad ? 14 : 12;
    pile.StirrupSpacingMm = 100;
    pile.StirrupLegCount = 4;
    pile.IsConfirmed = true;
    pile.SoilLayers =
    [
        new PileSoilLayerInput
        {
            Name = "粉质黏土",
            ThicknessM = 4,
            SideResistanceKpa = 35 + index % 4 * 5,
            TipResistanceKpa = 600,
            UpliftCoefficient = 0.75
        },
        new PileSoilLayerInput
        {
            Name = "粉土",
            ThicknessM = 8,
            SideResistanceKpa = 70 + index % 3 * 10,
            TipResistanceKpa = 1600,
            UpliftCoefficient = 0.70
        },
        new PileSoilLayerInput
        {
            Name = "圆砾",
            ThicknessM = 12,
            SideResistanceKpa = 110 + index % 3 * 10,
            TipResistanceKpa = 2800 + index % 4 * 200,
            UpliftCoefficient = 0.60,
            IsSandOrGravel = true
        }
    ];
    return settings;
}

static void ValidateScenarioResult(
    ProjectModel project,
    TowerLoadCatalogRecord record,
    FoundationType foundationType,
    IReadOnlyList<FoundationScheme> schemes)
{
    var standard = record.OverallBaseReaction?.Standard;
    Assert(schemes.Count == 3, "每个场景必须生成经济型、施工型、稳健型三种建议。" );
    Assert(
        schemes.Select(item => item.Preference).Distinct().Count() == 3,
        "三种方案的策略标识必须互不重复。" );
    Assert(
        schemes.Select(item => item.Id).Distinct().Count() == 3 &&
        !ReferenceEquals(schemes[0], schemes[1]) &&
        !ReferenceEquals(schemes[1], schemes[2]),
        "三策略方案必须是独立对象，不能因策略收敛而相互覆盖。" );
    Assert(schemes.All(item => item.FoundationType == foundationType), "方案基础形式与场景输入不一致。" );
    Assert(schemes.All(item => item.IsFeasible), "场景生成结果包含失败或未计算项目。" );
    Assert(
        schemes.SelectMany(item => item.Checks).All(item =>
            double.IsFinite(item.Demand) &&
            double.IsFinite(item.Capacity) &&
            double.IsFinite(item.Utilization)),
        "通过方案中不得出现NaN或Infinity。" );
    var pileCount = PileLayoutRules.GetPileCount(project.TowerMast.StructureType);
    var foundationUnitCount = PileLayoutRules.GetFoundationUnitCount(
        project.TowerMast.StructureType,
        foundationType);
    if (foundationUnitCount > 1)
    {
        var leg = record.SingleLegReaction!.Standard!;
        Assert(project.FoundationLoad.UsesIndividualPileReactions, "多腿塔的独立基础单元必须标记为采用一个塔脚反力。" );
        AssertClose(project.FoundationLoad.IndividualPileCompressionKn, leg.CompressionControl!.CompressionKn, 1e-9, "单塔腿压力回填不一致。" );
        AssertClose(project.FoundationLoad.IndividualPileUpliftKn, leg.TensionControl!.TensionKn, 1e-9, "单塔腿上拔力回填不一致。" );
        AssertClose(project.FoundationLoad.VerticalKn, leg.CompressionControl.CompressionKn, 1e-9, "单个基础的压力控制值未进入计算顶层荷载。" );
        Assert(schemes.All(item => item.Geometry.FoundationUnitCount == foundationUnitCount), "方案几何必须保存3/4个独立基础单元数量。" );
        Assert(
            schemes.All(item => item.Geometry.TieBeamCount == foundationUnitCount &&
                                (foundationType == FoundationType.Pile ||
                                 item.Checks.Any(check => check.Code == "TIE_BEAM_LAYOUT"))),
            "多塔柱采用任一独立基础型式时，都必须按塔脚拓扑生成同数量闭合周边连系梁。" );
        if (foundationType != FoundationType.Pile)
        {
            Assert(schemes.All(item => item.Geometry.PileCount == 1), "非灌注桩独立基础不得误写为多根灌注桩。" );
        }
    }

    if (foundationType == FoundationType.Pile && pileCount > 1)
    {
        Assert(
            schemes.All(item => item.Geometry.PileCount == pileCount &&
                                item.Geometry.TieBeamCount == pileCount &&
                                item.Geometry.BaseLengthM == 0),
            "三管塔/增高架/角钢塔方案必须按塔型形成3或4根独立桩和同数周边连梁，且不得生成承台。" );
    }
    if (foundationUnitCount <= 1)
    {
        AssertClose(project.FoundationLoad.VerticalKn, Math.Abs(standard!.AxialKn), 1e-9, "企业图集轴力回填不一致。" );
        AssertClose(project.FoundationLoad.ShearXKn, Math.Abs(standard.ShearKn), 1e-9, "企业图集剪力回填不一致。" );
        AssertClose(project.FoundationLoad.MomentYKnM, Math.Abs(standard.MomentKnM), 1e-9, "企业图集弯矩回填不一致。" );
    }

    if (foundationType == FoundationType.Pile)
    {
        Assert(
            schemes.All(item => item.Checks.Any(check => check.Code == "PILE_COMPRESSION") &&
                                item.Checks.Any(check => check.Code == "PILE_UPLIFT") &&
                                item.Checks.Any(check => check.Code == "PILE_HORIZONTAL")),
            "桩基础场景必须完成抗压、抗拔和水平承载力校核。" );
        return;
    }

    if (foundationType == FoundationType.RigidShortPile)
    {
        Assert(
            schemes.All(item =>
                item.Checks.Any(check => check.Code == "RIGID_OVERTURNING") &&
                item.Checks.Any(check => check.Code == "RIGID_CLASSIFICATION") &&
                item.Checks.Any(check => check.Code == "RIGID_LONGITUDINAL_REINFORCEMENT") &&
                item.ReinforcementDesigns.Count == 2),
            "刚性短柱桩场景必须完成抗倾覆、刚性判别、纵筋和箍筋计算。" );
        return;
    }

    foreach (var scheme in schemes)
    {
        var geometry = scheme.Geometry;
        var pedestalArea = foundationType == FoundationType.CircularShortColumn
            ? Math.PI * geometry.PedestalLengthM * geometry.PedestalLengthM / 4
            : geometry.PedestalLengthM * geometry.PedestalWidthM;
        var pedestalQuantityCount = foundationType == FoundationType.Raft
            ? pileCount
            : 1;
        var totalPedestalArea = pedestalArea * pedestalQuantityCount;
        var concreteVolume =
            geometry.BaseLengthM * geometry.BaseWidthM * geometry.BaseThicknessM +
            totalPedestalArea * geometry.PedestalHeightM;
        var soilCoverArea = Math.Max(
            0,
            geometry.BaseLengthM * geometry.BaseWidthM - totalPedestalArea);
        var soilCoverVolume = soilCoverArea * geometry.PedestalHeightM;
        var submergedPedestalHeight = Math.Clamp(
            geometry.PedestalHeightM - project.Geotechnical.GroundwaterDepthM,
            0,
            geometry.PedestalHeightM);
        var submergedSlabHeight = Math.Clamp(
            geometry.EmbedmentDepthM -
            Math.Max(project.Geotechnical.GroundwaterDepthM, geometry.PedestalHeightM),
            0,
            geometry.BaseThicknessM);
        var submergedConcreteVolume =
            totalPedestalArea * submergedPedestalHeight +
            geometry.BaseLengthM * geometry.BaseWidthM * submergedSlabHeight;
        var submergedSoilVolume = soilCoverArea * submergedPedestalHeight;
        var effectiveWeight =
            concreteVolume * project.FoundationSettings.ConcreteUnitWeightKnPerM3 +
            soilCoverVolume * project.Geotechnical.SoilUnitWeightKnPerM3 -
            (submergedConcreteVolume + submergedSoilVolume) *
            project.FoundationSettings.WaterUnitWeightKnPerM3;
        var expectedAverage =
            (project.FoundationLoad.VerticalKn + effectiveWeight) /
            (geometry.BaseLengthM * geometry.BaseWidthM);
        AssertClose(
            scheme.Checks.Single(item => item.Code == "BEARING_AVERAGE").Demand,
            expectedAverage,
            1e-8,
            "地基平均压力未与塔脚轴力、基础/覆土有效自重保持平衡。" );
    }
}

static Dictionary<string, object?> BuildScenarioReportRow(
    int number,
    string status,
    ProjectModel project,
    TowerLoadCatalogRecord record,
    FoundationType foundationType,
    FoundationScheme? scheme,
    string? error)
{
    var standard = record.OverallBaseReaction?.Standard;
    var singleLeg = record.SingleLegReaction?.Standard;
    return new Dictionary<string, object?>
    {
        ["scenario"] = number,
        ["status"] = status,
        ["foundationType"] = foundationType.ToString(),
        ["towerCode"] = record.TowerCode,
        ["towerType"] = record.TowerType,
        ["sourceTitle"] = record.SourceTitle,
        ["sourcePdfPage"] = record.SourcePdfPage,
        ["axialKn"] = standard?.AxialKn,
        ["shearKn"] = standard?.ShearKn,
        ["momentKnM"] = standard?.MomentKnM,
        ["individualPileCompressionKn"] =
            project.FoundationLoad.UsesIndividualPileReactions
                ? project.FoundationLoad.IndividualPileCompressionKn
                : singleLeg?.CompressionControl?.CompressionKn,
        ["individualPileUpliftKn"] =
            project.FoundationLoad.UsesIndividualPileReactions
                ? project.FoundationLoad.IndividualPileUpliftKn
                : singleLeg?.TensionControl?.TensionKn,
        ["individualPileHorizontalKn"] =
            project.FoundationLoad.UsesIndividualPileReactions
                ? project.FoundationLoad.IndividualPileHorizontalKn
                : null,
        ["pileCount"] = scheme?.Geometry.PileCount,
        ["tieBeamCount"] = scheme?.Geometry.TieBeamCount,
        ["bearingCapacityKpa"] = project.Geotechnical.BearingCapacityKpa,
        ["groundwaterDepthM"] = project.Geotechnical.GroundwaterDepthM,
        ["frictionCoefficient"] = project.Geotechnical.BaseFrictionCoefficient,
        ["baseLengthM"] = scheme?.Geometry.BaseLengthM,
        ["baseWidthM"] = scheme?.Geometry.BaseWidthM,
        ["baseThicknessM"] = scheme?.Geometry.BaseThicknessM,
        ["pileDiameterM"] = scheme?.Geometry.PileDiameterM,
        ["pileLengthM"] = scheme?.Geometry.PileLengthM,
        ["maximumUtilization"] = scheme?.MaximumUtilization,
        ["warningCodes"] = scheme is null
            ? null
            : string.Join(
                ";",
                scheme.Checks
                    .Where(item => item.Status is CheckStatus.Warning or
                        CheckStatus.PendingInput or CheckStatus.SpecialReview or
                        CheckStatus.NotEvaluated)
                    .Select(item => item.Code)),
        ["error"] = error
    };
}

static string BuildScenarioCsv(IReadOnlyList<Dictionary<string, object?>> rows)
{
    var headers = rows[0].Keys.ToArray();
    var builder = new StringBuilder();
    builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
    foreach (var row in rows)
    {
        builder.AppendLine(string.Join(",", headers.Select(header =>
            EscapeCsv(Convert.ToString(row[header], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty))));
    }

    return builder.ToString();
}

static string EscapeCsv(string value) =>
    $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

static DesignWorkflowService BuildWorkflow()
{
    var poleCalculator = new MonitoringPoleLoadCalculator();
    var foundationCalculator = new RectangularShortColumnFoundationCalculator();
    var optimizer = new ThreeStrategyFoundationOptimizer(foundationCalculator);
    var advisor = new FoundationAdjustmentAdvisor(foundationCalculator);
    return new DesignWorkflowService(
        poleCalculator,
        foundationCalculator,
        optimizer,
        advisor);
}

static ProjectModel CreateConfirmedProject()
{
    return new ProjectModel
    {
        Name = "离线监控杆基础测试项目",
        Province = "甘肃省",
        City = "兰州市",
        County = "城关区",
        ProjectType = ProjectType.MonitoringPole,
        Geotechnical = new GeotechnicalInput
        {
            BearingCapacityKpa = 150,
            SoilUnitWeightKnPerM3 = 18,
            BaseFrictionCoefficient = 0.30,
            GroundwaterDepthM = 5,
            IsConfirmed = true,
            SourceType = ParameterSourceType.Manual
        }
    };
}

static async Task<string> ReadCalculationDocumentXmlAsync(OutputPackageResult output)
{
    var calculationBook = output.Files.Single(path => path.EndsWith("01_基础计算书.docx"));
    using var archive = ZipFile.OpenRead(calculationBook);
    var entry = archive.GetEntry("word/document.xml") ??
                throw new InvalidOperationException("Word计算书缺少document.xml。");
    using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
    var xml = await reader.ReadToEndAsync();
    _ = System.Xml.Linq.XDocument.Parse(xml);
    return xml;
}

static string DecodeDxfUnicodeEscapes(string value) =>
    System.Text.RegularExpressions.Regex.Replace(
        value,
        @"\\U\+([0-9A-Fa-f]{4})",
        match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());

static IReadOnlyList<(int Code, string Value)> ParseDxfPairs(string value)
{
    var lines = value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .TrimEnd('\n')
        .Split('\n');
    Assert(lines.Length % 2 == 0, "DXF必须由完整的组码/值二元组组成。" );

    var result = new List<(int Code, string Value)>(lines.Length / 2);
    for (var index = 0; index < lines.Length; index += 2)
    {
        Assert(
            int.TryParse(lines[index].Trim(), out var code),
            $"DXF第{index + 1}行不是有效组码：{lines[index]}" );
        result.Add((code, lines[index + 1]));
    }

    return result;
}

static void CopyOutputSampleIfRequested(OutputPackageResult output, string sampleName)
{
    var root = Environment.GetEnvironmentVariable("TOWER_FOUNDATION_EXPORT_SAMPLE");
    if (string.IsNullOrWhiteSpace(root))
    {
        return;
    }

    var targetDirectory = Path.Combine(root, sampleName);
    Directory.CreateDirectory(targetDirectory);
    foreach (var file in output.Files)
    {
        File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)), overwrite: true);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertClose(double actual, double expected, double tolerance, string message)
{
    if (Math.Abs(actual - expected) > tolerance)
    {
        throw new InvalidOperationException($"{message} 实际值={actual:G17}，期望值={expected:G17}。");
    }
}

static byte[] BuildSimplePdf(string text) => BuildSimplePdfPages(text);

static byte[] BuildSimplePdfPages(params string[] pageTexts)
{
    Assert(pageTexts.Length > 0, "PDF测试样本至少需要一页。" );
    var fontObjectNumber = 3 + pageTexts.Length * 2;
    var pageObjectNumbers = Enumerable.Range(0, pageTexts.Length)
        .Select(index => 3 + index * 2)
        .ToArray();
    var objects = new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectNumbers.Select(number => $"{number} 0 R"))}] /Count {pageTexts.Length} >>"
    };
    for (var index = 0; index < pageTexts.Length; index++)
    {
        var pageObjectNumber = pageObjectNumbers[index];
        var contentObjectNumber = pageObjectNumber + 1;
        var escapedText = pageTexts[index]
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
        var content = $"BT /F1 30 Tf 72 650 Td ({escapedText}) Tj ET";
        objects.Add(
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fontObjectNumber} 0 R >> >> /Contents {contentObjectNumber} 0 R >>");
        objects.Add(
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
    }
    objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");

    var builder = new StringBuilder("%PDF-1.4\n");
    var offsets = new List<int> { 0 };
    for (var index = 0; index < objects.Count; index++)
    {
        offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
        builder.Append(index + 1)
            .Append(" 0 obj\n")
            .Append(objects[index])
            .Append("\nendobj\n");
    }

    var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
    builder.Append("xref\n0 ")
        .Append(objects.Count + 1)
        .Append("\n0000000000 65535 f \n");
    foreach (var offset in offsets.Skip(1))
    {
        builder.Append(offset.ToString("D10"))
            .Append(" 00000 n \n");
    }

    builder.Append("trailer\n<< /Size ")
        .Append(objects.Count + 1)
        .Append(" /Root 1 0 R >>\nstartxref\n")
        .Append(xrefOffset)
        .Append("\n%%EOF\n");
    return Encoding.ASCII.GetBytes(builder.ToString());
}

static HttpResponseMessage BuildAiHttpResponse(
    string aiContent,
    HttpStatusCode statusCode = HttpStatusCode.OK)
{
    if (statusCode != HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(aiContent, Encoding.UTF8, "application/json")
        };
    }

    var response = JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = aiContent } } }
    });
    return new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(response, Encoding.UTF8, "application/json")
    };
}

static bool JsonContainsText(string json, string expected)
{
    using var document = JsonDocument.Parse(json);
    return JsonElementContainsText(document.RootElement, expected);
}

static bool JsonElementContainsText(JsonElement element, string expected) =>
    element.ValueKind switch
    {
        JsonValueKind.String =>
            element.GetString()?.Contains(expected, StringComparison.Ordinal) == true,
        JsonValueKind.Array =>
            element.EnumerateArray().Any(item => JsonElementContainsText(item, expected)),
        JsonValueKind.Object =>
            element.EnumerateObject().Any(property =>
                JsonElementContainsText(property.Value, expected)),
        _ => false
    };

internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(handler(request));
    }
}

internal sealed class FakeSettingsService(ApplicationSettings settings)
    : IApplicationSettingsService
{
    public ApplicationSettings Load() => settings;

    public void Save(
        ApplicationSettings replacement,
        string? replacementApiKey = null,
        bool clearApiKey = false,
        string? replacementVisionApiKey = null,
        bool clearVisionApiKey = false)
    {
    }

    public string? GetApiKey() => settings.HasApiKey ? "fake-key" : null;

    public string? GetVisionApiKey() => settings.HasVisionApiKey ? "fake-vision-key" : null;
}

internal sealed class FakeDeepSeekService(GeotechnicalAiExtractionResult result)
    : IDeepSeekService
{
    public string? LastDocumentText { get; private set; }

    public Task<AiConnectionResult> TestConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AiConnectionResult { Success = true });
    }

    public Task<GeotechnicalAiExtractionResult> ExtractGeotechnicalParametersAsync(
        string documentText,
        IProgress<AiOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LastDocumentText = documentText;
        progress?.Report(new AiOperationProgress(1, 1, "测试AI已完成"));
        return Task.FromResult(result);
    }
}

internal sealed class FakeWordTextExtractor : IWordTextExtractor
{
    public Task<DocumentTextExtractionResult> ExtractAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new DocumentTextExtractionResult
        {
            SourceName = Path.GetFileName(path),
            Content = "Word sample"
        });
    }
}

internal sealed class FakePdfOcrService(string content) : ILocalPdfOcrService
{
    public Task<OcrDocumentResult> ExtractAsync(
        string path,
        IProgress<OcrProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new OcrProgress(1, 1, "本地 OCR 已完成"));
        return Task.FromResult(new OcrDocumentResult
        {
            SourceName = "地勘报告.pdf",
            Content = content,
            PageCount = 1,
            ProcessedPageCount = 1,
            MeanConfidence = 0.88
        });
    }

    public Task<OcrDocumentResult> ExtractRangeAsync(
        string path,
        int startPage,
        int endPage,
        IProgress<OcrProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExtractAsync(path, progress, cancellationToken);
}
