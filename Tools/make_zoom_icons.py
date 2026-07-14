from PIL import Image, ImageDraw
import math
import os

out_dir = r"Assets/Resources/UI"
os.makedirs(out_dir, exist_ok=True)


def make_search(path, with_minus=False, size=128):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    stroke = 7
    color = (70, 72, 78, 255)
    cx, cy, r = 52, 50, 28
    bbox = [cx - r, cy - r, cx + r, cy + r]
    d.ellipse(bbox, outline=color, width=stroke)
    ang = math.radians(45)
    x0 = cx + (r - 2) * math.cos(ang)
    y0 = cy + (r - 2) * math.sin(ang)
    x1 = x0 + 28 * math.cos(ang)
    y1 = y0 + 28 * math.sin(ang)
    d.line([(x0, y0), (x1, y1)], fill=color, width=stroke)
    er = stroke // 2 + 1
    d.ellipse([x1 - er, y1 - er, x1 + er, y1 + er], fill=color)
    if with_minus:
        mw = 16
        d.line([(cx - mw, cy), (cx + mw, cy)], fill=color, width=stroke)
    img.save(path)
    print("wrote", path, img.size)


make_search(os.path.join(out_dir, "proposal_zoom_in.png"), False)
make_search(os.path.join(out_dir, "proposal_zoom_out.png"), True)
