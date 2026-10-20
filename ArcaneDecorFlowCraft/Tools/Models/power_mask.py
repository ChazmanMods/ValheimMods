import bpy,numpy as np,struct
from pathlib import Path
r=Path(__file__).resolve().parents[2]/'Assets/Plumbing'
im=bpy.data.images.load(str(r/'powered.jpg'));w,h=im.size;pixels=np.empty(w*h*4,dtype=np.float32);im.pixels.foreach_get(pixels);a=pixels.reshape((h,w,4))
mask=(a[:,:,0]>.3)&(a[:,:,0]>a[:,:,1]*2.4)&(a[:,:,0]>a[:,:,2]*2)&(a[:,:,1]<.24)
# Restrict emission to the three UV islands belonging to the red orb.
xx=np.arange(w)[None,:]/w;yy=1-np.arange(h)[:,None]/h
islands=((xx>.845)&(xx<.913)&(yy>.294)&(yy<.368))|((xx>.765)&(xx<.87)&(yy>.37)&(yy<.445))|((xx>.663)&(xx<.735)&(yy>.464)&(yy<.535))
mask=((a[:,:,0]>.5)&(a[:,:,0]>a[:,:,1]*1.6)&(a[:,:,2]<.22))&islands
out=np.zeros_like(a);out[:,:,:3]=mask[:,:,None];out[:,:,3]=1
img=bpy.data.images.new('orb emission mask',w,h);img.pixels.foreach_set(out.ravel());img.filepath_raw=str(r/'powered_glow.png');img.file_format='PNG';img.save()
d=(r/'powered.meshbin').read_bytes();_,n,t=struct.unpack_from('<iii',d);verts=np.array([struct.unpack_from('<8f',d,12+i*32) for i in range(n)]);uv=verts[:,6:];x=(uv[:,0]*w).astype(int).clip(0,w-1);y=(uv[:,1]*h).astype(int).clip(0,h-1);v=verts[mask[y,x],:3];print('ORB',len(v),v.min(axis=0),v.max(axis=0),np.median(v,axis=0))
