using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public static class PileLayoutRules
{
    public static int GetPileCount(TowerStructureType structureType) =>
        structureType switch
        {
            TowerStructureType.ThreeTube => 3,
            TowerStructureType.HeighteningFrame => 3,
            TowerStructureType.AngleSteel => 4,
            _ => 1
        };

    public static int GetPileCount(TowerMastInput tower) =>
        tower.FoundationLegCount is 1 or 3 or 4
            ? tower.FoundationLegCount
            : GetPileCount(tower.StructureType);

    public static bool UsesSeparateFoundationUnits(FoundationType foundationType) =>
        foundationType is
            FoundationType.RectangularShortColumn or
            FoundationType.CircularShortColumn or
            FoundationType.RigidShortPile or
            FoundationType.RigidRectangularShortPile or
            FoundationType.Pile;

    public static int GetFoundationUnitCount(
        TowerStructureType structureType,
        FoundationType foundationType) =>
        UsesSeparateFoundationUnits(foundationType)
            ? GetPileCount(structureType)
            : 1;

    public static int GetFoundationUnitCount(
        TowerMastInput tower,
        FoundationType foundationType) =>
        UsesSeparateFoundationUnits(foundationType)
            ? GetPileCount(tower)
            : 1;

    public static bool RequiresSingleLegReactions(
        TowerStructureType structureType,
        FoundationType foundationType) =>
        GetFoundationUnitCount(structureType, foundationType) > 1;

    public static bool RequiresSingleLegReactions(
        TowerMastInput tower,
        FoundationType foundationType) =>
        GetFoundationUnitCount(tower, foundationType) > 1;

    public static bool RequiresTieBeams(
        TowerStructureType structureType,
        FoundationType foundationType) =>
        UsesSeparateFoundationUnits(foundationType) &&
        GetFoundationUnitCount(structureType, foundationType) > 1;

    public static bool RequiresTieBeams(
        TowerMastInput tower,
        FoundationType foundationType) =>
        UsesSeparateFoundationUnits(foundationType) &&
        GetFoundationUnitCount(tower, foundationType) > 1;

    public static void Synchronize(ProjectModel project)
    {
        var count = project.ProjectType == ProjectType.CommunicationTower
            ? GetPileCount(project.TowerMast)
            : 1;
        var pile = project.FoundationSettings.Pile;
        pile.PileCount = count;
        pile.TieBeamRequired = count > 1;
    }

    public static string Describe(TowerStructureType structureType)
    {
        var count = GetPileCount(structureType);
        return count switch
        {
            3 => "3根独立灌注桩，按三角形布置并以3根连梁拉接，无承台",
            4 => "4根独立灌注桩，按四角布置并以4根周边连梁拉接，无承台",
            _ => "1根灌注桩直接承受单管塔塔脚反力，无承台、无连梁"
        };
    }

    public static string DescribeFoundationLayout(
        TowerStructureType structureType,
        FoundationType foundationType)
        => DescribeFoundationLayout(
            GetFoundationUnitCount(structureType, foundationType),
            foundationType,
            GetPileCount(structureType));

    public static string DescribeFoundationLayout(
        TowerMastInput tower,
        FoundationType foundationType)
        => DescribeFoundationLayout(
            GetFoundationUnitCount(tower, foundationType),
            foundationType,
            GetPileCount(tower));

    private static string DescribeFoundationLayout(
        int count,
        FoundationType foundationType,
        int towerLegCount)
    {
        if (foundationType == FoundationType.Pile)
        {
            return count switch
            {
                3 => "3根独立灌注桩，按三角形布置并以3根连梁拉接，无承台",
                4 => "4根独立灌注桩，按四角布置并以4根周边连梁拉接，无承台",
                _ => "1根灌注桩直接承受单管塔塔脚反力，无承台、无连梁"
            };
        }

        if (count <= 1)
        {
            return foundationType == FoundationType.Raft && towerLegCount > 1
                ? "采用共用整体筏板承托全部塔柱，筏板本身形成整体连接，不另设独立连系梁。"
                : "1个基础单元，采用整塔基础端反力。";
        }

        var typeName = foundationType switch
        {
            FoundationType.RectangularShortColumn => "独立基础－矩形柱",
            FoundationType.CircularShortColumn => "独立基础－圆形柱",
            FoundationType.RigidShortPile => "刚性短柱桩基础－圆形",
            FoundationType.RigidRectangularShortPile => "刚性短柱桩基础－矩形",
            _ => "独立基础"
        };
        var tieBeamLayout = count == 3
            ? "3根连系梁按三角形闭合拉接"
            : "4根周边连系梁按四角闭合拉接";
        return $"{count}个相互独立的{typeName}；每个按一个塔脚反力包络验算，{tieBeamLayout}，材料和工程量按{count}个基础及连系梁汇总。";
    }
}
