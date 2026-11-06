import sys
from PIL import Image
from paint import W, H
import sketch_overlay as SO
img = Image.open(sys.argv[1]).convert('RGBA').resize((W * 2, H * 2))
lines = SO.load(W, H)
red = Image.new('RGBA', img.size, (255, 0, 40, 255))
red.putalpha(lines.point(lambda v: int(v * 0.9)))
bg = Image.new('RGBA', img.size, (235, 235, 225, 255))
bg.alpha_composite(img)
bg.alpha_composite(red)
bg.save(sys.argv[2])
