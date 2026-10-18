"""Export the FluentDL logo to Windows assets and repository artwork.

Run from any directory with Python 3.10 or newer:
    python -m pip install -r Branding/requirements.txt
    python Branding/export.py

The app consumes the checked-in exports. Python is not a build dependency.
"""

from io import BytesIO
from pathlib import Path

from PIL import Image
import resvg_py


HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
ASSETS = ROOT / "FluentDL" / "Assets"
SCALES = (100, 125, 150, 200, 250, 300, 400)
TARGETS = (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
RESAMPLE = Image.Resampling.LANCZOS


def scaled(value, scale):
    return (value * scale + 50) // 100


def target_filename(target, form=None):
    qualifier = f"_altform-{form}" if form else ""
    # Preserve the project's existing qualifier ordering, without adding
    # a second file with the same resource qualifiers.
    legacy_sizes = (16, 32, 48, 256) if form == "unplated" else (16, 24, 32, 48, 256)
    if form and target in legacy_sizes:
        return f"Square44x44Logo.altform-{form}_targetsize-{target}.png"
    return f"Square44x44Logo.targetsize-{target}{qualifier}.png"


def load_master():
    # Supersample once. resvg supports the SVG masks and filters used in this logo.
    data = resvg_py.svg_to_bytes(
        svg_path=str(HERE / "fluentdl.svg"), width=3072, height=3072
    )
    return Image.open(BytesIO(data)).convert("RGBA")


def canvas(master, size, icon_size):
    image = Image.new("RGBA", size)
    icon = master.resize((icon_size, icon_size), RESAMPLE)
    image.alpha_composite(icon, ((size[0] - icon_size) // 2, (size[1] - icon_size) // 2))
    return image


def save(image, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, optimize=True)


def windows_assets(master):
    # Transparent canvases with consistent optical padding. The master itself has
    # 8 units of padding on each side of its 256-unit viewBox.
    families = {
        "Square44x44Logo": ((44, 44), 0.66),
        "Square150x150Logo": ((150, 150), 0.64),
        "SmallTile": ((71, 71), 0.64),
        "LargeTile": ((310, 310), 0.64),
        "Wide310x150Logo": ((310, 150), 0.64),
        "SplashScreen": ((620, 300), 0.32),
        "StoreLogo": ((50, 50), 1.0),
    }
    outputs = []
    for name, (base, fraction) in families.items():
        for scale in SCALES:
            size = tuple(scaled(v, scale) for v in base)
            icon_size = max(1, round(min(size) * fraction))
            path = ASSETS / f"{name}.scale-{scale}.png"
            save(canvas(master, size, icon_size), path)
            outputs.append(path)

    for target in TARGETS:
        for form, fraction in (
            (None, 0.75),
            ("unplated", 1.0),
            ("lightunplated", 1.0),
        ):
            path = ASSETS / target_filename(target, form)
            save(canvas(master, (target, target), max(1, round(target * fraction))), path)
            outputs.append(path)

    # Windows badge images are monochrome alpha masks, not colored app icons.
    badge = canvas(master, (48, 48), 44)
    alpha = badge.getchannel("A")
    badge = Image.new("RGBA", badge.size, "white")
    badge.putalpha(alpha)
    path = ASSETS / "LockScreenLogo.scale-200.png"
    save(badge, path)
    outputs.append(path)

    path = ASSETS / "FluentDL_logo.png"
    save(master.resize((500, 500), RESAMPLE), path)
    outputs.append(path)
    path = ASSETS / "WindowIcon.ico"
    icon_sizes = sorted(set(TARGETS) | {128})
    master.save(path, format="ICO", sizes=[(s, s) for s in icon_sizes])
    outputs.append(path)
    return outputs


def repository_artwork(master):
    outputs = []
    for name, size in (("logo.png", 1024), ("store-icon-300.png", 300), ("store-icon-150.png", 150)):
        path = HERE / name
        save(master.resize((size, size), RESAMPLE), path)
        outputs.append(path)

    # The alpha mask defines the wordmark independently of its theme color.
    mask = Image.open(HERE / "wordmark-mask.png").convert("L")
    for theme, color in (("dark", "#F0F6FC"), ("light", "#1F2328")):
        header = Image.new("RGBA", mask.size, color)
        header.putalpha(mask)
        mark = master.resize((224, 224), RESAMPLE)
        header.alpha_composite(mark, (0, 20))
        path = HERE / f"readme-{theme}.png"
        save(header, path)
        outputs.append(path)

    # A repository social-preview image, distinct from a GitHub profile avatar.
    social = Image.new("RGBA", (1280, 640), "#17181D")
    header = Image.open(HERE / "readme-dark.png").resize((1000, 234), RESAMPLE)
    social.alpha_composite(header, (140, 203))
    path = HERE / "social-preview.png"
    save(social.convert("RGB"), path)
    outputs.append(path)

    favicon = HERE / "favicon.ico"
    master.save(favicon, format="ICO", sizes=[(s, s) for s in (16, 32, 48, 64)])
    outputs.append(favicon)
    return outputs


def preview():
    cards = []
    for theme, color in (("light", "#F4F4F4"), ("dark", "#1A1A1A")):
        samples = "".join(
            f'<img src="../FluentDL/Assets/{target_filename(s, "unplated")}" '
            f'width="{s}" height="{s}" alt="{s} px icon">'
            for s in TARGETS
        )
        cards.append(
            f'<section style="background:{color};color:{"#eee" if theme == "dark" else "#222"}">'
            f'<h2>{theme.capitalize()} background</h2>'
            f'<img src="readme-{theme}.png" width="567" alt="FluentDL header">'
            f'<div class="sizes">{samples}</div></section>'
        )
    (HERE / "preview.html").write_text(
        '<!doctype html><html lang="en"><meta charset="utf-8"><title>FluentDL branding</title>'
        '<style>body{font-family:Segoe UI,sans-serif;background:#29292d;margin:24px}'
        'section{padding:24px;margin-bottom:16px;border-radius:12px}'
        'h2{font-size:18px}.sizes{display:flex;align-items:center;gap:20px;margin-top:20px;flex-wrap:wrap}</style>'
        + "".join(cards) + '</html>',
        encoding="utf-8",
    )


def main():
    master = load_master()
    outputs = windows_assets(master) + repository_artwork(master)
    preview()
    for path in outputs:
        print(path.relative_to(ROOT))
    print(f"Exported {len(outputs)} images. Preview: Branding/preview.html")


if __name__ == "__main__":
    main()
