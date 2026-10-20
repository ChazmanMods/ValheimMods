from pathlib import Path
import struct,numpy as np,json
from PIL import Image,ImageDraw
root=Path(__file__).resolve().parents[1];d=(root/'Assets/bed.meshbin').read_bytes();magic,n,t=struct.unpack_from('<iii',d)
assert magic==0x57474D31 and len(d)==12+n*32+t*4
v=np.frombuffer(d,dtype='<f4',count=n*8,offset=12).reshape(n,8);indices=np.frombuffer(d,dtype='<i4',count=t,offset=12+n*32)
assert np.isfinite(v).all() and t%3==0 and indices.min()>=0 and indices.max()<n
assert np.all(np.linalg.norm(v[:,3:6],axis=1)>.99) and np.all(np.linalg.norm(v[:,3:6],axis=1)<1.01)
assert v[:,6:].min()>=-.001 and v[:,6:].max()<=1.001
print('PASS imported mesh: finite vertices, unit normals, UVs, index range, complete binary;',t//3,'triangles')
im=Image.open(root/'Assets/bed-texture.png');print('PASS source texture:',im.size)
icon=Image.open(root/'Assets/bed-icon.png').convert('RGBA');preview=Image.new('RGB',(768,256))
for col,color in enumerate(['white','#181818','#aaaaaa']):
 bg=Image.new('RGBA',(256,256),color)
 if col==2:
  draw=ImageDraw.Draw(bg)
  for y in range(0,256,16):
   for x in range(0,256,16):
    if (x//16+y//16)%2:draw.rectangle((x,y,x+15,y+15),fill='#eeeeee')
 bg.alpha_composite(icon);preview.paste(bg.convert('RGB'),(col*256,0))
preview.save(root/'Assets/bed-icon-background-review.png')

