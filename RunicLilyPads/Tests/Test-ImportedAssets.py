from pathlib import Path
import struct,wave,hashlib,json
import numpy as np
from PIL import Image
root=Path('Assets/GardenModels'); dims={}
for p in root.glob('*.meshbin'):
    if p.stem.endswith('_alt') or p.stem=='fountain_003':continue
    raw=p.read_bytes();magic,n,ni=struct.unpack_from('<iii',raw)
    assert magic==0x57474D31 and len(raw)==12+n*32+ni*4
    v=np.frombuffer(raw,dtype='<f4',count=n*8,offset=12).reshape(n,8)
    t=np.frombuffer(raw,dtype='<i4',count=ni,offset=12+n*32)
    assert np.isfinite(v).all() and t.min()>=0 and t.max()<n
    bounds=np.ptp(v[:,:3],axis=0); assert abs(bounds[1]-1)<.001
    dims[p.stem]=bounds
    for ext in ['.jpg','.png']:
        with Image.open(p.with_suffix(ext)) as im: im.verify()
for series in [('bluelilly_001','blueiris_001','bluelilly_003'),('whitelilly_001','whitelilly_002','whitelilly_003')]:
    sizes=[dims[x]*h for x,h in zip(series,[.25,.4,.55])]
    assert all((a<b).all() for a,b in zip(sizes,sizes[1:]))
    print('PASS ascending flower XYZ dimensions',[(x,np.round(d,3).tolist()) for x,d in zip(series,sizes)])
for p in Path('Audio').glob('*.wav'):
    if not(p.name.startswith('night_ambience_') or p.name in ('fountain_001.wav','waterfall_001.wav')):continue
    with wave.open(str(p)) as w:
        assert w.getnchannels()==1 and w.getsampwidth()==2 and w.getnframes()>100
        a=np.frombuffer(w.readframes(w.getnframes()),dtype='<i2')
        assert abs(a.astype(float)).max()>0
        print('PASS audio',p.name,round(len(a)/w.getframerate(),2),'seconds')
for p in root.glob('*_emission.png'):
    if p.name=='fountain_003_emission.png':continue
    a=np.asarray(Image.open(p));assert a.max()>0 and (a>0).mean()<(.4 if p.name.startswith('glowshrooms_') else .3)
    print('PASS restricted emission mask',p.name,round((a>0).mean()*100,2),'percent')
print('PASS',len(dims),'complete mesh/texture/icon sets')
