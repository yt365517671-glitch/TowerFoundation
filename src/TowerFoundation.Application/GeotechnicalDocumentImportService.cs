using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public sealed class GeotechnicalDocumentImportService
{
    private readonly IApplicationSettingsService _settingsService;
    private readonly IDeepSeekService _deepSeekService;
    private readonly IWordTextExtractor _wordTextExtractor;
    private readonly ILocalPdfOcrService _localPdfOcrService;

    public GeotechnicalDocumentImportService(
        IApplicationSettingsService settingsService,
        IDeepSeekService deepSeekService,
        IWordTextExtractor wordTextExtractor,
        ILocalPdfOcrService localPdfOcrService)
    {
        _settingsService = settingsService;
        _deepSeekService = deepSeekService;
        _wordTextExtractor = wordTextExtractor;
        _localPdfOcrService = localPdfOcrService;
    }

    public async Task<GeotechnicalDocumentImportResult> ImportWordAsync(
        string path,
        FoundationType foundationType,
        CancellationToken cancellationToken = default,
        IProgress<AiOperationProgress>? aiProgress = null)
    {
        var document = await _wordTextExtractor.ExtractAsync(
            path,
            cancellationToken);
        var aiResult = await AnalyzeWithDeepSeekAsync(
            BuildFoundationSpecificDocumentText(document.Content, foundationType),
            requireAi: true,
            cancellationToken,
            aiProgress);
        return new GeotechnicalDocumentImportResult
        {
            Document = document,
            AiResult = aiResult
        };
    }

    public async Task<GeotechnicalDocumentImportResult> ImportPdfAsync(
        string path,
        FoundationType foundationType,
        IProgress<OcrProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IProgress<AiOperationProgress>? aiProgress = null)
    {
        var currentSettings = _settingsService.Load();
        var ocr = await _localPdfOcrService.ExtractRangeAsync(
            path,
            currentSettings.OcrStartPage,
            currentSettings.OcrEndPage,
            progress,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(ocr.Content))
        {
            throw new InvalidOperationException(
                "本地 OCR 没有读取到有效文字。请检查扫描清晰度，或继续手工录入。");
        }

        var document = new DocumentTextExtractionResult
        {
            SourceName = ocr.ExtractionMode == PdfTextExtractionMode.NativeTextLayer
                ? $"{ocr.SourceName}（PDF原生文字层）"
                : $"{ocr.SourceName}（本地OCR）",
            Content = ocr.Content
        };
        if (currentSettings.AiMode == AiOperatingMode.OnlinePreferred &&
            currentSettings.HasApiKey)
        {
            progress?.Report(new OcrProgress(
                ocr.ProcessedPageCount,
                ocr.PageCount,
                $"本地 OCR 已完成，正在由 {currentSettings.DeepSeekModel} 分析并提取关键参数"));
        }

        var aiResult = await AnalyzeWithDeepSeekAsync(
            BuildFoundationSpecificDocumentText(document.Content, foundationType),
            requireAi: false,
            cancellationToken,
            aiProgress);
        var skipReason = aiResult is not null
            ? string.Empty
            : currentSettings.AiMode == AiOperatingMode.OfflineOnly
                ? "当前为纯离线模式，仅完成本地 OCR。"
                : "尚未配置 DeepSeek API 密钥，仅完成本地 OCR。";
        return new GeotechnicalDocumentImportResult
        {
            Document = document,
            OcrResult = ocr,
            AiResult = aiResult,
            AiSkipReason = skipReason
        };
    }

    public GeotechnicalParameterApplicationResult ApplyAiCandidates(
        ProjectModel project,
        GeotechnicalDocumentImportResult import)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(import);
        var providerDisplay = string.IsNullOrWhiteSpace(import.AiProviderDisplay)
            ? "AI"
            : import.AiProviderDisplay.Trim();
        var result = import.AiResult ??
                     throw new InvalidOperationException($"{providerDisplay} 未返回地勘候选参数。");
        var document = import.Document;
        var assigned = new List<string>();

        if (!string.IsNullOrWhiteSpace(result.ProjectName) &&
            (string.IsNullOrWhiteSpace(project.Name) ||
             project.Name.StartsWith("新建", StringComparison.Ordinal)))
        {
            project.Name = result.ProjectName.Trim();
            assigned.Add($"项目名称“{project.Name}”");
        }

        if (result.BearingCapacityKpa is { } bearingCapacity)
        {
            project.Geotechnical.BearingCapacityKpa = bearingCapacity;
            project.Geotechnical.UseBearingCapacityCorrection = false;
            assigned.Add($"承载力 {bearingCapacity:F1} kPa");
        }

        if (result.CharacteristicBearingCapacityKpa is { } characteristicBearingCapacity)
        {
            project.Geotechnical.CharacteristicBearingCapacityKpa =
                characteristicBearingCapacity;
            assigned.Add($"fak {characteristicBearingCapacity:F1} kPa");

            if (result.BearingCapacityKpa is null &&
                project.FoundationSettings.FoundationType is
                    FoundationType.RectangularShortColumn or
                    FoundationType.CircularShortColumn or
                    FoundationType.Raft)
            {
                // The calculator ignores this baseline while correction mode is enabled.
                // Keeping it equal to fak avoids leaving an unrelated sample fa in the visible form.
                project.Geotechnical.BearingCapacityKpa = characteristicBearingCapacity;
                project.Geotechnical.UseBearingCapacityCorrection = true;
                assigned.Add("已展开并启用 fak 宽深修正");
            }
        }

        if (result.BearingCapacityWidthCorrectionFactor is { } widthFactor)
        {
            project.Geotechnical.BearingCapacityWidthCorrectionFactor = widthFactor;
            assigned.Add($"ηb {widthFactor:F2}");
        }

        if (result.BearingCapacityDepthCorrectionFactor is { } depthFactor)
        {
            project.Geotechnical.BearingCapacityDepthCorrectionFactor = depthFactor;
            assigned.Add($"ηd {depthFactor:F2}");
        }

        if (result.SoilUnitWeightKnPerM3 is { } soilUnitWeight)
        {
            project.Geotechnical.SoilUnitWeightKnPerM3 = soilUnitWeight;
            assigned.Add($"土重度 {soilUnitWeight:F1} kN/m³");
        }

        if (result.InternalFrictionAngleDegree is { } confirmedFrictionAngle)
        {
            project.Geotechnical.InternalFrictionAngleDegree = confirmedFrictionAngle;
            assigned.Add($"内摩擦角 {confirmedFrictionAngle:F1}°");
        }

        if (result.CompressionModulusMpa is { } compressionModulus)
        {
            project.Geotechnical.CompressionModulusMpa = compressionModulus;
            assigned.Add($"压缩模量 Es {compressionModulus:F2} MPa");
        }

        if (result.SoilBelowBaseUnitWeightKnPerM3 is { } soilBelowBase)
        {
            project.Geotechnical.SoilBelowBaseUnitWeightKnPerM3 = soilBelowBase;
            assigned.Add($"基底以下土重度 {soilBelowBase:F1} kN/m³");
        }

        if (result.SoilAboveBaseAverageUnitWeightKnPerM3 is { } soilAboveBase)
        {
            project.Geotechnical.SoilAboveBaseAverageUnitWeightKnPerM3 = soilAboveBase;
            assigned.Add($"基底以上平均重度 {soilAboveBase:F1} kN/m³");
        }

        if (result.BaseFrictionCoefficient is { } frictionCoefficient)
        {
            project.Geotechnical.BaseFrictionCoefficient = frictionCoefficient;
            assigned.Add($"摩擦系数 {frictionCoefficient:F2}");
        }

        if (result.GroundwaterDepthM is { } groundwaterDepth)
        {
            project.Geotechnical.GroundwaterDepthM = groundwaterDepth;
            assigned.Add($"地下水埋深 {groundwaterDepth:F2} m");
        }

        if (result.SeismicIntensityDegree is { } seismicIntensity)
        {
            project.Geotechnical.SeismicIntensityDegree = seismicIntensity;
        }
        if (result.DesignBasicGroundAccelerationG is { } seismicAcceleration)
        {
            project.Geotechnical.DesignBasicGroundAccelerationG = seismicAcceleration;
        }
        if (!string.IsNullOrWhiteSpace(result.DesignEarthquakeGroup))
        {
            project.Geotechnical.DesignEarthquakeGroup = result.DesignEarthquakeGroup;
        }
        if (!string.IsNullOrWhiteSpace(result.SiteClass))
        {
            project.Geotechnical.SiteClass = result.SiteClass;
        }
        if (result.CharacteristicPeriodS is { } characteristicPeriod)
        {
            project.Geotechnical.CharacteristicPeriodS = characteristicPeriod;
        }
        if (result.SeismicIntensityDegree is not null ||
            result.DesignBasicGroundAccelerationG is not null ||
            !string.IsNullOrWhiteSpace(result.DesignEarthquakeGroup) ||
            !string.IsNullOrWhiteSpace(result.SiteClass))
        {
            project.Geotechnical.SeismicParameterSource =
                $"地勘AI候选；{document.SourceName}；{result.Evidence}";
        }
        project.Geotechnical.SpecialSoilRisks = result.SpecialSoilRisks;
        var evidenceParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Evidence))
        {
            evidenceParts.Add(result.Evidence.Trim());
        }
        if (result.EvidencePages.Count > 0)
        {
            evidenceParts.Add(
                "证据页：" + string.Join("、", result.EvidencePages.Select(page => $"第{page}页")));
        }
        if (result.EvidenceLocations.Count > 0)
        {
            evidenceParts.Add("证据位置：" + string.Join("；", result.EvidenceLocations));
        }
        project.Geotechnical.Evidence = string.Join("；", evidenceParts);
        project.Geotechnical.AiConfidence = result.Confidence;

        if (project.FoundationSettings.FoundationType == FoundationType.Pile &&
            result.PileSoilLayers.Count > 0)
        {
            project.FoundationSettings.Pile.SoilLayers = result.PileSoilLayers
                .Select(layer => new PileSoilLayerInput
                {
                    Name = layer.Name,
                    ThicknessM = layer.ThicknessM,
                    SideResistanceKpa = layer.SideResistanceKpa,
                    TipResistanceKpa = layer.TipResistanceKpa,
                    UpliftCoefficient = layer.UpliftCoefficient
                })
                .ToList();
            project.FoundationSettings.Pile.IsConfirmed = false;
            assigned.Add($"桩土分层 {result.PileSoilLayers.Count} 层");
        }

        if (project.FoundationSettings.FoundationType == FoundationType.Pile &&
            result.SinglePileHorizontalCapacityKn is { } horizontalCapacity)
        {
            project.FoundationSettings.Pile.SinglePileHorizontalCapacityKn =
                horizontalCapacity;
            project.FoundationSettings.Pile.IsConfirmed = false;
            assigned.Add($"单桩水平承载力 {horizontalCapacity:F1} kN");
        }

        if (project.FoundationSettings.FoundationType is
            FoundationType.RigidShortPile or
            FoundationType.RigidRectangularShortPile)
        {
            var rigidLayers = result.PileParameterOptions
                .Where(option =>
                    option.ThicknessM is > 0 &&
                    option.HorizontalResistanceCoefficientMnPerM4 is >= 0)
                .Select(option => new RigidShortPileSoilLayerInput
                {
                    Name = option.LayerName,
                    ThicknessM = option.ThicknessM!.Value,
                    HorizontalResistanceCoefficientMnPerM4 =
                        option.HorizontalResistanceCoefficientMnPerM4!.Value
                })
                .ToList();
            if (rigidLayers.Count > 0)
            {
                project.FoundationSettings.RigidShortPile.SoilLayers = rigidLayers;
                project.FoundationSettings.RigidShortPile.IsConfirmed = false;
                assigned.Add($"刚性短柱桩m值分层 {rigidLayers.Count} 层");
            }
        }

        var settlementLayers = result.PileParameterOptions
            .Where(option =>
                option.ThicknessM is > 0 &&
                option.CompressionModulusMpa is > 0)
            .GroupBy(option => new
            {
                Name = option.LayerName.Trim(),
                Top = Math.Round(option.TopDepthM ?? -1, 3),
                Bottom = Math.Round(option.BottomDepthM ?? -1, 3)
            })
            .Select(group => group.First())
            .Select(option => new SettlementSoilLayerInput
            {
                Name = option.LayerName,
                ThicknessM = option.ThicknessM!.Value,
                CompressionModulusMpa = option.CompressionModulusMpa!.Value
            })
            .Take(20)
            .ToList();
        if (settlementLayers.Count > 0)
        {
            project.FoundationSettings.SpecialtyDesign.Settlement.SoilLayers =
                settlementLayers;
            project.FoundationSettings.SpecialtyDesign.Settlement.Source = new EngineeringParameterSource
            {
                SourceType = import.AiSourceType,
                SourceDocument = document.SourceName,
                SourceLocation = "地勘分层参数表（AI候选）",
                Note = "分层厚度与压缩模量已提取；允许沉降值和经验系数仍需人工确认。",
                Confidence = result.Confidence,
                IsConfirmed = false
            };
            assigned.Add($"沉降计算分层 {settlementLayers.Count} 层（待确认）");
        }

        var description = string.IsNullOrWhiteSpace(result.SoilDescription)
            ? $"{providerDisplay} 未形成土层简述。"
            : result.SoilDescription;
        var layerParameters = new[]
        {
            result.SoilUnitWeightKnPerM3 is { } gamma ? $"γ={gamma:0.###}kN/m³" : null,
            result.CohesionKpa is { } cohesion ? $"c={cohesion:0.###}kPa" : null,
            result.InternalFrictionAngleDegree is { } frictionAngle ? $"φ={frictionAngle:0.###}°" : null,
            result.CompressionModulusMpa is { } summaryCompressionModulus ? $"Es={summaryCompressionModulus:0.###}MPa" : null,
            result.CharacteristicBearingCapacityKpa is { } fak ? $"fak={fak:0.###}kPa" : null
        }.Where(value => value is not null).ToList();
        var layerParameterSummary = layerParameters.Count == 0
            ? string.Empty
            : $"主要土层参数：{string.Join("，", layerParameters)}";
        var foundationRecommendation =
            string.IsNullOrWhiteSpace(result.RecommendedFoundationType)
                ? "报告未明确给出基础形式建议。"
                : $"基础形式建议：{result.RecommendedFoundationType}";
        var specialSoilRisks =
            string.IsNullOrWhiteSpace(result.SpecialSoilRisks)
                ? "未从报告中提取到明确的特殊土风险。"
                : $"特殊土风险：{result.SpecialSoilRisks}";

        var context = string.Join("；", new[]
        {
            string.IsNullOrWhiteSpace(result.ProjectName) ? null : $"项目：{result.ProjectName}",
            string.IsNullOrWhiteSpace(result.SiteLocation) ? null : $"场址：{result.SiteLocation}",
            string.IsNullOrWhiteSpace(result.Province + result.City + result.County)
                ? null
                : $"行政区：{result.Province}{result.City}{result.County}",
            result.Longitude is { } longitude && result.Latitude is { } latitude
                ? $"坐标：{longitude:F5}°, {latitude:F5}°"
                : null,
            string.IsNullOrWhiteSpace(result.InvestigationStage) ? null : $"阶段：{result.InvestigationStage}",
            string.IsNullOrWhiteSpace(result.InvestigationGrade) ? null : $"勘察等级：{result.InvestigationGrade}",
            string.IsNullOrWhiteSpace(result.BuildingSafetyGrade) ? null : $"安全等级：{result.BuildingSafetyGrade}"
        }.Where(value => value is not null));
        var groundwaterCandidates = result.GroundwaterDepthCandidatesM.Count == 0
            ? string.Empty
            : $"地下水埋深原文候选：{string.Join("、", result.GroundwaterDepthCandidatesM.Select(value => $"{value:0.###}m"))}";
        var pileOptions = result.PileParameterOptions.Count == 0
            ? string.Empty
            : "桩参数候选（未必可直接用于当前桩型）：\n" +
              string.Join("\n", result.PileParameterOptions.Select(option =>
                  $"- {option.PileMethod}/{option.LayerName}：qsik={FormatCandidate(option.SideResistanceLimitStandardKpa)} kPa，" +
                  $"qpk={FormatCandidate(option.TipResistanceLimitStandardKpa)} kPa，" +
                  $"Es={FormatCandidate(option.CompressionModulusMpa)} MPa，" +
                  $"λ={FormatCandidate(option.UpliftCoefficient)}；{option.Evidence}"));
        var seismic = result.SeismicIntensityDegree is null &&
                      result.DesignBasicGroundAccelerationG is null &&
                      string.IsNullOrWhiteSpace(result.SiteClass)
            ? string.Empty
            : $"抗震信息：设防烈度{result.SeismicIntensityDegree?.ToString() ?? "未提取"}度，" +
              $"基本地震加速度{FormatCandidate(result.DesignBasicGroundAccelerationG)}g，" +
              $"分组{result.DesignEarthquakeGroup}，场地类别{result.SiteClass}，特征周期{FormatCandidate(result.CharacteristicPeriodS)}s";
        var warnings = result.CriticalWarnings.Count == 0
            ? string.Empty
            : "关键冲突/待核对：\n" + string.Join("\n", result.CriticalWarnings.Select(value => $"- {value}"));

        project.Geotechnical.SoilDescription = string.Join("\n", new[]
        {
            $"AI候选结果（来源：{document.SourceName}）",
            context,
            description,
            layerParameterSummary,
            foundationRecommendation,
            specialSoilRisks,
            groundwaterCandidates,
            pileOptions,
            seismic,
            warnings,
            $"依据：{result.Evidence}"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        project.Geotechnical.SourceType = import.AiSourceType;
        project.Geotechnical.IsConfirmed = false;
        project.ModifiedAt = DateTimeOffset.Now;
        project.AuditTrail.Add(new AuditRecord
        {
            Action = $"{providerDisplay} 地勘候选提取",
            Details =
                $"来源文件：{document.SourceName}；提取字符数：{document.CharacterCount}；" +
                $"置信度：{result.Confidence:P0}。结果尚未人工确认。"
        });

        var warningSuffix = result.CriticalWarnings.Count == 0
            ? string.Empty
            : $"；发现{result.CriticalWarnings.Count}项关键冲突，仅冲突字段保留人工确认";
        var summary = assigned.Count == 0
            ? $"{providerDisplay} 未找到可安全回填的数值，置信度{result.Confidence:P0}；请查看依据并手工填写。"
            : $"已直接填入：{string.Join("、", assigned)}；AI置信度{result.Confidence:P0}{warningSuffix}。请依据原报告核对后确认。";
        return new GeotechnicalParameterApplicationResult(
            assigned,
            summary,
            result.Confidence);
    }

    private async Task<GeotechnicalAiExtractionResult?> AnalyzeWithDeepSeekAsync(
        string documentText,
        bool requireAi,
        CancellationToken cancellationToken,
        IProgress<AiOperationProgress>? aiProgress)
    {
        var settings = _settingsService.Load();
        var aiAvailable =
            settings.AiMode == AiOperatingMode.OnlinePreferred &&
            settings.HasApiKey;
        if (!aiAvailable)
        {
            if (requireAi)
            {
                throw new InvalidOperationException(
                    settings.AiMode == AiOperatingMode.OfflineOnly
                        ? "当前为纯离线模式。Word 已在本机读取，但没有调用 DeepSeek；请手工录入，或在设置中启用 AI 在线优先。"
                        : "尚未配置 DeepSeek API 密钥。Word 已在本机读取，但无法进行 AI 参数提取。");
            }

            return null;
        }

        return await _deepSeekService.ExtractGeotechnicalParametersAsync(
            documentText,
            aiProgress,
            cancellationToken);
    }

    public static string BuildFoundationSpecificRequirements(
        FoundationType foundationType)
    {
        return foundationType switch
        {
            FoundationType.Pile =>
                "当前已选基础形式：桩基础。优先提取逐层厚度、压缩模量Es、桩侧极限阻力标准值qsik、桩端极限阻力标准值qpk、抗拔系数、地下水埋深，以及报告或试桩明确给出的单桩水平承载力。不同成桩方法的参数必须分列，只有与推荐/已选成桩方法明确一致时才允许回填。",
            FoundationType.RigidShortPile =>
                "当前已选基础形式：刚性短柱桩基础－圆形。优先提取土重度、内摩擦角、地下水埋深，并在pile_parameter_options中按土层填写thickness_m、compression_modulus_mpa和horizontal_resistance_coefficient_mn_per_m4；pile_method固定写刚性短柱桩。不得用qsik、qpk代替水平抗力比例系数m。",
            FoundationType.RigidRectangularShortPile =>
                "当前已选基础形式：刚性短柱桩基础－矩形。优先提取土重度、内摩擦角、地下水埋深，并在pile_parameter_options中按土层填写thickness_m、compression_modulus_mpa和horizontal_resistance_coefficient_mn_per_m4；pile_method固定写矩形刚性短柱桩。不得用qsik、qpk代替水平抗力比例系数m。",
            FoundationType.Raft =>
                "当前已选基础形式：中央塔柱筏板基础。优先提取持力层、fak/fa、宽深修正系数、基底上下土重度、地下水、摩擦系数、基底以下逐层厚度与压缩模量Es，以及软弱下卧层风险。",
            FoundationType.CircularShortColumn =>
                "当前已选基础形式：独立基础－圆形柱。优先提取持力层、fak/fa、宽深修正系数、基底上下土重度、地下水、摩擦系数、基底以下逐层厚度与压缩模量Es，以及特殊土风险。",
            _ =>
                "当前已选基础形式：独立基础－矩形柱。优先提取持力层、fak/fa、宽深修正系数、基底上下土重度、地下水、摩擦系数、基底以下逐层厚度与压缩模量Es，以及特殊土风险。"
        };
    }

    private static string BuildFoundationSpecificDocumentText(
        string documentText,
        FoundationType foundationType) =>
        BuildFoundationSpecificRequirements(foundationType) + "\n\n" + documentText;

    private static string FormatCandidate(double? value)
    {
        return value?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "未提供";
    }
}

public sealed class GeotechnicalDocumentImportResult
{
    public DocumentTextExtractionResult Document { get; init; } = new();

    public OcrDocumentResult? OcrResult { get; init; }

    public GeotechnicalAiExtractionResult? AiResult { get; init; }

    public string AiSkipReason { get; init; } = string.Empty;

    public string AiProviderDisplay { get; init; } = "DeepSeek";

    public ParameterSourceType AiSourceType { get; init; } =
        ParameterSourceType.DeepSeek;

    public string EvidencePaneTitle { get; init; } = "本机提取原文";

    public bool UsedAi => AiResult is not null;
}

public sealed record GeotechnicalParameterApplicationResult(
    IReadOnlyList<string> AssignedFields,
    string Summary,
    double Confidence);
