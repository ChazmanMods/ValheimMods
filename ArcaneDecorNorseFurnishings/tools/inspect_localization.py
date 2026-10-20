import UnityPy
from pathlib import Path
e=UnityPy.load(r'E:/SteamLibrary/steamapps/common/Valheim/valheim_Data/resources.assets')
for o in e.objects:
 if o.type.name=='TextAsset':
  d=o.read(); s=d.m_Script
  if isinstance(s,bytes):s=s.decode('utf8',errors='replace')
  if 'item_frostwood' in s:
   for line in s.splitlines():
    if 'item_frostwood' in line:print(d.m_Name,line[:250])

