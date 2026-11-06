import time
from paint import *
import shirt as S
t=time.time()
near = S.Sleeve((46,198,0),(1,0.42,-0.5),96,16,18.5,15.5,seed=1)
far = S.Sleeve((-46,196,0),(-1,0.10,0.5),86,7,17,14.5,seed=2)
navy=hexc('#16224a')
out=blank()
for q in (far.quads(), S.torso_quads(), near.quads()):
    L=S.shade_layers(*S.draw_quads(q))
    out.alpha_composite(with_outline(L,navy,2.2))
print(time.time()-t)
bg=Image.new('RGBA',out.size,(200,205,210,255)); bg.alpha_composite(out)
downsample(bg,2).save('t_shirt.png')
