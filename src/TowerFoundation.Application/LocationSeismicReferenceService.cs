using TowerFoundation.Domain;

namespace TowerFoundation.Application;

public sealed record LocationSeismicReference(
    string Province,
    string City,
    string County,
    int IntensityDegree,
    double BasicAccelerationG,
    string EarthquakeGroup,
    string SourceLocation);

public sealed record LocationSeismicApplicationResult(
    LocationSeismicReference? Reference,
    bool Applied,
    string Message);

/// <summary>
/// 按建设地点提供可确定的抗震设防候选值。场地类别和特征周期仍以地勘为准。
/// 当前内置甘肃省完整县区及项目验证用的河北省三河市记录；其他地区未命中时不猜值。
/// </summary>
public sealed class LocationSeismicReferenceService
{
    public const string BuiltInSourcePrefix = "GB/T 50011-2010附录A建设地点参考";

    private static readonly IReadOnlyList<LocationSeismicReference> Records = BuildRecords();

    public LocationSeismicApplicationResult ApplyIfAvailable(ProjectModel project)
    {
        var reference = Resolve(project.Province, project.City, project.County);
        if (reference is null)
        {
            return new LocationSeismicApplicationResult(
                null,
                false,
                string.IsNullOrWhiteSpace(project.County)
                    ? "选到县/区后，软件会尝试带出设防烈度、基本地震加速度和设计地震分组。"
                    : "当前内置抗震地点表未命中；优先采用地勘提取值，缺失项转专业核对。" );
        }

        var geotechnical = project.Geotechnical;
        var replaceBuiltIn = geotechnical.SeismicParameterSource.StartsWith(
            BuiltInSourcePrefix,
            StringComparison.Ordinal);
        var changed = false;
        if (replaceBuiltIn || geotechnical.SeismicIntensityDegree <= 0)
        {
            geotechnical.SeismicIntensityDegree = reference.IntensityDegree;
            changed = true;
        }
        if (replaceBuiltIn || geotechnical.DesignBasicGroundAccelerationG <= 0)
        {
            geotechnical.DesignBasicGroundAccelerationG = reference.BasicAccelerationG;
            changed = true;
        }
        if (replaceBuiltIn || string.IsNullOrWhiteSpace(geotechnical.DesignEarthquakeGroup))
        {
            geotechnical.DesignEarthquakeGroup = reference.EarthquakeGroup;
            changed = true;
        }

        if (changed)
        {
            geotechnical.SeismicParameterSource =
                $"{BuiltInSourcePrefix}；{reference.SourceLocation}；场地类别和特征周期仍以地勘为准";
        }

        return new LocationSeismicApplicationResult(
            reference,
            changed,
            $"地点参考：{reference.IntensityDegree}度、{reference.BasicAccelerationG:F2}g、{reference.EarthquakeGroup}；场地类别仍从地勘提取。" );
    }

    public LocationSeismicReference? Resolve(
        string province,
        string city,
        string county)
    {
        if (string.IsNullOrWhiteSpace(province))
        {
            return null;
        }

        var normalizedProvince = Normalize(province);
        var normalizedCity = Normalize(city);
        var normalizedCounty = Normalize(county);
        var candidates = Records.Where(item =>
            Normalize(item.Province) == normalizedProvince &&
            (string.IsNullOrWhiteSpace(normalizedCity) || Normalize(item.City) == normalizedCity));
        if (!string.IsNullOrWhiteSpace(normalizedCounty))
        {
            var countyMatch = candidates.FirstOrDefault(item =>
                Normalize(item.County) == normalizedCounty);
            if (countyMatch is not null)
            {
                return countyMatch;
            }
        }

        var cityRecords = candidates.ToList();
        if (cityRecords.Count > 0 &&
            cityRecords.Select(item => new
            {
                item.IntensityDegree,
                item.BasicAccelerationG,
                item.EarthquakeGroup
            }).Distinct().Count() == 1)
        {
            return cityRecords[0] with { County = string.IsNullOrWhiteSpace(county) ? city : county };
        }

        return null;
    }

    private static IReadOnlyList<LocationSeismicReference> BuildRecords()
    {
        var records = new List<LocationSeismicReference>();
        Add(records, "甘肃省", "兰州市", 8, 0.20, "第三组", "附录A.0.28，第211页",
            "城关区", "七里河区", "西固区", "安宁区", "永登县");
        Add(records, "甘肃省", "兰州市", 7, 0.15, "第三组", "附录A.0.28，第211页",
            "红古区", "皋兰县", "榆中县");
        Add(records, "甘肃省", "嘉峪关市", 8, 0.20, "第二组", "附录A.0.28，第211页", "嘉峪关市");
        Add(records, "甘肃省", "金昌市", 7, 0.15, "第三组", "附录A.0.28，第211页", "金川区", "永昌县");
        Add(records, "甘肃省", "白银市", 8, 0.30, "第三组", "附录A.0.28，第212页", "平川区");
        Add(records, "甘肃省", "白银市", 8, 0.20, "第三组", "附录A.0.28，第212页", "靖远县", "会宁县", "景泰县");
        Add(records, "甘肃省", "白银市", 7, 0.15, "第三组", "附录A.0.28，第212页", "白银区");
        Add(records, "甘肃省", "天水市", 8, 0.30, "第二组", "附录A.0.28，第212页", "秦州区", "麦积区");
        Add(records, "甘肃省", "天水市", 8, 0.20, "第三组", "附录A.0.28，第212页", "清水县", "秦安县", "武山县", "张家川回族自治县");
        Add(records, "甘肃省", "天水市", 8, 0.20, "第二组", "附录A.0.28，第212页", "甘谷县");
        Add(records, "甘肃省", "武威市", 8, 0.30, "第三组", "附录A.0.28，第212页", "古浪县");
        Add(records, "甘肃省", "武威市", 8, 0.20, "第三组", "附录A.0.28，第212页", "凉州区", "天祝藏族自治县");
        Add(records, "甘肃省", "武威市", 7, 0.10, "第三组", "附录A.0.28，第212页", "民勤县");
        Add(records, "甘肃省", "张掖市", 8, 0.20, "第三组", "附录A.0.28，第212页", "临泽县");
        Add(records, "甘肃省", "张掖市", 8, 0.20, "第二组", "附录A.0.28，第212页", "肃南裕固族自治县", "高台县");
        Add(records, "甘肃省", "张掖市", 7, 0.15, "第三组", "附录A.0.28，第212页", "甘州区");
        Add(records, "甘肃省", "张掖市", 7, 0.15, "第二组", "附录A.0.28，第212页", "民乐县", "山丹县");
        Add(records, "甘肃省", "平凉市", 8, 0.20, "第三组", "附录A.0.28，第212页", "华亭县", "庄浪县", "静宁县");
        Add(records, "甘肃省", "平凉市", 7, 0.15, "第三组", "附录A.0.28，第212页", "崆峒区", "崇信县");
        Add(records, "甘肃省", "平凉市", 7, 0.10, "第三组", "附录A.0.28，第212页", "泾川县", "灵台县");
        Add(records, "甘肃省", "酒泉市", 8, 0.20, "第二组", "附录A.0.28，第212页", "肃北蒙古族自治县");
        Add(records, "甘肃省", "酒泉市", 7, 0.15, "第三组", "附录A.0.28，第212页", "肃州区", "玉门市");
        Add(records, "甘肃省", "酒泉市", 7, 0.15, "第二组", "附录A.0.28，第212页", "金塔县", "阿克塞哈萨克族自治县");
        Add(records, "甘肃省", "酒泉市", 7, 0.10, "第三组", "附录A.0.28，第212页", "瓜州县", "敦煌市");
        Add(records, "甘肃省", "庆阳市", 7, 0.10, "第三组", "附录A.0.28，第212页", "西峰区", "环县", "镇原县");
        Add(records, "甘肃省", "庆阳市", 6, 0.05, "第三组", "附录A.0.28，第212页", "庆城县", "华池县", "合水县", "正宁县", "宁县");
        Add(records, "甘肃省", "定西市", 8, 0.20, "第三组", "附录A.0.28，第212页", "通渭县", "陇西县", "漳县");
        Add(records, "甘肃省", "定西市", 7, 0.15, "第三组", "附录A.0.28，第212页", "安定区", "渭源县", "临洮县", "岷县");
        Add(records, "甘肃省", "陇南市", 8, 0.30, "第二组", "附录A.0.28，第213页", "西和县", "礼县");
        Add(records, "甘肃省", "陇南市", 8, 0.20, "第三组", "附录A.0.28，第213页", "两当县");
        Add(records, "甘肃省", "陇南市", 8, 0.20, "第二组", "附录A.0.28，第213页", "武都区", "成县", "文县", "宕昌县", "康县", "徽县");
        Add(records, "甘肃省", "临夏回族自治州", 8, 0.20, "第三组", "附录A.0.28，第213页", "永靖县");
        Add(records, "甘肃省", "临夏回族自治州", 7, 0.15, "第三组", "附录A.0.28，第213页", "临夏市", "康乐县", "广河县", "和政县", "东乡族自治县");
        Add(records, "甘肃省", "临夏回族自治州", 7, 0.15, "第二组", "附录A.0.28，第213页", "临夏县");
        Add(records, "甘肃省", "临夏回族自治州", 7, 0.10, "第三组", "附录A.0.28，第213页", "积石山保安族东乡族撒拉族自治县");
        Add(records, "甘肃省", "甘南藏族自治州", 8, 0.20, "第三组", "附录A.0.28，第213页", "舟曲县");
        Add(records, "甘肃省", "甘南藏族自治州", 8, 0.20, "第二组", "附录A.0.28，第213页", "玛曲县");
        Add(records, "甘肃省", "甘南藏族自治州", 7, 0.15, "第三组", "附录A.0.28，第213页", "临潭县", "卓尼县", "迭部县");
        Add(records, "甘肃省", "甘南藏族自治州", 7, 0.15, "第二组", "附录A.0.28，第213页", "合作市", "夏河县");
        Add(records, "甘肃省", "甘南藏族自治州", 7, 0.10, "第三组", "附录A.0.28，第213页", "碌曲县");

        Add(records, "河北省", "廊坊市", 8, 0.20, "第二组", "附录A.0.3，第174页", "三河市");
        return records;
    }

    private static void Add(
        ICollection<LocationSeismicReference> records,
        string province,
        string city,
        int intensityDegree,
        double accelerationG,
        string earthquakeGroup,
        string sourceLocation,
        params string[] counties)
    {
        foreach (var county in counties)
        {
            records.Add(new LocationSeismicReference(
                province,
                city,
                county,
                intensityDegree,
                accelerationG,
                earthquakeGroup,
                sourceLocation));
        }
    }

    private static string Normalize(string value)
    {
        var normalized = value.Trim();
        foreach (var suffix in new[]
                 {
                     "保安族东乡族撒拉族自治县", "哈萨克族自治县", "蒙古族自治县",
                     "回族自治县", "藏族自治县", "裕固族自治县", "自治县",
                     "自治州", "地区", "省", "市", "县", "区"
                 })
        {
            if (normalized.EndsWith(suffix, StringComparison.Ordinal))
            {
                normalized = normalized[..^suffix.Length];
                break;
            }
        }

        return normalized;
    }
}
