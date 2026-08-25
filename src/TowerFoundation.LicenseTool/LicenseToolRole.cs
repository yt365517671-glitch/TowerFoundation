namespace TowerFoundation.LicenseTool;
internal static class LicenseToolRole
{
#if TOWER_FOUNDATION_ROOT_MANAGER
    public const bool IsRootManager = true;
#else
    public const bool IsRootManager = false;
#endif
}
