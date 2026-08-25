using System.Reflection;
using System.Text.Json;
using TowerFoundation.Application;

namespace TowerFoundation.Infrastructure;

public sealed class EmbeddedTowerLoadCatalog : ITowerLoadCatalog
{
    private const string CurrentResourceSuffix = "enterprise-tower-load-library-v2.json";
    private const string LegacyResourceSuffix = "enterprise-tower-load-library-legacy.json";
    private readonly IReadOnlyList<TowerLoadCatalogRecord> _records;
    private readonly IReadOnlyList<TowerLoadCatalogRecord> _legacyRecords;
    private readonly IReadOnlyDictionary<string, TowerLoadCatalogRecord> _recordsById;
    private readonly IReadOnlyDictionary<string, TowerLoadCatalogRecord> _legacyRecordsById;

    public EmbeddedTowerLoadCatalog()
    {
        var currentDocument = ReadDocument(CurrentResourceSuffix);
        var legacyDocument = ReadDocument(LegacyResourceSuffix);
        if (currentDocument.SchemaVersion <= 0)
        {
            throw new InvalidOperationException("内置现行V2.0企业塔型荷载库版本无效。");
        }

        if (currentDocument.IsCompleteForNewDesign && currentDocument.Records.Count == 0)
        {
            throw new InvalidOperationException("现行V2.0企业塔型荷载库标记为完整，但没有任何记录。");
        }

        ValidateIds(currentDocument.Records, "现行V2.0");
        ValidateIds(legacyDocument.Records, "已废止历史");
        var overlappingIds = currentDocument.Records
            .Select(item => item.Id)
            .Intersect(legacyDocument.Records.Select(item => item.Id), StringComparer.Ordinal)
            .ToArray();
        if (overlappingIds.Length > 0)
        {
            throw new InvalidOperationException("现行库与历史库存在重复记录编号。");
        }

        _records = currentDocument.Records.AsReadOnly();
        _legacyRecords = legacyDocument.Records.AsReadOnly();
        _recordsById = currentDocument.Records.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        _legacyRecordsById = legacyDocument.Records.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        Status = new TowerLoadCatalogStatus(
            currentDocument.CatalogEdition,
            currentDocument.NoticeNumber,
            currentDocument.EffectiveDate,
            currentDocument.IsCompleteForNewDesign,
            currentDocument.Records.Count,
            legacyDocument.Records.Count,
            currentDocument.StandardNumbers.AsReadOnly(),
            currentDocument.StatusMessage);
    }

    public TowerLoadCatalogStatus Status { get; }

    public IReadOnlyList<TowerLoadCatalogRecord> Records => _records;

    public IReadOnlyList<TowerLoadCatalogRecord> LegacyRecords => _legacyRecords;

    public TowerLoadCatalogRecord? FindById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return _recordsById.GetValueOrDefault(id) ??
               _legacyRecordsById.GetValueOrDefault(id);
    }

    public bool IsCurrentRecord(string id) =>
        !string.IsNullOrWhiteSpace(id) && _recordsById.ContainsKey(id);

    private static void ValidateIds(
        IReadOnlyList<TowerLoadCatalogRecord> records,
        string libraryName)
    {
        var duplicateIds = records
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException($"{libraryName}企业塔型荷载库存在重复或空记录编号。");
        }
    }

    private static TowerLoadCatalogDocument ReadDocument(string resourceSuffix)
    {
        var assembly = typeof(EmbeddedTowerLoadCatalog).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(item => item.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"发布包缺少企业标准塔型荷载库资源：{resourceSuffix}。");
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"无法打开企业标准塔型荷载库资源：{resourceSuffix}。");
        return JsonSerializer.Deserialize<TowerLoadCatalogDocument>(
                   stream,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true
                   })
               ?? throw new InvalidOperationException("企业标准塔型荷载库JSON格式无效。");
    }
}
