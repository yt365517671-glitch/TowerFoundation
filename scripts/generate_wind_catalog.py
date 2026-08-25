"""从已下载并本地 OCR 的 GB 50009-2012 表 E.5 生成高置信度风压台站库。

本脚本只接受与现行行政区名称直接匹配、数值列格式完整且满足
R10 <= R50 <= R100 的行。未通过门槛的台站不会猜测或补值。
"""

from __future__ import annotations

import json
import re
from collections import Counter
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
REGIONS_PATH = ROOT / "src/TowerFoundation.Infrastructure/Data/china-regions.json"
OCR_DIRECTORY = ROOT / "tmp/source-data/gb50009-e5-ocr"
OUTPUT_PATH = (
    ROOT
    / "src/TowerFoundation.Infrastructure/Data/gb50009-wind-stations.json"
)


def normalize_line(value: str) -> str:
    value = re.sub(r"0\.\s+(\d{1,2})", r"0.\1", value)
    value = re.sub(r"(?<!\d)0([1-9]\d)(?!\d)", r"0.\1", value)
    return value.replace("｜", "|")


def extract_pressures(text_after_name: str) -> tuple[float, float, float] | None:
    altitude_match = re.search(
        r"(?<![\d.])\d+(?:\.\d+)?(?![\d.])",
        text_after_name,
    )
    if altitude_match is None:
        return None
    after_altitude = text_after_name[altitude_match.end() :]
    first_pressure_match = re.search(
        r"(?<![\d.])\d+(?:\.\d+)?(?![\d.])",
        after_altitude,
    )
    if first_pressure_match is None:
        return None
    # 风压列为空时，OCR可能继续读到后面的雪压列；遇到横线必须拒绝，
    # 否则会把雪压误当成风压。
    if re.search(r"[-—一=]", after_altitude[: first_pressure_match.start()]):
        return None

    numbers = [
        float(token)
        for token in re.findall(r"(?<![\d.])\d+(?:\.\d+)?(?![\d.])", text_after_name)
    ]
    if len(numbers) < 4:
        return None

    # 第一列应为海拔；紧随其后的三列为 R10、R50、R100 风压。
    altitude, r10, r50, r100 = numbers[:4]
    if not 0 <= altitude <= 9000:
        return None
    if not all(0.10 <= value <= 1.50 for value in (r10, r50, r100)):
        return None
    if not r10 <= r50 <= r100:
        return None
    if any(abs(value * 20 - round(value * 20)) > 1e-6 for value in (r10, r50, r100)):
        return None
    return r10, r50, r100


def main() -> None:
    regions = json.loads(REGIONS_PATH.read_text(encoding="utf-8"))
    cities = regions["city"]
    counties = regions["county"]
    candidates: list[dict[str, str]] = []
    for item in cities:
        candidates.append(
            {
                "alias": item["name"],
                "province": item["p_name"],
                "official_name": item["name"],
            }
        )
        if item["name"].endswith("市") and len(item["name"]) >= 3:
            candidates.append(
                {
                    "alias": item["name"][:-1],
                    "province": item["p_name"],
                    "official_name": item["name"],
                }
            )
    for item in counties:
        candidates.append(
            {
                "alias": item["name"],
                "province": item["p_name"],
                "official_name": item["name"],
            }
        )

    alias_counts = Counter(item["alias"] for item in candidates)
    unique_candidates = {
        item["alias"]: item
        for item in candidates
        if alias_counts[item["alias"]] == 1 and len(item["alias"]) >= 2
    }

    stations: dict[tuple[str, str], dict[str, object]] = {}
    for ocr_path in sorted(OCR_DIRECTORY.glob("e5-*.txt")):
        for raw_line in ocr_path.read_text(encoding="utf-8").splitlines():
            line = normalize_line(raw_line)
            for alias, place in unique_candidates.items():
                index = line.find(alias)
                if index < 0:
                    continue
                pressures = extract_pressures(line[index + len(alias) :])
                if pressures is None:
                    continue
                r10, r50, r100 = pressures
                station = {
                    "province": place["province"],
                    "city": place["official_name"],
                    "tenYearKpa": r10,
                    "fiftyYearKpa": r50,
                    "hundredYearKpa": r100,
                    "sourcePage": f"GB 50009-2012 表E.5（{ocr_path.stem}）",
                }
                stations[(place["province"], place["official_name"])] = station

    # 首页四个直辖市的印刷表格已经逐行视觉核对，作为生成结果的固定基准。
    verified_municipalities = [
        ("北京市", "北京市", 0.30, 0.45, 0.50, "e5-01"),
        ("天津市", "天津市", 0.30, 0.50, 0.60, "e5-01"),
        ("上海市", "上海市", 0.40, 0.55, 0.60, "e5-01"),
        ("重庆市", "重庆市", 0.25, 0.40, 0.45, "e5-01"),
    ]
    for province, city, r10, r50, r100, page in verified_municipalities:
        stations[(province, city)] = {
            "province": province,
            "city": city,
            "tenYearKpa": r10,
            "fiftyYearKpa": r50,
            "hundredYearKpa": r100,
            "sourcePage": f"GB 50009-2012 表E.5（{page}，人工核对）",
        }

    # 甘肃常用城市/县区值依据表E.5第 e5-23、e5-24 页逐行视觉核对。
    verified_gansu = [
        ("兰州市", 0.20, 0.30, 0.35, "e5-23"),
        ("酒泉市", 0.40, 0.55, 0.60, "e5-23"),
        ("张掖市", 0.30, 0.50, 0.60, "e5-23"),
        ("武威市", 0.35, 0.55, 0.65, "e5-23"),
        ("民勤县", 0.40, 0.50, 0.55, "e5-23"),
        ("景泰县", 0.25, 0.40, 0.45, "e5-23"),
        ("靖远县", 0.20, 0.30, 0.35, "e5-23"),
        ("临夏市", 0.20, 0.30, 0.35, "e5-23"),
        ("临洮县", 0.20, 0.30, 0.35, "e5-23"),
        ("环县", 0.20, 0.30, 0.35, "e5-24"),
        ("平凉市", 0.25, 0.30, 0.35, "e5-24"),
        ("西峰区", 0.20, 0.30, 0.35, "e5-24"),
        ("玛曲县", 0.25, 0.30, 0.35, "e5-24"),
        ("合作市", 0.25, 0.30, 0.35, "e5-24"),
        ("武都区", 0.25, 0.35, 0.40, "e5-24"),
        ("天水市", 0.20, 0.35, 0.40, "e5-24"),
    ]
    for city, r10, r50, r100, page in verified_gansu:
        stations[("甘肃省", city)] = {
            "province": "甘肃省",
            "city": city,
            "tenYearKpa": r10,
            "fiftyYearKpa": r50,
            "hundredYearKpa": r100,
            "sourcePage": f"GB 50009-2012 表E.5（{page}，人工核对）",
        }

    ordered = sorted(
        stations.values(),
        key=lambda item: (str(item["province"]), str(item["city"])),
    )
    OUTPUT_PATH.write_text(
        json.dumps(ordered, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Generated {len(ordered)} high-confidence city stations: {OUTPUT_PATH}")


if __name__ == "__main__":
    main()
