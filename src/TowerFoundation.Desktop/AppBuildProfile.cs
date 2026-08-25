namespace TowerFoundation.Desktop;

internal static class AppBuildProfile
{
#if TOWER_FOUNDATION_PRODUCTION
    public const string Name = "Production";
    public const string SettingsDirectoryName = "production";
    public const string LicenseDirectoryName = "production-license";
    public const string WindowTitle = "塔基智设 · 铁塔及监控杆基础设计";
    public static bool RequiresLicense => true;
#else
    public const string Name = "Development";
    public const string SettingsDirectoryName = "";
    public const string LicenseDirectoryName = "development-license";
    public const string WindowTitle = "塔基智设 · 开发测试版";
    public static bool RequiresLicense => false;
#endif
}
