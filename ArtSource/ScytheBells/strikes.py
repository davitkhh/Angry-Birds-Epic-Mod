"""Finds the strikes in bell_ring.wav and prints onset, gap to the next strike, level and main partials per strike.
Run: ffmpeg -i Assets/Resources/Sound/Characters/Generic/bell_ring.wav -f s16le -acodec pcm_s16le /tmp/bell.raw; python3 strikes.py /tmp/bell.raw
"""
import array, math, cmath, sys
a=array.array('h'); a.frombytes(open(sys.argv[1],'rb').read())
sr=48000; m=max(abs(v) for v in a); x=[v/m for v in a]
hop=sr//200
db=[10*math.log10(sum(v*v for v in x[i:i+hop])/hop+1e-12) for i in range(0,len(x)-hop,hop)]
on=[]
for i in range(4,len(db)):
    if db[i]-min(db[i-4:i])>8 and db[i]>-35 and (not on or i-on[-1]>12): on.append(i)
def fft(v):
    n=len(v)
    if n==1: return v
    e=fft(v[0::2]); o=fft(v[1::2])
    t=[cmath.exp(-2j*math.pi*k/n)*o[k] for k in range(n//2)]
    return [e[k]+t[k] for k in range(n//2)]+[e[k]-t[k] for k in range(n//2)]
N=8192
res=[]
for j,o in enumerate(on):
    s=o*hop; nxt=on[j+1]*hop if j+1<len(on) else len(x)
    peak=max(db[o:o+6])
    # decay: dB drop over 0.15 s if room
    seg=x[s+int(0.01*sr):s+int(0.01*sr)+N]
    if len(seg)<N: continue
    w=[seg[k]*(0.5-0.5*math.cos(2*math.pi*k/N)) for k in range(N)]
    F=[abs(c) for c in fft(w)[:N//2]]
    lo=int(300*N/sr)
    pk=sorted(range(lo,N//2),key=lambda k:-F[k])
    peaks=[]
    for k in pk:
        if all(abs(k-p)>15 for p in peaks): peaks.append(k)
        if len(peaks)==4: break
    # centroid
    cen=sum(k*F[k] for k in range(lo,N//2))/sum(F[lo:N//2])*sr/N
    res.append((o*hop/sr,(nxt-s)/sr,peak,[round(p*sr/N) for p in peaks],round(cen)))
for r in res: print(f"{r[0]:6.3f} gap {r[1]:.3f} peak {r[2]:6.1f}  partials {r[3]} centroid {r[4]}")
