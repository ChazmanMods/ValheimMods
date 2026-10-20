import json,struct,zlib
from pathlib import Path
def stable(text):
    a=b=5381
    for i,c in enumerate(text):
        if i%2:a2=b; b=((b*33)^ord(c))&0xffffffff
        else:a=((a*33)^ord(c))&0xffffffff
    return (a+b*1566083941)&0xffffffff
key=struct.pack('<I',stable('HearthBelow_VoxelOps'))
roots={
'pre_install':'/home/valheim/backups/pre-hearthbelow-20260924-JE3LmNHe/snapshot/Valheim-state/worlds_local/RunicValheim10Test',
'at_removal':'/home/valheim/backups/remove-hearthbelow-20260924-xC8s0hny/Valheim-state/worlds_local/RunicValheim10Test',
'latest_stopped_snapshot':'/home/valheim/mod-delta-staging/arcanewaters-0.2.0-20260924/backup/worlds_local/RunicValheim10Test'}
result={}
for label,root in roots.items():
    files=list(Path(root).glob('*.chunk'));rows=[];hits=0;total=0
    for p in files:
        data=p.read_bytes();total+=len(data);offset=0
        while True:
            offset=data.find(key,offset)
            if offset<0:break
            hits+=1;at=offset;offset+=4
            if at+8>len(data):continue
            n=struct.unpack_from('<i',data,at+4)[0]
            if n<0 or n>2000000 or at+8+n>len(data):continue
            if n==0:rows.append({'file':p.name,'bytes':0,'operations':0});continue
            compressed=data[at+8:at+8+n]
            for mode in (31,15,-15):
                try:
                    raw=zlib.decompress(compressed,mode)
                    version,count=struct.unpack_from('<ii',raw)
                    if 1<=version<=8 and 0<=count<=5000:rows.append({'file':p.name,'bytes':n,'operations':count,'version':version})
                    break
                except (zlib.error,struct.error):pass
    result[label]={'chunks':len(files),'chunk_bytes':total,'raw_key_matches':hits,'validated_payloads':len(rows),'compressed_payload_bytes':sum(r['bytes'] for r in rows),'operations':sum(r['operations'] for r in rows),'largest_payloads':sorted(rows,key=lambda r:r['bytes'],reverse=True)[:5]}
print(json.dumps(result,indent=2))
