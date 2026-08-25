using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PDFtoImage;
using SkiaSharp;
using TowerFoundation.Application;

namespace TowerFoundation.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class VisualGeotechnicalAiService : IVisualGeotechnicalAiService, IDisposable
{
    private const int MaximumVisionPages = 40;
    private const long MaximumPdfBytes = 120L * 1024 * 1024;
    private const int MinimumVisionTimeoutSeconds = 150;
    private const string FastFallbackModel = "qwen3.6-flash";
    private const string AlternateFallbackModel = "qwen3-vl-flash";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IApplicationSettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public VisualGeotechnicalAiService(
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
        var content = new List<object>
        {
            new { type = "text", text = "直接观察图片，只输出JSON：{\"value\":\"图片中的黑色数字\"}。" },
            new
            {
                type = "image_url",
                image_url = new { url = BuildVisionTestDataUrl() }
            }
        };
        var response = await SendWithRetryAsync(
            content,
            "你是视觉连接测试助手。必须读取图片，不得猜测。",
            jsonOutput: true,
            maxTokens: 80,
            modelOverride: null,
            maxAttempts: 2,
            cancellationToken);
        var success = response.Contains("37", StringComparison.Ordinal);
        return new AiConnectionResult
        {
            Success = success,
            Message = success
                ? $"视觉模型 {GetSettings().VisionModel} 连接正常，已成功读取测试图片。"
                : $"视觉模型返回了内容，但未读出测试图片中的数字37：{response.Trim()}"
        };
    }

    public async Task<VisualGeotechnicalAnalysisResult> AnalyzePdfAsync(
        string path,
        string foundationRequirement,
        IProgress<AiOperationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        VisionModelSwitchOptions? switchOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("找不到所选地勘 PDF。", path);
        }

        if (!file.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("视觉大模型直接分析当前只接收 PDF 文件。");
        }

        if (file.Length > MaximumPdfBytes)
        {
            throw new InvalidOperationException("PDF 超过120MB。请拆分报告后再使用视觉分析，手工录入仍可继续。");
        }

        var settings = GetSettings();
        EnsureAvailable(settings);
        var pdfBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var totalPages = Conversion.GetPageCount(pdfBytes);
        if (totalPages <= 0)
        {
            throw new InvalidOperationException("PDF 中没有可分析页面。");
        }

        var startPage = Math.Clamp(settings.OcrStartPage, 1, totalPages);
        var requestedEndPage = settings.OcrEndPage <= 0
            ? totalPages
            : Math.Clamp(settings.OcrEndPage, startPage, totalPages);
        var requestedCount = requestedEndPage - startPage + 1;
        var processedCount = Math.Min(requestedCount, MaximumVisionPages);
        var endPage = startPage + processedCount - 1;
        var pages = Enumerable.Range(startPage, processedCount).ToArray();
        var batchSize = settings.VisionPagesPerBatch;
        var batches = pages.Chunk(batchSize).ToArray();
        var totalSteps = pages.Length + 3;
        var warnings = new List<string>();
        if (requestedCount > MaximumVisionPages)
        {
            warnings.Add(
                $"所选范围共{requestedCount}页；本次视觉分析从第{startPage}页起处理{MaximumVisionPages}页。请在设置中调整页码范围继续分析其余页面。");
        }

        progress?.Report(new AiOperationProgress(
            1,
            totalSteps,
            $"正在准备PDF第{startPage}至{endPage}页的视觉分析"));

        var completedPages = 0;
        var evidence = new List<VisionBatchEvidence>();
        foreach (var batchPages in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AiOperationProgress(
                Math.Min(totalSteps - 2, completedPages + 2),
                totalSteps,
                $"{settings.VisionModel} 正在观察第{batchPages.First()}至{batchPages.Last()}页；失败会自动拆页重试"));
            var groupEvidence = await AnalyzePageGroupAsync(
                pdfBytes,
                batchPages,
                foundationRequirement,
                settings.VisionModel,
                warnings,
                cancellationToken,
                switchOptions);
            evidence.AddRange(groupEvidence);
            completedPages += batchPages.Length;
            progress?.Report(new AiOperationProgress(
                Math.Min(totalSteps - 2, completedPages + 1),
                totalSteps,
                $"已完成 {completedPages}/{pages.Length} 页视觉读取"));
        }

        var evidenceBuilder = new StringBuilder();
        foreach (var item in evidence.OrderBy(item => item.Pages[0]))
        {
            foreach (var page in item.Pages)
            {
                evidenceBuilder.AppendLine(
                    $"--- 第 {page} 页（视觉模型 {item.Model}）---");
            }
            evidenceBuilder.AppendLine(item.Json.Trim());
        }
        progress?.Report(new AiOperationProgress(
            totalSteps - 1,
            totalSteps,
            $"{settings.VisionModel} 正在汇总页码、表头和冲突"));
        var evidenceText = evidenceBuilder.ToString();
        var finalContent = new List<object>
        {
            new
            {
                type = "text",
                text = "基础形式所需字段：\n" + foundationRequirement +
                       "\n\n逐页视觉证据（唯一可用事实源）：\n" + evidenceText
            }
        };
        string finalJson;
        try
        {
            finalJson = await SendWithRetryAsync(
                finalContent,
                VisualAuditSystemPrompt,
                jsonOutput: true,
                maxTokens: 4200,
                modelOverride: null,
                maxAttempts: 2,
                cancellationToken);
        }
        catch (VisualRequestException exception) when (exception.IsTransient)
        {
            var fallbackModel = GetFallbackModel(settings.VisionModel);
            progress?.Report(new AiOperationProgress(
                totalSteps - 1,
                totalSteps,
                $"{settings.VisionModel} 跨页复核未完成；等待确认是否切换到 {fallbackModel}"));
            if (!await RequestModelSwitchApprovalAsync(
                    switchOptions,
                    settings.VisionModel,
                    fallbackModel,
                    "地勘PDF跨页复核",
                    exception.Message,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    $"{settings.VisionModel}跨页复核未完成；未获得用户确认，未切换到{fallbackModel}。",
                    exception);
            }
            warnings.Add($"跨页复核使用{settings.VisionModel}重试后仍失败；经用户确认改用{fallbackModel}完成汇总。");
            progress?.Report(new AiOperationProgress(
                totalSteps - 1,
                totalSteps,
                $"用户已确认，正在由 {fallbackModel} 完成跨页汇总"));
            try
            {
                finalJson = await SendWithRetryAsync(
                    finalContent,
                    VisualAuditSystemPrompt,
                    jsonOutput: true,
                    maxTokens: 4200,
                    modelOverride: fallbackModel,
                    maxAttempts: 2,
                    cancellationToken);
            }
            catch (VisualRequestException fallbackException)
            {
                throw new InvalidOperationException(
                    "视觉分析已重试，并按用户确认切换备用模型，但本次跨页汇总仍未完成。请稍后再次点击视觉分析；无需手工缩小页码范围。",
                    fallbackException);
            }
        }

        progress?.Report(new AiOperationProgress(
            totalSteps,
            totalSteps,
            "正在执行本机数值范围、页码和冲突校验"));
        var result = DeepSeekService.ParseGeotechnicalResponse(
            finalJson,
            evidenceText,
            $"视觉模型 {settings.VisionModel}");
        return new VisualGeotechnicalAnalysisResult
        {
            SourceName = file.Name,
            Model = settings.VisionModel,
            PageCount = totalPages,
            ProcessedPageCount = processedCount,
            EvidenceText = evidenceText,
            AiResult = result,
            Warnings = warnings
        };
    }

    private async Task<IReadOnlyList<VisionBatchEvidence>> AnalyzePageGroupAsync(
        byte[] pdfBytes,
        IReadOnlyList<int> pages,
        string foundationRequirement,
        string selectedModel,
        List<string> warnings,
        CancellationToken cancellationToken,
        VisionModelSwitchOptions? switchOptions)
    {
        var content = BuildPageContent(pdfBytes, pages, foundationRequirement);
        try
        {
            var json = await SendWithRetryAsync(
                content,
                VisualEvidenceSystemPrompt,
                jsonOutput: true,
                maxTokens: pages.Count == 1 ? 2400 : 3200,
                modelOverride: selectedModel,
                maxAttempts: pages.Count == 1 ? 2 : 1,
                cancellationToken);
            return [new VisionBatchEvidence(pages.ToArray(), selectedModel, json)];
        }
        catch (VisualRequestException exception) when (
            exception.IsTransient && pages.Count > 1)
        {
            var splitAt = (pages.Count + 1) / 2;
            var first = pages.Take(splitAt).ToArray();
            var second = pages.Skip(splitAt).ToArray();
            warnings.Add(
                $"PDF第{pages.First()}至{pages.Last()}页批量请求未成功，软件已自动拆为更小批次继续，无需手工调整页码。");
            var result = new List<VisionBatchEvidence>();
            result.AddRange(await AnalyzePageGroupAsync(
                pdfBytes,
                first,
                foundationRequirement,
                selectedModel,
                warnings,
                cancellationToken,
                switchOptions));
            result.AddRange(await AnalyzePageGroupAsync(
                pdfBytes,
                second,
                foundationRequirement,
                selectedModel,
                warnings,
                cancellationToken,
                switchOptions));
            return result;
        }
        catch (VisualRequestException exception) when (
            exception.IsTransient && pages.Count == 1)
        {
            var fallbackModel = GetFallbackModel(selectedModel);
            if (!await RequestModelSwitchApprovalAsync(
                    switchOptions,
                    selectedModel,
                    fallbackModel,
                    $"地勘PDF第{pages[0]}页识别",
                    exception.Message,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    $"PDF第{pages[0]}页使用{selectedModel}重试后仍未成功；未获得用户确认，未切换到{fallbackModel}。",
                    exception);
            }
            warnings.Add(
                $"PDF第{pages[0]}页使用{selectedModel}重试后仍未成功；经用户确认改用备用快速模型{fallbackModel}完成该页识别。");
            try
            {
                var json = await SendWithRetryAsync(
                    content,
                    VisualEvidenceSystemPrompt,
                    jsonOutput: true,
                    maxTokens: 2400,
                    modelOverride: fallbackModel,
                    maxAttempts: 2,
                    cancellationToken);
                return [new VisionBatchEvidence(pages.ToArray(), fallbackModel, json)];
            }
            catch (VisualRequestException fallbackException)
            {
                throw new InvalidOperationException(
                    $"PDF第{pages[0]}页已重试，并按用户确认切换备用视觉模型，但服务仍未成功响应。请稍后再次点击视觉分析；本地OCR和手工录入仍可使用。",
                    fallbackException);
            }
        }
    }

    private static async Task<bool> RequestModelSwitchApprovalAsync(
        VisionModelSwitchOptions? switchOptions,
        string currentModel,
        string proposedModel,
        string operation,
        string failureReason,
        CancellationToken cancellationToken) =>
        switchOptions?.ConfirmAsync is not null &&
        await switchOptions.ConfirmAsync(
            new VisionModelSwitchRequest(
                currentModel,
                proposedModel,
                operation,
                failureReason),
            cancellationToken);

    private static List<object> BuildPageContent(
        byte[] pdfBytes,
        IReadOnlyList<int> pages,
        string foundationRequirement)
    {
        var content = new List<object>
        {
            new
            {
                type = "text",
                text = BuildBatchPrompt(foundationRequirement, pages)
            }
        };
        foreach (var page in pages)
        {
            content.Add(new
            {
                type = "text",
                text = $"下一张图固定为PDF第{page}页，pdf_page只能写{page}。"
            });
            content.Add(new
            {
                type = "image_url",
                image_url = new { url = RenderPageDataUrl(pdfBytes, page) }
            });
        }

        return content;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private ApplicationSettings GetSettings() => _settingsService.Load();

    private void EnsureAvailable(ApplicationSettings settings)
    {
        if (settings.AiMode == AiOperatingMode.OfflineOnly)
        {
            throw new InvalidOperationException("当前为纯离线模式。请手工录入，或在设置中启用AI在线优先。");
        }

        if (!VisualAiModelCatalog.IsSupported(settings.VisionModel))
        {
            throw new InvalidOperationException("当前选择的型号不支持地勘视觉理解，请在设置中选择Qwen视觉理解模型。");
        }

        if (string.IsNullOrWhiteSpace(_settingsService.GetVisionApiKey()))
        {
            throw new InvalidOperationException("尚未配置百炼视觉API密钥。请在设置中导入业务空间CSV或粘贴密钥。");
        }
    }

    private async Task<string> SendWithRetryAsync(
        IReadOnlyList<object> userContent,
        string systemPrompt,
        bool jsonOutput,
        int maxTokens,
        string? modelOverride,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        VisualRequestException? lastException = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await SendOnceAsync(
                    userContent,
                    systemPrompt,
                    jsonOutput,
                    maxTokens,
                    modelOverride,
                    cancellationToken);
            }
            catch (VisualRequestException exception) when (
                exception.IsTransient && attempt < maxAttempts)
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(900 * attempt), cancellationToken);
            }
        }

        throw lastException ?? new VisualRequestException(
            "视觉模型请求未完成。",
            isTransient: true);
    }

    private async Task<string> SendOnceAsync(
        IReadOnlyList<object> userContent,
        string systemPrompt,
        bool jsonOutput,
        int maxTokens,
        string? modelOverride,
        CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        EnsureAvailable(settings);
        var apiKey = _settingsService.GetVisionApiKey()!;
        var model = string.IsNullOrWhiteSpace(modelOverride)
            ? settings.VisionModel
            : modelOverride;
        var body = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            },
            response_format = jsonOutput ? new { type = "json_object" } : null,
            enable_thinking = false,
            temperature = 0,
            max_tokens = maxTokens,
            stream = false
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildChatCompletionUri(settings.VisionBaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            Math.Max(MinimumVisionTimeoutSeconds, settings.RequestTimeoutSeconds)));
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VisualRequestException(
                "视觉模型响应超时，软件将自动缩小批次；如需切换备用模型会先征求用户确认。",
                isTransient: true);
        }
        catch (HttpRequestException exception)
        {
            throw new VisualRequestException(
                "当前网络未能连接百炼视觉模型，软件将自动重试。",
                isTransient: true,
                exception);
        }

        using (response)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new VisualRequestException(
                    BuildFailureMessage(response.StatusCode, responseText),
                    IsTransientStatus(response.StatusCode));
            }

            ChatResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ChatResponse>(responseText, JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new VisualRequestException(
                    "百炼视觉模型返回了无法解析的响应，软件将自动重试。",
                    isTransient: true,
                    exception);
            }

            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new VisualRequestException(
                    "百炼视觉模型返回了空内容，软件将自动重试。",
                    isTransient: true);
            }
            if (jsonOutput && !IsJsonObject(content))
            {
                throw new VisualRequestException(
                    "视觉模型返回的JSON不完整，软件将自动重试并缩小批次。",
                    isTransient: true);
            }

            return StripCodeFence(content);
        }
    }

    private static string BuildBatchPrompt(
        string foundationRequirement,
        IReadOnlyList<int> pages) =>
        $$"""
        任务：逐张读取PDF第{{string.Join("、", pages)}}页，只摘录图片中明确出现的岩土事实。
        当前基础需要：{{foundationRequirement}}
        必查：封面或前言中的项目名称、建设地点（两者分开）；地下水；地震烈度、加速度、分组、场地类别、特征周期；土层深度/厚度、fak、fa、γ、c、φ、Es、ηb、ηd、摩擦系数、m；按成桩方法分列的qsik、qpk、抗拔系数；持力层、特殊土与基础建议。
        表格按“总表头→桩型子表头→土层行”读取，禁止横向串列。`/`、`-`、空白、看不清均写null。保留单位和短证据，不解释、不推断、不复述无关正文。
        只输出紧凑JSON：
        {
          "pages":[{
            "pdf_page":null,"section":"","project_name":[],"site_location":[],"groundwater":[],"seismic":[],
            "soil_layers":[{"layer":"","depth":"","fak":null,"fa":null,"gamma":null,"c":null,"phi":null,"Es":null,"eta_b":null,"eta_d":null,"m":null,"evidence":""}],
            "pile_values":[{"pile_method":"","layer":"","qsik":null,"qpk":null,"uplift":null,"evidence":""}],
            "recommendations":[],"special_risks":[],"uncertain":[]
          }],
          "cross_page_conflicts":[]
        }
        """;

    private static string RenderPageDataUrl(byte[] pdfBytes, int pageNumber)
    {
        using var bitmap = Conversion.ToImage(
            pdfBytes,
            new Index(pageNumber - 1),
            password: null,
            new RenderOptions
            {
                Dpi = 150,
                Grayscale = false,
                UseTiling = true
            });
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        return "data:image/jpeg;base64," + Convert.ToBase64String(encoded.ToArray());
    }

    private static string BuildVisionTestDataUrl()
    {
        using var bitmap = new SKBitmap(240, 120);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 78);
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        canvas.DrawText("37", 70, 88, SKTextAlign.Left, font, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return "data:image/png;base64," + Convert.ToBase64String(encoded.ToArray());
    }

    private static Uri BuildChatCompletionUri(string baseUrl)
    {
        var normalized = baseUrl.Trim().TrimEnd('/');
        if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(normalized, UriKind.Absolute);
        }

        return new Uri(normalized + "/chat/completions", UriKind.Absolute);
    }

    private static string GetFallbackModel(string selectedModel) =>
        selectedModel.Equals(FastFallbackModel, StringComparison.OrdinalIgnoreCase)
            ? AlternateFallbackModel
            : FastFallbackModel;

    private static bool IsTransientStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or
            HttpStatusCode.BadRequest or
            (HttpStatusCode)429 or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private static bool IsJsonObject(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(StripCodeFence(content));
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
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

    private static string BuildFailureMessage(HttpStatusCode statusCode, string responseText)
    {
        var detail = responseText.Length > 500 ? responseText[..500] : responseText;
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $"：{detail}";
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "百炼视觉API密钥无效，请在设置中重新导入或填写。",
            HttpStatusCode.PaymentRequired => "百炼视觉模型额度不足，当前仍可使用OCR或手工录入。",
            (HttpStatusCode)429 => "百炼视觉模型请求过于频繁，请稍后重试。",
            _ => $"百炼视觉模型请求失败（HTTP {(int)statusCode}）{suffix}"
        };
    }

    private const string VisualEvidenceSystemPrompt = """
    你是岩土地勘页面抄录器。直接看图，逐页抄录，不做设计、不补常识。
    四条禁令：不得把fak写成fa；不得把m写成qsik；不得合并不同桩型列；不得选择冲突值。
    数字必须附PDF页码、单位和短证据；看不清写null。仅返回一个完整、紧凑的JSON对象。
    """;

    private const string VisualAuditSystemPrompt = DeepSeekService.EngineeringAuditPrompt + """

    输入仅为逐页视觉证据。只使用证据中明确出现的数字；evidence_pages只写固定PDF页码。
    同一字段多值冲突时最终值必须为null并保留全部候选；推荐桩型与参数表桩型不一致时pile_parameters_safe_to_apply=false。
    不复述过程，不输出Markdown，只返回完整JSON对象。
    """;

    private sealed record VisionBatchEvidence(
        IReadOnlyList<int> Pages,
        string Model,
        string Json);

    private sealed class VisualRequestException : Exception
    {
        public VisualRequestException(
            string message,
            bool isTransient,
            Exception? innerException = null)
            : base(message, innerException)
        {
            IsTransient = isTransient;
        }

        public bool IsTransient { get; }
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
}
