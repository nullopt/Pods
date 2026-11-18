"""Builds the Pods icons from the AirPods Pro 2 turntable sheet and writes them to src/Pods.App/Assets,
plus the README logo at .github/logo.png.

Run: python tools/make_icons.py
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ASSETS = Path(__file__).resolve().parent.parent / "src" / "Pods.App" / "Assets"
SHEET, FRAME_W, FRAME_H = "1420", 220, 110  # 12 x 15 grid, see Turntable.cs
BUDS = (2, 96)  # earbuds' horizontal span in each frame (Turntable.PartsBySheet)
S = 1024


def buds() -> Image.Image:
    """Frame 0 (both buds facing front), trimmed to its pixels."""
    sheet = Image.open(ASSETS / "Turntables" / f"{SHEET}.webp").convert("RGBA")
    frame = sheet.crop((BUDS[0], 0, BUDS[1], FRAME_H))
    return frame.crop(frame.getbbox())


def fit(img: Image.Image, scale: float) -> Image.Image:
    """img scaled to `scale` of an S x S transparent canvas, centred."""
    k = S * scale / max(img.size)
    img = img.resize((round(img.width * k), round(img.height * k)), Image.LANCZOS)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.alpha_composite(img, ((S - img.width) // 2, (S - img.height) // 2))
    return out


def shadowed(img: Image.Image, spread: int, alpha: int) -> Image.Image:
    """A soft dark halo under img so white buds read on light backgrounds too."""
    a = img.getchannel("A").filter(ImageFilter.MaxFilter(spread | 1)).filter(ImageFilter.GaussianBlur(spread / 3))
    halo = Image.new("RGBA", img.size, (0, 0, 0, 0))
    halo.putalpha(a.point(lambda v: v * alpha // 255))
    return Image.alpha_composite(halo, img)


def tile(art: Image.Image) -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    m = S * 0.04
    ImageDraw.Draw(img).rounded_rectangle((m, m, S - m, S - m), radius=S * 0.2, fill=(38, 120, 210, 255))
    return Image.alpha_composite(img, shadowed(fit(art, 0.74), 24, 90))


def low_badge(img: Image.Image) -> Image.Image:
    img = img.copy()
    d = ImageDraw.Draw(img)
    r, ring = S * 0.19, S * 0.04
    c = S - r - S * 0.02
    d.ellipse((c - r - ring, c - r - ring, c + r + ring, c + r + ring), fill=(255, 255, 255, 255))
    d.ellipse((c - r, c - r, c + r, c + r), fill=(232, 17, 35, 255))
    return img


def save_ico(img: Image.Image, name: str, sizes: list[int]) -> None:
    frames = [img.resize((s, s), Image.LANCZOS) for s in sizes]
    frames[-1].save(ASSETS / name, sizes=[(s, s) for s in sizes], append_images=frames[:-1])


if __name__ == "__main__":
    art = buds()
    tray = shadowed(fit(art, 0.94), 40, 160)
    app = tile(art)
    save_ico(app, "Pods.ico", [16, 24, 32, 48, 64, 256])
    app.resize((256, 256), Image.LANCZOS).save(ASSETS.parent.parent.parent / ".github" / "logo.png", optimize=True)
    save_ico(tray, "Tray.ico", [16, 20, 24, 32, 48])
    save_ico(low_badge(tray), "TrayLow.ico", [16, 20, 24, 32, 48])
