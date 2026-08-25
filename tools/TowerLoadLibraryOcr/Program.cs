using System.Text;
using System.Text.Json;
using TesseractOCR;
using TesseractOCR.Enums;
using TowerFoundation.Application;
using TowerFoundation.Infrastructure;
using PixImage = TesseractOCR.Pix.Image;

if (args.Length == 5 &&
    args[0].Equals("--cells", StringComparison.OrdinalIgnoreCase))
{
    var imagePath = Path.GetFullPath(args[1]);
    var xLines = ParseLines(args[2]);
    var yLines = ParseLines(args[3]);
    var cellOutputPath = Path.GetFullPath(args[4]);
    Directory.CreateDirectory(Path.GetDirectoryName(cellOutputPath)!);

    var tessdataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TowerFoundation",
        "ocr",
        "tessdata");
    using var engine = new Engine(
        tessdataPath,
        new List<Language> { Language.English },
        EngineMode.Default);
    engine.SetVariable(
        "tessedit_char_whitelist",
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz()./+-");
    engine.SetVariable("classify_bln_numeric_mode", false);

    using var image = PixImage.LoadFromFile(imagePath);
    var cells = new List<object>();
    for (var row = 0; row < yLines.Length - 1; row++)
    {
        for (var column = 0; column < xLines.Length - 1; column++)
        {
            var x = xLines[column] + 4;
            var y = yLines[row] + 4;
            var width = Math.Max(1, xLines[column + 1] - xLines[column] - 8);
            var height = Math.Max(1, yLines[row + 1] - yLines[row] - 8);
            using var page = engine.Process(
                image,
                new Rect(x, y, width, height),
                PageSegMode.SingleLine);
            cells.Add(new
            {
                Row = row + 1,
                Column = column + 1,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Text = page.Text.Trim(),
                page.MeanConfidence
            });
        }
    }

    await File.WriteAllTextAsync(
        cellOutputPath,
        JsonSerializer.Serialize(cells, new JsonSerializerOptions { WriteIndented = true }),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine($"完成：{yLines.Length - 1}行 × {xLines.Length - 1}列。");
    return 0;
}

if (args.Length != 2)
{
    Console.Error.WriteLine("用法：TowerLoadLibraryOcr <输入PDF> <输出TXT>");
    return 2;
}

var inputPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

var progress = new Progress<OcrProgress>(item =>
{
    var percent = item.TotalPages <= 0
        ? 0
        : (int)Math.Round(item.CurrentPage * 100d / item.TotalPages);
    Console.WriteLine($"{percent,3}%  {item.Message}");
});
var result = await new LocalPdfOcrService(forceOcr: true).ExtractAsync(
    inputPath,
    progress,
    CancellationToken.None);
await File.WriteAllTextAsync(
    outputPath,
    result.Content,
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

Console.WriteLine(
    $"完成：{result.PageCount}页，{result.Content.Length}字符，" +
    $"{result.ExtractionMode}。");
return 0;

static int[] ParseLines(string value) =>
    value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(int.Parse)
        .ToArray();
