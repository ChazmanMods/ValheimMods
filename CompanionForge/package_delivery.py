"""Create the portable tool ZIP and audit the actual packaged entries."""
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

ROOT = Path(__file__).resolve().parent
DIST = ROOT / "dist"
DIST.mkdir(exist_ok=True)
demo_source = ROOT / "tests/fixtures/mannequin-build/dist/forge_mannequin-1.0.0.zip"
demo_target = DIST / "ForgeMannequin-1.0.0.zip"
shutil.copy2(demo_source, demo_target)
files = [ROOT / name for name in ("forge.py", "wizard.py", "Launch Companion Forge.cmd", "README.md", "LICENSE")]
files += list((ROOT / "Unity").glob("*.cs"))
files += list((ROOT / "Runtime").glob("*.cs")) + list((ROOT / "Runtime").glob("*.csproj"))
files += list((ROOT / "Examples/Mannequin").glob("*"))
files += [ROOT / "tests/test_forge.py", ROOT / "tests/make_fixture.py"]
target = DIST / "CompanionForge-1.0.0.zip"
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as z:
    for f in sorted(files):
        if f.is_file():
            z.write(f, "CompanionForge/" + f.relative_to(ROOT).as_posix())
with zipfile.ZipFile(target) as z:
    assert z.testzip() is None
    assert not any(name.endswith("Local.props") or "/obj/" in name or "/bin/" in name for name in z.namelist())
    for f in files:
        if f.is_file():
            assert z.read("CompanionForge/" + f.relative_to(ROOT).as_posix()) == f.read_bytes()
    example = json.loads(z.read("CompanionForge/Examples/Mannequin/mannequin.json"))
    assert not any(example[key] for key in ("game", "bepinex", "unity"))
with zipfile.ZipFile(demo_target) as z:
    assert z.testzip() is None
    base = "BepInEx/plugins/CompanionForge_forge_mannequin/"
    assert z.read(base + "companionassets").startswith(b"UnityFS")
    assert z.read(base + "CompanionForge.Runtime.dll").startswith(b"MZ")
    config = json.loads(z.read(base + "companion.json"))
    assert config["id"] == "forge_mannequin" and config["adult"]
    # Human-audited documents are printed here for the release audit; reports are outside the ZIP.
    for name in z.namelist():
        if name.endswith((".md", ".txt", ".json")):
            text = z.read(name).decode("utf-8")
            print("DOCUMENT", name)
            print(text)
            if name.endswith("README.md"):
                for phrase in ("experimental", "unvalidated", "not tested", "workaround", "validation report"):
                    assert phrase not in text.lower(), phrase
    assert not any(name.endswith("icon.png") for name in z.namelist())
for path in (target, demo_target):
    print("PASS", path, path.stat().st_size, "bytes", hashlib.sha256(path.read_bytes()).hexdigest())
