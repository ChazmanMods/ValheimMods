from pathlib import Path
import zipfile
root=Path(__file__).resolve().parent
for name in ['AW_Copper_Pipe_2m','AW_Copper_Elbow_90','AW_Copper_Intake','AW_Copper_Pipe_Surtling']:
    target=root/name
    target.mkdir(exist_ok=True)
    with zipfile.ZipFile(Path(r'C:\Users\Charles Sammons\Downloads')/(name+'.zip')) as archive:
        for item in archive.infolist():
            if not (target/item.filename).resolve().is_relative_to(target.resolve()):raise ValueError('Unsafe archive path')
        archive.extractall(target)
print('Extracted four plumbing models')
