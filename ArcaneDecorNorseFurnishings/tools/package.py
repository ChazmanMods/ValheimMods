from pathlib import Path
import zipfile,json,hashlib,shutil
root=Path(__file__).resolve().parents[1];out=root/'dist';out.mkdir(exist_ok=True)
zip_path=out/'Chazman-ArcaneDecor_NorseFurnishings-0.1.1-PRIVATE-TEST.zip'
dll=root/'bin/Release/netstandard2.1/ArcaneDecorNorseFurnishings.dll'
with zipfile.ZipFile(zip_path,'w',zipfile.ZIP_DEFLATED) as z:
 for name in ['manifest.json','README.md','CHANGELOG.md','VALIDATION.md','icon.png']:z.write(root/name,name)
 z.write(dll,'plugins/ArcaneDecorNorseFurnishings/ArcaneDecorNorseFurnishings.dll')
with zipfile.ZipFile(zip_path) as z:
 assert z.testzip() is None
 assert z.read('icon.png')==(root/'icon.png').read_bytes()
 assert z.read('plugins/ArcaneDecorNorseFurnishings/ArcaneDecorNorseFurnishings.dll')==dll.read_bytes()
 m=json.loads(z.read('manifest.json').decode('utf-8-sig'));assert m['version_number']=='0.1.1'
 assert len([n for n in z.namelist() if n.endswith('.dll')])==1
 (out/'extracted-icon.png').write_bytes(z.read('icon.png'))
sha=hashlib.sha256(zip_path.read_bytes()).hexdigest();Path(str(zip_path)+'.sha256').write_text(sha+'  '+zip_path.name+'\n')
(out/'package-checks.txt').write_text('PASS ZIP CRC, manifest version, only production DLL, DLL source byte match, final extracted icon source byte match.\nSHA256 '+sha+'\n')
print(zip_path)
