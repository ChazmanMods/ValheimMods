from pathlib import Path
import hashlib, json, re, struct, zipfile

root=Path(__file__).resolve().parents[1]
output=root.parent/'Thunderstore Uploads'/'Runic-All-Mods-20260924-Final'
output.mkdir(parents=True,exist_ok=True)
catalog=json.loads((root/'CANDIDATES.json').read_text(encoding='utf-8'))
versions={json.loads((root/module/'manifest.json').read_text(encoding='utf-8'))['name']:item['version'] for module,item in catalog.items()}
records=[]

def digest(data): return hashlib.sha256(data).hexdigest()
def package(module, details=None):
    source=root/module
    manifest=json.loads((source/'manifest.json').read_text(encoding='utf-8'))
    name=manifest['name'];version=manifest['version_number']
    assert re.fullmatch(r'\d+\.\d+\.\d+',version)
    assert len(manifest['description'])<=250
    icon=source/'icon.png'
    if not icon.exists():icon=source/'media/icon.png'
    assert struct.unpack('>II',icon.read_bytes()[16:24])==(256,256),(module,'icon size')
    filename=f'Chazman-{name}-{version}.zip'
    files={'manifest.json':source/'manifest.json','icon.png':icon,
           'README.md':source/'README.md','CHANGELOG.md':source/'CHANGELOG.md',
           'LICENSE':source/'LICENSE' if (source/'LICENSE').exists() else root/'LICENSE'}
    for extra in ['NOTICE.md','THIRD_PARTY.md','THIRD-PARTY-NOTICES.txt','USER_GUIDE.md','INSTRUCTIONS.md','CATALOG.md']:
        if (source/extra).exists():files[extra]=source/extra
    for file in source.glob('*.example*'):files[file.name]=file
    dll_hash=None
    if details:
        assembly=details['assembly']
        dll=source/'bin/Release/netstandard2.1'/(assembly+'.dll')
        files[f'plugins/{assembly}/{assembly}.dll']=dll
        files['TRANSLATING.md']=root/'TRANSLATING.md'
        files[f'plugins/{assembly}/Translations/{assembly}/English.json']=source/'Translations'/assembly/'English.json'
        dll_hash=digest(dll.read_bytes())
        assert version==details['version']
    archive=output/filename
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for target,file in sorted(files.items()):z.writestr(target,file.read_bytes())
    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None
        assert len(z.namelist())==len(set(z.namelist()))
        assert all('\\' not in n and not n.startswith('/') and '..' not in n.split('/') for n in z.namelist())
        for target,file in files.items():assert z.read(target)==file.read_bytes()
        assert sum(n.endswith('.dll') for n in z.namelist())==(1 if details else 0)
        assert json.loads(z.read('manifest.json'))==manifest
        if module=='RunicCharacterVault':assert 'NOTICE.md' in z.namelist() and b'MIT' in z.read('LICENSE')
    records.append(dict(module=module,name=name,version=version,file=filename,sha256=digest(archive.read_bytes()),dll_sha256=dll_hash,dependencies=manifest['dependencies']))
    print('VERIFIED',filename,flush=True)

for module,details in catalog.items():package(module,details)

for module in ['RunicModSuite','RunicModClientSuite','RunicModServerSuite']:
    source=root/module
    manifest=json.loads((source/'manifest.json').read_text(encoding='utf-8'))
    version=manifest['version_number']
    manifest['version_number']=version
    updated=[]
    for dep in manifest['dependencies']:
        author,name,old=dep.split('-',2)
        updated.append(f'{author}-{name}-{versions[name]}' if author=='Chazman' and name in versions else dep)
    manifest['dependencies']=updated
    (source/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    p=source/'CHANGELOG.md';text=p.read_text(encoding='utf-8-sig')
    if f'## {version}\n' not in text:text=f'## {version}\n\n- Updated included mods for language-file support and the September 24 fixes.\n\n'+text
    p.write_text(text,encoding='utf-8')
    p=source/'README.md';text=p.read_text(encoding='utf-8-sig')
    for name,new in versions.items():text=re.sub(rf'(Chazman-{re.escape(name)}-)\d+\.\d+\.\d+',rf'\g<1>{new}',text)
    p.write_text(text,encoding='utf-8');package(module)

# Dependency order ensures suite uploads never point at versions still waiting to be uploaded.
remaining={r['name']:r for r in records};ordered=[]
while remaining:
    ready=[r for r in remaining.values() if not any(dep.split('-')[1] in remaining for dep in r['dependencies'] if dep.startswith('Chazman-'))]
    assert ready,'Internal package dependency cycle'
    ready.sort(key=lambda r:(r['dll_sha256'] is None,r['name']))
    for record in ready:ordered.append(record);remaining.pop(record['name'])
release_records=root/'artifacts'/'release-records-20260924-final'
release_records.mkdir(parents=True,exist_ok=True)
(release_records/'PACKAGE-AUDIT.json').write_text(json.dumps(ordered,indent=2)+'\n',encoding='utf-8')
(release_records/'SHA256SUMS.txt').write_text(''.join(r['sha256']+'  '+r['file']+'\n' for r in ordered),encoding='utf-8')
lines=['# September 24 Runic upload set','',
       '22 mods (including ArcaneDecor WaterGardens) and 3 suites. Upload each ZIP individually in the order below.',
       '', '## Upload order','', '| # | Package | Version |','|---|---|---|']
lines += [f'| {i} | [{r["name"]}]({r["file"]}) | {r["version"]} |' for i,r in enumerate(ordered,1)]
lines += ['', 'Use the ZIPs in this folder for this release.','']
(output/'START-HERE.md').write_text('\n'.join(lines),encoding='utf-8')
print('TOTAL',len(records),'packages')
