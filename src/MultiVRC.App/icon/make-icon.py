import os
from PIL import Image, ImageChops, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
MINT, SKY, INK = (0x7c, 0xf0, 0xc4), (0x6a, 0xa8, 0xff), (0x0d, 0x10, 0x14)
STROKES = [[(9, 11.5), (14, 22), (16, 17.8)], [(23, 11.5), (18, 22)]]


def draw(size: int) -> Image.Image:
    scale = 16
    big = size * scale
    unit = big / 32
    down = Image.linear_gradient("L").resize((big, big))
    ramp = ImageChops.add(down, down.transpose(Image.Transpose.TRANSPOSE), scale=2)
    gradient = Image.composite(Image.new("RGB", (big, big), SKY), Image.new("RGB", (big, big), MINT), ramp)
    mask = Image.new("L", (big, big), 0)
    inset = 1 * unit
    ImageDraw.Draw(mask).rounded_rectangle([inset, inset, big - inset, big - inset], radius=9.5 * unit, fill=255)
    icon = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    icon.paste(gradient, (0, 0), mask)

    pen = ImageDraw.Draw(icon)
    width = (3.4 if size <= 24 else 2.8) * unit
    for stroke in STROKES:
        points = [(x * unit, y * unit) for x, y in stroke]
        pen.line(points, fill=INK, width=round(width), joint="curve")
        for x, y in (points[0], points[-1]):
            pen.ellipse([x - width / 2, y - width / 2, x + width / 2, y + width / 2], fill=INK)
    return icon.resize((size, size), Image.LANCZOS)


def main():
    images = [draw(size) for size in SIZES]
    out = os.path.join(HERE, "MultiVRC.ico")
    images[-1].save(out, format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[:-1])
    images[-1].save(os.path.join(HERE, "MultiVRC-256.png"))
    print(f"{out}: {os.path.getsize(out) / 1024:.0f} KB, sizes {SIZES}")


if __name__ == "__main__":
    main()
