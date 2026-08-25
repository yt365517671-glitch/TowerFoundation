"""Create optimized WPF logo and multi-resolution Windows icon assets."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def square_with_padding(image: Image.Image, padding_ratio: float = 0.07) -> Image.Image:
    rgba = image.convert("RGBA")
    alpha = rgba.getchannel("A")
    bounds = alpha.getbbox()
    if bounds is None:
        raise ValueError("输入图像没有可见像素。")

    cropped = rgba.crop(bounds)
    side = max(cropped.size)
    padding = max(1, round(side * padding_ratio))
    canvas_side = side + padding * 2
    canvas = Image.new("RGBA", (canvas_side, canvas_side), (0, 0, 0, 0))
    canvas.alpha_composite(
        cropped,
        ((canvas_side - cropped.width) // 2, (canvas_side - cropped.height) // 2),
    )
    return canvas


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output-dir", required=True)
    args = parser.parse_args()

    source_path = Path(args.input)
    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    with Image.open(source_path) as source:
        square = square_with_padding(source)

    logo_512 = square.resize((512, 512), Image.Resampling.LANCZOS)
    logo_64 = square.resize((64, 64), Image.Resampling.LANCZOS)
    logo_path = output_dir / "tower-foundation-logo.png"
    logo_64_path = output_dir / "tower-foundation-logo-64.png"
    icon_path = output_dir / "tower-foundation.ico"

    logo_512.save(logo_path, format="PNG", optimize=True)
    logo_64.save(logo_64_path, format="PNG", optimize=True)
    logo_512.save(icon_path, format="ICO", sizes=[(size, size) for size in ICON_SIZES])

    corners = [logo_512.getpixel(point)[3] for point in ((0, 0), (511, 0), (0, 511), (511, 511))]
    if any(corners):
        raise ValueError("输出图标四角不是透明像素。")

    print(f"LOGO={logo_path}")
    print(f"LOGO64={logo_64_path}")
    print(f"ICON={icon_path}")
    print(f"ICON_SIZES={','.join(map(str, ICON_SIZES))}")


if __name__ == "__main__":
    main()
