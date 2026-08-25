using System.Text;
using System.Text.Json;
using TowerFoundation.Application;
using TowerFoundation.Domain;

namespace TowerFoundation.Infrastructure;

public sealed class LocalMonitoringDrawingRecognitionHistoryService :
    IMonitoringDrawingRecognitionHistoryService
{
    private const string HistoryFileName = "monitoring-drawing-recognition-history.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _syncRoot = new();
    private readonly IApplicationSettingsService? _settingsService;
    private readonly string? _fixedDataDirectory;
    private string? _lastResolvedHistoryPath;

    public LocalMonitoringDrawingRecognitionHistoryService(
        IApplicationSettingsService settingsService)
    {
        _settingsService = settingsService ??
                           throw new ArgumentNullException(nameof(settingsService));
    }

    public LocalMonitoringDrawingRecognitionHistoryService(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _fixedDataDirectory = Path.GetFullPath(dataDirectory);
    }

    public IReadOnlyList<MonitoringDrawingCandidate> Load()
    {
        lock (_syncRoot)
        {
            return LoadCore(ResolveHistoryPath());
        }
    }

    public IReadOnlyList<MonitoringDrawingCandidate> FindBySourceHash(
        string sourceFileSha256)
    {
        if (string.IsNullOrWhiteSpace(sourceFileSha256))
        {
            return [];
        }

        lock (_syncRoot)
        {
            return LoadCore(ResolveHistoryPath())
                .Where(candidate => candidate.SourceFileSha256.Equals(
                    sourceFileSha256.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.PageNumber)
                .ThenByDescending(candidate => candidate.RecognizedAt)
                .ToArray();
        }
    }

    public void Save(IEnumerable<MonitoringDrawingCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var incoming = candidates
            .Where(candidate => candidate is not null &&
                                !string.IsNullOrWhiteSpace(candidate.SourceFileSha256))
            .ToArray();
        if (incoming.Length == 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            var historyPath = ResolveHistoryPath();
            var records = LoadCore(historyPath);
            foreach (var candidate in incoming)
            {
                candidate.Id = candidate.Id == Guid.Empty ? Guid.NewGuid() : candidate.Id;
                candidate.RecognizedAt = candidate.RecognizedAt == default
                    ? DateTimeOffset.Now
                    : candidate.RecognizedAt;
                records.RemoveAll(existing =>
                    existing.SourceFileSha256.Equals(
                        candidate.SourceFileSha256,
                        StringComparison.OrdinalIgnoreCase) &&
                    existing.PageNumber == candidate.PageNumber);
                records.Add(candidate);
            }

            SaveCore(
                historyPath,
                records.OrderByDescending(candidate => candidate.RecognizedAt).ToList());
        }
    }

    public void MarkApplied(Guid candidateId)
    {
        if (candidateId == Guid.Empty)
        {
            return;
        }

        lock (_syncRoot)
        {
            var historyPath = ResolveHistoryPath();
            var records = LoadCore(historyPath);
            var candidate = records.FirstOrDefault(item => item.Id == candidateId);
            if (candidate is null)
            {
                return;
            }

            candidate.AppliedAt = DateTimeOffset.Now;
            SaveCore(historyPath, records);
        }
    }

    private string ResolveHistoryPath()
    {
        var directory = _fixedDataDirectory ??
                        _settingsService!.Load().DefaultMonitoringDrawingHistoryDirectory;
        directory = ApplicationPathDefaults.NormalizeDirectory(
            directory,
            ApplicationPathDefaults.ResolveMonitoringDrawingHistoryDirectory());
        var historyPath = Path.Combine(directory, HistoryFileName);

        if (_fixedDataDirectory is null &&
            !string.Equals(
                _lastResolvedHistoryPath,
                historyPath,
                StringComparison.OrdinalIgnoreCase))
        {
            MergeExistingHistory(_lastResolvedHistoryPath, historyPath);
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

        var merged = LoadCore(targetPath)
            .Concat(LoadCore(sourcePath))
            .GroupBy(
                item => $"{item.SourceFileSha256}:{item.PageNumber}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.RecognizedAt).First())
            .OrderByDescending(item => item.RecognizedAt)
            .ToList();
        SaveCore(targetPath, merged);
    }

    private static List<MonitoringDrawingCandidate> LoadCore(string historyPath)
    {
        if (!File.Exists(historyPath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<MonitoringDrawingCandidate>>(
                       File.ReadAllText(historyPath, Encoding.UTF8),
                       JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void SaveCore(
        string historyPath,
        IReadOnlyList<MonitoringDrawingCandidate> candidates)
    {
        var directory = Path.GetDirectoryName(historyPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = historyPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(candidates, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, historyPath, overwrite: true);
    }
}
