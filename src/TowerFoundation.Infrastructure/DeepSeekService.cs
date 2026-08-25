using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TowerFoundation.Application;

namespace TowerFoundation.Infrastructure;

public sealed class DeepSeekService : IDeepSeekService, IAnchorDrawingAiService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IApplicationSettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public DeepSeekService(
        IApplicationSettingsService settingsService,
        HttpMessageHandler? handler = null)
    {
        _settingsService = settingsService;
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _ownsHttpClient = true;
    }

    public async Task<AiConnectionResult> TestConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var content = await SendAsync(
            "你是连接测试助手。只回复“连接正常”。",
            "请进行连接测试。",
            jsonOutput: false,
            maxTokens: 24,
            cancellationToken);
        return new AiConnectionResult
        {
            Success = !string.IsNullOrWhiteSpace(content),
            Message = string.IsNullOrWhiteSpace(content)
                ? "DeepSeek 返回了空内容。"
                : $"DeepSeek 连接正常：{content.Trim()}"
        };
    }

    public async Task<GeotechnicalAiExtractionResult> ExtractGeotechnicalParametersAsync(
        string documentText,
        IProgress<AiOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentText))
        {
            throw new ArgumentException("地勘资料中没有可供分析的正文或表格文字。", nameof(documentText));
        }

        var normalizedText = documentText.Length > 120_000
            ? documentText[..120_000]
            : documentText;
        progress?.Report(new AiOperationProgress(1, 4, "DeepSeek 第一轮：逐项建立原文证据清单"));
        var inventoryContent = await SendAsync(
            EvidenceInventoryPrompt,
            "以下是地勘报告的分页文字。只建立证据清单，不做设计取值：\n\n" + normalizedText,
            jsonOutput: true,
            maxTokens: 3200,
            cancellationToken);
        var inventoryJson = StripCodeFence(inventoryContent);
        try
        {
            using var _ = JsonDocument.Parse(inventoryJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "DeepSeek 第一轮未返回有效的证据清单 JSON，未写入项目。",
                exception);
        }

        progress?.Report(new AiOperationProgress(2, 4, "DeepSeek 第二轮：交叉核对表头、单位、冲突和适用桩型"));
        var content = await SendAsync(
            EngineeringAuditPrompt,
            "FACT_INVENTORY_JSON:\n" + inventoryJson +
            "\n\nORIGINAL_DOCUMENT_TEXT:\n" + normalizedText,
            jsonOutput: true,
            maxTokens: 3600,
            cancellationToken);
        var json = StripCodeFence(content);

        progress?.Report(new AiOperationProgress(3, 4, "正在执行本机确定性校验，拦截臆造值和矛盾值"));
        var result = ParseGeotechnicalResponse(json, normalizedText, "DeepSeek");
        progress?.Report(new AiOperationProgress(4, 4, "AI 提取与本机规则复核完成，等待人工确认"));
        return result;
    }

    internal static GeotechnicalAiExtractionResult ParseGeotechnicalResponse(
        string json,
        string evidenceText,
        string providerName)
    {
        GeotechnicalResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<GeotechnicalResponse>(
                StripCodeFence(json),
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"{providerName} 返回内容不是有效的地勘参数 JSON，未写入项目。",
                exception);
        }

        if (response is null)
        {
            throw new InvalidOperationException($"{providerName} 未返回可用的地勘参数，未写入项目。");
        }

        var groundwaterCandidates = NormalizeNumbers(
            response.GroundwaterDepthCandidatesM,
            0,
            500,
            evidenceText);
        var warnings = NormalizeTexts(response.CriticalWarnings);
        var evidencePages = NormalizeEvidencePages(
            response.EvidencePages,
            evidenceText);
        var evidenceLocations = NormalizeTexts(response.EvidenceLocations)
            .Take(30)
            .ToList();
        var groundwaterDepth = ExplicitNumber(
            response.GroundwaterDepthM,
            0,
            500,
            evidenceText);
        if (groundwaterCandidates.Count > 1)
        {
            groundwaterDepth = null;
            warnings.Add(
                $"报告存在互相矛盾的地下水埋深：{string.Join("m、", groundwaterCandidates.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)))}m；软件未自动选值，必须人工核对原报告。" );
        }
        else if (groundwaterCandidates.Count == 1)
        {
            groundwaterDepth = groundwaterCandidates[0];
        }

        var pileParameterOptions = NormalizePileParameterOptions(
            response.PileParameterOptions,
            evidenceText);
        var pileSoilLayers = response.PileParametersSafeToApply == true
            ? NormalizePileSoilLayers(response.PileSoilLayers, evidenceText)
            : [];
        if (pileParameterOptions.Count > 0 && pileSoilLayers.Count == 0)
        {
            warnings.Add("报告给出了桩参数，但推荐成桩方法与参数表桩型不完全对应，或抗拔系数缺失；未自动回填计算表。请先确认采用的成桩方法和参数列。");
        }

        var characteristicBearingCapacity =
            ExplicitNumber(response.CharacteristicBearingCapacityKpa, 20, 3000, evidenceText) ??
            UniqueValue(pileParameterOptions.Select(option => option.CharacteristicBearingCapacityKpa));
        var soilUnitWeight =
            ExplicitNumber(response.SoilUnitWeightKnPerM3, 8, 35, evidenceText) ??
            UniqueValue(pileParameterOptions.Select(option => option.SoilUnitWeightKnPerM3));

        var result = new GeotechnicalAiExtractionResult
        {
            ProjectName = response.ProjectName?.Trim() ?? string.Empty,
            SiteLocation = response.SiteLocation?.Trim() ?? string.Empty,
            Province = response.Province?.Trim() ?? string.Empty,
            City = response.City?.Trim() ?? string.Empty,
            County = response.County?.Trim() ?? string.Empty,
            Longitude = ExplicitNumber(response.Longitude, 70, 140, evidenceText),
            Latitude = ExplicitNumber(response.Latitude, 0, 60, evidenceText),
            InvestigationStage = response.InvestigationStage?.Trim() ?? string.Empty,
            InvestigationGrade = response.InvestigationGrade?.Trim() ?? string.Empty,
            BuildingSafetyGrade = response.BuildingSafetyGrade?.Trim() ?? string.Empty,
            BearingCapacityKpa = ExplicitNumber(response.BearingCapacityKpa, 20, 3000, evidenceText),
            CharacteristicBearingCapacityKpa = characteristicBearingCapacity,
            BearingCapacityWidthCorrectionFactor =
                ExplicitNumber(response.BearingCapacityWidthCorrectionFactor, 0, 20, evidenceText),
            BearingCapacityDepthCorrectionFactor =
                ExplicitNumber(response.BearingCapacityDepthCorrectionFactor, 0, 20, evidenceText),
            SoilUnitWeightKnPerM3 = soilUnitWeight,
            CohesionKpa = ExplicitNumber(response.CohesionKpa, 0, 5_000, evidenceText),
            InternalFrictionAngleDegree =
                ExplicitNumber(response.InternalFrictionAngleDegree, 0, 90, evidenceText),
            CompressionModulusMpa =
                ExplicitNumber(response.CompressionModulusMpa, 0.01, 10_000, evidenceText),
            SoilBelowBaseUnitWeightKnPerM3 =
                ExplicitNumber(response.SoilBelowBaseUnitWeightKnPerM3, 8, 35, evidenceText),
            SoilAboveBaseAverageUnitWeightKnPerM3 =
                ExplicitNumber(response.SoilAboveBaseAverageUnitWeightKnPerM3, 8, 35, evidenceText),
            BaseFrictionCoefficient = ExplicitNumber(response.BaseFrictionCoefficient, 0.05, 1.0, evidenceText),
            GroundwaterDepthM = groundwaterDepth,
            GroundwaterDepthCandidatesM = groundwaterCandidates,
            SoilDescription = response.SoilDescription?.Trim() ?? string.Empty,
            Evidence = response.Evidence?.Trim() ?? string.Empty,
            EvidencePages = evidencePages,
            EvidenceLocations = evidenceLocations,
            RecommendedFoundationType =
                response.RecommendedFoundationType?.Trim() ?? string.Empty,
            SpecialSoilRisks = response.SpecialSoilRisks?.Trim() ?? string.Empty,
            PileSoilLayers = pileSoilLayers,
            PileParameterOptions = pileParameterOptions,
            SinglePileHorizontalCapacityKn =
                ExplicitNumber(response.SinglePileHorizontalCapacityKn, 1, 100_000, evidenceText),
            SeismicIntensityDegree = response.SeismicIntensityDegree is >= 5 and <= 12
                ? response.SeismicIntensityDegree
                : null,
            DesignBasicGroundAccelerationG =
                ExplicitNumber(response.DesignBasicGroundAccelerationG, 0.01, 1, evidenceText),
            DesignEarthquakeGroup = response.DesignEarthquakeGroup?.Trim() ?? string.Empty,
            SiteClass = response.SiteClass?.Trim() ?? string.Empty,
            CharacteristicPeriodS =
                ExplicitNumber(response.CharacteristicPeriodS, 0.01, 5, evidenceText),
            CriticalWarnings = warnings.Distinct(StringComparer.Ordinal).ToList(),
            Confidence = Math.Clamp(response.Confidence ?? 0, 0, 1)
        };
        return result;
    }

    public async Task<AnchorBoltAiExtractionResult> ExtractAnchorBoltParametersAsync(
        string documentText,
        IProgress<AiOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentText))
        {
            throw new ArgumentException("塔脚详图中没有可供分析的文字。", nameof(documentText));
        }

        var normalizedText = documentText.Length > 80_000
            ? documentText[..80_000]
            : documentText;
        progress?.Report(new AiOperationProgress(1, 3, "正在逐项定位锚栓规格、尺寸和材料证据"));
        var content = await SendAsync(
            AnchorDrawingPrompt,
            "以下是塔脚板、锚栓笼或厂家连接详图的OCR/Word文字。只提取原文明确出现的值：\n\n" + normalizedText,
            jsonOutput: true,
            maxTokens: 1200,
            cancellationToken);
        AnchorDrawingResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<AnchorDrawingResponse>(
                StripCodeFence(content),
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("DeepSeek返回的锚栓参数不是有效JSON，未写入项目。", exception);
        }
        if (response is null)
        {
            throw new InvalidOperationException("DeepSeek未返回可用的锚栓详图候选参数。" );
        }

        progress?.Report(new AiOperationProgress(2, 3, "正在用本地原文拦截图纸中未出现的数值"));
        var boltCountNumber = ExplicitNumber(response.BoltCount, 3, 200, normalizedText);
        var result = new AnchorBoltAiExtractionResult
        {
            BoltCount = boltCountNumber is { } count ? (int)Math.Round(count) : null,
            NominalDiameterMm = ExplicitNumber(response.NominalDiameterMm, 6, 200, normalizedText),
            BoltCircleDiameterMm = ExplicitNumber(response.BoltCircleDiameterMm, 50, 20_000, normalizedText),
            EmbedmentDepthMm = ExplicitNumber(response.EmbedmentDepthMm, 50, 20_000, normalizedText),
            TensileStrengthDesignMpa = ExplicitNumber(response.TensileStrengthDesignMpa, 10, 2_000, normalizedText),
            ShearStrengthDesignMpa = ExplicitNumber(response.ShearStrengthDesignMpa, 10, 2_000, normalizedText),
            ThreadStressAreaFactor = ExplicitNumber(response.ThreadStressAreaFactor, 0.3, 1.0, normalizedText),
            MaterialGrade = !string.IsNullOrWhiteSpace(response.MaterialGrade) &&
                            normalizedText.Contains(response.MaterialGrade.Trim(), StringComparison.OrdinalIgnoreCase)
                ? response.MaterialGrade.Trim()
                : string.Empty,
            Evidence = response.Evidence?.Trim() ?? string.Empty,
            Warnings = NormalizeTexts(response.Warnings),
            Confidence = Math.Clamp(response.Confidence ?? 0, 0, 1)
        };
        progress?.Report(new AiOperationProgress(3, 3, "锚栓详图候选已提取，等待用户一次确认"));
        return result;
    }

    private const string AnchorDrawingPrompt = """
你是塔脚锚栓详图证据提取员，不是设计师。只输出JSON，不写解释性前后缀。

强制规则：
1. 只提取OCR/Word原文明确出现且能对应表头、标注或节点说明的值；禁止按塔高、荷载或经验猜规格。
2. 锚栓数量和直径必须区分，例如“12-M36”应输出数量12、直径36mm。
3. 锚栓圆直径、埋深统一输出原图标注的毫米数，不自行换算成米。
4. 材料牌号可以抄录；抗拉/抗剪强度设计值仅在原文明确给出时填写，不能由材料牌号自行推导。
5. 多组冲突值时相应字段填null，并在warnings说明冲突。
6. evidence必须包含原始标注、图号/页码或附近文字，便于人工复核。

输出字段：
{
  "bolt_count": null,
  "nominal_diameter_mm": null,
  "bolt_circle_diameter_mm": null,
  "embedment_depth_mm": null,
  "tensile_strength_design_mpa": null,
  "shear_strength_design_mpa": null,
  "thread_stress_area_factor": null,
  "material_grade": "",
  "evidence": "",
  "warnings": [],
  "confidence": 0.0
}
""";

    private const string EvidenceInventoryPrompt = """
你是“岩土勘察报告证据摘录员”，不是设计师。任务是逐页、逐表头抄录事实，绝不选设计值，绝不补全模糊数字。

强制规则：
1. 先检查封面、扉页、工程概况、勘察任务书和页眉中的建设地点；site_location保留原文，province/city/county按原文可明确判断的行政区分别填写。县级市填county，不得把项目名称中的同名词误当地点。
2. 保留页码标记、章节名、表号、列组标题、符号和单位；表格必须先识别“列组→子列→行”的对应关系再抄数字。
3. 同一参数在正文、表格、结论、剖面图出现多个值时全部保留，写入 data_conflicts，不得擅自采用结论值或正文值。
4. fak 是地基承载力特征值；fa 是按宽深修正后的承载力特征值，二者不得互换。没有明确“fa/修正后”就不得生成 fa。
5. m 的单位是 MN/m4，表示土的水平抗力系数的比例系数；绝不是 qsik。
6. qsik、qpk 在桩基表中通常是极限侧阻力/极限端阻力标准值；不得改称“特征值”。
7. 混凝土预制桩、泥浆护壁钻（冲）孔桩、干作业钻孔桩、人工挖孔桩等不同列必须分别记录，严禁串列。
8. “/”“—”“未提供”“空白”一律记为 null，不得按经验补成 0、0.6、0.7 等值。
9. OCR 模糊数字必须在 evidence 中标“疑似”，不要纠错或猜测。

只输出一个 JSON 对象：
{
  "context":{"project_name":"","site_location":"","province":"","city":"","county":"","longitude":null,"latitude":null,"investigation_stage":"","investigation_grade":"","building_safety_grade":""},
  "groundwater_mentions":[{"depth_m":null,"page":null,"section":"","evidence":""}],
  "soil_layers":[{"name":"","top_depth_m":null,"bottom_depth_m":null,"thickness_m":null,"unit_weight_kn_per_m3":null,"cohesion_kpa":null,"friction_angle_degree":null,"compression_modulus_mpa":null,"uplift_coefficient":null,"fak_kpa":null,"horizontal_resistance_coefficient_mn_per_m4":null,"evidence":""}],
  "pile_parameter_sets":[{"pile_method":"","layer_name":"","top_depth_m":null,"bottom_depth_m":null,"thickness_m":null,"unit_weight_kn_per_m3":null,"fak_kpa":null,"compression_modulus_mpa":null,"horizontal_resistance_coefficient_mn_per_m4":null,"qsik_limit_standard_kpa":null,"qpk_limit_standard_kpa":null,"uplift_coefficient":null,"evidence":""}],
  "foundation_recommendations":[{"foundation_type":"","pile_method":"","bearing_layer":"","page":null,"evidence":""}],
  "seismic":{"intensity_degree":null,"design_basic_ground_acceleration_g":null,"design_earthquake_group":"","site_class":"","characteristic_period_s":null,"evidence":""},
  "special_risks":[{"type":"","statement":"","page":null,"evidence":""}],
  "data_conflicts":[{"field":"","values":[],"explanation":""}]
}
""";

    internal const string EngineeringAuditPrompt = """
你是“岩土参数复核员”。输入包含第一轮 FACT_INVENTORY_JSON 和原报告分页文字。逐项回查原文，只输出最终 JSON，不写解释性前后缀。

判定规则：
1. 项目建设地点优先从封面、扉页、工程概况和勘察任务书交叉核对；province/city/county只有原文能明确对应行政区时才填写。site_location保留更详细原文。地点用于界面回填和规范数据库匹配，不得据此臆造场地类别。
2. 所有最终数值必须在原报告中明确出现并能指向章节/表号/列名；不能从经验、规范默认值或相邻数字推断。
3. 地下水埋深所有候选写入 groundwater_depth_candidates_m。若不同章节数值不一致，groundwater_depth_m 必须为 null，并写 critical_warnings。
4. bearing_capacity_kpa 只接受报告明确给出的修正后 fa；只有 fak 时 bearing_capacity_kpa=null，characteristic_bearing_capacity_kpa 填 fak。
5. pile_parameter_options 必须按桩型列分别保留 qsik/qpk。推荐“人工挖孔桩”而表格只有“混凝土预制桩、泥浆护壁钻（冲）孔桩”时，属于方法不完全对应：pile_parameters_safe_to_apply=false，pile_soil_layers=[]，但两套 options 都要保留。
6. pile_soil_layers 只有在推荐/已选成桩方法与唯一参数列明确对应，并且每层厚度、qsik、qpk、抗拔系数均有明确数值时才允许生成。任一项为“/”或空白就不得安全回填。
7. m(MN/m4) 与 qsik(kPa) 严格分开；qsik/qpk 是极限阻力标准值。单桩水平承载力只有报告或试桩明确给出 kN 数值才填写。
8. 任何冲突、表头错位风险、OCR 疑似、成桩方法不匹配都写 critical_warnings，并降低 confidence。
9. soil_unit_weight_kn_per_m3、cohesion_kpa、internal_friction_angle_degree、compression_modulus_mpa 应从主要持力层参数表逐项填写，不得只藏在 evidence 或 pile_parameter_options 中。
10. special_soil_risks 必须检查并概括：不良地质作用、液化/湿陷等特殊土、地下水类型及水土腐蚀性、邻近道路/地下管线/既有构筑物施工风险。原文明确提到时不得留空；原文未提到的不得臆造。
11. evidence_pages 只列原报告分页标记中真实出现的PDF页码；evidence_locations 逐项填写“页码+章节/表号+字段”，例如“第18页 表4.3 第⑤层fak”。无法定位时留空，不得猜页码。

表格校准示例（这是规则示例，不是待分析报告的预置答案）：若一行依次为“0~12、γ=21、C=15、φ=22、Es=7、λ=/、fak=110、m=14、预制桩qsik=35/qpk=950、泥浆护壁钻孔桩qsik=38/qpk=400”，则 m=14 绝不能填入 qsik，λ 必须为 null，两套桩参数必须按方法分开；若正文推荐人工挖孔桩，则不得自动生成 pile_soil_layers。

最终 JSON 字段：
{
  "project_name":"","site_location":"","province":"","city":"","county":"","longitude":null,"latitude":null,
  "investigation_stage":"","investigation_grade":"","building_safety_grade":"",
  "bearing_capacity_kpa":null,"characteristic_bearing_capacity_kpa":null,
  "bearing_width_correction_factor":null,"bearing_depth_correction_factor":null,
  "soil_unit_weight_kn_per_m3":null,"cohesion_kpa":null,
  "internal_friction_angle_degree":null,"compression_modulus_mpa":null,
  "soil_below_base_unit_weight_kn_per_m3":null,
  "soil_above_base_average_unit_weight_kn_per_m3":null,"base_friction_coefficient":null,
  "groundwater_depth_m":null,"groundwater_depth_candidates_m":[],
  "soil_description":"","recommended_foundation_type":"","special_soil_risks":"",
  "pile_parameters_safe_to_apply":false,
  "pile_soil_layers":[{"name":"","thickness_m":null,"side_resistance_kpa":null,"tip_resistance_kpa":null,"uplift_coefficient":null}],
  "pile_parameter_options":[{"pile_method":"","layer_name":"","top_depth_m":null,"bottom_depth_m":null,"thickness_m":null,"soil_unit_weight_kn_per_m3":null,"characteristic_bearing_capacity_kpa":null,"compression_modulus_mpa":null,"horizontal_resistance_coefficient_mn_per_m4":null,"side_resistance_limit_standard_kpa":null,"tip_resistance_limit_standard_kpa":null,"uplift_coefficient":null,"evidence":""}],
  "single_pile_horizontal_capacity_kn":null,
  "seismic_intensity_degree":null,"design_basic_ground_acceleration_g":null,
  "design_earthquake_group":"","site_class":"","characteristic_period_s":null,
  "critical_warnings":[],"evidence":"","evidence_pages":[],"evidence_locations":[],"confidence":0.0
}
""";

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static IReadOnlyList<int> NormalizeEvidencePages(
        IReadOnlyList<int>? pages,
        string sourceText)
    {
        if (pages is null || pages.Count == 0)
        {
            return [];
        }

        var availablePages = Regex.Matches(
                sourceText,
                @"---\s*第\s*(\d+)\s*页")
            .Select(match => int.TryParse(match.Groups[1].Value, out var page)
                ? page
                : 0)
            .Where(page => page > 0)
            .ToHashSet();
        if (availablePages.Count == 0)
        {
            return [];
        }

        return pages
            .Where(page => availablePages.Contains(page))
            .Distinct()
            .OrderBy(page => page)
            .Take(80)
            .ToList();
    }

    private static IReadOnlyList<PileSoilLayerCandidate> NormalizePileSoilLayers(
        IReadOnlyList<PileSoilLayerResponse>? layers,
        string sourceText)
    {
        if (layers is null)
        {
            return [];
        }

        return layers
            .Where(layer =>
                !string.IsNullOrWhiteSpace(layer.Name) &&
                ExplicitNumber(layer.ThicknessM, 0.01, 500, sourceText) is not null &&
                ExplicitNumber(layer.SideResistanceKpa, 0.01, 5_000, sourceText) is not null &&
                ExplicitNumber(layer.TipResistanceKpa, 0.01, 100_000, sourceText) is not null &&
                ExplicitNumber(layer.UpliftCoefficient, 0.01, 1, sourceText) is not null)
            .Take(20)
            .Select(layer => new PileSoilLayerCandidate
            {
                Name = layer.Name!.Trim(),
                ThicknessM = ExplicitNumber(layer.ThicknessM, 0.01, 500, sourceText)!.Value,
                SideResistanceKpa = ExplicitNumber(layer.SideResistanceKpa, 0.01, 5_000, sourceText)!.Value,
                TipResistanceKpa = ExplicitNumber(layer.TipResistanceKpa, 0.01, 100_000, sourceText)!.Value,
                UpliftCoefficient = ExplicitNumber(layer.UpliftCoefficient, 0.01, 1, sourceText)!.Value
            })
            .ToList();
    }

    private static IReadOnlyList<PileParameterSetCandidate> NormalizePileParameterOptions(
        IReadOnlyList<PileParameterOptionResponse>? options,
        string sourceText)
    {
        if (options is null)
        {
            return [];
        }

        return options
            .Where(option =>
                !string.IsNullOrWhiteSpace(option.PileMethod) &&
                !string.IsNullOrWhiteSpace(option.LayerName) &&
                (ExplicitNumber(option.SideResistanceLimitStandardKpa, 0.01, 5_000, sourceText) is not null ||
                 ExplicitNumber(option.TipResistanceLimitStandardKpa, 0.01, 100_000, sourceText) is not null ||
                 ExplicitNumber(option.HorizontalResistanceCoefficientMnPerM4, 0, 5_000, sourceText) is not null ||
                 ExplicitNumber(option.CompressionModulusMpa, 0.01, 10_000, sourceText) is not null))
            .Take(40)
            .Select(option => new PileParameterSetCandidate
            {
                PileMethod = option.PileMethod!.Trim(),
                LayerName = option.LayerName!.Trim(),
                TopDepthM = ExplicitNumber(option.TopDepthM, 0, 500, sourceText),
                BottomDepthM = ExplicitNumber(option.BottomDepthM, 0, 500, sourceText),
                ThicknessM = ExplicitNumber(option.ThicknessM, 0.01, 500, sourceText),
                SoilUnitWeightKnPerM3 = ExplicitNumber(option.SoilUnitWeightKnPerM3, 8, 35, sourceText),
                CharacteristicBearingCapacityKpa = ExplicitNumber(option.CharacteristicBearingCapacityKpa, 20, 3_000, sourceText),
                CompressionModulusMpa = ExplicitNumber(option.CompressionModulusMpa, 0.01, 10_000, sourceText),
                HorizontalResistanceCoefficientMnPerM4 = ExplicitNumber(option.HorizontalResistanceCoefficientMnPerM4, 0, 5_000, sourceText),
                SideResistanceLimitStandardKpa = ExplicitNumber(option.SideResistanceLimitStandardKpa, 0.01, 5_000, sourceText),
                TipResistanceLimitStandardKpa = ExplicitNumber(option.TipResistanceLimitStandardKpa, 0.01, 100_000, sourceText),
                UpliftCoefficient = ExplicitNumber(option.UpliftCoefficient, 0.01, 1, sourceText),
                Evidence = option.Evidence?.Trim() ?? string.Empty
            })
            .ToList();
    }

    private static List<double> NormalizeNumbers(
        IReadOnlyList<double>? numbers,
        double minimum,
        double maximum,
        string sourceText)
    {
        return (numbers ?? [])
            .Select(value => ExplicitNumber(value, minimum, maximum, sourceText))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .DistinctBy(value => Math.Round(value, 3))
            .OrderBy(value => value)
            .ToList();
    }

    private static List<string> NormalizeTexts(IReadOnlyList<string>? values)
    {
        return (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(30)
            .ToList();
    }

    private static double? UniqueValue(IEnumerable<double?> values)
    {
        var distinct = values
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .DistinctBy(value => Math.Round(value, 6))
            .Take(2)
            .ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    private static double? ExplicitNumber(
        double? value,
        double minimum,
        double maximum,
        string sourceText)
    {
        var ranged = InRange(value, minimum, maximum);
        if (ranged is null)
        {
            return null;
        }

        foreach (Match match in Regex.Matches(
                     sourceText,
                     @"(?<![\d.])[-+]?\d+(?:\.\d+)?(?![\d.])",
                     RegexOptions.CultureInvariant))
        {
            if (double.TryParse(
                    match.Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var sourceValue) &&
                Math.Abs(sourceValue - ranged.Value) <= Math.Max(1e-6, Math.Abs(ranged.Value) * 1e-6))
            {
                return ranged;
            }
        }

        return null;
    }

    private async Task<string> SendAsync(
        string systemPrompt,
        string userPrompt,
        bool jsonOutput,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var settings = _settingsService.Load();
        if (settings.AiMode == AiOperatingMode.OfflineOnly)
        {
            throw new InvalidOperationException("当前已手动切换为纯离线模式。请直接录入地勘参数，或在设置中启用 AI 在线优先。");
        }

        var apiKey = _settingsService.GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("尚未配置 DeepSeek API 密钥。请打开“设置”，粘贴密钥并测试连接。");
        }

        var requestBody = new ChatRequest
        {
            Model = settings.DeepSeekModel,
            Messages =
            [
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userPrompt)
            ],
            MaxTokens = maxTokens,
            Stream = false,
            Thinking = new ThinkingOptions("disabled"),
            ResponseFormat = jsonOutput ? new ResponseFormat("json_object") : null
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildChatCompletionUri(settings.DeepSeekBaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "DeepSeek 请求超时，已自动降级为手工录入；基础计算仍可继续。");
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "当前无法连接 DeepSeek，已自动降级为手工录入；请检查网络后重试。",
                exception);
        }

        using (response)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(BuildApiError(response.StatusCode, responseText));
            }

            ChatResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ChatResponse>(responseText, JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("DeepSeek 返回了无法解析的响应。", exception);
            }

            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("DeepSeek 返回了空内容，请稍后重试；基础计算仍可手工继续。");
            }

            return content;
        }
    }

    private static Uri BuildChatCompletionUri(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException("DeepSeek API 地址无效，请在设置中检查。");
        }

        var normalized = baseUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(normalized), "chat/completions");
    }

    private static string BuildApiError(HttpStatusCode statusCode, string responseText)
    {
        var message = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var value))
            {
                message = value.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Do not echo untrusted response bodies or credentials into the UI.
        }

        var suffix = string.IsNullOrWhiteSpace(message) ? string.Empty : $"：{message}";
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "DeepSeek API 密钥无效，请在设置中重新填写。",
            HttpStatusCode.PaymentRequired => "DeepSeek 账户余额不足，已自动降级为手工录入。",
            (HttpStatusCode)429 => "DeepSeek 请求过于频繁，请稍后重试；当前可继续手工录入。",
            _ => $"DeepSeek 请求失败（HTTP {(int)statusCode}）{suffix}"
        };
    }

    private static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine >= 0 && lastFence > firstNewLine
            ? trimmed[(firstNewLine + 1)..lastFence].Trim()
            : trimmed;
    }

    private static double? InRange(double? value, double minimum, double maximum)
    {
        return value is { } number &&
               !double.IsNaN(number) &&
               !double.IsInfinity(number) &&
               number >= minimum &&
               number <= maximum
            ? number
            : null;
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; init; } = [];

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("thinking")]
        public ThinkingOptions? Thinking { get; init; }

        [JsonPropertyName("response_format")]
        public ResponseFormat? ResponseFormat { get; init; }
    }

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ThinkingOptions(
        [property: JsonPropertyName("type")] string Type);

    private sealed record ResponseFormat(
        [property: JsonPropertyName("type")] string Type);

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; init; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatResponseMessage? Message { get; init; }
    }

    private sealed class ChatResponseMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed class GeotechnicalResponse
    {
        [JsonPropertyName("project_name")]
        public string? ProjectName { get; init; }

        [JsonPropertyName("site_location")]
        public string? SiteLocation { get; init; }

        [JsonPropertyName("province")]
        public string? Province { get; init; }

        [JsonPropertyName("city")]
        public string? City { get; init; }

        [JsonPropertyName("county")]
        public string? County { get; init; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; init; }

        [JsonPropertyName("latitude")]
        public double? Latitude { get; init; }

        [JsonPropertyName("investigation_stage")]
        public string? InvestigationStage { get; init; }

        [JsonPropertyName("investigation_grade")]
        public string? InvestigationGrade { get; init; }

        [JsonPropertyName("building_safety_grade")]
        public string? BuildingSafetyGrade { get; init; }

        [JsonPropertyName("bearing_capacity_kpa")]
        public double? BearingCapacityKpa { get; init; }

        [JsonPropertyName("characteristic_bearing_capacity_kpa")]
        public double? CharacteristicBearingCapacityKpa { get; init; }

        [JsonPropertyName("bearing_width_correction_factor")]
        public double? BearingCapacityWidthCorrectionFactor { get; init; }

        [JsonPropertyName("bearing_depth_correction_factor")]
        public double? BearingCapacityDepthCorrectionFactor { get; init; }

        [JsonPropertyName("soil_unit_weight_kn_per_m3")]
        public double? SoilUnitWeightKnPerM3 { get; init; }

        [JsonPropertyName("cohesion_kpa")]
        public double? CohesionKpa { get; init; }

        [JsonPropertyName("internal_friction_angle_degree")]
        public double? InternalFrictionAngleDegree { get; init; }

        [JsonPropertyName("compression_modulus_mpa")]
        public double? CompressionModulusMpa { get; init; }

        [JsonPropertyName("soil_below_base_unit_weight_kn_per_m3")]
        public double? SoilBelowBaseUnitWeightKnPerM3 { get; init; }

        [JsonPropertyName("soil_above_base_average_unit_weight_kn_per_m3")]
        public double? SoilAboveBaseAverageUnitWeightKnPerM3 { get; init; }

        [JsonPropertyName("base_friction_coefficient")]
        public double? BaseFrictionCoefficient { get; init; }

        [JsonPropertyName("groundwater_depth_m")]
        public double? GroundwaterDepthM { get; init; }

        [JsonPropertyName("groundwater_depth_candidates_m")]
        public List<double>? GroundwaterDepthCandidatesM { get; init; }

        [JsonPropertyName("soil_description")]
        public string? SoilDescription { get; init; }

        [JsonPropertyName("evidence")]
        public string? Evidence { get; init; }

        [JsonPropertyName("evidence_pages")]
        public List<int>? EvidencePages { get; init; }

        [JsonPropertyName("evidence_locations")]
        public List<string>? EvidenceLocations { get; init; }

        [JsonPropertyName("recommended_foundation_type")]
        public string? RecommendedFoundationType { get; init; }

        [JsonPropertyName("special_soil_risks")]
        public string? SpecialSoilRisks { get; init; }

        [JsonPropertyName("pile_soil_layers")]
        public List<PileSoilLayerResponse>? PileSoilLayers { get; init; }

        [JsonPropertyName("pile_parameters_safe_to_apply")]
        public bool? PileParametersSafeToApply { get; init; }

        [JsonPropertyName("pile_parameter_options")]
        public List<PileParameterOptionResponse>? PileParameterOptions { get; init; }

        [JsonPropertyName("single_pile_horizontal_capacity_kn")]
        public double? SinglePileHorizontalCapacityKn { get; init; }

        [JsonPropertyName("seismic_intensity_degree")]
        public int? SeismicIntensityDegree { get; init; }

        [JsonPropertyName("design_basic_ground_acceleration_g")]
        public double? DesignBasicGroundAccelerationG { get; init; }

        [JsonPropertyName("design_earthquake_group")]
        public string? DesignEarthquakeGroup { get; init; }

        [JsonPropertyName("site_class")]
        public string? SiteClass { get; init; }

        [JsonPropertyName("characteristic_period_s")]
        public double? CharacteristicPeriodS { get; init; }

        [JsonPropertyName("critical_warnings")]
        public List<string>? CriticalWarnings { get; init; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; init; }
    }

    private sealed class AnchorDrawingResponse
    {
        [JsonPropertyName("bolt_count")]
        public double? BoltCount { get; init; }

        [JsonPropertyName("nominal_diameter_mm")]
        public double? NominalDiameterMm { get; init; }

        [JsonPropertyName("bolt_circle_diameter_mm")]
        public double? BoltCircleDiameterMm { get; init; }

        [JsonPropertyName("embedment_depth_mm")]
        public double? EmbedmentDepthMm { get; init; }

        [JsonPropertyName("tensile_strength_design_mpa")]
        public double? TensileStrengthDesignMpa { get; init; }

        [JsonPropertyName("shear_strength_design_mpa")]
        public double? ShearStrengthDesignMpa { get; init; }

        [JsonPropertyName("thread_stress_area_factor")]
        public double? ThreadStressAreaFactor { get; init; }

        [JsonPropertyName("material_grade")]
        public string? MaterialGrade { get; init; }

        [JsonPropertyName("evidence")]
        public string? Evidence { get; init; }

        [JsonPropertyName("warnings")]
        public List<string>? Warnings { get; init; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; init; }
    }

    private sealed class PileSoilLayerResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("thickness_m")]
        public double? ThicknessM { get; init; }

        [JsonPropertyName("side_resistance_kpa")]
        public double? SideResistanceKpa { get; init; }

        [JsonPropertyName("tip_resistance_kpa")]
        public double? TipResistanceKpa { get; init; }

        [JsonPropertyName("uplift_coefficient")]
        public double? UpliftCoefficient { get; init; }
    }

    private sealed class PileParameterOptionResponse
    {
        [JsonPropertyName("pile_method")]
        public string? PileMethod { get; init; }

        [JsonPropertyName("layer_name")]
        public string? LayerName { get; init; }

        [JsonPropertyName("top_depth_m")]
        public double? TopDepthM { get; init; }

        [JsonPropertyName("bottom_depth_m")]
        public double? BottomDepthM { get; init; }

        [JsonPropertyName("thickness_m")]
        public double? ThicknessM { get; init; }

        [JsonPropertyName("soil_unit_weight_kn_per_m3")]
        public double? SoilUnitWeightKnPerM3 { get; init; }

        [JsonPropertyName("characteristic_bearing_capacity_kpa")]
        public double? CharacteristicBearingCapacityKpa { get; init; }

        [JsonPropertyName("compression_modulus_mpa")]
        public double? CompressionModulusMpa { get; init; }

        [JsonPropertyName("horizontal_resistance_coefficient_mn_per_m4")]
        public double? HorizontalResistanceCoefficientMnPerM4 { get; init; }

        [JsonPropertyName("side_resistance_limit_standard_kpa")]
        public double? SideResistanceLimitStandardKpa { get; init; }

        [JsonPropertyName("tip_resistance_limit_standard_kpa")]
        public double? TipResistanceLimitStandardKpa { get; init; }

        [JsonPropertyName("uplift_coefficient")]
        public double? UpliftCoefficient { get; init; }

        [JsonPropertyName("evidence")]
        public string? Evidence { get; init; }
    }
}
