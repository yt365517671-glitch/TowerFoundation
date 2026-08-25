using TowerFoundation.Application;
using TowerFoundation.Domain;

namespace TowerFoundation.Infrastructure;

public sealed class LocalProjectCatalogService : IProjectCatalogService
{
    private readonly IProjectRepository _repository;
    private readonly IApplicationSettingsService? _settingsService;
    private readonly string? _projectDirectoryOverride;

    public LocalProjectCatalogService(
        IProjectRepository repository,
        string? projectDirectory = null)
    {
        _repository = repository;
        _projectDirectoryOverride = Path.GetFullPath(
            string.IsNullOrWhiteSpace(projectDirectory)
                ? ResolveDefaultProjectDirectory()
                : projectDirectory);
    }

    public LocalProjectCatalogService(
        IProjectRepository repository,
        IApplicationSettingsService settingsService)
    {
        _repository = repository;
        _settingsService = settingsService;
    }

    public string ProjectDirectory =>
        _projectDirectoryOverride ??
        _settingsService?.Load().DefaultProjectDirectory ??
        ResolveDefaultProjectDirectory();

    public string CreateDefaultProjectPath(string projectName)
    {
        Directory.CreateDirectory(ProjectDirectory);

        var safeName = MakeSafeFileName(projectName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "未命名基础项目";
        }

        var candidate = Path.Combine(ProjectDirectory, safeName + ".tjproj");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; suffix < 10000; suffix++)
        {
            candidate = Path.Combine(
                ProjectDirectory,
                $"{safeName} ({suffix}).tjproj");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(
            ProjectDirectory,
            $"{safeName} {DateTime.Now:yyyyMMdd-HHmmss}.tjproj");
    }

    public async Task<IReadOnlyList<ProjectCatalogEntry>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ProjectDirectory);

        var entries = new List<ProjectCatalogEntry>();
        foreach (var path in Directory.EnumerateFiles(
                     ProjectDirectory,
                     "*.tjproj",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var project = await _repository.LoadAsync(path, cancellationToken);
                entries.Add(new ProjectCatalogEntry(
                    path,
                    project.Name,
                    project.ProjectType,
                    project.FoundationSettings.FoundationType,
                    FormatLocation(project),
                    project.ModifiedAt,
                    IsReadable: true));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                entries.Add(new ProjectCatalogEntry(
                    path,
                    Path.GetFileNameWithoutExtension(path),
                    ProjectType.NotSelected,
                    FoundationType.RectangularShortColumn,
                    "文件无法读取",
                    File.GetLastWriteTime(path),
                    IsReadable: false,
                    ErrorMessage: exception.Message));
            }
        }

        return entries
            .OrderByDescending(entry => entry.ModifiedAt)
            .ThenBy(entry => entry.ProjectName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string ResolveDefaultProjectDirectory()
    {
        var baseDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        return Path.Combine(baseDirectory, "塔基智设", "项目");
    }

    private static string FormatLocation(ProjectModel project)
    {
        if (project.ProjectType == ProjectType.CommunicationTower)
        {
            return "塔脚反力输入 · 无需城市风压";
        }

        var parts = new[] { project.Province, project.City, project.County }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var location = string.Join(" · ", parts);
        return string.IsNullOrWhiteSpace(location) ? "场址尚未填写" : location;
    }

    private static string MakeSafeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat((name ?? string.Empty).Select(character =>
            invalidCharacters.Contains(character) ? '_' : character)).Trim();
    }
}
