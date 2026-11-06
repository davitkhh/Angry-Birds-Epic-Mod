"""Composite the bucket variants onto the bare PigPirate render (Temp/PirateHeadgearProbe_0.png) for checking.
Optional --sketch draws the user's outline sketches on top.  -> out/preview.png"""
import json
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(HERE, 'out')
BODY_TO_RENDER = (151, 220)  # body px -> probe render px


def bare_pig():
    im = Image.open(os.path.join(ROOT, 'Temp', 'PirateHeadgearProbe_0.png')).convert('RGBA')
    # render stores alpha squared with premultiplied colour; recover straight colour + coverage
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a:
                cov = (a / 255) ** 0.5
                px[x, y] = tuple(min(255, int(c / cov)) for c in (r, g, b)) + (int(cov * 255),)
    return im


def main():
    sketch = '--sketch' in sys.argv
    manifest = json.load(open(os.path.join(OUT, 'manifest.json')))
    strokes = json.load(open(os.path.join(HERE, 'sketch_strokes.json')))
    pig = bare_pig()
    tiles = []
    for i, (name, meta) in enumerate(sorted(manifest.items())):
        tile = Image.new('RGBA', pig.size, (178, 178, 178, 255))
        ox = BODY_TO_RENDER[0] + meta['origin'][0]
        oy = BODY_TO_RENDER[1] + meta['origin'][1]
        if meta['back']:
            tile.alpha_composite(Image.open(os.path.join(OUT, name + '_Back.png')), (ox, oy))
        tile.alpha_composite(pig)
        tile.alpha_composite(Image.open(os.path.join(OUT, name + '.png')), (ox, oy))
        if sketch:
            d = ImageDraw.Draw(tile)
            for u, v in strokes[str(i + 1)]['brown']:
                d.point((BODY_TO_RENDER[0] + u, BODY_TO_RENDER[1] + v), fill=(255, 0, 255, 255))
        tiles.append(tile.crop((60, 40, 420, 430)))
    w, h = tiles[0].size
    sheet = Image.new('RGBA', (w * len(tiles), h), (178, 178, 178, 255))
    for i, t in enumerate(tiles):
        sheet.alpha_composite(t, (i * w, 0))
    sheet.save(os.path.join(OUT, 'preview_sketch.png' if sketch else 'preview.png'))


if __name__ == '__main__':
    main()
