"""Lays FrameSheet's pictures out as one labelled contact sheet, to read boundaries by eye.

    python scripts/sections/compose_sheet.py <frames-folder> <out.png> [columns] [first-s] [last-s]

Needs Pillow. Each picture is labelled with its time as m:ss.ss and in seconds.
"""
import os
import sys

from PIL import Image, ImageDraw


def main():
    folder, out = sys.argv[1], sys.argv[2]
    columns = int(sys.argv[3]) if len(sys.argv) > 3 else 6
    first = float(sys.argv[4]) if len(sys.argv) > 4 else -1
    last = float(sys.argv[5]) if len(sys.argv) > 5 else 1e9
    names = sorted(name for name in os.listdir(folder) if name.endswith(".ppm"))
    names = [name for name in names if first <= int(name[:-4]) / 1000 <= last]
    if not names:
        sys.exit("There are no pictures in that range.")
    images = [Image.open(os.path.join(folder, name)) for name in names]
    width, height = images[0].size
    label = 14
    rows = (len(images) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * width, rows * (height + label)), (40, 40, 40))
    draw = ImageDraw.Draw(sheet)
    for index, (name, image) in enumerate(zip(names, images)):
        x = (index % columns) * width
        y = (index // columns) * (height + label)
        sheet.paste(image, (x, y + label))
        seconds = int(name[:-4]) / 1000
        draw.text((x + 3, y + 1), "%d:%05.2f (%.1f)" % (seconds // 60, seconds % 60, seconds), fill=(255, 255, 0))
    sheet.save(out)
    print(out, len(images), sheet.size)


if __name__ == "__main__":
    main()
