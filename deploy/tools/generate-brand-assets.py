#!/usr/bin/env python3
"""Generates favicon/apple-touch-icon/OG-image PNGs for the static sites from the
same navy/blue mark used by the PMS app's favicon.svg (src/PerformanceManagement.Web/
wwwroot/favicon.svg) — kept as a script (not just checked-in binaries) so the mark can
be regenerated or resized without needing a design tool. Run: python3 generate-brand-assets.py
"""
import os
from PIL import Image, ImageDraw, ImageFont

NAVY = (15, 43, 92)        # #0f2b5c
NAVY2 = (30, 58, 138)      # #1e3a8a
BLUE = (47, 95, 214)       # #2f5fd6
WHITE = (255, 255, 255)

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def bars_mark(size, radius_ratio=0.22):
    """The same three-bar mark as favicon.svg, rasterized at `size`."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = int(size * radius_ratio)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=r, fill=NAVY)
    unit = size / 64
    bars = [
        (16, 34, 24, 52, WHITE),
        (28, 24, 36, 52, BLUE),
        (40, 14, 48, 52, WHITE),
    ]
    for x0, y0, x1, y1, color in bars:
        d.rounded_rectangle(
            [x0 * unit, y0 * unit, x1 * unit, y1 * unit],
            radius=2 * unit, fill=color,
        )
    return img


def save_favicons(out_dir):
    os.makedirs(out_dir, exist_ok=True)
    bars_mark(16).save(os.path.join(out_dir, "favicon-16.png"))
    bars_mark(32).save(os.path.join(out_dir, "favicon-32.png"))
    apple = Image.new("RGBA", (180, 180), (0, 0, 0, 0))
    ImageDraw.Draw(apple).rounded_rectangle([0, 0, 179, 179], radius=36, fill=NAVY)
    inner = bars_mark(140)
    apple.paste(inner, (20, 20), inner)
    apple.save(os.path.join(out_dir, "apple-touch-icon.png"))
    bars_mark(192).save(os.path.join(out_dir, "icon-192.png"))
    bars_mark(512).save(os.path.join(out_dir, "icon-512.png"))


def _font(size, bold=True):
    candidates = [
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf" if bold else "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
    ]
    for path in candidates:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except Exception:
                continue
    return ImageFont.load_default()


def og_image(title, subtitle, out_path):
    w, h = 1200, 630
    img = Image.new("RGB", (w, h), NAVY)
    d = ImageDraw.Draw(img)
    # subtle diagonal gradient band, mirroring the app's header gradient (navy -> #1e3a8a)
    for i in range(h):
        t = i / h
        r = int(NAVY[0] + (NAVY2[0] - NAVY[0]) * t)
        g = int(NAVY[1] + (NAVY2[1] - NAVY[1]) * t)
        b = int(NAVY[2] + (NAVY2[2] - NAVY[2]) * t)
        d.line([(0, i), (w, i)], fill=(r, g, b))

    mark = bars_mark(120)
    img.paste(mark, (90, 90), mark)

    d.text((90, 250), title, font=_font(64), fill=WHITE)
    d.text((90, 330), subtitle, font=_font(34, bold=False), fill=(197, 210, 235))
    d.text((90, h - 80), "aryanb.dev", font=_font(28, bold=False), fill=(140, 160, 200))
    img.save(out_path)


if __name__ == "__main__":
    sites = {
        "aryanb.dev": ("Aryan Bhandary", "Software Engineer — ASP.NET Core Developer"),
        "docs.aryanb.dev": ("PMS Documentation", "Performance Management System — Demo Docs"),
        "renewalflow.aryanb.dev": ("RenewalFlow", "Coming Soon"),
    }
    for site, (title, subtitle) in sites.items():
        out_dir = os.path.join(ROOT, "deploy", "sites", site, "assets")
        save_favicons(out_dir)
        og_image(title, subtitle, os.path.join(out_dir, "og-image.png"))
        print(f"generated assets for {site} -> {out_dir}")

    # Also drop a favicon set into the PMS app's own wwwroot (static files only, no code change)
    app_wwwroot = os.path.join(ROOT, "src", "PerformanceManagement.Web", "wwwroot")
    save_favicons(app_wwwroot)
    print(f"generated favicon set for the PMS app -> {app_wwwroot}")
