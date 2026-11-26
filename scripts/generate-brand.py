"""Rebuild CREST's original SVG, PNG and multi-resolution Windows icon assets.

The path matches CrestBrand.GeometryData. Pillow is used only for asset generation,
not by the shipped native application.
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets"
PATH = "M 29,4 Q 26,3 23,6 L 6,23 Q 3,26 6,29 L 23,46 Q 26,49 29,48 L 43,43 L 35,35 L 27,38 L 15,26 L 27,14 L 35,17 L 43,9 Z"


def quadratic(start, control, end):
    return [((1-t)**2*start[0] + 2*(1-t)*t*control[0] + t*t*end[0],
             (1-t)**2*start[1] + 2*(1-t)*t*control[1] + t*t*end[1])
            for t in [i / 16 for i in range(1, 17)]]


points = [(29, 4)]
points += quadratic(points[-1], (26, 3), (23, 6))
points += [(6, 23)]
points += quadratic(points[-1], (3, 26), (6, 29))
points += [(23, 46)]
points += quadratic(points[-1], (26, 49), (29, 48))
points += [(43, 43), (35, 35), (27, 38), (15, 26), (27, 14), (35, 17), (43, 9)]

ASSETS.mkdir(exist_ok=True)
icon_svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" role="img" aria-label="Crest">
  <rect x="1" y="1" width="62" height="62" rx="18" fill="#141A18" />
  <path d="{PATH}" transform="translate(8 6)" fill="#B3E7C5" />
</svg>
'''
(ASSETS / "crest.svg").write_text(icon_svg, encoding="utf-8")
(ASSETS / "crest-mark.svg").write_text(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 52 52" role="img" aria-label="Crest"><path d="{PATH}" fill="currentColor" /></svg>\n', encoding="utf-8")
for variant, color in [("light", "#192521"), ("dark", "#F2F5EF")]:
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 174 52" role="img" aria-label="Crest">
  <path d="{PATH}" fill="{color}" />
  <text x="61" y="39" font-family="Segoe UI Variable Display, Segoe UI, sans-serif" font-size="38" font-weight="600" letter-spacing="-1.5" fill="{color}">crest</text>
</svg>
'''
    (ASSETS / f"crest-{variant}.svg").write_text(svg, encoding="utf-8")

scale = 16
canvas = Image.new("RGBA", (64 * scale, 64 * scale), (0, 0, 0, 0))
draw = ImageDraw.Draw(canvas)
draw.rounded_rectangle((scale, scale, 63 * scale, 63 * scale), radius=18 * scale, fill="#141A18")
draw.polygon([((x + 8) * scale, (y + 6) * scale) for x, y in points], fill="#B3E7C5")
canvas.resize((512, 512), Image.Resampling.LANCZOS).save(ASSETS / "crest.png")
canvas.resize((256, 256), Image.Resampling.LANCZOS).save(ASSETS / "crest.ico", sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
print("Rebuilt original CREST brand: SVG mark and two wordmarks, 512px PNG, nine-size ICO.")
