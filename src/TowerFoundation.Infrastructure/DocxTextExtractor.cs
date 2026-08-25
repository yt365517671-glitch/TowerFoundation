using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using TowerFoundation.Application;

namespace TowerFoundation.Infrastructure;

public sealed class DocxTextExtractor : IWordTextExtractor
{
    private static readonly XNamespace WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public async Task<DocumentTextExtractionResult> ExtractAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("没有找到所选地勘 Word 文件。", path);
        }

        if (!string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前仅支持 .docx；旧版 .doc 请先另存为 .docx。");
        }

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > 50 * 1024 * 1024)
        {
            throw new InvalidOperationException("Word 文件超过 50 MB，请先压缩图片或拆分后再识别。");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("word/document.xml") ??
                    throw new InvalidOperationException("该文件不是有效的 DOCX 文档。");
        await using var documentStream = entry.Open();
        var document = await XDocument.LoadAsync(
            documentStream,
            LoadOptions.None,
            cancellationToken);
        var body = document.Root?.Element(WordNamespace + "body") ??
                   throw new InvalidOperationException("Word 文档缺少正文内容。");

        var builder = new StringBuilder();
        foreach (var element in body.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (element.Name == WordNamespace + "p")
            {
                AppendParagraph(builder, element);
            }
            else if (element.Name == WordNamespace + "tbl")
            {
                AppendTable(builder, element);
            }
        }

        var content = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                "Word 中没有读取到正文或表格文字；如果内容全是扫描图片，请改用后续 PDF/OCR 功能或手工录入。");
        }

        return new DocumentTextExtractionResult
        {
            SourceName = Path.GetFileName(path),
            Content = content
        };
    }

    private static void AppendParagraph(StringBuilder builder, XElement paragraph)
    {
        var text = string.Concat(paragraph
            .Descendants(WordNamespace + "t")
            .Select(node => node.Value));
        if (!string.IsNullOrWhiteSpace(text))
        {
            builder.AppendLine(text.Trim());
        }
    }

    private static void AppendTable(StringBuilder builder, XElement table)
    {
        builder.AppendLine("[表格]");
        foreach (var row in table.Elements(WordNamespace + "tr"))
        {
            var cells = row.Elements(WordNamespace + "tc")
                .Select(cell => string.Join(
                    " ",
                    cell.Elements(WordNamespace + "p")
                        .Select(paragraph => string.Concat(paragraph
                            .Descendants(WordNamespace + "t")
                            .Select(node => node.Value)).Trim())
                        .Where(value => !string.IsNullOrWhiteSpace(value))))
                .ToArray();
            if (cells.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                builder.AppendLine(string.Join(" | ", cells));
            }
        }

        builder.AppendLine("[/表格]");
    }
}
