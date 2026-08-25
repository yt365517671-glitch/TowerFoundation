using System.IO;
using System.Text;
using System.Text.Json;
using TowerFoundation.Application;
using TowerFoundation.Domain;
using TowerFoundation.Infrastructure;
using TowerFoundation.Licensing;

namespace TowerFoundation.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 &&
            args[0].Equals("--ocr-self-test", StringComparison.OrdinalIgnoreCase))
        {
            Environment.Exit(App.RunPublishedOcrSelfTest(args[1]));
        }

        if (args.Length == 2 &&
            args[0].Equals("--release-audit", StringComparison.OrdinalIgnoreCase))
        {
            var settings = CreateSettingsService().Load();
            WriteJson(args[1], new
            {
                profile = AppBuildProfile.Name,
                requiresLicense = AppBuildProfile.RequiresLicense,
                settingsDirectory = AppDataPaths.ResolveSettingsDirectory(),
                licenseDirectory = AppDataPaths.ResolveLicenseDirectory(),
                hasDeepSeekApiKey = settings.HasApiKey,
                hasVisionApiKey = settings.HasVisionApiKey,
                machineCode = MachineCodeProvider.GetCurrent()
            });
            Environment.Exit(0);
        }

        if (AppBuildProfile.RequiresLicense &&
            args.Length > 0 &&
            args[0].StartsWith("--", StringComparison.Ordinal) &&
            !args[0].Equals("--data-directory", StringComparison.OrdinalIgnoreCase))
        {
            var assessment = new ClientLicenseManager(
                new ClientLicenseStore(AppDataPaths.ResolveLicenseDirectory()))
                .Assess();
            if (!assessment.IsUsable)
            {
                Environment.Exit(5);
            }
        }

        if (args.Length == 2 &&
            args[0].Equals("--formal-use-self-test", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(
                args[1],
                "PASS 正式功能授权门禁已放行。",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Environment.Exit(0);
        }

        if (args.Length == 2 &&
            args[0].Equals("--import-deepseek-from-451", StringComparison.OrdinalIgnoreCase))
        {
            var import = CreateSettingsService()
                .ImportFrom451BudgetAssistant();
            File.WriteAllText(
                args[1],
                import.Message,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Environment.Exit(import.Imported ? 0 : 2);
        }

        if (args.Length == 2 &&
            args[0].Equals("--test-deepseek-connection", StringComparison.OrdinalIgnoreCase))
        {
            var settingsService = CreateSettingsService();
            using var deepSeekService = new DeepSeekService(settingsService);
            var result = deepSeekService.TestConnectionAsync().GetAwaiter().GetResult();
            File.WriteAllText(
                args[1],
                result.Message,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Environment.Exit(result.Success ? 0 : 2);
        }

        if (args.Length == 3 &&
            args[0].Equals("--import-vision-api", StringComparison.OrdinalIgnoreCase))
        {
            var import = CreateSettingsService()
                .ImportVisualApiFromCsv(args[1]);
            File.WriteAllText(
                args[2],
                import.Message,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Environment.Exit(import.Imported ? 0 : 2);
        }

        if (args.Length == 2 &&
            args[0].Equals("--test-vision-connection", StringComparison.OrdinalIgnoreCase))
        {
            var settingsService = CreateSettingsService();
            using var visualService = new VisualGeotechnicalAiService(settingsService);
            var result = visualService.TestConnectionAsync().GetAwaiter().GetResult();
            File.WriteAllText(
                args[1],
                result.Message,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Environment.Exit(result.Success ? 0 : 2);
        }

        if (args.Length == 4 &&
            args[0].Equals("--analyze-geotechnical-pdf", StringComparison.OrdinalIgnoreCase))
        {
            Environment.Exit(RunGeotechnicalPdfDiagnostic(args[1], args[2], args[3]));
        }

        if (args.Length == 4 &&
            args[0].Equals("--analyze-geotechnical-pdf-vision", StringComparison.OrdinalIgnoreCase))
        {
            Environment.Exit(RunVisualGeotechnicalPdfDiagnostic(args[1], args[2], args[3]));
        }

        if (args.Length == 3 &&
            args[0].Equals("--benchmark-monitoring-drawing-vision", StringComparison.OrdinalIgnoreCase))
        {
            Environment.Exit(RunMonitoringDrawingVisionBenchmark(args[1], args[2]));
        }

        var application = new App();
        application.InitializeComponent();
        application.Run();
    }

    private static int RunGeotechnicalPdfDiagnostic(
        string pdfPath,
        string foundationTypeText,
        string resultPath)
    {
        try
        {
            if (!Enum.TryParse<FoundationType>(foundationTypeText, ignoreCase: true, out var foundationType))
            {
                throw new InvalidOperationException($"无法识别基础形式：{foundationTypeText}");
            }

            var settingsService = CreateSettingsService();
            using var deepSeekService = new DeepSeekService(settingsService);
            var importService = new GeotechnicalDocumentImportService(
                settingsService,
                deepSeekService,
                new DocxTextExtractor(),
                new LocalPdfOcrService());
            var import = importService.ImportPdfAsync(pdfPath, foundationType)
                .GetAwaiter()
                .GetResult();
            var output = new
            {
                import.Document.SourceName,
                import.Document.CharacterCount,
                Ocr = import.OcrResult,
                Ai = import.AiResult,
                import.AiSkipReason
            };
            File.WriteAllText(
                resultPath,
                JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return import.AiResult is null ? 2 : 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                resultPath,
                JsonSerializer.Serialize(
                    new { Error = exception.Message },
                    new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return 3;
        }
    }

    private static int RunVisualGeotechnicalPdfDiagnostic(
        string pdfPath,
        string foundationTypeText,
        string resultPath)
    {
        try
        {
            if (!Enum.TryParse<FoundationType>(foundationTypeText, ignoreCase: true, out var foundationType))
            {
                throw new InvalidOperationException($"无法识别基础形式：{foundationTypeText}");
            }

            var settingsService = CreateSettingsService();
            using var visualService = new VisualGeotechnicalAiService(settingsService);
            var analysis = visualService.AnalyzePdfAsync(
                    pdfPath,
                    GeotechnicalDocumentImportService.BuildFoundationSpecificRequirements(foundationType))
                .GetAwaiter()
                .GetResult();
            File.WriteAllText(
                resultPath,
                JsonSerializer.Serialize(analysis, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                resultPath,
                JsonSerializer.Serialize(
                    new { Error = exception.Message },
                    new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return 3;
        }
    }

    private static int RunMonitoringDrawingVisionBenchmark(
        string sourceDirectory,
        string resultPath)
    {
        var startedAt = DateTimeOffset.Now;
        try
        {
            var paths = Directory.GetFiles(sourceDirectory, "*.pdf")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (paths.Length != 7)
            {
                throw new InvalidOperationException(
                    $"监控杆视觉基准目录应包含7个PDF，当前找到{paths.Length}个。");
            }

            var settingsService = CreateSettingsService();
            var settings = settingsService.Load();
            using var service = new MonitoringDrawingVisionAiService(settingsService);
            var batch = service.AnalyzePdfsAsync(paths).GetAwaiter().GetResult();
            var report = MonitoringDrawingAccuracyBenchmark.Evaluate(
                paths,
                batch,
                settings.VisionModel,
                startedAt,
                DateTimeOffset.Now);
            WriteJson(resultPath, report);
            return report.Status == "failed" ? 2 : 0;
        }
        catch (Exception exception)
        {
            WriteJson(resultPath, new
            {
                status = "failed",
                startedAt,
                completedAt = DateTimeOffset.Now,
                error = exception.Message,
                accuracy = (double?)null,
                note = "未使用人工基准替代视觉模型结果。"
            });
            return 3;
        }
    }

    private static void WriteJson(string path, object value)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory);
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static LocalApplicationSettingsService CreateSettingsService() =>
        new(AppDataPaths.ResolveSettingsDirectory());
}
