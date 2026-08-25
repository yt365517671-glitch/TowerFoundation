using System.Reflection;
using System.Text.Json;
using TowerFoundation.Application;

namespace TowerFoundation.Infrastructure;

public sealed class EmbeddedRegionWindCatalog : IRegionWindCatalog
{
    private readonly IReadOnlyList<RegionOption> _provinces;
    private readonly IReadOnlyList<CityRecord> _cities;
    private readonly IReadOnlyList<CountyRecord> _counties;
    private readonly IReadOnlyList<WindPressureStation> _stations;

    public EmbeddedRegionWindCatalog()
    {
        var regionData = ReadEmbeddedJson<RegionData>("china-regions.json");
        _provinces = regionData.Province
            .Select(item => new RegionOption(item.Code, item.Name))
            .OrderBy(item => item.Name.Equals("甘肃省", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(item => item.Code)
            .ToArray();
        _cities = regionData.City;
        _counties = regionData.County;
        _stations = ReadEmbeddedJson<List<WindPressureStation>>("gb50009-wind-stations.json");
    }

    public IReadOnlyList<RegionOption> Provinces => _provinces;

    public IReadOnlyList<RegionOption> GetCities(int provinceCode)
    {
        return _cities
            .Where(item => item.ProvinceCode == provinceCode)
            .Select(item => new RegionOption(item.Code, item.Name))
            .OrderBy(item => item.Code)
            .ToArray();
    }

    public IReadOnlyList<RegionOption> GetCounties(int cityCode)
    {
        return _counties
            .Where(item => item.CityCode == cityCode)
            .Select(item => new RegionOption(item.Code, item.Name))
            .OrderBy(item => item.Code)
            .ToArray();
    }

    public WindPressureLookupResult Lookup(
        string province,
        string city,
        string county)
    {
        var provinceStations = GetStations(province);
        var countyStation = FindStation(provinceStations, county);
        if (countyStation is not null)
        {
            return new WindPressureLookupResult(
                WindPressureSourceKind.DirectNormativeStation,
                countyStation.FiftyYearKpa,
                $"{countyStation.Province}·{countyStation.City}",
                $"县区名称与 GB 50009-2012 表E.5台站直接匹配，采用50年重现期基本风压 {countyStation.FiftyYearKpa:F2} kPa。");
        }

        var cityStation = FindStation(provinceStations, city);
        if (cityStation is not null)
        {
            return new WindPressureLookupResult(
                WindPressureSourceKind.ParentCityReference,
                cityStation.FiftyYearKpa,
                $"{cityStation.Province}·{cityStation.City}",
                $"所选县区在表E.5中无独立台站，暂引用上级城市50年重现期基本风压 {cityStation.FiftyYearKpa:F2} kPa；应由设计人员结合就近气象资料确认。");
        }

        return new WindPressureLookupResult(
            WindPressureSourceKind.ManualRequired,
            null,
            string.Empty,
            "GB 50009-2012 表E.5中未找到该县区或上级城市的直接台站值。请选择同省就近台站或依据当地气象资料手工确认，软件不会自动编造县级风压。");
    }

    public IReadOnlyList<WindPressureStation> GetStations(string province)
    {
        var normalized = NormalizePlaceName(province);
        return _stations
            .Where(item => NormalizePlaceName(item.Province) == normalized)
            .OrderBy(item => item.City, StringComparer.Ordinal)
            .ToArray();
    }

    private static WindPressureStation? FindStation(
        IReadOnlyList<WindPressureStation> stations,
        string placeName)
    {
        if (string.IsNullOrWhiteSpace(placeName))
        {
            return null;
        }

        var normalized = NormalizePlaceName(placeName);
        return stations.FirstOrDefault(item =>
            NormalizePlaceName(item.City) == normalized);
    }

    private static string NormalizePlaceName(string value)
    {
        var result = value.Trim();
        string[] suffixes =
        [
            "特别行政区", "维吾尔自治区", "壮族自治区", "回族自治区",
            "自治区", "自治州", "自治县", "地区", "盟", "省", "市",
            "县", "区", "旗"
        ];
        foreach (var suffix in suffixes)
        {
            if (result.EndsWith(suffix, StringComparison.Ordinal) &&
                result.Length > suffix.Length)
            {
                return result[..^suffix.Length];
            }
        }

        return result;
    }

    private static T ReadEmbeddedJson<T>(string suffix)
    {
        var assembly = typeof(EmbeddedRegionWindCatalog).Assembly;
        var name = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(item => item.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"内置数据资源缺失：{suffix}");
        using var stream = assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"无法读取内置数据资源：{suffix}");
        return JsonSerializer.Deserialize<T>(
                   stream,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true
                   })
               ?? throw new InvalidOperationException($"内置数据资源格式无效：{suffix}");
    }

    private sealed class RegionData
    {
        public List<ProvinceRecord> Province { get; init; } = [];

        public List<CityRecord> City { get; init; } = [];

        public List<CountyRecord> County { get; init; } = [];
    }

    private sealed class ProvinceRecord
    {
        public int Code { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    private sealed class CityRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("p_code")]
        public int ProvinceCode { get; init; }

        public int Code { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    private sealed class CountyRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("c_code")]
        public int CityCode { get; init; }

        public int Code { get; init; }

        public string Name { get; init; } = string.Empty;
    }
}
