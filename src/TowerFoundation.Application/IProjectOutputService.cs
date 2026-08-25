using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public interface IProjectOutputService
{
    Task<OutputPackageResult> ExportPrototypePackageAsync(
        ProjectModel project,
        string parentDirectory,
        CancellationToken cancellationToken = default);
}

public sealed record OutputPackageResult(
    string DirectoryPath,
    IReadOnlyList<string> Files);
