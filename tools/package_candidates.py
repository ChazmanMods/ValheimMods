from pathlib import Path
import json, hashlib, zipfile

root=Path(__file__).resolve().parents[1]
output=root.parent/'Test Builds'/'Runic-Languages-20260923'
output.mkdir(parents=True,exist_ok=True)
catalog=json.loads((root/'CANDIDATES.json').read_text())
records=[]
for module,details in catalog.items():
    source=root/module;version=details['version'];assembly=details['assembly']
    manifest=json.loads((source/'manifest.json').read_text())
    heading=f'## {version}\n\n'
    changes='- Added per-mod language files using Valheim\'s selected language, with English fallback and no new plugin dependency.\n'
    if module=='RunicPortals':changes+='- Destination pins no longer force a delayed map zoom-out or recenter while choosing a portal.\n'
    if module=='RunicLilyPads':changes+='- Removed inherited floor/building usage tags from garden prefabs; retained the Water Garden category.\n- Added native localization tokens for garden piece names and descriptions.\n'
    changelog=source/'CHANGELOG.md';old=changelog.read_text(encoding='utf-8-sig') if changelog.exists() else ''
    if heading not in old:changelog.write_text(heading+changes+'\n'+old,encoding='utf-8')
    readme=source/'README.md';text=readme.read_text(encoding='utf-8-sig')
    if '## Language files' not in text:
        text+='\n\n## Language files\n\nThis version follows Valheim\'s selected language using files in `Translations/'+assembly+'` beside the DLL. Missing translations fall back to English. Copy `English.json` to the selected language name and translate its values. See `TRANSLATING.md`. No additional translation plugin is required.\n'
        readme.write_text(text,encoding='utf-8')
    dll=source/'bin/Release/netstandard2.1'/(assembly+'.dll')
    language=source/'Translations'/assembly/'English.json'
    filename='Chazman-'+manifest['name']+'-'+version+'.zip'
    archive=output/filename
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for name in ['manifest.json','README.md','CHANGELOG.md','icon.png','THIRD_PARTY.md','USER_GUIDE.md']:
            if (source/name).exists():z.write(source/name,name)
        z.write(root/'LICENSE','LICENSE')
        z.write(root/'TRANSLATING.md','TRANSLATING.md')
        for file in source.glob('*.example*'):z.write(file,file.name)
        z.write(dll,'plugins/'+assembly+'/'+dll.name)
        z.write(language,'plugins/'+assembly+'/Translations/'+assembly+'/English.json')
    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None
        packaged=json.loads(z.read('manifest.json'))
        assert packaged['version_number']==version
        assert z.read('plugins/'+assembly+'/'+dll.name)==dll.read_bytes()
        assert json.loads(z.read('plugins/'+assembly+'/Translations/'+assembly+'/English.json'))==json.loads(language.read_text())
        assert sum(n.endswith('.dll') for n in z.namelist())==1
    records.append({'module':module,'version':version,'file':filename,'english_entries':len(json.loads(language.read_text())),
        'dll_sha256':hashlib.sha256(dll.read_bytes()).hexdigest(),'zip_sha256':hashlib.sha256(archive.read_bytes()).hexdigest()})
    print('PACKAGED',filename,flush=True)
(output/'PACKAGE-AUDIT.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
(output/'SHA256SUMS.txt').write_text(''.join(v['zip_sha256']+'  '+v['file']+'\n' for v in records),encoding='utf-8')
(output/'TRANSLATING.md').write_text((root/'TRANSLATING.md').read_text(),encoding='utf-8')
lines=['# Language and menu-fix candidates','',
       'All 22 mods compile. Automated localization, binary/catalog, and existing WaterGardens tests pass. In-game validation of these new candidates is still required.','',
       'Install only the mods you already use. Sentinel and SentinelServer are alternative packages, not a pair to install together. These ZIPs have not been published or deployed to the server.','',
       'Source: `E:\\Valheim Mods\\RunicLocalization`. Translation and testing instructions: `TRANSLATING.md`.','',
       '| Mod | Version | English entries |','| --- | --- | --- |']
lines += ['| '+v['module']+' | '+v['version']+' | '+str(v['english_entries'])+' |' for v in records]
(output/'START-HERE.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
