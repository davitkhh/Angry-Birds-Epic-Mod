# Map the user's pencil sketch into HR canvas coords (pole bottom/hem anchored) -> line mask
import sys
from PIL import Image, ImageOps, ImageFilter, ImageChops
K = 0.274
PY = 372   # pole bottom (pivot) y in HR
def load(W, H, scale=2):
    im = ImageOps.exif_transpose(Image.open('/home/davit/Downloads/gmail_images20261030_181223.png')).convert('L')
    im = im.resize((1500, 2000), Image.LANCZOS)
    bgd = im.filter(ImageFilter.GaussianBlur(20))
    d = ImageChops.subtract(bgd, im).point(lambda v: 0 if v < 6 else min(255, (v - 6) * 14))
    a = 1 / (K * scale)
    return d.transform((W * scale, H * scale), Image.AFFINE, (a, 0, 545 - 165 / K, 0, a, 1700 - PY / K), Image.BILINEAR)
if __name__ == '__main__':
    load(390, 390).save('out/sketch_lines_2x.png')
