using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PDFtoImage;
using SkiaSharp;
using TowerFoundation.Application;
using TowerFoundation.Domain;

namespace TowerFoundation.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class MonitoringDrawingVisionAiService :
    IMonitoringDrawingVisionAiService,
    IDisposable
{
    private const long MaximumPdfBytes = 30L * 1024 * 1024;
    private const int MaximumPagesPerFile = 10;
    private const int RenderDpi = 280;
    private const string FastFallbackModel = "qwen3.6-flash";
    private const string AlternateFallbackModel = "qwen3-vl-flash";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IApplicationSettingsService _settingsService;
    private readonly HttpClient _httpClient;

    public MonitoringDrawingVisionAiService(
        IApplicationSettingsService settingsService,
        HttpMessageHandler? handler = null)
    {
        _settingsService = settingsService;
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<MonitoringDrawingVisionBatchResult> AnalyzePdfsAsync(
        IReadOnlyList<string> paths,
        IProgress<AiOperationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        VisionModelSwitchOptions? switchOptions = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return new MonitoringDrawingVisionBatchResult();
        }

        var settings = _settingsService.Load();
        EnsureAvailable(settings);
        var jobs = new List<PageJob>();
        var failures = new List<string>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists)
                {
                    failures.Add($"{Path.GetFileName(path)}：文件不存在。");
                    continue;
                }
                if (!file.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{file.Name}：只支持PDF文件。");
                    continue;
                }
                if (file.Length > MaximumPdfBytes)
                {
                    failures.Add($"{file.Name}：文件超过30MB，请先拆分。");
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                var pageCount = Conversion.GetPageCount(bytes);
                if (pageCount <= 0)
                {
                    failures.Add($"{file.Name}：没有可识别页面。");
                    continue;
                }
                if (pageCount > MaximumPagesPerFile)
                {
                    failures.Add($"{file.Name}：共{pageCount}页，本次只处理前{MaximumPagesPerFile}页。");
                }

                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                for (var page = 1; page <= Math.Min(pageCount, MaximumPagesPerFile); page++)
                {
                    jobs.Add(new PageJob(file.Name, hash, bytes, page, pageCount));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add($"{Path.GetFileName(path)}：{exception.Message}");
            }
        }

        var candidates = new List<MonitoringDrawingCandidate>();
        var totalSteps = Math.Max(1, jobs.Count * 3);
        for (var index = 0; index < jobs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var job = jobs[index];
            var remaining = jobs.Count - index - 1;
            try
            {
                progress?.Report(new AiOperationProgress(
                    index * 3 + 1,
                    totalSteps,
                    $"正在渲染 {job.FileName} 第{job.PageNumber}/{job.PageCount}页（{RenderDpi} DPI）；剩余{remaining}页"));
                using var rendered = RenderPage(job.Bytes, job.PageNumber);
                var imageSet = BuildImageSet(rendered);

                progress?.Report(new AiOperationProgress(
                    index * 3 + 2,
                    totalSteps,
                    $"{settings.VisionModel} 首轮提取 {job.FileName} 第{job.PageNumber}页；如需换模型将先征求确认；剩余{remaining}页"));
                var firstResponse = await SendWithFallbackAsync(
                    BuildFirstPassContent(imageSet, job.PageNumber),
                    FirstPassSystemPrompt,
                    settings.VisionModel,
                    progress,
                    index * 3 + 2,
                    totalSteps,
                    job,
                    cancellationToken,
                    switchOptions);
                var firstJson = firstResponse.Json;
                var first = ParseCandidateResponse(
                    firstJson,
                    job.FileName,
                    job.Sha256,
                    job.PageNumber,
                    firstResponse.Model);

                progress?.Report(new AiOperationProgress(
                    index * 3 + 3,
                    totalSteps,
                    $"正在复核关键尺寸与歧义字段：{job.FileName} 第{job.PageNumber}页；剩余{remaining}页"));
                var reviewResponse = await SendWithFallbackAsync(
                    BuildReviewContent(imageSet, job.PageNumber, firstJson),
                    ReviewSystemPrompt,
                    settings.VisionModel,
                    progress,
                    index * 3 + 3,
                    totalSteps,
                    job,
                    cancellationToken,
                    switchOptions);
                var reviewJson = reviewResponse.Json;
                var reviewed = ParseCandidateResponse(
                    reviewJson,
                    job.FileName,
                    job.Sha256,
                    job.PageNumber,
                    reviewResponse.Model);
                var candidate = MergeReview(first, reviewed);
                if (firstResponse.UsedFallback || reviewResponse.UsedFallback)
                {
                    candidate.Warnings.Add(
                        $"主模型未完成，实际使用{string.Join("/", new[] { firstResponse.Model, reviewResponse.Model }.Distinct())}完成识别或复核。");
                }
                MonitoringDrawingCandidateRules.ValidateAndInitialize(candidate);
                candidates.Add(candidate);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add($"{job.FileName} 第{job.PageNumber}页：{exception.Message}");
            }
        }

        return new MonitoringDrawingVisionBatchResult
        {
            Candidates = candidates,
            Failures = failures
        };
    }

    public static MonitoringDrawingCandidate ParseCandidateResponse(
        string json,
        string sourceFileName,
        string sourceFileSha256,
        int pageNumber,
        string visionModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        DrawingResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<DrawingResponse>(StripCodeFence(json), JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("视觉模型返回的监控杆图纸JSON无法解析。", exception);
        }

        if (response is null)
        {
            throw new InvalidOperationException("视觉模型未返回监控杆图纸候选。");
        }

        var fields = new List<MonitoringDrawingFieldCandidate>();
        foreach (var definition in FieldDefinitions)
        {
            response.Fields.TryGetValue(definition.Name, out var source);
            fields.Add(BuildField(definition, source, pageNumber));
        }
        ReconcileSimpleMemberSpecifications(fields);

        var segments = response.ArmSegments
            .Select(segment => new MonitoringPoleArmSegment
            {
                LengthM = ConvertLengthToMetres(segment.Length?.Value, segment.Length?.Unit) ?? 0,
                NearDimensionM = ConvertLengthToMetres(segment.NearDimension?.Value, segment.NearDimension?.Unit) ?? 0,
                FarDimensionM = ConvertLengthToMetres(segment.FarDimension?.Value, segment.FarDimension?.Unit) ?? 0,
                WallThicknessM = ConvertLengthToMetres(segment.WallThickness?.Value, segment.WallThickness?.Unit) ?? 0
            })
            .Where(segment => segment.LengthM > 0)
            .ToList();
        if (segments.Count > 0)
        {
            var segmentConfidence = response.ArmSegments
                .Select(segment => segment.Confidence)
                .DefaultIfEmpty(0)
                .Min();
            fields.Add(new MonitoringDrawingFieldCandidate
            {
                FieldName = MonitoringDrawingFieldNames.ArmSegments,
                DisplayName = "横杆分段明细",
                Value = segments.Count,
                Unit = "段",
                RawAnnotation = string.Join("；", response.ArmSegments.Select(segment => segment.RawAnnotation)),
                Region = string.Join("；", response.ArmSegments.Select(segment => segment.Region).Distinct()),
                PageNumber = pageNumber,
                Confidence = segmentConfidence,
                HasConflict = response.ArmSegments.Any(segment => segment.Conflict),
                Warning = string.Join("；", response.ArmSegments
                    .Select(segment => segment.Warning)
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
            });
        }

        return new MonitoringDrawingCandidate
        {
            SourceFileName = sourceFileName,
            SourceFileSha256 = sourceFileSha256,
            PageNumber = pageNumber,
            DrawingModel = response.DrawingModel?.Trim() ?? string.Empty,
            VisionModel = visionModel,
            Fields = fields,
            ArmSegments = segments,
            Warnings = response.Warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            EvidenceSummary = string.Join(Environment.NewLine, fields
                .Where(field => !string.IsNullOrWhiteSpace(field.RawAnnotation))
                .Select(field => $"{field.DisplayName}：{field.RawAnnotation}（{field.Region}，第{pageNumber}页）"))
        };
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<VisionSendResult> SendWithFallbackAsync(
        IReadOnlyList<object> content,
        string systemPrompt,
        string selectedModel,
        IProgress<AiOperationProgress>? progress,
        int currentStep,
        int totalSteps,
        PageJob job,
        CancellationToken cancellationToken,
        VisionModelSwitchOptions? switchOptions)
    {
        var models = new[] { selectedModel, FastFallbackModel, AlternateFallbackModel }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var failures = new List<string>();
        for (var index = 0; index < models.Length; index++)
        {
            var model = models[index];
            try
            {
                return new VisionSendResult(
                    await SendAsync(content, systemPrompt, model, cancellationToken),
                    model,
                    index > 0);
            }
            catch (DrawingVisionRequestException exception) when (
                exception.IsTransient && index < models.Length - 1)
            {
                failures.Add($"{model}：{exception.Message}");
                var nextModel = models[index + 1];
                progress?.Report(new AiOperationProgress(
                    currentStep,
                    totalSteps,
                    $"{job.FileName} 第{job.PageNumber}页 {model} 未完成；等待确认是否切换到 {nextModel}"));
                var approved = switchOptions?.ConfirmAsync is not null &&
                               await switchOptions.ConfirmAsync(
                                   new VisionModelSwitchRequest(
                                       model,
                                       nextModel,
                                       $"监控杆图纸 {job.FileName} 第{job.PageNumber}页",
                                       exception.Message),
                                   cancellationToken);
                if (!approved)
                {
                    throw new DrawingVisionRequestException(
                        $"{model}未完成；未获得用户确认，未切换到{nextModel}。",
                        exception.IsTransient,
                        exception);
                }
                progress?.Report(new AiOperationProgress(
                    currentStep,
                    totalSteps,
                    $"用户已确认：{job.FileName} 第{job.PageNumber}页由 {model} 切换到 {nextModel}（第{index + 2}/{models.Length}次）"));
            }
            catch (DrawingVisionRequestException exception)
            {
                failures.Add($"{model}：{exception.Message}");
                throw new DrawingVisionRequestException(
                    $"视觉模型轮换未完成：{string.Join("；", failures)}",
                    exception.IsTransient,
                    exception);
            }
        }

        throw new DrawingVisionRequestException("视觉模型轮换未取得结果。", true);
    }

    private async Task<string> SendAsync(
        IReadOnlyList<object> userContent,
        string systemPrompt,
        string model,
        CancellationToken cancellationToken)
    {
        var settings = _settingsService.Load();
        EnsureAvailable(settings);
        var body = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            },
            response_format = new { type = "json_object" },
            enable_thinking = false,
            temperature = 0,
            max_tokens = 3600,
            stream = false
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildChatCompletionUri(settings.VisionBaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _settingsService.GetVisionApiKey()!);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.RequestTimeoutSeconds, 20, 60)));
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DrawingVisionRequestException("视觉模型响应超时。", true, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new DrawingVisionRequestException("网络未能连接视觉模型。", true, exception);
        }

        using (response)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new DrawingVisionRequestException(
                    BuildFailureMessage(response.StatusCode),
                    response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or
                        HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout ||
                    (int)response.StatusCode == 429);
            }

            ChatResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ChatResponse>(responseText, JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new DrawingVisionRequestException("视觉模型响应外层JSON无法解析。", true, exception);
            }

            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content) || !IsJsonObject(StripCodeFence(content)))
            {
                throw new DrawingVisionRequestException("视觉模型返回了空内容或不完整JSON。", true);
            }
            return StripCodeFence(content);
        }
    }

    private static MonitoringDrawingCandidate MergeReview(
        MonitoringDrawingCandidate first,
        MonitoringDrawingCandidate reviewed)
    {
        for (var index = 0; index < first.Fields.Count; index++)
        {
            var field = first.Fields[index];
            var reviewField = reviewed.Fields.FirstOrDefault(item => item.FieldName == field.FieldName);
            if (reviewField is null || !reviewField.Value.HasValue)
            {
                if (field.Value.HasValue)
                {
                    field.Confidence = Math.Min(field.Confidence, 0.79);
                    field.Warning = AppendWarning(field.Warning, "第二遍未确认该值，需人工确认");
                }
                continue;
            }

            if (field.Value.HasValue && Math.Abs(field.Value.Value - reviewField.Value.Value) > 1e-6)
            {
                reviewField.HasConflict = true;
                reviewField.Warning = AppendWarning(reviewField.Warning, "两遍识别结果不一致，需人工确认");
            }
            if (SimpleMemberSpecificationRegex.IsMatch(field.RawAnnotation) &&
                !SimpleMemberSpecificationRegex.IsMatch(reviewField.RawAnnotation))
            {
                reviewField.RawAnnotation = field.RawAnnotation;
                reviewField.Region = string.IsNullOrWhiteSpace(reviewField.Region)
                    ? field.Region
                    : $"{field.Region}；{reviewField.Region}";
                reviewField.Warning = AppendWarning(
                    reviewField.Warning,
                    "第二遍未保留完整杆件规格，已保留首轮原始规格证据并按固定语义复核");
            }
            first.Fields[index] = reviewField;
        }

        ReconcileSimpleMemberSpecifications(first.Fields);

        if (reviewed.ArmSegments.Count > 0)
        {
            first.ArmSegments = reviewed.ArmSegments;
        }
        if (!string.IsNullOrWhiteSpace(reviewed.DrawingModel))
        {
            first.DrawingModel = reviewed.DrawingModel;
        }
        first.VisionModel = reviewed.VisionModel;
        first.Warnings.AddRange(reviewed.Warnings);
        first.Warnings = first.Warnings.Distinct(StringComparer.Ordinal).ToList();
        first.EvidenceSummary = reviewed.EvidenceSummary;
        return first;
    }

    private static MonitoringDrawingFieldCandidate BuildField(
        FieldDefinition definition,
        VisionField? source,
        int pageNumber)
    {
        var value = source?.Value;
        var normalized = value.HasValue
            ? ConvertToSi(definition.Name, value.Value, source?.Unit)
            : null;
        return new MonitoringDrawingFieldCandidate
        {
            FieldName = definition.Name,
            DisplayName = definition.DisplayName,
            Value = normalized,
            Unit = definition.NormalizedUnit,
            RawAnnotation = source?.RawAnnotation?.Trim() ?? string.Empty,
            Region = source?.Region?.Trim() ?? string.Empty,
            PageNumber = pageNumber,
            Confidence = source?.Confidence ?? 0,
            HasConflict = source?.Conflict ?? false,
            Warning = source?.Warning?.Trim() ?? string.Empty
        };
    }

    private static void ReconcileSimpleMemberSpecifications(
        IList<MonitoringDrawingFieldCandidate> fields)
    {
        ReconcileSimpleMemberSpecification(
            fields,
            [
                MonitoringDrawingFieldNames.PoleHeight,
                MonitoringDrawingFieldNames.PoleBottomDimension,
                MonitoringDrawingFieldNames.PoleTopDimension,
                MonitoringDrawingFieldNames.PoleWallThickness
            ],
            MonitoringDrawingFieldNames.PoleTopDimension,
            MonitoringDrawingFieldNames.PoleBottomDimension,
            MonitoringDrawingFieldNames.PoleWallThickness,
            MonitoringDrawingFieldNames.PoleHeight,
            "立杆");
        ReconcileSimpleMemberSpecification(
            fields,
            [
                MonitoringDrawingFieldNames.ArmLength,
                MonitoringDrawingFieldNames.ArmNearDimension,
                MonitoringDrawingFieldNames.ArmFarDimension,
                MonitoringDrawingFieldNames.ArmWallThickness
            ],
            MonitoringDrawingFieldNames.ArmFarDimension,
            MonitoringDrawingFieldNames.ArmNearDimension,
            MonitoringDrawingFieldNames.ArmWallThickness,
            MonitoringDrawingFieldNames.ArmLength,
            "横杆");
    }

    private static void ReconcileSimpleMemberSpecification(
        IList<MonitoringDrawingFieldCandidate> fields,
        IReadOnlyCollection<string> evidenceFieldNames,
        string firstDimensionFieldName,
        string secondDimensionFieldName,
        string wallThicknessFieldName,
        string lengthFieldName,
        string memberName)
    {
        var evidenceField = fields
            .Where(field => evidenceFieldNames.Contains(field.FieldName))
            .FirstOrDefault(field => SimpleMemberSpecificationRegex.IsMatch(field.RawAnnotation));
        if (evidenceField is null)
        {
            return;
        }

        var match = SimpleMemberSpecificationRegex.Match(evidenceField.RawAnnotation);
        if (!TryParseInvariant(match.Groups["a"].Value, out var firstDimensionMm) ||
            !TryParseInvariant(match.Groups["b"].Value, out var secondDimensionMm) ||
            !TryParseInvariant(match.Groups["t"].Value, out var wallThicknessMm) ||
            !TryParseInvariant(match.Groups["l"].Value, out var lengthMm))
        {
            return;
        }

        var evidenceConfidence = Math.Clamp(evidenceField.Confidence, 0, 0.98);
        ReconcileSpecificationValue(
            fields,
            firstDimensionFieldName,
            firstDimensionMm / 1000,
            evidenceField,
            evidenceConfidence,
            memberName);
        ReconcileSpecificationValue(
            fields,
            secondDimensionFieldName,
            secondDimensionMm / 1000,
            evidenceField,
            evidenceConfidence,
            memberName);
        ReconcileSpecificationValue(
            fields,
            wallThicknessFieldName,
            wallThicknessMm / 1000,
            evidenceField,
            evidenceConfidence,
            memberName);
        ReconcileSpecificationValue(
            fields,
            lengthFieldName,
            lengthMm / 1000,
            evidenceField,
            evidenceConfidence,
            memberName);
    }

    private static void ReconcileSpecificationValue(
        IList<MonitoringDrawingFieldCandidate> fields,
        string fieldName,
        double expectedValue,
        MonitoringDrawingFieldCandidate evidenceField,
        double evidenceConfidence,
        string memberName)
    {
        var field = fields.First(item => item.FieldName == fieldName);
        var differed = field.Value.HasValue && Math.Abs(field.Value.Value - expectedValue) > 1e-6;
        field.Value = expectedValue;
        field.Confidence = Math.Max(field.Confidence, evidenceConfidence);
        if (string.IsNullOrWhiteSpace(field.RawAnnotation))
        {
            field.RawAnnotation = evidenceField.RawAnnotation;
        }
        if (string.IsNullOrWhiteSpace(field.Region))
        {
            field.Region = evidenceField.Region;
        }
        if (differed)
        {
            field.Warning = AppendWarning(
                field.Warning,
                $"视觉结构化值与{memberName}原始规格标注不一致，已按原始标注的固定语义本地纠正");
        }
    }

    private static bool TryParseInvariant(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static double? ConvertToSi(string fieldName, double value, string? unit)
    {
        var normalizedUnit = (unit ?? string.Empty).Trim().ToLowerInvariant()
            .Replace("²", "2", StringComparison.Ordinal);
        if (fieldName == MonitoringDrawingFieldNames.ArmCount)
        {
            return value;
        }
        if (fieldName == MonitoringDrawingFieldNames.AttachmentProjectedArea)
        {
            return normalizedUnit switch
            {
                "cm2" or "平方厘米" => value / 10_000,
                "mm2" or "平方毫米" => value / 1_000_000,
                _ => value
            };
        }
        if (fieldName == MonitoringDrawingFieldNames.AttachmentWeight)
        {
            return normalizedUnit switch
            {
                "kg" or "千克" => value * 9.80665 / 1000,
                "n" or "牛" => value / 1000,
                _ => value
            };
        }
        return ConvertLengthToMetres(value, unit);
    }

    private static double? ConvertLengthToMetres(double? value, string? unit)
    {
        if (!value.HasValue)
        {
            return null;
        }
        var normalizedUnit = (unit ?? string.Empty).Trim().ToLowerInvariant();
        return normalizedUnit switch
        {
            "mm" or "毫米" => value.Value / 1000,
            "cm" or "厘米" => value.Value / 100,
            _ => value.Value
        };
    }

    private static SKBitmap RenderPage(byte[] pdfBytes, int pageNumber) =>
        Conversion.ToImage(
            pdfBytes,
            new Index(pageNumber - 1),
            password: null,
            new RenderOptions
            {
                Dpi = RenderDpi,
                Grayscale = false,
                UseTiling = true
            });

    private static DrawingImageSet BuildImageSet(SKBitmap full)
    {
        return new DrawingImageSet(
            EncodePng(full),
            EncodeCrop(full, 0.03, 0.04, 0.76, 0.62),
            EncodeCrop(full, 0.02, 0.84, 0.96, 0.15),
            EncodeCrop(full, 0.02, 0.02, 0.96, 0.46));
    }

    private static SKBitmap Crop(
        SKBitmap source,
        double left,
        double top,
        double width,
        double height)
    {
        var rectangle = new SKRectI(
            (int)(source.Width * left),
            (int)(source.Height * top),
            Math.Min(source.Width, (int)(source.Width * (left + width))),
            Math.Min(source.Height, (int)(source.Height * (top + height))));
        var result = new SKBitmap(rectangle.Width, rectangle.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(source, rectangle, new SKRect(0, 0, result.Width, result.Height));
        canvas.Flush();
        return result;
    }

    private static string EncodePng(SKBitmap bitmap)
    {
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            return "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
        }
    }

    private static string EncodeCrop(
        SKBitmap source,
        double left,
        double top,
        double width,
        double height)
    {
        using var crop = Crop(source, left, top, width, height);
        return EncodePng(crop);
    }

    private static IReadOnlyList<object> BuildFirstPassContent(DrawingImageSet images, int page) =>
    [
        new { type = "text", text = BuildTaskPrompt(page, review: false, null) },
        new { type = "text", text = "完整图纸" },
        Image(images.FullPage),
        new { type = "text", text = "主视图及杆件规格标注高清裁切" },
        Image(images.MainView),
        new { type = "text", text = "标题栏裁切" },
        Image(images.TitleBlock),
        new { type = "text", text = "横杆规格、分段和δ壁厚标注局部裁切" },
        Image(images.SegmentDetail)
    ];

    private static IReadOnlyList<object> BuildReviewContent(
        DrawingImageSet images,
        int page,
        string firstJson) =>
    [
        new { type = "text", text = BuildTaskPrompt(page, review: true, firstJson) },
        new { type = "text", text = "主视图及规格标注复核图" },
        Image(images.MainView),
        new { type = "text", text = "标题栏复核图" },
        Image(images.TitleBlock),
        new { type = "text", text = "分段横杆和δ壁厚标注复核图" },
        Image(images.SegmentDetail)
    ];

    private static object Image(string url) => new
    {
        type = "image_url",
        image_url = new { url }
    };

    private static string BuildTaskPrompt(int page, bool review, string? firstJson) =>
        $$$"""
        固定来源为PDF第{{{page}}}页。{{{(review ? "这是第二遍复核，只复核关键尺寸、方向、单位、冲突和14m分段壁厚。" : "这是第一遍结构化提取。")}}}
        规格语义：立杆“八角对角(a-b)×t×L”中a是上端、b是下端；横杆中a是远端、b是近端。
        横杆总长只能抄“八角对角(...)×t×L”完整杆件规格的最后一项L；1000、200等局部尺寸链只用于孔位或分段定位，禁止累加后覆盖总长。
        若完整杆件规格总长、标题L和局部尺寸链不一致，以完整杆件规格为候选值并标记conflict；完整规格看不清时返回null，不得用局部尺寸链猜总长。
        “八角对角(a-b-c)×(t1+t2)×L”是分段变截面，必须结合δ壁厚标注逐段返回，不得平均壁厚。
        对14m分段横杆必须沿尺寸线箭头核对7000标注：若7000的两个界点分别落在杆端、变截面中点/接头或立杆中心线上，它就是明确的分段长度证据，不得误判成1000、200一类孔位尺寸。若一段明确为7000且完整规格总长为14000，另一段可按14000-7000=7000确定，并在raw_annotation写明原始7000标注和减法校核。
        分段顺序固定从立杆向远端；三点尺寸a-b-c按远端-中间-近端排列，所以近端段为c到b并采用t2，远端段为b到a并采用t1。例如(110-195-280)×(4+6)×14000应核对为近端280→195、厚6，远端195→110、厚4；这只是规格语义映射，长度仍须由尺寸线证据确认。
        法兰外径、螺栓圆、地脚螺栓M值、加劲板厚度都不是杆件尺寸。标题H/L只用于交叉校核。
        图纸没有设备迎风面积或设备重量时必须返回value:null。看不清也返回null，禁止用常识补值。
        每个字段返回value、unit、raw_annotation、region、confidence(0~1)、conflict、warning。
        字段名固定为：title_height,title_arm_length,pole_height,pole_bottom_dimension,pole_top_dimension,pole_wall_thickness,
        arm_mounting_height,arm_length,arm_near_dimension,arm_far_dimension,arm_wall_thickness,arm_count,
        attachment_projected_area,attachment_weight。
        arm_segments按从立杆向远端顺序；每段含length、near_dimension、far_dimension、wall_thickness及证据元数据。
        只输出一个完整JSON对象：
        {"drawing_model":"","fields":{"pole_height":{"value":null,"unit":"m","raw_annotation":"","region":"","confidence":0,"conflict":false,"warning":""}},
        "arm_segments":[{"length":{"value":null,"unit":"m"},"near_dimension":{"value":null,"unit":"mm"},"far_dimension":{"value":null,"unit":"mm"},"wall_thickness":{"value":null,"unit":"mm"},"raw_annotation":"","region":"","confidence":0,"conflict":false,"warning":""}],"warnings":[]}
        {{{(review ? "首轮JSON（只能作为待核对象，必须重新看图）：\n" + firstJson : string.Empty)}}}
        """;

    private void EnsureAvailable(ApplicationSettings settings)
    {
        if (settings.AiMode == AiOperatingMode.OfflineOnly)
        {
            throw new InvalidOperationException("当前为纯离线模式；可继续手工录入并完成本地计算。");
        }
        if (!VisualAiModelCatalog.IsSupported(settings.VisionModel))
        {
            throw new InvalidOperationException("当前设置的模型不在可识图模型白名单内。");
        }
        if (string.IsNullOrWhiteSpace(settings.VisionBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.VisionModel) ||
            !settings.HasVisionApiKey ||
            string.IsNullOrWhiteSpace(_settingsService.GetVisionApiKey()))
        {
            throw new InvalidOperationException("尚未配置可用的视觉模型与加密密钥。");
        }
    }

    private static Uri BuildChatCompletionUri(string baseUrl) =>
        new(baseUrl.TrimEnd('/') + "/chat/completions", UriKind.Absolute);

    private static string BuildFailureMessage(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "视觉API密钥无效或无权限。",
        HttpStatusCode.Forbidden => "当前视觉模型无访问权限。",
        HttpStatusCode.PaymentRequired => "视觉模型额度不足。",
        (HttpStatusCode)429 => "视觉模型请求过于频繁。",
        _ => $"视觉模型请求失败（HTTP {(int)statusCode}）。"
    };

    private static string StripCodeFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }
        var firstLineEnd = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd >= 0 && lastFence > firstLineEnd
            ? trimmed[(firstLineEnd + 1)..lastFence].Trim()
            : trimmed;
    }

    private static bool IsJsonObject(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string AppendWarning(string current, string addition) =>
        string.IsNullOrWhiteSpace(current) ? addition : current + "；" + addition;

    private static readonly Regex SimpleMemberSpecificationRegex = new(
        @"八角(?:对角)?\s*[\(（]\s*(?<a>\d+(?:\.\d+)?)\s*[-－—]\s*(?<b>\d+(?:\.\d+)?)\s*[\)）]\s*[×xX*]\s*(?<t>\d+(?:\.\d+)?)\s*[×xX*]\s*(?<l>\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string FirstPassSystemPrompt = """
        你是监控杆施工图视觉抄录器。只抄图中明确标注，不做结构计算，不补常识。
        必须区分正八边形对角尺寸、法兰尺寸、螺栓尺寸和板厚。横杆总长只取完整杆件规格最后一项L，局部尺寸链不得冒充总长。单位、原始标注、区域、页码和置信度必须可审计。
        看不清、未给出或互相冲突的字段返回null或conflict=true。只返回JSON。
        """;

    private const string ReviewSystemPrompt = """
        你是监控杆施工图第二遍独立复核器。重新观察裁切图，重点复核端部方向、mm/m、壁厚、14m两段和标题H/L。
        不得沿用首轮猜测；横杆总长只取完整杆件规格最后一项L，局部尺寸链不得累加冒充总长；看不清完整规格时返回null。法兰、螺栓圆、M值和加劲板厚度不得写入杆件字段。设备面积和重量未标注必须为null。
        对14m图，必须沿7000尺寸线的界线/箭头核实它是否连接杆端与变截面中点或接头；若是，它是分段长度而不是孔位尺寸。一个明确7000加总长14000可用14000-7000复核另一段。按从立杆向远端顺序返回：近端280→195采用6mm，远端195→110采用4mm；不得把两段壁厚合并或平均。
        仍按相同完整JSON结构返回；两遍不一致时conflict=true并写warning。只返回JSON。
        """;

    private static readonly FieldDefinition[] FieldDefinitions =
    [
        new(MonitoringDrawingFieldNames.TitleHeight, "标题H", "m"),
        new(MonitoringDrawingFieldNames.TitleArmLength, "标题L", "m"),
        new(MonitoringDrawingFieldNames.PoleHeight, "立杆高度", "m"),
        new(MonitoringDrawingFieldNames.PoleBottomDimension, "立杆下端对角尺寸", "m"),
        new(MonitoringDrawingFieldNames.PoleTopDimension, "立杆上端对角尺寸", "m"),
        new(MonitoringDrawingFieldNames.PoleWallThickness, "立杆壁厚", "m"),
        new(MonitoringDrawingFieldNames.ArmMountingHeight, "横杆安装高度", "m"),
        new(MonitoringDrawingFieldNames.ArmLength, "横杆长度", "m"),
        new(MonitoringDrawingFieldNames.ArmNearDimension, "横杆近端对角尺寸", "m"),
        new(MonitoringDrawingFieldNames.ArmFarDimension, "横杆远端对角尺寸", "m"),
        new(MonitoringDrawingFieldNames.ArmWallThickness, "横杆壁厚", "m"),
        new(MonitoringDrawingFieldNames.ArmCount, "横杆数量", "个"),
        new(MonitoringDrawingFieldNames.AttachmentProjectedArea, "设备迎风面积", "m²"),
        new(MonitoringDrawingFieldNames.AttachmentWeight, "设备重量", "kN")
    ];

    private sealed record FieldDefinition(string Name, string DisplayName, string NormalizedUnit);

    private sealed record PageJob(
        string FileName,
        string Sha256,
        byte[] Bytes,
        int PageNumber,
        int PageCount);

    private sealed record DrawingImageSet(
        string FullPage,
        string MainView,
        string TitleBlock,
        string SegmentDetail);

    private sealed record VisionSendResult(string Json, string Model, bool UsedFallback);

    private sealed class DrawingResponse
    {
        [JsonPropertyName("drawing_model")]
        public string? DrawingModel { get; init; }

        [JsonPropertyName("fields")]
        public Dictionary<string, VisionField> Fields { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("arm_segments")]
        public List<VisionSegment> ArmSegments { get; init; } = [];

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; init; } = [];
    }

    private sealed class VisionField
    {
        [JsonPropertyName("value")]
        public double? Value { get; init; }

        [JsonPropertyName("unit")]
        public string? Unit { get; init; }

        [JsonPropertyName("raw_annotation")]
        public string? RawAnnotation { get; init; }

        [JsonPropertyName("region")]
        public string? Region { get; init; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; init; }

        [JsonPropertyName("conflict")]
        public bool Conflict { get; init; }

        [JsonPropertyName("warning")]
        public string? Warning { get; init; }
    }

    private sealed class VisionSegment
    {
        [JsonPropertyName("length")]
        public VisionField? Length { get; init; }

        [JsonPropertyName("near_dimension")]
        public VisionField? NearDimension { get; init; }

        [JsonPropertyName("far_dimension")]
        public VisionField? FarDimension { get; init; }

        [JsonPropertyName("wall_thickness")]
        public VisionField? WallThickness { get; init; }

        [JsonPropertyName("raw_annotation")]
        public string RawAnnotation { get; init; } = string.Empty;

        [JsonPropertyName("region")]
        public string Region { get; init; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; init; }

        [JsonPropertyName("conflict")]
        public bool Conflict { get; init; }

        [JsonPropertyName("warning")]
        public string Warning { get; init; } = string.Empty;
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; init; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; init; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed class DrawingVisionRequestException : Exception
    {
        public DrawingVisionRequestException(
            string message,
            bool isTransient,
            Exception? innerException = null)
            : base(message, innerException)
        {
            IsTransient = isTransient;
        }

        public bool IsTransient { get; }
    }
}
