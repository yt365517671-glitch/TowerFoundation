#!/usr/bin/env python3
"""把企业标准图集荷载表 OCR 候选整理为可审查的结构化荷载库。

本脚本不把 OCR 当作最终事实：
1. 保留每个单元格的原始文本和置信度；
2. 利用塔重合计、标准/基本组合系数做一致性修复；
3. 任何无法通过一致性检查的记录均标为 needs_review，禁止自动套用。
"""

from __future__ import annotations

import csv
import json
import math
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable


ROOT = Path(__file__).resolve().parents[1]
OCR_DIR = ROOT / "tmp" / "pdfs" / "load-library" / "ocr"
OUTPUT_DIR = ROOT / "标准图集荷载库" / "提取结果"


@dataclass(frozen=True)
class SourceSpec:
    source_id: str
    title: str
    standard_no: str
    version: str
    page: int
    first_row: int
    cell_file: str
    axial_factor: float
    lateral_factor: float
    moment_factor: float
    category_ranges: tuple[tuple[int, int, str, str], ...]


SPECS = (
    SourceSpec(
        "lowcost", "低成本铁塔标准图集", "Q/ZTT 1030-2021", "V2.0", 12, 1,
        "lowcost-page12-cells.json", 1.3, 1.5, 1.5,
        ((1, 34, "地面塔", "外爬支架式单管塔"), (35, 50, "地面塔", "单柱三管塔"))),
    SourceSpec(
        "lowcost", "低成本铁塔标准图集", "Q/ZTT 1030-2021", "V2.0", 13, 51,
        "low13-cells.json", 1.3, 1.5, 1.5,
        ((51, 62, "地面塔", "单柱三管塔"), (63, 74, "地面塔", "屋顶拉线塔"),
         (75, 78, "屋面塔", "屋面支撑杆"), (79, 86, "屋面塔", "屋面增高架"),
         (87, 102, "地面塔", "高铁沿线单管塔"))),
    SourceSpec(
        "lowcost", "低成本铁塔标准图集", "Q/ZTT 1030-2021", "V2.0", 14, 103,
        "low14-cells.json", 1.3, 1.5, 1.5,
        ((103, 118, "地面塔", "高铁沿线单柱三管塔"), (119, 130, "地面塔", "景观塔"))),
    SourceSpec(
        "communication", "通信铁塔标准图集", "Q/ZTT 1023-2016", "V1.3", 12, 1,
        "comm12-cells.json", 1.2, 1.4, 1.4,
        ((1, 26, "地面塔", "外爬支架式单管塔"), (27, 33, "地面塔", "插接式单管塔（增高平台）"),
         (34, 48, "地面塔", "插接式单管塔（普通平台）"))),
    SourceSpec(
        "communication", "通信铁塔标准图集", "Q/ZTT 1023-2016", "V1.3", 13, 49,
        "comm13-cells.json", 1.2, 1.4, 1.4,
        ((49, 67, "地面塔", "插接式单管塔"), (68, 101, "地面塔", "三管塔"))),
    SourceSpec(
        "communication", "通信铁塔标准图集", "Q/ZTT 1023-2016", "V1.3", 14, 102,
        "comm14a-cells.json", 1.2, 1.4, 1.4,
        ((102, 104, "地面塔", "角钢塔"), (105, 108, "地面塔", "落地增高架"))),
    SourceSpec(
        "camouflage", "美化塔标准图集", "Q/ZTT 1024-2017", "V1.1", 9, 1,
        "camo9-cells.json", 1.2, 1.4, 1.4,
        ((1, 20, "美化塔", "路灯杆塔"), (21, 28, "美化塔", "仿生树"),
         (29, 32, "美化塔", "灯杆/花瓣景观塔"), (33, 48, "美化塔", "灯杆/花瓣插接景观塔"),
         (49, 53, "美化塔", "双轮/风帆景观塔"))),
    SourceSpec(
        "camouflage", "美化塔标准图集", "Q/ZTT 1024-2017", "V1.1", 10, 54,
        "camo10a-cells.json", 1.2, 1.4, 1.4,
        ((54, 77, "美化塔", "双轮/风帆插接景观塔"),)),
    SourceSpec(
        "camouflage", "美化塔标准图集", "Q/ZTT 1024-2017", "V1.1", 10, 78,
        "camo10b-cells.json", 1.2, 1.4, 1.4,
        ((78, 78, "创新美化塔", "三管塔"), (79, 83, "创新美化塔", "路灯杆塔"),
         (84, 86, "创新美化塔", "多功能杆"))),
)


UNAVAILABLE = (
    # 低成本图集：仅做杆塔/拉线或屋面结构设计，不含基础端荷载。
    ("lowcost", 13, 63,  "LXWG(DM/PF)-12-0.45-1ZJ-3F", "屋顶拉线塔仅含杆塔设计，不含拉线、基础设计"),
    ("lowcost", 13, 64,  "LXWG(DM/PF)-12-0.75-1ZJ-3F", "屋顶拉线塔仅含杆塔设计，不含拉线、基础设计"),
    ("lowcost", 13, 65,  "LXWG(DM/PF)-15-0.45-1ZJ-3F", "屋顶拉线塔仅含杆塔设计，不含拉线、基础设计"),
    ("lowcost", 13, 66,  "LXWG(DM/PF)-15-0.75-1ZJ-3F", "屋顶拉线塔仅含杆塔设计，不含拉线、基础设计"),
    ("lowcost", 13, 67,  "LXWG(DM/PF)-18-0.45-1ZJ-3F", "屋顶拉线塔仅含杆塔设计，不含拉线、基础设计"),
    ("lowcost", 13, 68,  "LXWG(DM/PF)-18-0.75-1ZJ-3F", "屋顶拉线塔仅含杆塔设计，不含拉线、基础设计"),
    ("lowcost", 13, 69,  "LXWG(DM/PF)-12-0.45-2ZJ-6F", "屋顶拉线塔仅含杆塔设计，不含拉线、基础设计"),
    ("lowcost", 13, 70,  "LXWG(DM/PF)-12-0.75-2ZJ-6F", "屋顶拉线塔仅含杆塔设计，不含拉线、基础设计"),
    ("lowcost", 13, 71,  "LXWG(DM/PF)-15-0.45-2ZJ-6F", "屋顶拉线塔仅含杆塔设计，不含拉线、基础设计"),
    ("lowcost", 13, 72,  "LXWG(DM/PF)-15-0.75-2ZJ-6F", "屋顶拉线塔仅含杆塔设计，不含拉线、基础设计"),
    ("lowcost", 13, 73,  "LXWG(DM/PF)-18-0.45-2ZJ-6F", "屋顶拉线塔仅含杆塔设计，不含拉线、基础设计"),
    ("lowcost", 13, 74,  "LXWG(DM/PF)-18-0.75-2ZJ-6F", "屋顶拉线塔仅含杆塔设计，不含拉线、基础设计"),
    ("lowcost", 14, 131, "BG(WMFQ/DZ)-3-0.65-1ZJ-2F", "屋面塔桅仅含杆件设计，屋面及连接结构另行设计"),
    ("lowcost", 14, 132, "BG(WMFQ/DZ)-3-0.65-1ZJ-3F", "屋面塔桅仅含杆件设计，屋面及连接结构另行设计"),
    ("lowcost", 14, 133, "BG(WMPZ/DZ)-3-0.45-1ZJ-2F", "屋面塔桅仅含杆件设计，屋面及连接结构另行设计"),
    ("lowcost", 14, 134, "BG(WMPZ/DZ)-3-0.45-1ZJ-3F", "屋面塔桅仅含杆件设计，屋面及连接结构另行设计"),
    ("lowcost", 14, 135, "BG(WMPZ/DZ)-3-0.65-1ZJ-2F", "屋面塔桅仅含杆件设计，屋面及连接结构另行设计"),
    ("lowcost", 14, 136, "BG(WMPZ/DZ)-3-0.65-1ZJ-3F", "屋面塔桅仅含杆件设计，屋面及连接结构另行设计"),
    ("lowcost", 14, 137, "BG(WMZL/LH)-3-0.65-1ZJ-6F", "屋面独立式抱杆仅含杆件设计，屋面及连接结构另行设计"),
    # 通信铁塔图集：表中明确写明不含基础或需另行设计。
    ("communication", 14, 109, "LXWG(WM)-15-0.45-2ZJ", "屋顶拉线桅杆不含拉线、屋顶拉点及杆身基础设计"),
    ("communication", 14, 110, "LXWG(WM)-15-0.65-2ZJ", "屋顶拉线桅杆不含拉线、屋顶拉点及杆身基础设计"),
    ("communication", 14, 111, "ZGJ(WM)-9-0.45-2ZJ", "屋顶增高架不含拉线点及塔身基础设计"),
    ("communication", 14, 112, "ZGJ(WM)-9-0.65-2ZJ", "屋顶增高架不含拉线点及塔身基础设计"),
    ("communication", 14, 113, "ZGJ(WM)-12-0.45-3ZJ", "屋顶增高架不含拉线点及塔身基础设计"),
    ("communication", 14, 114, "ZGJ(WM)-12-0.65-3ZJ", "屋顶增高架不含拉线点及塔身基础设计"),
    ("communication", 14, 115, "ZGJ(WM)-15-0.45-3ZJ", "屋顶增高架不含拉线点及塔身基础设计"),
    ("communication", 14, 116, "ZGJ(WM)-15-0.65-3ZJ", "屋顶增高架不含拉线点及塔身基础设计"),
    ("communication", 14, 117, "BG(WM)-3-0.65-1F", "屋顶女儿墙抱杆仅含杆件设计，连接与屋面结构另行设计"),
    ("communication", 14, 118, "BG(WMZL)-3-0.65-1F", "屋顶自立式抱杆仅含杆件设计，连接与屋面结构另行设计"),
    ("communication", 14, 119, "BG(WMZL)-6-0.65-1F", "屋顶自立式抱杆仅含杆件设计，连接与屋面结构另行设计"),
)


THREE_TUBE_ROWS = (
    (1, "3GT(DX)-30-0.35-3NPT3", 5.22, .43, 5.64, 55.2, 50.5, 1063.4, 66.2, 70.7, 1488.8, 390.2, 28.2, 387.3, 29.7, 541.9, 39.2, 546.6, 41.8),
    (2, "3GT(DX)-30-0.45-3NPT3", 5.62, .43, 6.05, 56.2, 60.7, 1273.4, 67.44, 85.0, 1782.7, 510.0, 38.8, 470.2, 38.8, 710.8, 54.3, 664.8, 54.3),
    (3, "3GT(DX)-30-0.55-3NPT3", 6.09, .53, 6.62, 60.9, 74.5, 1670.8, 73.1, 104.3, 2339.2, 658.5, 45.7, 616.4, 45.7, 911.7, 64.0, 856.8, 64.0),
    (4, "3GT(DX)-30-0.65-3NPT3", 6.63, .53, 7.16, 66.3, 88.0, 1855.1, 79.6, 123.2, 2597.1, 691.5, 54.0, 647.3, 54.0, 963.7, 75.6, 915.1, 75.6),
    (5, "3GT(DX)-30-0.75-3NPT3", 6.92, .61, 7.53, 69.2, 103.6, 2067.7, 83.1, 145.1, 2894.8, 761.6, 67.9, 728.1, 67.9, 1063.0, 95.1, 1022.7, 95.1),
    (6, "3GT(DX)-40-0.35-3NPT3", 7.39, .53, 7.92, 73.9, 63.8, 1724.1, 88.7, 89.3, 2413.7, 475.7, 34.5, 484.5, 37.4, 660.3, 48.0, 689.6, 52.9),
    (7, "3GT(DX)-40-0.45-3NPT3", 8.28, .53, 8.81, 82.8, 75.4, 2073.1, 99.4, 105.6, 2902.3, 644.6, 46.2, 589.5, 46.2, 902.4, 64.7, 825.3, 64.7),
    (8, "3GT(DX)-40-0.55-3NPT3", 8.96, .79, 9.75, 89.6, 111.3, 2881.03, 107.5, 155.8, 4033.44, 822.9, 67.5, 761.3, 67.5, 1152.1, 94.5, 1065.8, 94.5),
    (9, "3GT-30-0.45-3NPT3", 6.13, .52, 6.65, 61.3, 63.2, 1319.9, 73.6, 88.5, 1847.9, 630.0, 43.0, 589.2, 41.3, 882.0, 60.2, 824.9, 57.8),
    (10, "3GT-35-0.45-3NPT3", 7.79, .71, 8.50, 77.9, 77.3, 1825.7, 93.5, 108.2, 2556.0, 759.3, 50.9, 707.3, 48.8, 1063.0, 71.3, 990.2, 68.3),
    (11, "3GT-40-0.45-3NPT3", 9.70, .72, 10.42, 97.0, 89.0, 2365.2, 116.4, 124.6, 3311.3, 872.7, 56.3, 808.1, 53.6, 1221.8, 78.8, 1131.3, 75.0),
)


# 对一致性筛查未能可靠判定的行，依据 400 DPI 原表逐项复核后的覆盖值。
# overall 顺序：标准 N/H/M，基本 N/H/M；singleLeg 顺序与 JSON 结构中的八列一致。
MANUAL_OVERRIDES: dict[str, dict[str, Any]] = {
    "camouflage-p9-r2":  {"weights": [.60, .14, .74], "overall": [7.0, 9.1, 68.6, 8.4, 12.8, 96.0]},
    "camouflage-p9-r7":  {"weights": [1.55, .24, 1.79], "overall": [18.3, 16.5, 187.1, 22.0, 23.1, 262.0]},
    "camouflage-p9-r9":  {"weights": [1.38, .20, 1.58], "overall": [18.4, 14.7, 157.1, 22.1, 20.6, 219.9]},
    "camouflage-p9-r11": {"weights": [2.10, .25, 2.35], "overall": [26.4, 18.7, 269.0, 31.7, 26.2, 376.6]},
    "camouflage-p9-r12": {"weights": [2.50, .37, 2.87], "overall": [30.4, 28.6, 408.0, 36.5, 40.0, 571.2]},
    "camouflage-p9-r18": {"weights": [3.88, .48, 4.36], "overall": [44.2, 36.0, 656.0, 53.1, 50.4, 918.4]},
    "camouflage-p9-r19": {"weights": [4.11, .60, 4.71], "overall": [46.5, 42.4, 763.0, 55.8, 59.4, 1068.2]},
    "camouflage-p9-r25": {"weights": [9.62, .90, 10.52], "overall": [96.20, 71.1, 1524.1, 115.44, 99.5, 2133.7]},
    "camouflage-p9-r27": {"weights": [11.59, 1.22, 12.81], "overall": [115.90, 116.2, 2474.6, 139.08, 162.3, 3464.4]},
    "camouflage-p9-r30": {"weights": [9.84, .85, 10.69], "overall": [108.0, 54.7, 1420.0, 129.6, 76.6, 1988.0]},
    "camouflage-p9-r36": {"weights": [5.19, .54, 5.73], "overall": [60.5, 40.8, 753.0, 72.6, 57.1, 1054.2]},
    "camouflage-p9-r37": {"weights": [5.93, .55, 6.48], "overall": [72.2, 30.6, 671.0, 86.6, 42.8, 939.4]},
    "camouflage-p9-r43": {"weights": [9.32, .93, 10.25], "overall": [106.1, 62.7, 1567.0, 127.3, 87.8, 2193.8]},
    "camouflage-p10-r55": {"weights": [13.32, 1.35, 14.67], "overall": [142.2, 77.1, 2135.0, 170.7, 108.0, 2989.0]},
    "camouflage-p10-r63": {"weights": [8.90, 1.06, 9.96], "overall": [89.0, 87.3, 1750.22, 106.80, 122.21, 2450.30]},
    "camouflage-p10-r65": {"weights": [8.46, .64, 9.10], "overall": [84.60, 46.59, 1104.54, 101.52, 65.23, 1546.36]},
    "camouflage-p10-r68": {"weights": [10.90, 1.17, 12.07], "overall": [109.0, 85.92, 1985.89, 130.80, 120.29, 2780.24]},
    "camouflage-p10-r69": {"weights": [11.14, 1.17, 12.31], "overall": [111.40, 97.98, 2266.03, 133.68, 137.17, 3172.45]},
    "camouflage-p10-r78": {"weights": [10.57, .85, 11.42], "overall": [118.36, 66.48, 1791.26, 142.03, 93.07, 2507.76],
        "singleLeg": [1069.64, 28.00, 998.73, 27.83, 1490.40, 39.18, 1412.40, 38.98]},
    "camouflage-p10-r80": {"weights": [2.27, .28, 2.55], "overall": [32.29, 16.43, 244.66, 38.74, 23.01, 344.35]},
    "camouflage-p10-r85": {"code": "DGN(L)-10-0.45-1ZJ", "weights": [.58, .11, .69], "overall": [7.5, 5.8, 37.8, 9.0, 8.1, 53.0]},
    "camouflage-p10-r86": {"code": "DGN(G)-12-0.45-1JS", "weights": [2.61, .17, 2.78], "overall": [30.0, 10.0, 66.2, 36.0, 14.0, 92.3]},

    "communication-p12-r5":  {"weights": [8.22, 1.12, 9.34], "overall": [93.0, 80.9, 1807.4, 111.6, 113.2, 2530.4]},
    "communication-p12-r8":  {"weights": [5.68, .51, 6.19], "overall": [64.0, 29.1, 713.5, 76.8, 40.8, 998.9]},
    "communication-p12-r18": {"code": "DGT(Z)-40-0.55-4ZJ", "weights": [9.35, .86, 10.21], "overall": [101.6, 58.3, 1605.1, 120.8, 81.6, 2247.1]},
    "communication-p12-r34": {"weights": [7.43, .70, 8.13], "overall": [85.10, 40.4, 904.1, 102.12, 56.6, 1265.7]},
    "communication-p12-r42": {"weights": [12.17, .87, 13.04], "overall": [132.5, 53.5, 1624.4, 159.0, 74.8, 2274.2]},

    "communication-p13-r50": {"weights": [15.64, 1.26, 16.90], "overall": [167.2, 69.4, 2325.6, 200.6, 97.1, 3255.8]},
    "communication-p13-r52": {"weights": [17.26, 1.39, 18.65], "overall": [183.4, 88.1, 2930.4, 220.1, 123.3, 4102.5]},
    "communication-p13-r55": {"weights": [19.39, 1.75, 21.14], "overall": [204.7, 116.2, 3847.4, 245.6, 162.7, 5386.4]},
    "communication-p13-r63": {"weights": [20.02, 1.75, 21.77], "overall": [214.6, 134.4, 4248.3, 257.5, 188.0, 5947.2]},
    "communication-p13-r64": {"weights": [19.21, 1.27, 20.48], "overall": [206.5, 77.7, 2776.2, 247.7, 109.2, 3887.1]},
    "communication-p13-r73": {"weights": [8.76, .61, 9.37], "overall": [87.6, 90.3, 1920.9, 105.1, 126.4, 2689.3]},
    "communication-p13-r75": {"weights": [9.38, .72, 10.10], "overall": [93.8, 108.2, 2289.7, 112.6, 151.4, 3205.6]},
    "communication-p13-r76": {"weights": [8.71, .53, 9.24], "overall": [87.1, 64.1, 1592.1, 104.5, 89.7, 2228.9]},
    "communication-p13-r77": {"weights": [10.19, .72, 10.91], "overall": [101.9, 83.0, 2073.1, 122.3, 116.1, 2902.3]},
    "communication-p13-r78": {"weights": [10.58, .72, 11.30], "overall": [105.8, 102.9, 2552.6, 127.0, 144.1, 3573.6]},
    "communication-p13-r81": {"weights": [11.16, .61, 11.77], "overall": [111.6, 76.7, 2228.2, 133.9, 107.3, 3119.6]},
    "communication-p13-r82": {"weights": [11.83, .72, 12.55], "overall": [118.3, 86.1, 2485.3, 142.0, 120.5, 3479.5]},
    "communication-p13-r83": {"weights": [12.41, .72, 13.13], "overall": [124.1, 106.1, 3038.4, 148.9, 148.5, 4253.8]},
    "communication-p13-r86": {"weights": [12.00, .61, 12.61], "overall": [120.0, 73.5, 2386.5, 144.0, 102.9, 3341.1]},
    "communication-p13-r89": {"weights": [15.90, 1.02, 16.92], "overall": [159.0, 142.8, 4540.9, 190.8, 200.0, 6357.3]},
    "communication-p13-r90": {"weights": [10.01, .53, 10.54], "overall": [100.1, 65.1, 1815.5, 120.1, 91.1, 2541.7]},
    "communication-p13-r92": {"weights": [11.80, .72, 12.52], "overall": [118.0, 105.0, 2864.8, 141.6, 147.0, 4010.7]},
    "communication-p13-r93": {"weights": [12.63, .85, 13.48], "overall": [126.3, 122.9, 3392.7, 151.6, 172.0, 4749.8]},
    "communication-p13-r94": {"code": "3GT-45-0.35-4PT5", "weights": [12.61, .61, 13.20], "overall": [126.1, 85.1, 2572.3, 151.3, 119.1, 3601.2]},
    "communication-p13-r97": {"code": "3GT-45-0.65-4PT5", "weights": [16.37, 1.02, 17.39], "overall": [163.7, 165.9, 5006.8, 196.4, 232.3, 7009.5]},
    "communication-p13-r100": {"weights": [17.11, 1.02, 18.13], "overall": [171.1, 152.3, 5087.3, 205.3, 213.2, 7122.2]},
    "communication-p13-r101": {"code": "3GT-50-0.65-4PT5", "weights": [18.79, 1.18, 19.97], "overall": [187.9, 188.0, 6279.1, 225.5, 263.1, 8790.8]},

    "communication-p14-r102": {"weights": [20.29, .88, 21.17], "singleLeg": [756.4, 118.2, 639.3, 102.2, 1049.3, 163.9, 906.1, 144.7]},
    "communication-p14-r103": {"weights": [23.56, 1.03, 24.59], "singleLeg": [940.5, 119.7, 807.6, 106.5, 1305.6, 166.3, 1143.3, 150.6]},
    "communication-p14-r104": {"weights": [27.61, 1.30, 28.91], "singleLeg": [1076.9, 131.1, 925.2, 116.5, 1494.8, 182.1, 1309.9, 164.7]},
    "communication-p14-r105": {"code": "ZGJ-12-0.45-2ZJ", "weights": [2.375, .09, 2.462], "overall": [27.00, 21.74, 181.85, 32.40, 30.44, 254.59],
        "singleLeg": [68.82, 12.94, 60.99, 12.28, 94.55, 17.79, 87.19, 17.51]},
    "communication-p14-r106": {"code": "ZGJ-12-0.65-2ZJ", "weights": [2.557, .09, 2.648], "overall": [29.20, 31.27, 261.66, 35.04, 43.78, 366.32],
        "singleLeg": [95.02, 17.89, 91.71, 18.37, 131.22, 24.72, 130.20, 26.04]},
    "communication-p14-r107": {"code": "ZGJ-18-0.45-2ZJ", "weights": [3.081, .09, 3.168], "overall": [33.37, 32.59, 425.44, 40.05, 45.62, 595.61],
        "singleLeg": [152.63, 21.49, 151.21, 21.76, 215.91, 29.69, 209.47, 30.86]},
    "communication-p14-r108": {"code": "ZGJ-18-0.65-2ZJ", "weights": [3.348, .09, 3.440], "overall": [35.01, 45.41, 597.00, 42.01, 63.57, 835.80],
        "singleLeg": [219.78, 29.07, 218.12, 29.35, 307.70, 40.35, 305.36, 41.43]},

    "lowcost-p12-r6":  {"weights": [1.38, .21, 1.60], "overall": [16.2, 18.4, 202.0, 21.1, 27.6, 303.0]},
    "lowcost-p12-r13": {"weights": [2.50, .26, 2.76], "overall": [27.4, 21.9, 378.3, 35.6, 32.6, 564.6]},
    "lowcost-p12-r16": {"weights": [3.23, .32, 3.56], "overall": [34.7, 22.4, 470.5, 45.1, 33.6, 705.8]},
    "lowcost-p12-r19": {"weights": [4.21, .29, 4.50], "overall": [44.5, 22.2, 522.3, 57.8, 33.3, 783.5]},
    "lowcost-p12-r30": {"weights": [3.15, .33, 3.48], "overall": [35.1, 31.4, 561.4, 45.6, 47.2, 842.1]},
    "lowcost-p12-r32": {"weights": [3.86, .35, 4.21], "overall": [42.2, 26.6, 581.1, 54.9, 39.9, 871.6]},
    "lowcost-p12-r35": {"weights": [2.38, .21, 2.59], "overall": [26.2, 20.0, 282.1, 34.1, 29.6, 418.2]},
    "lowcost-p12-r40": {"weights": [3.21, .21, 3.42], "overall": [34.6, 33.3, 554.5, 45.0, 50.0, 832.3]},
    "lowcost-p12-r45": {"code": "3GT(DX/BD)-30-0.55-1NPT-6F", "weights": [4.50, .33, 4.82], "overall": [47.4, 53.4, 1022.9, 61.6, 80.1, 1534.3]},
    "lowcost-p13-r54": {"weights": [3.08, .33, 3.40], "overall": [34.4, 48.3, 653.0, 44.7, 72.5, 979.5]},
    "lowcost-p13-r57": {"weights": [3.66, .33, 3.98], "overall": [40.2, 49.2, 816.4, 52.3, 73.9, 1224.6]},
    "lowcost-p13-r60": {"weights": [4.21, .32, 4.53], "overall": [45.8, 40.9, 834.6, 59.6, 61.4, 1251.2]},
    "lowcost-p13-r61": {"code": "3GT(DX/BD)-30-0.55-2NPT-9F", "weights": [4.67, .41, 5.08], "overall": [50.4, 58.0, 1137.6, 65.5, 87.0, 1706.5]},
    "lowcost-p13-r75": {"weights": [.77, .09, .85], "singleLeg": [32.9, 8.8, 23.1, 8.8, 48.1, 13.2, 34.6, 13.2]},
    "lowcost-p13-r76": {"weights": [.82, .10, .92], "singleLeg": [36.9, 8.8, 34.8, 8.8, 55.3, 13.2, 52.2, 13.2]},
    "lowcost-p13-r77": {"weights": [.97, .09, 1.06], "singleLeg": [44.9, 9.8, 33.2, 9.8, 66.1, 14.7, 49.8, 14.7]},
    "lowcost-p13-r78": {"weights": [1.06, .11, 1.17], "singleLeg": [49.5, 11.1, 47.4, 11.1, 74.2, 14.7, 71.1, 14.7]},
    "lowcost-p13-r82": {"weights": [1.68, .18, 1.86], "overall": [18.0, 28.3, 333.8, 23.4, 42.5, 500.7]},
    "lowcost-p13-r95": {"weights": [5.44, .46, 5.90], "overall": [59.3, 27.7, 711.1, 77.1, 41.5, 1066.6]},
    "lowcost-p13-r98": {"weights": [6.83, .71, 7.54], "overall": [73.1, 55.4, 1334.8, 95.0, 83.1, 2002.2]},
    "lowcost-p14-r122": {"weights": [4.23, .36, 4.59], "overall": [45.9, 39.9, 702.9, 59.7, 59.5, 1048.8]},
}


SOURCE_CATALOG_CONFLICTS = {
    "camouflage-p9-r13": (20, None),
    "camouflage-p9-r14": (20, None),
    "camouflage-p9-r15": (20, None),
    "camouflage-p9-r18": (25, "DGT(CL)-25-0.75-3ZJ"),
    "camouflage-p9-r19": (25, "DGT(CL)-25-0.85-3ZJ"),
    "camouflage-p9-r20": (25, "DGT(CL)-25-1.00-3ZJ"),
}


SINGLE_LEG_OVERRIDES: dict[str, list[float]] = {
    # 通信铁塔 V1.3，第13页三管塔单塔腿反力异常单元格复核。
    "communication-p13-r69": [683.5, 49.8, 637.5, 49.8, 952.2, 69.7, 901.6, 69.7],
    "communication-p13-r71": [669.4, 42.8, 615.8, 42.8, 931.8, 59.9, 872.8, 59.9],
    "communication-p13-r73": [838.6, 59.6, 780.6, 59.6, 1168.2, 83.5, 1104.4, 83.5],
    "communication-p13-r74": [919.6, 66.1, 859.2, 66.1, 1281.3, 92.6, 1214.9, 92.6],
    "communication-p13-r76": [668.5, 41.6, 610.5, 41.6, 930.0, 58.3, 866.2, 58.3],
    "communication-p13-r77": [866.7, 54.7, 798.7, 54.7, 1206.5, 76.6, 1131.7, 76.6],
    "communication-p13-r78": [959.0, 65.7, 889.0, 65.7, 1335.6, 92.0, 1258.6, 92.0],
    "communication-p13-r81": [828.9, 48.6, 754.5, 48.6, 1153.1, 68.1, 1071.2, 68.1],
    "communication-p13-r82": [929.1, 54.4, 844.1, 54.4, 1283.1, 76.2, 1197.3, 76.2],
    "communication-p13-r83": [1004.9, 67.4, 922.9, 67.4, 1398.7, 94.4, 1308.5, 94.4],
    "communication-p13-r86": [800.2, 46.3, 720.2, 46.3, 1112.3, 64.8, 1024.3, 64.8],
    "communication-p13-r87": [1038.3, 61.1, 946.3, 61.1, 1444.4, 85.5, 1343.2, 85.5],
    "communication-p13-r89": [1335.1, 90.3, 1229.1, 90.3, 1858.5, 126.4, 1741.9, 126.4],
    "communication-p13-r91": [929.5, 51.5, 853.5, 51.5, 1293.6, 72.1, 1210.1, 72.1],
    "communication-p13-r97": [1311.4, 102.9, 1202.4, 102.9, 1825.0, 144.1, 1705.0, 144.1],
    "communication-p13-r101": [1484.2, 120.8, 1359.2, 120.8, 2065.4, 169.1, 1927.9, 169.1],
    # 低成本图集 V2.0，第12页。
    "lowcost-p12-r40": [284.8, 21.0, 272.9, 20.5, 425.6, 31.5, 410.9, 30.8],
    "lowcost-p12-r43": [251.8, 17.7, 236.2, 17.0, 375.7, 26.4, 356.2, 25.5],
    "lowcost-p12-r44": [330.8, 23.5, 315.4, 22.7, 494.2, 35.1, 475.1, 34.2],
    "lowcost-p12-r46": [456.7, 35.6, 425.0, 35.6, 677.5, 53.2, 644.2, 53.2],
    "lowcost-p12-r47": [277.8, 19.7, 253.7, 18.6, 414.3, 29.5, 382.9, 28.0],
    # 低成本图集 V2.0，第13页。
    "lowcost-p13-r52": [253.0, 18.2, 238.0, 17.5, 378.0, 27.2, 358.5, 26.3],
    "lowcost-p13-r53": [294.8, 22.9, 296.3, 24.1, 439.7, 34.3, 450.6, 36.5],
    "lowcost-p13-r54": [348.8, 27.4, 353.8, 28.9, 520.6, 40.9, 537.1, 43.8],
    "lowcost-p13-r57": [358.4, 27.3, 363.2, 29.2, 534.5, 40.8, 552.5, 44.2],
    "lowcost-p13-r58": [424.0, 32.6, 432.3, 35.0, 632.6, 48.7, 656.8, 52.9],
    "lowcost-p13-r59": [280.8, 19.4, 261.1, 18.5, 418.9, 29.0, 394.0, 27.9],
    "lowcost-p13-r60": [362.9, 25.4, 343.4, 24.5, 541.9, 37.9, 517.5, 36.8],
    "lowcost-p13-r61": [420.8, 32.0, 427.7, 34.3, 627.4, 47.8, 651.1, 52.0],
    "lowcost-p13-r80": [167.6, 15.6, 159.3, 15.6, 250.1, 23.5, 239.7, 23.5],
    # 低成本图集 V2.0，第14页。
    "lowcost-p14-r103": [312.1, 21.8, 285.2, 20.6, 465.5, 32.6, 430.5, 31.0],
    "lowcost-p14-r104": [421.2, 29.7, 392.0, 28.4, 628.8, 44.5, 590.9, 42.7],
    "lowcost-p14-r105": [484.7, 37.0, 493.2, 39.8, 722.4, 55.2, 751.5, 60.3],
    "lowcost-p14-r108": [489.8, 35.0, 455.0, 33.4, 731.3, 52.3, 686.0, 50.2],
    "lowcost-p14-r109": [550.3, 42.1, 561.2, 45.3, 819.9, 62.8, 855.6, 68.6],
    "lowcost-p14-r111": [334.7, 23.1, 305.9, 21.8, 499.2, 34.5, 461.7, 32.8],
    "lowcost-p14-r112": [447.2, 31.2, 416.0, 29.8, 667.6, 46.7, 627.1, 44.8],
    "lowcost-p14-r113": [520.9, 39.5, 525.2, 42.1, 776.2, 58.9, 800.7, 63.8],
    "lowcost-p14-r114": [617.0, 46.6, 627.6, 49.9, 920.0, 69.6, 955.3, 75.6],
    "lowcost-p14-r115": [391.9, 27.0, 356.8, 25.4, 584.3, 40.3, 538.8, 38.2],
    "lowcost-p14-r116": [517.0, 36.4, 480.4, 34.8, 771.9, 54.5, 724.3, 52.3],
    "lowcost-p14-r117": [588.0, 44.4, 593.4, 47.4, 875.8, 66.3, 905.4, 71.8],
}


def load_matrix(path: Path) -> list[list[dict[str, Any]]]:
    cells = json.loads(path.read_text(encoding="utf-8"))
    rows: dict[int, dict[int, dict[str, Any]]] = {}
    for cell in cells:
        rows.setdefault(cell["Row"], {})[cell["Column"]] = cell
    return [[columns[index] for index in sorted(columns)] for _, columns in sorted(rows.items())]


def category(spec: SourceSpec, row_no: int) -> tuple[str, str]:
    for start, end, group, tower_type in spec.category_ranges:
        if start <= row_no <= end:
            return group, tower_type
    return "未分类", "未分类"


def normalize_code(raw: str) -> str:
    code = raw.strip().upper().replace(" ", "")
    code = code.replace("（", "(").replace("）", ")").replace("—", "-")
    code = re.sub(r"^(?:NGT|NG1|DG1|DGI|DET|DNGT|NAT)", "DGT", code)
    code = re.sub(r"^DG1(?=\()", "DGT", code)
    code = re.sub(r"^DGI(?=\()", "DGT", code)
    code = re.sub(r"^NGT(?=\()", "DGT", code)
    code = re.sub(r"^DET(?=\()", "DGT", code)
    code = re.sub(r"^DGT\(7\)", "DGT(Z)", code)
    code = re.sub(r"^DGT\(Z7\)", "DGT(Z)", code)
    code = re.sub(r"^DGT\(C[C.]?\)", "DGT(C)", code)
    code = re.sub(r"^DGT\(CD\.\/H\)", "DGT(CD/H)", code)
    code = re.sub(r"^DG(?=\()", "DGT", code)
    code = re.sub(r"^(?:Z7GU|7GU|7GJ)", "ZGJ", code)
    code = re.sub(r"^3(?:G1T|G61T|G671T|G67T|G7T|G71|G61|G671|G1|GT1|C67|67|61|6T|GT)", "3GT", code)
    code = code.replace("P15", "PT5").replace("P1T5", "PT5")
    code = re.sub(r"-([12345])(?:2U|ZU|7ZU|Z7U|7U|7J|7\)|Z7J)(?=-|$)", lambda m: f"-{m.group(1)}ZJ", code)
    code = re.sub(r"-(\d+)2U(?=-|$)", r"-\1ZJ", code)
    code = code.replace("Z7J", "ZJ").replace("27J", "2ZJ").replace("37J", "3ZJ").replace("47J", "4ZJ").replace("57J", "5ZJ")
    code = code.replace("+", "-").replace("B8D", "BD").replace("BD.", "BD")
    code = code.replace("(CS7)", "(CSZ)").replace("3ZD", "3ZJ")
    code = code.replace("32J", "3ZJ").replace("-32", "-3ZJ")
    code = code.replace("37ZJ", "3ZJ").replace("47ZJ", "4ZJ").replace("272J", "2ZJ").replace("PT15", "PT5")
    code = re.sub(r"\)(?=\d)", ")-", code)
    return code


CHAR_MAP = str.maketrans({
    "/": "7", ")": "7", "]": "7", "Q": "9", "q": "9", "O": "0", "o": "0",
    "R": "8", "B": "8", "A": "4", "S": "5", "I": "1", "l": "1", "i": "1",
})


def numeric_candidates(text: str, lower: float, upper: float) -> list[tuple[float, float]]:
    """返回 (候选值, 变换成本)，成本越低越接近 OCR 原文。"""
    value = text.strip().translate(CHAR_MAP)
    value = re.sub(r"[^0-9.+-]", "", value)
    if not value or not re.search(r"\d", value):
        return []
    if value.count(".") > 1:
        first = value.find(".")
        value = value[: first + 1] + value[first + 1 :].replace(".", "")
    try:
        base = abs(float(value))
    except ValueError:
        return []
    result: dict[float, float] = {}
    for shift in range(-4, 5):
        candidate = base * (10 ** shift)
        if lower <= candidate <= upper:
            rounded = round(candidate, 6)
            result[rounded] = min(result.get(rounded, 99.0), abs(shift) * 1.4)
    # 无小数点时，允许在任意位置插入小数点；这是 588 -> 5.88 等常见误读。
    digits = re.sub(r"\D", "", value)
    if "." not in value and len(digits) >= 2:
        for pos in range(1, len(digits)):
            candidate = float(digits[:pos] + "." + digits[pos:])
            if lower <= candidate <= upper:
                result[candidate] = min(result.get(candidate, 99.0), 1.0)
    return sorted(result.items(), key=lambda item: item[1])


def nearest_cost(value: float, candidates: Iterable[tuple[float, float]]) -> float:
    options = list(candidates)
    if not options:
        return 9.0
    return min(cost + abs(candidate - value) / max(value, 0.1) * 12 for candidate, cost in options)


def solve_weights(cells: list[dict[str, Any]]) -> tuple[list[float] | None, list[str]]:
    candidate_sets = [numeric_candidates(cell["Text"], .01, 100.0) for cell in cells]
    choices: list[tuple[float, list[float], str]] = []
    for a, ac in candidate_sets[0] or [(0.0, 9.0)]:
        for b, bc in candidate_sets[1] or [(0.0, 9.0)]:
            c = round(a + b, 2)
            if .01 <= c <= 100:
                score = ac + bc + nearest_cost(c, candidate_sets[2])
                choices.append((score, [round(a, 3), round(b, 3), c], "由塔重+附件重核对合计"))
    for a, ac in candidate_sets[0] or [(0.0, 9.0)]:
        for c, cc in candidate_sets[2] or [(0.0, 9.0)]:
            b = round(c - a, 2)
            if 0 <= b <= 10:
                score = ac + cc + nearest_cost(b, candidate_sets[1])
                choices.append((score, [round(a, 3), b, round(c, 3)], "由塔重+合计反算附件重"))
    for b, bc in candidate_sets[1] or [(0.0, 9.0)]:
        for c, cc in candidate_sets[2] or [(0.0, 9.0)]:
            a = round(c - b, 2)
            if .01 <= a <= 100:
                score = bc + cc + nearest_cost(a, candidate_sets[0])
                choices.append((score, [a, round(b, 3), round(c, 3)], "由附件重+合计反算塔重"))
    if not choices:
        return None, ["重量三元组无法解析"]
    score, values, note = min(choices, key=lambda item: item[0])
    issues = [] if score <= 5.0 else [f"重量需复核（{note}，评分{score:.2f}）"]
    return values, issues


def solve_pair(
    standard_cell: dict[str, Any], basic_cell: dict[str, Any], factor: float,
    lower: float, upper: float, precision: int,
) -> tuple[list[float] | None, list[str]]:
    standards = numeric_candidates(standard_cell["Text"], lower, upper)
    basics = numeric_candidates(basic_cell["Text"], lower, upper * factor * 1.2)
    choices: list[tuple[float, float, float, str]] = []
    for standard, sc in standards:
        expected = round(standard * factor, precision)
        score = sc + nearest_cost(expected, basics)
        choices.append((score, standard, expected, "由标准组合核对基本组合"))
    for basic, bc in basics:
        expected = round(basic / factor, precision)
        score = bc + nearest_cost(expected, standards)
        choices.append((score, expected, basic, "由基本组合反算标准组合"))
    if not choices:
        return None, ["标准/基本组合无法解析"]
    score, standard, basic, note = min(choices, key=lambda item: item[0])
    issues = [] if score <= 5.0 else [f"组合关系需复核（{note}，评分{score:.2f}）"]
    return [round(standard, precision), round(basic, precision)], issues


def parse_single_leg(cells: list[dict[str, Any]]) -> tuple[dict[str, Any] | None, list[str]]:
    if not any(re.search(r"\d", cell["Text"]) for cell in cells):
        return None, []
    values: list[float | None] = []
    issues: list[str] = []
    for index, cell in enumerate(cells):
        upper = 3000.0 if index % 2 == 0 else 400.0
        candidates = numeric_candidates(cell["Text"], 0.01, upper)
        if not candidates:
            values.append(None)
            issues.append(f"单塔腿第{index + 1}列无法解析")
        else:
            values.append(round(candidates[0][0], 2))
    if issues:
        return {"rawValues": values}, issues
    return {
        "standard": {
            "compressionControl": {"compressionKn": values[0], "shearKn": values[1]},
            "tensionControl": {"tensionKn": values[2], "shearKn": values[3]},
        },
        "basic": {
            "compressionControl": {"compressionKn": values[4], "shearKn": values[5]},
            "tensionControl": {"tensionKn": values[6], "shearKn": values[7]},
        },
    }, []


def expects_single_leg(spec: SourceSpec, row_no: int) -> bool:
    if spec.source_id == "lowcost":
        return 35 <= row_no <= 62 or 75 <= row_no <= 86 or 103 <= row_no <= 118
    if spec.source_id == "communication":
        return 68 <= row_no <= 108
    return spec.source_id == "camouflage" and row_no == 78


def make_record(spec: SourceSpec, local_index: int, cells: list[dict[str, Any]]) -> dict[str, Any]:
    row_no = spec.first_row + local_index
    group, tower_type = category(spec, row_no)
    raw_code = cells[0]["Text"]
    code = normalize_code(raw_code)
    weights, weight_issues = solve_weights(cells[1:4])
    axial, axial_issues = solve_pair(cells[4], cells[7], spec.axial_factor, .1, 500.0, 2)
    shear, shear_issues = solve_pair(cells[5], cells[8], spec.lateral_factor, .1, 500.0, 2)
    moment, moment_issues = solve_pair(cells[6], cells[9], spec.moment_factor, .1, 10000.0, 2)
    overall_issues = weight_issues + axial_issues + shear_issues + moment_issues
    single_leg = None
    single_leg_issues: list[str] = []
    if len(cells) >= 18 and expects_single_leg(spec, row_no):
        single_leg, single_leg_issues = parse_single_leg(cells[10:18])
        if single_leg is not None and not single_leg_issues:
            single_leg_issues.append("单塔腿反力已提取，仍需逐项视觉核对")
    code_match = re.match(r"^(?:DGT|3GT|ZGJ|ZCG|JGT|FSS|DGN)[A-Z0-9()/.-]+$", code)
    code_issues: list[str] = []
    if not code_match:
        code_issues.append("塔型编码需复核")
    overall = None
    if axial and shear and moment:
        overall = {
            "standard": {"axialKn": axial[0], "shearKn": shear[0], "momentKnM": moment[0]},
            "basic": {"axialKn": axial[1], "shearKn": shear[1], "momentKnM": moment[1]},
        }
    overall_ready = overall is not None and not overall_issues and not code_issues
    single_leg_ready = False
    issues = code_issues + overall_issues + single_leg_issues
    if overall_ready and single_leg is not None:
        review_status = "overall_ready_single_leg_review"
    elif overall_ready:
        review_status = "consistency_checked"
    else:
        review_status = "needs_review"
    return {
        "id": f"{spec.source_id}-p{spec.page}-r{row_no}",
        "sourceId": spec.source_id,
        "sourceTitle": spec.title,
        "standardNo": spec.standard_no,
        "catalogVersion": spec.version,
        "sourcePdfPage": spec.page,
        "sourceTableRow": row_no,
        "group": group,
        "towerType": tower_type,
        "towerCode": code,
        "towerCodeOcr": raw_code,
        "towerWeightT": weights[0] if weights else None,
        "attachmentWeightT": weights[1] if weights else None,
        "totalWeightT": weights[2] if weights else None,
        "overallBaseReaction": overall,
        "singleLegReaction": single_leg,
        "usableForAutomaticOverallLoad": overall_ready,
        "usableForAutomaticSingleLegLoad": single_leg_ready,
        "usableForAutomaticLoad": overall_ready,
        "reviewStatus": review_status,
        "overallReviewIssues": code_issues + overall_issues,
        "singleLegReviewIssues": single_leg_issues,
        "reviewIssues": issues,
        "rawCells": [{"text": c["Text"], "confidence": round(float(c["MeanConfidence"]), 4)} for c in cells],
    }


def apply_source_catalog_conflict(record: dict[str, Any]) -> dict[str, Any]:
    conflict = SOURCE_CATALOG_CONFLICTS.get(record["id"])
    if not conflict:
        return record
    declared_height, suggested_code = conflict
    note = (
        "图集原表编号冲突：20m 与 25m 分组出现相同塔型编号但荷载不同；"
        "不得仅按塔型编号自动套用，必须结合图集页码、行号和分组高度人工确认。"
    )
    record["sourceDeclaredHeightM"] = declared_height
    record["suggestedCanonicalCode"] = suggested_code
    record["catalogAnomaly"] = note
    record["usableForAutomaticOverallLoad"] = False
    record["usableForAutomaticLoad"] = False
    record["overallReviewIssues"] = record.get("overallReviewIssues", []) + [note]
    record["reviewIssues"] = record.get("overallReviewIssues", []) + record.get("singleLegReviewIssues", [])
    record["reviewStatus"] = "source_catalog_conflict"
    return record


def apply_manual_override(record: dict[str, Any]) -> dict[str, Any]:
    override = MANUAL_OVERRIDES.get(record["id"], {})
    single_leg_override = override.get("singleLeg") or SINGLE_LEG_OVERRIDES.get(record["id"])
    if not override and not single_leg_override:
        return apply_source_catalog_conflict(record)
    if "code" in override:
        record["towerCode"] = override["code"]
    if "weights" in override:
        record["towerWeightT"], record["attachmentWeightT"], record["totalWeightT"] = override["weights"]
    if "overall" in override:
        sn, sh, sm, bn, bh, bm = override["overall"]
        record["overallBaseReaction"] = {
            "standard": {"axialKn": sn, "shearKn": sh, "momentKnM": sm},
            "basic": {"axialKn": bn, "shearKn": bh, "momentKnM": bm},
        }
        record["overallReviewIssues"] = []
        record["usableForAutomaticOverallLoad"] = True
        record["usableForAutomaticLoad"] = True
    if single_leg_override:
        scp, scs, stt, sts, bcp, bcs, btt, bts = single_leg_override
        record["singleLegReaction"] = {
            "standard": {
                "compressionControl": {"compressionKn": scp, "shearKn": scs},
                "tensionControl": {"tensionKn": stt, "shearKn": sts},
            },
            "basic": {
                "compressionControl": {"compressionKn": bcp, "shearKn": bcs},
                "tensionControl": {"tensionKn": btt, "shearKn": bts},
            },
        }
        record["singleLegReviewIssues"] = []
        record["usableForAutomaticSingleLegLoad"] = True
    record["reviewIssues"] = record.get("overallReviewIssues", []) + record.get("singleLegReviewIssues", [])
    if record["usableForAutomaticOverallLoad"] and record["usableForAutomaticSingleLegLoad"]:
        record["reviewStatus"] = "visually_verified"
    elif record["usableForAutomaticOverallLoad"] and record["singleLegReaction"] is not None:
        record["reviewStatus"] = "overall_ready_single_leg_review"
    elif record["usableForAutomaticOverallLoad"]:
        record["reviewStatus"] = "visually_verified"
    elif record["usableForAutomaticSingleLegLoad"]:
        record["reviewStatus"] = "single_leg_visually_verified"
    else:
        record["reviewStatus"] = "needs_review"
    return apply_source_catalog_conflict(record)


def finalize_single_leg_consistency(record: dict[str, Any]) -> dict[str, Any]:
    if record.get("usableForAutomaticSingleLegLoad"):
        return record
    reaction = record.get("singleLegReaction")
    if not reaction or "rawValues" in reaction:
        return record
    values = [
        reaction["standard"]["compressionControl"]["compressionKn"],
        reaction["standard"]["compressionControl"]["shearKn"],
        reaction["standard"]["tensionControl"]["tensionKn"],
        reaction["standard"]["tensionControl"]["shearKn"],
        reaction["basic"]["compressionControl"]["compressionKn"],
        reaction["basic"]["compressionControl"]["shearKn"],
        reaction["basic"]["tensionControl"]["tensionKn"],
        reaction["basic"]["tensionControl"]["shearKn"],
    ]
    if any(value is None or value <= 0 for value in values):
        return record
    lower, upper = ((1.35, 1.70) if record["sourceId"] == "lowcost" else (1.25, 1.55))
    ratios = [values[index + 4] / values[index] for index in range(4)]
    if not all(lower <= ratio <= upper for ratio in ratios):
        return record
    if not (values[0] > values[1] and values[2] > values[3] and values[4] > values[5] and values[6] > values[7]):
        return record
    record["usableForAutomaticSingleLegLoad"] = True
    record["singleLegReviewIssues"] = []
    record["singleLegVerification"] = "单元格识别+标准/基本组合比值+量级关系校核"
    record["reviewIssues"] = record.get("overallReviewIssues", [])
    if record.get("usableForAutomaticOverallLoad"):
        record["reviewStatus"] = "fully_consistency_checked"
    elif not record["reviewIssues"]:
        record["reviewStatus"] = "single_leg_consistency_checked"
    return record


def three_tube_records() -> list[dict[str, Any]]:
    records = []
    for values in THREE_TUBE_ROWS:
        (row_no, code, tower, attachment, total, sn, sh, sm, bn, bh, bm,
         scp, scs, stt, sts, bcp, bcs, btt, bts) = values
        records.append({
            "id": f"three-tube-p7-r{row_no}", "sourceId": "three-tube",
            "sourceTitle": "创新三管塔标准图集", "standardNo": "Q/ZTT 1028-2018",
            "catalogVersion": "V1.0", "sourcePdfPage": 7, "sourceTableRow": row_no,
            "group": "创新三管塔", "towerType": "单管塔三管塔" if row_no <= 8 else "双斜柱三管塔",
            "towerCode": code, "towerCodeOcr": None, "towerWeightT": tower,
            "attachmentWeightT": attachment, "totalWeightT": total,
            "overallBaseReaction": {
                "standard": {"axialKn": sn, "shearKn": sh, "momentKnM": sm},
                "basic": {"axialKn": bn, "shearKn": bh, "momentKnM": bm},
            },
            "singleLegReaction": {
                "standard": {"compressionControl": {"compressionKn": scp, "shearKn": scs},
                             "tensionControl": {"tensionKn": stt, "shearKn": sts}},
                "basic": {"compressionControl": {"compressionKn": bcp, "shearKn": bcs},
                          "tensionControl": {"tensionKn": btt, "shearKn": bts}},
            },
            "usableForAutomaticOverallLoad": True, "usableForAutomaticSingleLegLoad": True,
            "usableForAutomaticLoad": True, "reviewStatus": "visually_transcribed",
            "overallReviewIssues": [], "singleLegReviewIssues": [],
            "reviewIssues": [], "rawCells": None,
        })
    return records


def unavailable_records() -> list[dict[str, Any]]:
    source_meta = {
        "lowcost": ("低成本铁塔标准图集", "Q/ZTT 1030-2021", "V2.0"),
        "communication": ("通信铁塔标准图集", "Q/ZTT 1023-2016", "V1.3"),
    }
    records = []
    for source_id, page, row_no, code, note in UNAVAILABLE:
        title, standard, version = source_meta[source_id]
        records.append({
            "id": f"{source_id}-p{page}-r{row_no}", "sourceId": source_id,
            "sourceTitle": title, "standardNo": standard, "catalogVersion": version,
            "sourcePdfPage": page, "sourceTableRow": row_no, "group": "目录保留",
            "towerType": "不含基础荷载", "towerCode": code, "towerCodeOcr": None,
            "towerWeightT": None, "attachmentWeightT": None, "totalWeightT": None,
            "overallBaseReaction": None, "singleLegReaction": None,
            "usableForAutomaticOverallLoad": False, "usableForAutomaticSingleLegLoad": False,
            "usableForAutomaticLoad": False, "reviewStatus": "catalog_only",
            "overallReviewIssues": [note], "singleLegReviewIssues": [],
            "reviewIssues": [note], "rawCells": None,
        })
    return records


def write_csv(records: list[dict[str, Any]], path: Path) -> None:
    fields = [
        "id", "sourceTitle", "standardNo", "catalogVersion", "sourcePdfPage", "sourceTableRow",
        "group", "towerType", "towerCode", "towerWeightT", "attachmentWeightT", "totalWeightT",
        "stdAxialKn", "stdShearKn", "stdMomentKnM", "basicAxialKn", "basicShearKn", "basicMomentKnM",
        "hasSingleLegReaction", "usableForAutomaticLoad", "reviewStatus", "sourceDeclaredHeightM",
        "suggestedCanonicalCode", "catalogAnomaly", "reviewIssues",
    ]
    with path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        for record in records:
            overall = record.get("overallBaseReaction") or {}
            standard = overall.get("standard") or {}
            basic = overall.get("basic") or {}
            writer.writerow({
                **{key: record.get(key) for key in fields},
                "stdAxialKn": standard.get("axialKn"), "stdShearKn": standard.get("shearKn"),
                "stdMomentKnM": standard.get("momentKnM"), "basicAxialKn": basic.get("axialKn"),
                "basicShearKn": basic.get("shearKn"), "basicMomentKnM": basic.get("momentKnM"),
                "hasSingleLegReaction": record.get("singleLegReaction") is not None,
                "reviewIssues": "；".join(record.get("reviewIssues") or []),
            })


def main() -> None:
    records: list[dict[str, Any]] = []
    unavailable_keys = {(source, page, row) for source, page, row, _, _ in UNAVAILABLE}
    for spec in SPECS:
        matrix = load_matrix(OCR_DIR / spec.cell_file)
        for local_index, cells in enumerate(matrix):
            row_no = spec.first_row + local_index
            if (spec.source_id, spec.page, row_no) in unavailable_keys:
                continue
            records.append(apply_manual_override(make_record(spec, local_index, cells)))
    records.extend(three_tube_records())
    records.extend(unavailable_records())
    records = [finalize_single_leg_consistency(record) for record in records]
    records.sort(key=lambda r: (r["sourceId"], r["sourcePdfPage"], r["sourceTableRow"]))

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    payload = {
        "schemaVersion": 1,
        "generatedFrom": "企业标准图集荷载表逐页渲染、单元格 OCR、组合关系一致性校核",
        "safetyRule": "仅 usableForAutomaticLoad=true 的记录可被软件自动套用；其余记录必须人工复核或仅作目录展示。",
        "records": records,
    }
    (OUTPUT_DIR / "企业标准塔型荷载库.json").write_text(
        json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    write_csv(records, OUTPUT_DIR / "企业标准塔型荷载库.csv")

    counts: dict[str, int] = {}
    for record in records:
        counts[record["reviewStatus"]] = counts.get(record["reviewStatus"], 0) + 1
    summary = {
        "total": len(records),
        "overallLoad": sum(r["overallBaseReaction"] is not None for r in records),
        "singleLegLoad": sum(r["singleLegReaction"] is not None for r in records),
        "automaticLoadReady": sum(bool(r["usableForAutomaticLoad"]) for r in records),
        "singleLegAutomaticReady": sum(bool(r.get("usableForAutomaticSingleLegLoad")) for r in records),
        "catalogOnly": sum(r["reviewStatus"] == "catalog_only" for r in records),
        "sourceCatalogConflicts": sum(r["reviewStatus"] == "source_catalog_conflict" for r in records),
        "reviewStatus": counts,
        "needsReviewIds": [r["id"] for r in records if r["reviewStatus"] in {"needs_review", "source_catalog_conflict"}],
        "singleLegNeedsVisualReviewIds": [r["id"] for r in records if r.get("singleLegReviewIssues")],
    }
    (OUTPUT_DIR / "提取审查摘要.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
