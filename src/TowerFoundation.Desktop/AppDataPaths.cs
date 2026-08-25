using System.IO;

namespace TowerFoundation.Desktop;

internal static class AppDataPaths
{
    public static string ResolveSettingsDirectory()
    {
        var explicitDirectory = ResolveExplicitDirectory();
        if (explicitDirectory is not null) return explicitDirectory;
        var root = ProductRoot;
        return string.IsNullOrWhiteSpace(AppBuildProfile.SettingsDirectoryName)
            ? root
            : Path.Combine(root, AppBuildProfile.SettingsDirectoryName);
    }

    public static string ResolveLicenseDirectory()
    {
        var explicitDirectory = ResolveExplicitDirectory();
        return explicitDirectory ?? Path.Combine(ProductRoot, AppBuildProfile.LicenseDirectoryName);
    }

    private static string ProductRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TowerFoundation");

    private static string? ResolveExplicitDirectory()
    {
        var value = Environment.GetEnvironmentVariable("TOWER_FOUNDATION_DATA_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(value)) return Path.GetFullPath(value);
        var arguments = Environment.GetCommandLineArgs();
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (arguments[index].Equals("--data-directory", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(arguments[index + 1]))
                return Path.GetFullPath(arguments[index + 1]);
        }
        return null;
    }
}
