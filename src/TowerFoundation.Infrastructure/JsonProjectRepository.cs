using System.Text.Json;
using System.Text.Json.Serialization;
using TowerFoundation.Application;
using TowerFoundation.Domain;

namespace TowerFoundation.Infrastructure;

public sealed class JsonProjectRepository : IProjectRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SaveAsync(
        ProjectModel project,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ??
                        throw new InvalidOperationException("项目文件路径无效。");
        Directory.CreateDirectory(directory);

        project.ModifiedAt = DateTimeOffset.Now;
        var temporaryPath = fullPath + ".tmp";
        var backupPath = fullPath + ".bak";

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 64 * 1024,
                         useAsync: true))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                project,
                SerializerOptions,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (File.Exists(fullPath))
        {
            File.Replace(temporaryPath, fullPath, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporaryPath, fullPath);
        }
    }

    public async Task<ProjectModel> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        var project = await JsonSerializer.DeserializeAsync<ProjectModel>(
            stream,
            SerializerOptions,
            cancellationToken);

        if (project is null)
        {
            throw new InvalidDataException("项目文件内容为空或格式无效。");
        }

        project.MonitoringPole ??= new MonitoringPoleInput();
        project.MonitoringPole.ArmSegments ??= [];
        project.MonitoringPole.ExplicitDrawingInputFields ??= [];
        project.MonitoringDrawingCandidates ??= [];
        foreach (var candidate in project.MonitoringDrawingCandidates)
        {
            candidate.Fields ??= [];
            candidate.ArmSegments ??= [];
            candidate.Warnings ??= [];
        }
        project.SchemaVersion = Math.Max(project.SchemaVersion, 6);
        return project;
    }
}
