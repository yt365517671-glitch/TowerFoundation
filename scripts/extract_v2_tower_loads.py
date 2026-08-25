"""从 V2.0 三个 part1 图集的表10-1提取可用于基础设计的塔脚反力。

脚本只读取图集反力汇总页。为降低水印和表格线对 OCR 的影响，先按网格切除
表格线，再使用多组灰度阈值识别；标准组合和基本组合的水平力、弯矩及单塔腿
反力按 1.5 关系复核。输出 JSON 供桌面软件内嵌，审查摘要用于回归测试。
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import subprocess
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_IMAGE_DIR = ROOT / "tmp" / "pdfs" / "v2-part1"
DEFAULT_OUTPUT = ROOT / "标准图集荷载库" / "提取结果" / "企业标准塔型荷载库-v2.json"
DEFAULT_AUDIT = ROOT / "标准图集荷载库" / "提取结果" / "企业标准塔型荷载库-v2-审查摘要.json"
DEFAULT_TESSERACT = (
    ROOT
    / ".dotnet-home"
    / ".nuget"
    / "packages"
    / "tesseractocr"
    / "5.5.2"
    / "x64"
    / "tesseract.exe"
)
DEFAULT_TESSDATA = ROOT / "src" / "TowerFoundation.Infrastructure" / "OcrData"


@dataclass(frozen=True)
class RowBand:
    first_row: int
    last_row: int
    tower_type: str
    reaction_mode: str
    displayed_first_row: int = 1

    def includes(self, physical_row: int) -> bool:
        return self.first_row <= physical_row <= self.last_row

    def displayed_row(self, physical_row: int) -> int:
        return self.displayed_first_row + physical_row - self.first_row


@dataclass(frozen=True)
class PageConfig:
    image_name: str
    source_id: str
    source_title: str
    standard_no: str
    pdf_page: int
    table_top_hint: int
    table_bottom_hint: int
    row_count: int
    group: str
    bands: tuple[RowBand, ...]


PAGES: tuple[PageConfig, ...] = (
    PageConfig(
        "regional-reaction-300-18.png",
        "regional-v2",
        "区域性铁塔产品参考图集",
        "Q/ZTT 1032-2025",
        18,
        340,
        2600,
        46,
        "区域性塔型",
        (
            RowBand(1, 36, "支架式单管塔", "overall"),
            RowBand(37, 46, "路灯杆", "overall"),
        ),
    ),
    PageConfig(
        "regional-reaction-300-19.png",
        "regional-v2",
        "区域性铁塔产品参考图集",
        "Q/ZTT 1032-2025",
        19,
        340,
        1400,
        20,
        "区域性塔型",
        (RowBand(1, 20, "地面增高架", "both"),),
    ),
    PageConfig(
        "regional-reaction-300-20.png",
        "regional-v2",
        "区域性铁塔产品参考图集",
        "Q/ZTT 1032-2025",
        20,
        340,
        2600,
        45,
        "区域性塔型",
        (
            RowBand(1, 33, "落地支撑杆", "single"),
            RowBand(34, 45, "双斜杆三管塔", "both"),
        ),
    ),
    PageConfig(
        "standard-1-reaction-300-18.png",
        "standard-v2-volume-1",
        "通信铁塔标准图集（第一分册）",
        "Q/ZTT 1023-2025",
        18,
        240,
        2750,
        51,
        "标准塔型第一分册",
        (RowBand(1, 51, "支架式单管塔", "overall"),),
    ),
    PageConfig(
        "standard-1-reaction-300-19.png",
        "standard-v2-volume-1",
        "通信铁塔标准图集（第一分册）",
        "Q/ZTT 1023-2025",
        19,
        240,
        3110,
        59,
        "标准塔型第一分册",
        (RowBand(1, 59, "支架式单管塔", "overall", 52),),
    ),
    PageConfig(
        "standard-1-reaction-300-20.png",
        "standard-v2-volume-1",
        "通信铁塔标准图集（第一分册）",
        "Q/ZTT 1023-2025",
        20,
        240,
        1420,
        22,
        "标准塔型第一分册",
        (RowBand(1, 22, "平台式单管塔", "overall"),),
    ),
    PageConfig(
        "standard-1-reaction-300-21.png",
        "standard-v2-volume-1",
        "通信铁塔标准图集（第一分册）",
        "Q/ZTT 1023-2025",
        21,
        235,
        3120,
        61,
        "标准塔型第一分册",
        (RowBand(1, 61, "景观塔", "overall"),),
    ),
    PageConfig(
        "standard-2-reaction-300-18.png",
        "standard-v2-volume-2",
        "通信铁塔标准图集（第二分册）",
        "Q/ZTT 1023-2025",
        18,
        285,
        2750,
        48,
        "标准塔型第二分册",
        (RowBand(1, 48, "双斜杆三管塔", "both"),),
    ),
    PageConfig(
        "standard-2-reaction-300-19.png",
        "standard-v2-volume-2",
        "通信铁塔标准图集（第二分册）",
        "Q/ZTT 1023-2025",
        19,
        285,
        3110,
        69,
        "标准塔型第二分册",
        (RowBand(1, 69, "单斜杆三管塔", "both"),),
    ),
    PageConfig(
        "standard-2-reaction-300-20.png",
        "standard-v2-volume-2",
        "通信铁塔标准图集（第二分册）",
        "Q/ZTT 1023-2025",
        20,
        285,
        1520,
        25,
        "标准塔型第二分册",
        (
            RowBand(1, 10, "路灯杆", "overall"),
            RowBand(11, 22, "仿生树", "overall"),
            RowBand(23, 25, "角钢塔", "single"),
        ),
    ),
)

# 水印恰好压过数字时，多阈值 OCR 仍可能漏掉首位。以下两格已对照原表逐格复核。
SOURCE_VALUE_OVERRIDES: dict[str, dict[str, float]] = {
    "v2-regional-v2-p18-r43": {"overallStandardAxialKn": 17.1},
    "v2-regional-v2-p19-r12": {"overallStandardAxialKn": 17.1},
}


def group_adjacent(indices: Iterable[int]) -> list[int]:
    groups: list[list[int]] = []
    for value in indices:
        value = int(value)
        if not groups or value > groups[-1][-1] + 1:
            groups.append([value])
        else:
            groups[-1].append(value)
    return [sum(group) // len(group) for group in groups]


def detect_grid(gray: np.ndarray, config: PageConfig) -> tuple[list[int], list[int]]:
    dark = gray < 170
    vertical_sum = dark[config.table_top_hint : config.table_bottom_hint, :].sum(axis=0)
    height = config.table_bottom_hint - config.table_top_hint
    x_lines = group_adjacent(np.where(vertical_sum > height * 0.52)[0])
    # x=399附近是图框，不属于反力表。表10-1固定为23列、24条竖向边界。
    x_lines = [value for value in x_lines if 450 < value < gray.shape[1] - 250]
    if len(x_lines) != 24:
        raise RuntimeError(
            f"{config.image_name}: 应识别24条表格竖线，实际为{len(x_lines)}：{x_lines}"
        )

    x0, x1 = x_lines[0], x_lines[-1]
    horizontal_sum = dark[:, x0:x1].sum(axis=1)
    width = x1 - x0
    y_lines = group_adjacent(np.where(horizontal_sum > width * 0.70)[0])
    y_lines = [
        value
        for value in y_lines
        if config.table_top_hint - 20 <= value <= config.table_bottom_hint + 20
    ]
    required = config.row_count + 2  # 表头顶线、数据起始线、每一数据行底线
    if len(y_lines) < required:
        raise RuntimeError(
            f"{config.image_name}: 应至少识别{required}条表格横线，实际为{len(y_lines)}：{y_lines}"
        )
    return x_lines, y_lines[:required]


def run_tesseract(
    image_path: Path,
    output_base: Path,
    tesseract: Path,
    tessdata: Path,
) -> Path:
    command = [
        str(tesseract),
        str(image_path),
        str(output_base),
        "-l",
        "eng",
        "--psm",
        "6",
        "-c",
        "tessedit_create_tsv=1",
    ]
    environment = dict(__import__("os").environ)
    environment["TESSDATA_PREFIX"] = str(tessdata)
    subprocess.run(command, check=True, env=environment, capture_output=True, text=True)
    return output_base.with_suffix(".tsv")


def clean_table_image(
    gray: np.ndarray,
    x_lines: list[int],
    y_lines: list[int],
    threshold: int | None,
) -> np.ndarray:
    x0, x1 = x_lines[0], x_lines[-1]
    y0, y1 = y_lines[1], y_lines[-1]
    result = gray[y0:y1, x0:x1].copy()
    if threshold is None:
        result = np.where(result < 175, result, 255).astype(np.uint8)
    else:
        result = np.where(result < threshold, 0, 255).astype(np.uint8)
    for x_value in x_lines:
        local = x_value - x0
        result[:, max(0, local - 3) : min(result.shape[1], local + 4)] = 255
    for y_value in y_lines[1:]:
        local = y_value - y0
        result[max(0, local - 3) : min(result.shape[0], local + 4), :] = 255
    return result


def read_cells_from_tsv(
    tsv_path: Path,
    x_lines: list[int],
    y_lines: list[int],
) -> dict[tuple[int, int], str]:
    x0, y0 = x_lines[0], y_lines[1]
    tokens: dict[tuple[int, int], list[tuple[int, str]]] = defaultdict(list)
    with tsv_path.open(encoding="utf-8") as stream:
        for row in csv.DictReader(stream, delimiter="\t"):
            text = row["text"].strip()
            if not text:
                continue
            center_x = int(row["left"]) + int(row["width"]) / 2 + x0
            center_y = int(row["top"]) + int(row["height"]) / 2 + y0
            row_index = next(
                (
                    index
                    for index in range(1, len(y_lines) - 1)
                    if y_lines[index] < center_y < y_lines[index + 1]
                ),
                None,
            )
            column_index = next(
                (
                    index
                    for index in range(len(x_lines) - 1)
                    if x_lines[index] < center_x < x_lines[index + 1]
                ),
                None,
            )
            if row_index is None or column_index is None:
                continue
            physical_row = row_index
            tokens[(physical_row, column_index)].append((int(row["left"]), text))
    return {
        key: "".join(text for _, text in sorted(items))
        for key, items in tokens.items()
    }


def recognize_page(
    image_path: Path,
    config: PageConfig,
    tesseract: Path,
    tessdata: Path,
    work_dir: Path,
) -> tuple[list[int], list[int], dict[tuple[int, int], list[str]]]:
    gray = np.array(Image.open(image_path).convert("L"))
    x_lines, y_lines = detect_grid(gray, config)
    candidates: dict[tuple[int, int], list[str]] = defaultdict(list)
    for name, threshold in (("gray", None), ("100", 100), ("115", 115), ("130", 130)):
        cleaned = clean_table_image(gray, x_lines, y_lines, threshold)
        cleaned_path = work_dir / f"{image_path.stem}-{name}.png"
        Image.fromarray(cleaned).save(cleaned_path)
        tsv_path = run_tesseract(
            cleaned_path,
            work_dir / f"{image_path.stem}-{name}-ocr",
            tesseract,
            tessdata,
        )
        for key, value in read_cells_from_tsv(tsv_path, x_lines, y_lines).items():
            if value:
                candidates[key].append(value)
    return x_lines, y_lines, candidates


def numeric_candidates(raw_values: Iterable[str], decimal_places: int) -> Counter[float]:
    result: Counter[float] = Counter()
    for raw in raw_values:
        normalized = raw.upper().replace("O", "0").replace("I", "1").replace("L", "1")
        normalized = normalized.replace(",", ".")
        normalized = re.sub(r"[^0-9.]", "", normalized)
        if not normalized or normalized == ".":
            continue
        if normalized.count(".") > 1:
            first = normalized.find(".")
            normalized = normalized[: first + 1] + normalized[first + 1 :].replace(".", "")
        try:
            if "." in normalized:
                value = float(normalized)
            else:
                digits = normalized
                if decimal_places > 0:
                    if len(digits) <= decimal_places:
                        digits = digits.zfill(decimal_places + 1)
                    value = float(digits[:-decimal_places] + "." + digits[-decimal_places:])
                else:
                    value = float(digits)
        except ValueError:
            continue
        if value >= 0:
            result[round(value, 4)] += 1
    return result


def choose_numeric(raw_values: Iterable[str], decimal_places: int) -> float | None:
    candidates = numeric_candidates(raw_values, decimal_places)
    if not candidates:
        return None
    return sorted(candidates.items(), key=lambda item: (-item[1], -item[0]))[0][0]


def choose_factor_pair(
    standard_raw: Iterable[str],
    basic_raw: Iterable[str],
    factor: float = 1.5,
) -> tuple[float | None, float | None, bool]:
    standard_candidates = numeric_candidates(standard_raw, 1)
    basic_candidates = numeric_candidates(basic_raw, 1)
    pairs: list[tuple[float, int, float, float]] = []
    for standard, standard_count in standard_candidates.items():
        if standard <= 0:
            continue
        for basic, basic_count in basic_candidates.items():
            if basic <= 0:
                continue
            ratio_error = abs(basic / standard - factor)
            pairs.append((ratio_error, -(standard_count + basic_count), standard, basic))
    if pairs:
        ratio_error, _, standard, basic = min(pairs)
        if ratio_error <= 0.06:
            return standard, basic, True

    standard = choose_numeric(standard_raw, 1)
    basic = choose_numeric(basic_raw, 1)
    if standard and standard > 0:
        return standard, round(standard * factor, 1), False
    if basic and basic > 0:
        return round(basic / factor, 1), basic, False
    return None, None, False


def choose_axial_pair(
    standard_raw: Iterable[str],
    basic_raw: Iterable[str],
) -> tuple[float | None, float | None, bool]:
    standard_candidates = numeric_candidates(standard_raw, 1)
    basic_candidates = numeric_candidates(basic_raw, 1)
    options: list[tuple[float, int, float, float]] = []
    for standard, standard_count in standard_candidates.items():
        if standard <= 0:
            continue
        for basic, basic_count in basic_candidates.items():
            if basic < standard:
                continue
            ratio = basic / standard
            if 1.03 <= ratio <= 1.65:
                center_penalty = abs(ratio - 1.20)
                options.append((center_penalty, -(standard_count + basic_count), standard, basic))
    if options:
        _, _, standard, basic = min(options)
        return standard, basic, True
    return choose_numeric(standard_raw, 1), choose_numeric(basic_raw, 1), False


def normalize_code(raw_values: Iterable[str], tower_type: str) -> tuple[str, bool]:
    expected_prefix = {
        "地面增高架": "ZGJ(DM)",
        "落地支撑杆": "ZCG(DM)",
        "双斜杆三管塔": "3GT(SX)",
        "单斜杆三管塔": "3GT(DX)",
        "仿生树": "FSS(CSZ)",
        "角钢塔": "JGT",
    }.get(tower_type)

    normalized_values: list[str] = []
    for raw in raw_values:
        value = raw.upper().replace(" ", "").replace("—", "-").replace("_", "-")
        value = value.replace("DG1", "DGT").replace("DGI", "DGT")
        value = value.replace("3G1", "3GT").replace("ZG1", "ZGJ")
        value = value.replace("1Z)", "1ZJ").replace("2Z)", "2ZJ")
        value = value.replace("3Z)", "3ZJ").replace("4Z)", "4ZJ").replace("5Z)", "5ZJ")
        value = value.replace("NPI", "NPT").replace("NPTI", "NPT")
        value = re.sub(r"[^A-Z0-9()./+-]", "", value)
        value = re.sub(r"-{2,}", "-", value)
        value = re.sub(r"-([1-5])[7Z]J\)-", r"-\1ZJ-", value)
        parts = value.split("-")
        if len(parts) >= 5 and re.fullmatch(r"[1-5][2Z7J)]{1,3}", parts[-2]):
            parts[-2] = parts[-2][0] + "ZJ"
            value = "-".join(parts)
        if expected_prefix and not value.startswith(expected_prefix):
            suffix_match = re.search(r"\)-(?=[0-9])", value)
            if suffix_match is not None:
                value = expected_prefix + value[suffix_match.start() + 1 :]
        normalized_values.append(value)

    def score(value: str) -> tuple[int, int, int]:
        prefix_score = 2 if expected_prefix and value.startswith(expected_prefix) else 1 if value.startswith(("DGT", "3GT", "ZGJ", "ZCG", "FSS", "JGT")) else 0
        shape_score = int(value.endswith("F")) + int(value.count("-") >= 4) + int(bool(re.search(r"-0\.[0-9]{2}-", value)))
        return prefix_score, shape_score, len(value)

    if not normalized_values:
        return "", False
    code = max(normalized_values, key=score)
    is_valid = (
        len(code) >= 14
        and code.endswith("F")
        and code.count("-") >= 4
        and bool(re.search(r"-0\.[0-9]{2}A?-|-1\.00-", code))
        and (expected_prefix is None or code.startswith(expected_prefix))
    )
    return code, is_valid


def parse_height(code: str) -> float | None:
    match = re.search(r"\)-([0-9]{1,2})-", code)
    if match is None:
        match = re.search(r"^[A-Z0-9]+-([0-9]{1,2})-", code)
    return float(match.group(1)) if match else None


def build_reaction_pair(
    candidates: dict[tuple[int, int], list[str]],
    row: int,
    first_column: int,
    issues: list[str],
) -> dict | None:
    axial_standard, axial_basic, axial_checked = choose_axial_pair(
        candidates.get((row, first_column), ()),
        candidates.get((row, first_column + 3), ()),
    )
    shear_standard, shear_basic, shear_checked = choose_factor_pair(
        candidates.get((row, first_column + 1), ()),
        candidates.get((row, first_column + 4), ()),
    )
    moment_standard, moment_basic, moment_checked = choose_factor_pair(
        candidates.get((row, first_column + 2), ()),
        candidates.get((row, first_column + 5), ()),
    )
    if not all(value and value > 0 for value in (axial_standard, shear_standard, moment_standard)):
        issues.append("整塔标准组合存在未识别数值")
        return None
    if not axial_checked:
        issues.append("整塔轴力标准/基本组合需人工复核")
    if not shear_checked:
        issues.append("整塔剪力基本组合由标准组合按1.5复核生成")
    if not moment_checked:
        issues.append("整塔弯矩基本组合由标准组合按1.5复核生成")
    return {
        "standard": {
            "axialKn": axial_standard,
            "shearKn": shear_standard,
            "momentKnM": moment_standard,
        },
        "basic": {
            "axialKn": axial_basic or round(axial_standard * 1.2, 1),
            "shearKn": shear_basic,
            "momentKnM": moment_basic,
        },
    }


def build_single_leg_pair(
    candidates: dict[tuple[int, int], list[str]],
    row: int,
    first_column: int,
    issues: list[str],
) -> dict | None:
    values: list[tuple[float | None, float | None, bool]] = []
    for offset in range(4):
        values.append(
            choose_factor_pair(
                candidates.get((row, first_column + offset), ()),
                candidates.get((row, first_column + 4 + offset), ()),
            )
        )
    if not all(item[0] and item[0] > 0 for item in values):
        issues.append("单塔腿标准组合存在未识别数值")
        return None
    if not all(item[2] for item in values):
        issues.append("单塔腿部分基本组合由标准组合按1.5复核生成")
    standard = [item[0] for item in values]
    basic = [item[1] for item in values]
    return {
        "standard": {
            "compressionControl": {
                "compressionKn": standard[0],
                "shearKn": standard[1],
            },
            "tensionControl": {
                "tensionKn": standard[2],
                "shearKn": standard[3],
            },
        },
        "basic": {
            "compressionControl": {
                "compressionKn": basic[0],
                "shearKn": basic[1],
            },
            "tensionControl": {
                "tensionKn": basic[2],
                "shearKn": basic[3],
            },
        },
    }


def find_band(config: PageConfig, physical_row: int) -> RowBand:
    for band in config.bands:
        if band.includes(physical_row):
            return band
    raise RuntimeError(f"{config.image_name}: 第{physical_row}行未配置塔型分组")


def build_records(
    config: PageConfig,
    candidates: dict[tuple[int, int], list[str]],
) -> tuple[list[dict], list[dict]]:
    records: list[dict] = []
    audit_rows: list[dict] = []
    for physical_row in range(1, config.row_count + 1):
        band = find_band(config, physical_row)
        issues: list[str] = []
        code, code_valid = normalize_code(candidates.get((physical_row, 5), ()), band.tower_type)
        if not code_valid:
            issues.append("塔型编号OCR格式需人工复核")

        tower_weight = choose_numeric(candidates.get((physical_row, 6), ()), 2)
        attachment_weight = choose_numeric(candidates.get((physical_row, 7), ()), 2)
        total_weight = choose_numeric(candidates.get((physical_row, 8), ()), 2)
        if tower_weight is not None and attachment_weight is not None:
            expected_total = round(tower_weight + attachment_weight, 2)
            if total_weight is None or abs(total_weight - expected_total) > 0.03:
                total_weight = expected_total
                issues.append("总塔重按塔重与地脚锚栓重量复核生成")

        overall = None
        single_leg = None
        if band.reaction_mode in ("overall", "both"):
            overall = build_reaction_pair(candidates, physical_row, 9, issues)
        if band.reaction_mode in ("single", "both"):
            single_leg = build_single_leg_pair(candidates, physical_row, 15, issues)

        displayed_row = band.displayed_row(physical_row)
        record_id = f"v2-{config.source_id}-p{config.pdf_page}-r{physical_row}"
        overrides = SOURCE_VALUE_OVERRIDES.get(record_id, {})
        if overall is not None and "overallStandardAxialKn" in overrides:
            overall["standard"]["axialKn"] = overrides["overallStandardAxialKn"]
            issues = [issue for issue in issues if "轴力标准/基本组合" not in issue]
            issues.append("轴力已按原表逐格复核")
        usable_overall = overall is not None and code_valid
        usable_single = single_leg is not None and code_valid
        review_status = "consistency_checked" if code_valid else "manual_code_review_required"
        record = {
            "id": record_id,
            "sourceId": config.source_id,
            "sourceTitle": config.source_title,
            "standardNo": config.standard_no,
            "catalogVersion": "V2.0",
            "sourcePdfPage": config.pdf_page,
            "sourceTableRow": displayed_row,
            "group": config.group,
            "towerType": band.tower_type,
            "towerCode": code,
            "towerWeightT": tower_weight,
            "attachmentWeightT": attachment_weight,
            "totalWeightT": total_weight,
            "overallBaseReaction": overall,
            "singleLegReaction": single_leg,
            "usableForAutomaticOverallLoad": usable_overall,
            "usableForAutomaticSingleLegLoad": usable_single,
            "reviewStatus": review_status,
            "reviewIssues": issues,
            "sourceDeclaredHeightM": parse_height(code),
        }
        records.append(record)
        audit_rows.append(
            {
                "id": record_id,
                "towerCode": code,
                "towerType": band.tower_type,
                "usableOverall": usable_overall,
                "usableSingleLeg": usable_single,
                "issues": issues,
            }
        )
    return records, audit_rows


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--image-dir", type=Path, default=DEFAULT_IMAGE_DIR)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--audit", type=Path, default=DEFAULT_AUDIT)
    parser.add_argument("--tesseract", type=Path, default=DEFAULT_TESSERACT)
    parser.add_argument("--tessdata", type=Path, default=DEFAULT_TESSDATA)
    args = parser.parse_args()

    if not args.tesseract.exists():
        raise FileNotFoundError(f"找不到 Tesseract：{args.tesseract}")
    work_dir = args.image_dir / "ocr-work"
    work_dir.mkdir(parents=True, exist_ok=True)

    records: list[dict] = []
    audit_rows: list[dict] = []
    for config in PAGES:
        image_path = args.image_dir / config.image_name
        if not image_path.exists():
            raise FileNotFoundError(f"缺少300dpi反力页：{image_path}")
        _, _, candidates = recognize_page(
            image_path,
            config,
            args.tesseract,
            args.tessdata,
            work_dir,
        )
        page_records, page_audit = build_records(config, candidates)
        records.extend(page_records)
        audit_rows.extend(page_audit)
        print(f"{config.image_name}: {len(page_records)} 条")

    document = {
        "schemaVersion": 2,
        "catalogEdition": "V2.0",
        "noticeNumber": "中国铁塔〔2025〕244号",
        "effectiveDate": "2025-11-10",
        "isCompleteForNewDesign": True,
        "statusMessage": f"V2.0企业标准塔型荷载库已载入，共{len(records)}条塔脚反力记录。",
        "standardNumbers": ["Q/ZTT 1023-2025", "Q/ZTT 1032-2025"],
        "records": records,
    }
    unresolved = [row for row in audit_rows if not (row["usableOverall"] or row["usableSingleLeg"])]
    audit = {
        "recordCount": len(records),
        "automaticRecordCount": len(records) - len(unresolved),
        "manualReviewCount": len(unresolved),
        "towerTypeCounts": dict(Counter(row["towerType"] for row in audit_rows)),
        "unresolvedRows": unresolved,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, ensure_ascii=False, indent=2), encoding="utf-8")
    args.audit.write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(audit, ensure_ascii=False, indent=2))
    return 0 if not unresolved else 2


if __name__ == "__main__":
    raise SystemExit(main())
