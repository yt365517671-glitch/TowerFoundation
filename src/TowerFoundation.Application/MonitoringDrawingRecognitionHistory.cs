using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public interface IMonitoringDrawingRecognitionHistoryService
{
    IReadOnlyList<MonitoringDrawingCandidate> Load();

    IReadOnlyList<MonitoringDrawingCandidate> FindBySourceHash(string sourceFileSha256);

    void Save(IEnumerable<MonitoringDrawingCandidate> candidates);

    void MarkApplied(Guid candidateId);
}
