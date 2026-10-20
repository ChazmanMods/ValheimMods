from PIL import Image
from pathlib import Path
import struct,json
p=Path('ArcaneDecorNorseFurnishings/Assets/bed.meshbin');data=p.read_bytes();magic,n,nt=struct.unpack_from('<iii',data);unique={};verts=[];remap=[]
for i in range(n):
 row=data[12+i*32:12+(i+1)*32]
 if row not in unique:unique[row]=len(verts);verts.append(row)
 remap.append(unique[row])
tri=struct.unpack_from('<%di'%nt,data,12+n*32)
with p.open('wb') as f:
 f.write(struct.pack('<iii',magic,len(verts),nt));f.write(b''.join(verts));f.write(struct.pack('<%di'%nt,*(remap[i] for i in tri)))
print('Exact vertex deduplication:',n,'->',len(verts),'vertices;',nt//3,'triangles preserved')
Image.open('ArcaneDecorNorseFurnishings/Assets/model-preview.png').resize((256,256),Image.Resampling.LANCZOS).save('ArcaneDecorNorseFurnishings/Assets/bed-icon.png')
