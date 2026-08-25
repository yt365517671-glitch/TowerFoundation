using System.Text;
using System.Text.Json;
using TowerFoundation.Application;

namespace TowerFoundation.Infrastructure;

public sealed class LocalGeotechnicalAnalysisHistoryService :
    IGeotechnicalAnalysisHistoryService
{
    private const string HistoryFileName = "geotechnical-analysis-history.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _syncRoot = new();
    private readonly IApplicationSettingsService? _settingsService;
    private readonly string? _fixedDataDirectory;
    private string? _lastResolvedHistoryPath;

    public LocalGeotechnicalAnalysisHistoryService(
        IApplicationSettingsService settingsService)
    {
        _settingsService = settingsService ??
                           throw new ArgumentNullException(nameof(settingsService));
    }

    public LocalGeotechnicalAnalysisHistoryService(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _fixedDataDirectory = Path.GetFullPath(dataDirectory);
    }

    public IReadOnlyList<GeotechnicalAnalysisRecord> Load()
    {
        lock (_syncRoot)
        {
            return LoadCore(ResolveHistoryPath());
        }
    }

    public GeotechnicalAnalysisRecord Save(GeotechnicalAnalysisRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_syncRoot)
        {
            var historyPath = ResolveHistoryPath();
            var normalized = record with
            {
                Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
                CreatedAt = record.CreatedAt == default ? DateTimeOffset.Now : record.CreatedAt
            };
            var records = LoadCore(historyPath)
                .Where(item => item.Id != normalized.Id)
                .Prepend(normalized)
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
            SaveCore(historyPath, records);
            return normalized;
        }
    }

    public void MarkApplied(Guid id)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        lock (_syncRoot)
        {
            var historyPath = ResolveHistoryPath();
            var changed = false;
            var now = DateTimeOffset.Now;
            var records = LoadCore(historyPath)
                .Select(item =>
                {
                    if (item.Id != id)
                    {
                        return item;
                    }

                    changed = true;
                    return item with
                    {
                        WasApplied = true,
                        LastUsedAt = now,
                        UsageCount = item.UsageCount + 1
                    };
                })
                .ToList();
            if (changed)
            {
                SaveCore(historyPath, records);
            }
        }
    }

    public bool Delete(Guid id)
    {
        if (id == Guid.Empty)
        {
            return false;
        }

        lock (_syncRoot)
        {
            var historyPath = ResolveHistoryPath();
            var records = LoadCore(historyPath);
            var remaining = records.Where(item => item.Id != id).ToList();
            if (remaining.Count == records.Count)
            {
                return false;
            }

            SaveCore(historyPath, remaining);
            return true;
        }
    }

    private string ResolveHistoryPath()
    {
        var directory = _fixedDataDirectory ??
                        _settingsService!.Load().DefaultGeotechnicalHistoryDirectory;
        directory = ApplicationPathDefaults.NormalizeDirectory(
            directory,
            ApplicationPathDefaults.ResolveGeotechnicalHistoryDirectory());
        var historyPath = Path.Combine(directory, HistoryFileName);

        if (_fixedDataDirectory is null &&
            !string.Equals(
                _lastResolvedHistoryPath,
                historyPath,
                StringComparison.OrdinalIgnoreCase))
        {
            var legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TowerFoundation",
                HistoryFileName);
            MergeExistingHistory(_lastResolvedHistoryPath, historyPath);
            if (_lastResolvedHistoryPath is null)
            {
                MergeExistingHistory(legacyPath, historyPath);
            }
        }

        _lastResolvedHistoryPath = historyPath;
        return historyPath;
    }

    private static void MergeExistingHistory(string? sourcePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            Path.GetFullPath(sourcePath).Equals(
                Path.GetFullPath(targetPath),
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourcePath))
        {
            return;
        }

        var source = LoadCore(sourcePath);
        if (source.Count == 0)
        {
            return;
        }

        var merged = LoadCore(targetPath)
            .Concat(source)
            .GroupBy(item => item.Id)
            .Select(group => group
                .OrderByDescending(item => item.LastUsedAt ?? item.CreatedAt)
                .First())
            .OrderByDescending(item => item.CreatedAt)
            .ToList();
        SaveCore(targetPath, merged);
    }

    private static List<GeotechnicalAnalysisRecord> LoadCore(string historyPath)
    {
        try
        {
            if (!File.Exists(historyPath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<GeotechnicalAnalysisRecord>>(
                       File.ReadAllText(historyPath, Encoding.UTF8),
                       JsonOptions) ?? [];
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void SaveCore(
        string historyPath,
        IReadOnlyList<GeotechnicalAnalysisRecord> records)
    {
        var directory = Path.GetDirectoryName(historyPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = historyPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(records, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, historyPath, overwrite: true);
    }
}
