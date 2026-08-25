using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using PDFtoImage;
using SkiaSharp;
using TesseractOCR;
using TesseractOCR.Enums;
using TowerFoundation.Application;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using PixImage = TesseractOCR.Pix.Image;

namespace TowerFoundation.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class LocalPdfOcrService : ILocalPdfOcrService
{
    private const int MaximumPages = 80;
    private const long MaximumFileBytes = 120L * 1024 * 1024;
    private readonly string? _tessDataDirectory;
    private readonly bool _forceOcr;

    public LocalPdfOcrService(
        string? tessDataDirectory = null,
        bool forceOcr = false)
    {
        _tessDataDirectory = tessDataDirectory;
        _forceOcr = forceOcr;
    }

    public async Task<OcrDocumentResult> ExtractAsync(
        string path,
        IProgress<OcrProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        await ExtractRangeAsync(
            path,
            1,
            0,
            progress,
            cancellationToken);

    public async Task<OcrDocumentResult> ExtractRangeAsync(
        string path,
        int startPage,
        int endPage,
        IProgress<OcrProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("找不到所选 PDF 文件。", path);
        }

        if (!file.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("本地 OCR 当前只接收 PDF 文件。");
        }

        if (file.Length > MaximumFileBytes)
        {
            throw new InvalidOperationException("PDF 超过 120 MB。请拆分地勘报告后再识别，手工录入始终可用。");
        }

        var pdfBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        progress?.Report(new OcrProgress(0, 1, "正在检查 PDF 是否包含可直接读取的文字层"));
        var nativeText = _forceOcr
            ? null
            : TryExtractNativeText(
                pdfBytes,
                file.Name,
                startPage,
                endPage,
                progress,
                cancellationToken);
        if (nativeText is not null)
        {
            return nativeText;
        }

        var totalPages = Conversion.GetPageCount(pdfBytes);
        if (totalPages <= 0)
        {
            throw new InvalidOperationException("PDF 中没有可识别页面。");
        }

        var normalizedStartPage = Math.Clamp(startPage, 1, totalPages);
        var normalizedEndPage = endPage <= 0
            ? totalPages
            : Math.Clamp(endPage, normalizedStartPage, totalPages);
        var requestedPageCount = normalizedEndPage - normalizedStartPage + 1;
        var pagesToProcess = Math.Min(requestedPageCount, MaximumPages);
        var warnings = new List<string>();
        if (requestedPageCount > MaximumPages)
        {
            warnings.Add($"所选范围共{requestedPageCount}页；为控制单机内存，本次从第{normalizedStartPage}页起识别{MaximumPages}页。请调整页码范围继续识别其余页面。");
        }

        var tessdataPath = EnsureTessData();
        var builder = new StringBuilder();
        var confidenceSum = 0d;
        var processed = 0;

        using var engine = new Engine(
            tessdataPath,
            new List<Language>
            {
                Language.ChineseSimplified,
                Language.English
            },
            EngineMode.Default);

        for (var offset = 0; offset < pagesToProcess; offset++)
        {
            var pageIndex = normalizedStartPage - 1 + offset;
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new OcrProgress(
                offset + 1,
                pagesToProcess,
                $"正在本地识别PDF第 {pageIndex + 1} 页（范围进度 {offset + 1} / {pagesToProcess}）"));

            using var bitmap = Conversion.ToImage(
                pdfBytes,
                new Index(pageIndex),
                password: null,
                new RenderOptions
                {
                    Dpi = 220,
                    Grayscale = true,
                    UseTiling = true
                });
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var image = PixImage.LoadFromMemory(encoded.ToArray());
            using var page = engine.Process(image);

            builder.AppendLine($"--- 第 {pageIndex + 1} 页 ---");
            builder.AppendLine(page.Text.Trim());
            builder.AppendLine();
            confidenceSum += page.MeanConfidence;
            processed++;
        }

        progress?.Report(new OcrProgress(
            processed,
            pagesToProcess,
            "本地 OCR 已完成"));

        return new OcrDocumentResult
        {
            SourceName = file.Name,
            Content = builder.ToString(),
            PageCount = totalPages,
            ProcessedPageCount = processed,
            MeanConfidence = processed == 0 ? 0 : confidenceSum / processed,
            Warnings = warnings
        };
    }

    private static OcrDocumentResult? TryExtractNativeText(
        byte[] pdfBytes,
        string sourceName,
        int startPage,
        int endPage,
        IProgress<OcrProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = PdfDocument.Open(pdfBytes);
            var totalPages = document.NumberOfPages;
            if (totalPages <= 0)
            {
                return null;
            }

            var normalizedStartPage = Math.Clamp(startPage, 1, totalPages);
            var normalizedEndPage = endPage <= 0
                ? totalPages
                : Math.Clamp(endPage, normalizedStartPage, totalPages);
            var requestedPageCount = normalizedEndPage - normalizedStartPage + 1;
            var pagesToProcess = Math.Min(requestedPageCount, MaximumPages);
            var builder = new StringBuilder();
            var meaningfulPages = 0;
            for (var offset = 0; offset < pagesToProcess; offset++)
            {
                var pageNumber = normalizedStartPage + offset;
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OcrProgress(
                    offset + 1,
                    pagesToProcess,
                    $"正在读取PDF原生文字层第 {pageNumber} 页（范围进度 {offset + 1} / {pagesToProcess}）"));
                var page = document.GetPage(pageNumber);
                var text = ContentOrderTextExtractor.GetText(page).Trim();
                if (CountMeaningfulCharacters(text) >= 30)
                {
                    meaningfulPages++;
                }

                builder.AppendLine($"--- 第 {pageNumber} 页（PDF原生文字层）---");
                builder.AppendLine(text);
                builder.AppendLine();
            }

            var content = builder.ToString();
            var minimumCharacters = Math.Max(30, pagesToProcess * 50);
            var minimumMeaningfulPages = Math.Max(1, (int)Math.Ceiling(pagesToProcess * 0.35));
            if (CountMeaningfulCharacters(content) < minimumCharacters ||
                meaningfulPages < minimumMeaningfulPages)
            {
                return null;
            }

            var warnings = new List<string>
            {
                "检测到可用的 PDF 原生文字层，已优先读取；未对文字页重复 OCR，表格数字准确性更高。"
            };
            if (requestedPageCount > MaximumPages)
            {
                warnings.Add($"所选范围共{requestedPageCount}页；本次从第{normalizedStartPage}页起读取{MaximumPages}页。请调整页码范围继续读取其余页面。");
            }

            progress?.Report(new OcrProgress(
                pagesToProcess,
                pagesToProcess,
                "PDF 原生文字层读取完成"));
            return new OcrDocumentResult
            {
                SourceName = sourceName,
                Content = content,
                PageCount = totalPages,
                ProcessedPageCount = pagesToProcess,
                MeanConfidence = 1,
                ExtractionMode = PdfTextExtractionMode.NativeTextLayer,
                Warnings = warnings
            };
        }
        catch
        {
            // Damaged, encrypted, image-only, or unsupported PDFs continue through local OCR.
            return null;
        }
    }

    private static int CountMeaningfulCharacters(string text)
    {
        return text.Count(character =>
            char.IsLetterOrDigit(character) ||
            character is >= '\u4e00' and <= '\u9fff');
    }

    private string EnsureTessData()
    {
        var targetDirectory = _tessDataDirectory ??
                              Path.Combine(
                                  Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                  "TowerFoundation",
                                  "ocr",
                                  "tessdata");
        Directory.CreateDirectory(targetDirectory);

        var assembly = typeof(LocalPdfOcrService).Assembly;
        foreach (var languageFile in new[] { "chi_sim.traineddata", "eng.traineddata" })
        {
            var resourceName = assembly
                .GetManifestResourceNames()
                .SingleOrDefault(name =>
                    name.EndsWith(languageFile, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"OCR 语言资源缺失：{languageFile}");
            var targetPath = Path.Combine(targetDirectory, languageFile);
            using var source = assembly.GetManifestResourceStream(resourceName)
                               ?? throw new InvalidOperationException($"无法读取 OCR 语言资源：{languageFile}");
            if (!File.Exists(targetPath) || new FileInfo(targetPath).Length != source.Length)
            {
                using var target = File.Create(targetPath);
                source.CopyTo(target);
            }
        }

        return targetDirectory;
    }
}
