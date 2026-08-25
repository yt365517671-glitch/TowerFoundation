using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public sealed record ProjectCatalogEntry(
    string FilePath,
    string ProjectName,
    ProjectType ProjectType,
    FoundationType FoundationType,
    string Location,
    DateTimeOffset ModifiedAt,
    bool IsReadable,
    string? ErrorMessage = null)
{
    public string FileName => Path.GetFileName(FilePath);
}

public interface IProjectCatalogService
{
    string ProjectDirectory { get; }

    string CreateDefaultProjectPath(string projectName);

    Task<IReadOnlyList<ProjectCatalogEntry>> ListAsync(
        CancellationToken cancellationToken = default);
}
