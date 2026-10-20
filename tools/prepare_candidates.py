from pathlib import Path
import json, re, subprocess

root=Path(__file__).resolve().parents[1]
baseline=json.loads((root/'SOURCE-BASELINE.json').read_text(encoding='utf-8'))
catalogs={}
for module in baseline:
    assembly='ArcaneDecorWaterGardens' if module=='RunicLilyPads' else module
    path=root/module/'Translations'/assembly/'English.json'
    catalogs.update(json.loads(path.read_text(encoding='utf-8')))
report={}
for module, original in baseline.items():
    project=root/module/(module+'.csproj')
    evaluated=json.loads(subprocess.check_output(['dotnet','msbuild',str(project),'-getItem:Compile','-nologo'],encoding='utf-8'))
    paths=[Path(i['FullPath']) for i in evaluated['Items']['Compile']]
    keys=set()
    for path in paths:
        if path.exists():
            keys.update(re.findall(r'(?:RunicText\.(?:Get|Format|English)|GardenLanguage.Token)\("([^"]+)"',path.read_text(encoding='utf-8-sig')))
    if module in ("RunicSentinel", "RunicSentinelServer"):
        keys.update(k for k in catalogs if k.startswith("catalog_"))
    # The tooltip helper uses fixed English identifiers to find the corresponding display entry.
    if module=='RunicStorage':
        own=json.loads((root/module/'Translations'/module/'English.json').read_text(encoding='utf-8'))
        labels=(root/module/'Runtime/ChestRulesHelp.cs').read_text(encoding='utf-8')
        keys.update(k for k,v in own.items() if json.dumps(v) in labels)
    if module=='RunicLilyPads': keys.add('menu_water_garden')
    missing=keys-catalogs.keys()
    if missing: raise RuntimeError((module,missing))
    assembly='ArcaneDecorWaterGardens' if module=='RunicLilyPads' else module
    language=root/module/'Translations'/assembly/'English.json'
    language.write_text(json.dumps({k:catalogs[k] for k in sorted(keys)},indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    old=original['version']; parts=old.split('.');parts[-1]=str(int(parts[-1])+1);new='.'.join(parts)
    for path in paths:
        if not path.exists() or not path.is_relative_to(root/module) or 'obj' in path.parts:continue
        text=path.read_text(encoding='utf-8-sig')
        text=re.sub(r'((?:PluginVersion|\bVersion)\s*=\s*")'+re.escape(old)+r'(")',lambda m:m[1]+new+m[2],text)
        text=re.sub(r'(Assembly(?:File|Informational)?Version\(")'+re.escape(old)+r'(\.0)?("\))',lambda m:m[1]+new+(m[2] or '')+m[3],text)
        text=re.sub(r'(BepInPlugin\([^\n]+,\s*")'+re.escape(old)+r'("\))',lambda m:m[1]+new+m[2],text)
        path.write_text(text,encoding='utf-8')
    manifest=root/module/'manifest.json';data=json.loads(manifest.read_text(encoding='utf-8-sig'));data['version_number']=new
    manifest.write_text(json.dumps(data,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    report[module]={'version':new,'assembly':assembly,'entries':len(keys),'compile_files':[str(p.relative_to(root)) for p in paths if p.is_relative_to(root)]}
    print(module,new,len(keys),flush=True)
(root/'CANDIDATES.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
