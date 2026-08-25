using System.Globalization;
using System.Windows.Data;
using TowerFoundation.Domain;

namespace TowerFoundation.Desktop;

public sealed class ChineseEnumConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            CheckStatus.Pass => "通过",
            CheckStatus.Fail => "不通过",
            CheckStatus.Warning => "需专项复核",
            CheckStatus.NotEvaluated => "未校核",
            CheckStatus.Result => "计算结果",
            CheckStatus.PendingInput => "待补参数",
            CheckStatus.SpecialReview => "已转专业核对",
            CheckStatus.Advisory => "施工提醒",
            OptimizationPreference.Economy => "经济优先",
            OptimizationPreference.Constructability => "施工优先",
            OptimizationPreference.Robustness => "稳健优先",
            ProjectType.NotSelected => "尚未选择",
            ProjectType.MonitoringPole => "监控杆基础",
            ProjectType.CommunicationTower => "通信塔桅基础",
            TowerStructureType.SingleTube => "单管塔",
            TowerStructureType.ThreeTube => "三管塔",
            TowerStructureType.HeighteningFrame => "增高架",
            TowerStructureType.AngleSteel => "角钢塔",
            TowerStructureType.GuyedMast => "拉线桅杆",
            TowerStructureType.Other => "其他塔桅",
            TowerLoadSourceType.Manual => "手工录入",
            TowerLoadSourceType.EnterpriseCatalog => "企业荷载库",
            TubeSectionType.CircularTube => "圆形管",
            TubeSectionType.RegularOctagonDiagonalTube => "正八边形（对角尺寸）",
            AnchorConnectionType.NotDetermined => "尚未确定",
            AnchorConnectionType.AnchorBoltCage => "锚栓笼连接",
            AnchorConnectionType.DirectEmbedded => "直埋 / 无锚栓",
            AnchorConnectionType.Other => "其他连接形式",
            FoundationType.RectangularShortColumn => "独立基础－矩形柱",
            FoundationType.CircularShortColumn => "独立基础－圆形柱",
            FoundationType.Raft => "中央塔柱筏板基础",
            FoundationType.RigidShortPile => "刚性短柱桩基础－圆形",
            FoundationType.RigidRectangularShortPile => "刚性短柱桩基础－矩形",
            FoundationType.Pile => "独立灌注桩及连梁基础",
            ProjectStage.Created => "项目已创建",
            ProjectStage.SiteReady => "场址已确认",
            ProjectStage.GeotechnicalReady => "地勘已确认",
            ProjectStage.LoadReady => "荷载已计算",
            ProjectStage.CandidateReady => "方案已生成",
            ProjectStage.SchemeSelected => "方案已选择",
            ProjectStage.Verified => "原型校核通过",
            ProjectStage.OutputReady => "成果已生成",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is true
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
