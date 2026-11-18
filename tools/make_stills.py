"""Cuts each turntable sheet's front frame (frame 0) into Assets/Turntables/Stills/<sheet>.webp.

The app shows the still while a tile stands still and decodes the whole sheet (about 17 MB) only
while it spins. Lossless, so the still matches the spin's first frame pixel for pixel. Run after
adding or changing a sheet: python tools/make_stills.py
"""
from pathlib import Path

from PIL import Image

COLUMNS, ROWS = 12, 15   # Pods.Core.Apple.Turntable
SHEETS = Path(__file__).resolve().parent.parent / "src" / "Pods.App" / "Assets" / "Turntables"
STILLS = SHEETS / "Stills"


def main() -> None:
    STILLS.mkdir(exist_ok=True)
    sheets = sorted(SHEETS.glob("*.webp"))
    for sheet in sheets:
        with Image.open(sheet) as image:
            width, height = image.size
            if width % COLUMNS or height % ROWS:
                raise SystemExit(f"{sheet.name}: {width}x{height} is not a {COLUMNS}x{ROWS} grid")
            frame = image.convert("RGBA").crop((0, 0, width // COLUMNS, height // ROWS))
            frame.save(STILLS / sheet.name, lossless=True, quality=100, method=6)
    for stale in STILLS.glob("*.webp"):
        if not (SHEETS / stale.name).exists():
            stale.unlink()
    print(f"{len(sheets)} stills in {STILLS}")


if __name__ == "__main__":
    main()
