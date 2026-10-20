from pathlib import Path
import struct,numpy as np,json
root=Path(__file__).resolve().parents[1];p=root/'Assets/bed.meshbin';data=p.read_bytes();magic,n,t=struct.unpack_from('<iii',data)
a=np.frombuffer(data,dtype='<f4',count=n*8,offset=12).reshape(n,8).copy();indices=np.frombuffer(data,dtype='<i4',count=t,offset=12+n*32).copy();tri=a[indices.reshape(-1,3),:3]
# Area-weighted normals at shared geometric positions, independent of UV seams.
_,inverse=np.unique(np.round(a[:,:3],6),axis=0,return_inverse=True)
face=np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]);sums=np.zeros((inverse.max()+1,3),dtype=np.float64)
for corner in range(3):np.add.at(sums,inverse[indices.reshape(-1,3)[:,corner]],face)
lengths=np.linalg.norm(sums,axis=1);assert (lengths>1e-10).all();smooth=(sums/lengths[:,None])[inverse].astype('<f4')
a[:,3:6]=smooth
# Shared shading makes identical position/normal/UV tuples share GPU vertices.
unique,remap=np.unique(a,axis=0,return_inverse=True);new_indices=remap[indices].astype('<i4')
# Triangle order/positions and texture UVs must be bit-identical to the input.
assert np.array_equal(unique[new_indices][:,[0,1,2,6,7]],a[indices][:,[0,1,2,6,7]])
with p.open('wb') as f:f.write(struct.pack('<iii',magic,len(unique),t));f.write(unique.astype('<f4').tobytes());f.write(new_indices.tobytes())
report={'before_vertices':int(n),'after_vertices':len(unique),'triangles':int(t//3),'geometry_uv_unchanged':True,'flat_triangle_fraction_before':1.0,'flat_triangle_fraction_after':float(np.mean(np.linalg.norm(smooth[indices.reshape(-1,3)[:,0]]-smooth[indices.reshape(-1,3)[:,1]],axis=1)<1e-6))}
(root/'Assets/smoothing-report.json').write_text(json.dumps(report,indent=2));print(report)
p=root/'Plugin.cs';s=p.read_text().replace('"0.1.0"','"0.1.1"')
s=s.replace('material.SetColor("_Color",Color.white);','''// Imported albedo already contains deep shadows; lift detail without emissive glow.
    material.SetColor("_Color",new Color(1.65f,1.65f,1.65f,1f));
    if(material.HasProperty("_ValueNoise"))material.SetFloat("_ValueNoise",0);
    if(material.HasProperty("_ValueNoiseVertex"))material.SetFloat("_ValueNoiseVertex",0);
    material.DisableKeyword("_NORMALMAP");''')
s=s.replace('bed.m_hoverOffset*=1.5f;','bed.m_hoverOffset*=1.5f;\n    // The imported headboard is at +Z, opposite the native bed animation orientation.\n    bed.m_spawnPoint.localRotation*=Quaternion.Euler(0,180,0);')
p.write_text(s)
p=root/'ArcaneDecorNorseFurnishings.csproj';p.write_text(p.read_text().replace('<Version>0.1.0</Version>','<Version>0.1.1</Version>').replace('<Compile Remove="Diagnostics/**/*.cs" />','<Compile Remove="Diagnostics/**/*.cs;Baseline-0.1.0/**/*.cs" />'))
p=root/'manifest.json';d=json.loads(p.read_text(encoding='utf-8-sig'));d['version_number']='0.1.1';p.write_text(json.dumps(d,indent=2))
for name in ['tools/package.py','Build-Package.ps1']:
 p=root/name;p.write_text(p.read_text().replace('0.1.0','0.1.1'))
