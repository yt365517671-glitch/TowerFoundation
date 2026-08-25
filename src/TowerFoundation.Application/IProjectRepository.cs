using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public interface IProjectRepository
{
    Task SaveAsync(ProjectModel project, string path, CancellationToken cancellationToken = default);

    Task<ProjectModel> LoadAsync(string path, CancellationToken cancellationToken = default);
}

