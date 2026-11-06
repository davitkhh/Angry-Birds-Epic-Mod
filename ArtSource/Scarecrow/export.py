import json, math, os, subprocess
from PIL import Image
from paint import W, H, SS, downsample
import scarecrow as SC
parts = {k: Image.open('out/_ss_%s.png' % k) for k in SC.ORDER}
os.makedirs('out/parts', exist_ok=True)
man = {'pivot_hr': SC.PIVOT, 'order_back_to_front': SC.ORDER, 'parts': {}}
for k in SC.ORDER:
    for scale, tag in ((1, 'hr'), (2, '2x')):
        im = downsample(parts[k], scale)
        bb = im.getbbox()
        im.crop(bb).save('out/parts/Scarecrow_%s_%s.png' % (k, tag))
        if tag == 'hr':
            man['parts'][k] = {'x': bb[0], 'y': bb[1], 'w': bb[2] - bb[0], 'h': bb[3] - bb[1]}
json.dump(man, open('out/parts/manifest.json', 'w'), indent=1)
# sway preview: whole figure rotates about the pole base, head lags a little
p2 = {k: downsample(v, 2) for k, v in parts.items()}
os.makedirs('out/frames', exist_ok=True)
N = 48
for i in range(N):
    t = i / N * 2 * math.pi
    ang = 4.0 * math.sin(t)
    lag = 1.2 * math.sin(t - 0.9) - 1.2 * math.sin(t) * 0.5
    fr = Image.new('RGBA', (W * 2, H * 2), (196, 214, 170, 255))
    for k in SC.ORDER:
        L = p2[k]
        if k != 'shadow':
            a = ang + (lag if k in SC.HEADS else 0)
            if k in ('hat_back', 'hat_front'):
                a += 0.6 * math.sin(t - 1.6)
            L = L.rotate(a, resample=Image.BICUBIC, center=(SC.PIVOT[0] * 2, SC.PIVOT[1] * 2))
        fr.alpha_composite(L)
    fr.convert('RGB').save('out/frames/f%02d.png' % i)
subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-framerate', '16', '-i', 'out/frames/f%02d.png',
                '-vf', 'scale=340:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse', 'out/sway_preview.gif'])
