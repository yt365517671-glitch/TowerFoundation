namespace TowerFoundation.Application;

public sealed record RegionOption(int Code, string Name);

public enum WindPressureSourceKind
{
    DirectNormativeStation,
    ParentCityReference,
    ManualRequired
}

public sealed record WindPressureStation(
    string Province,
    string City,
    double? TenYearKpa,
    double FiftyYearKpa,
    double? HundredYearKpa,
    string SourcePage);

public sealed record WindPressureLookupResult(
    WindPressureSourceKind SourceKind,
    double? FiftyYearKpa,
    string SourceStation,
    string Explanation)
{
    public bool HasValue => FiftyYearKpa is > 0;
}

public interface IRegionWindCatalog
{
    IReadOnlyList<RegionOption> Provinces { get; }

    IReadOnlyList<RegionOption> GetCities(int provinceCode);

    IReadOnlyList<RegionOption> GetCounties(int cityCode);

    WindPressureLookupResult Lookup(
        string province,
        string city,
        string county);

    IReadOnlyList<WindPressureStation> GetStations(string province);
}

public sealed record OcrProgress(
    int CurrentPage,
    int TotalPages,
    string Message);

public enum PdfTextExtractionMode
{
    NativeTextLayer,
    LocalOcr
}

public sealed class OcrDocumentResult
{
    public string SourceName { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public int PageCount { get; init; }

    public int ProcessedPageCount { get; init; }

    public double MeanConfidence { get; init; }

    public PdfTextExtractionMode ExtractionMode { get; init; } =
        PdfTextExtractionMode.LocalOcr;

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface ILocalPdfOcrService
{
    Task<OcrDocumentResult> ExtractAsync(
        string path,
        IProgress<OcrProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<OcrDocumentResult> ExtractRangeAsync(
        string path,
        int startPage,
        int endPage,
        IProgress<OcrProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
