"""Local asset-to-Valheim pipeline. Python standard library only."""
from __future__ import annotations
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time
import uuid
import zipfile

ROOT = Path(__file__).resolve().parent
ROLES = ("idle", "walk", "run", "attack", "wave", "sit", "sleep", "pose")
REQUIRED_ROLES = ("idle", "walk", "run")


def default_project():
    return {"schema": 1, "name": "My Companion", "author": "LocalCreator", "id": "my_companion",
            "version": "1.0.0", "model": "", "animations": {r: {"path": "", "clip": ""} for r in ROLES},
            "icon": "", "animation_menu": [], "walk_clip_speed": 1.181818, "run_clip_speed": 3.214286,
            "materials": [], "scale": 1.0, "yaw": 0.0, "offset_y": 0.0, "texture_limit": 4096,
            "triangle_budget": 80000, "tint": "#ffffff", "greeting": "It is good to see you.",
            "affection": "I am glad we get to spend time together.", "adult": False,
            "credits": "", "rights_confirmed": False,
            "unity": "", "game": "", "bepinex": ""}


def save_project(path, project):
    path = Path(path).resolve()
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(project, indent=2, ensure_ascii=False), encoding="utf-8")
    temp.replace(path)


def load_project(path):
    path = Path(path).resolve()
    raw = json.loads(path.read_text(encoding="utf-8"))
    if raw.get("schema") != 1:
        raise ValueError("Unsupported project format. Use a Companion Forge schema 1 project.")
    p = default_project()
    p.update(raw)
    for role in ROLES:
        p["animations"].setdefault(role, {"path": "", "clip": ""})
    # Relative asset and tool paths are relative to the saved project, never the shell.
    for key in ("model", "icon", "unity", "game", "bepinex"):
        if p[key] and not Path(p[key]).is_absolute():
            p[key] = str((path.parent / p[key]).resolve())
    for a in [*p["animations"].values(), *p.get("animation_menu", [])]:
        if a["path"] and not Path(a["path"]).is_absolute():
            a["path"] = str((path.parent / a["path"]).resolve())
    for m in p["materials"]:
        for key in ("base", "normal", "metallic", "emission"):
            if m.get(key) and not Path(m[key]).is_absolute():
                m[key] = str((path.parent / m[key]).resolve())
    return p


def unity_game_version(game):
    f = Path(game) / "valheim_Data" / "globalgamemanagers"
    if not f.is_file():
        return ""
    matches = re.findall(rb"(?:20\d\d|6000)\.\d+\.\d+[fp]\d+", f.read_bytes())
    return matches[0].decode() if matches else ""


def detect_tools():
    game_candidates = [Path(r"E:\SteamLibrary\steamapps\common\Valheim"),
                       Path(r"C:\Program Files (x86)\Steam\steamapps\common\Valheim")]
    game = next((p for p in game_candidates if (p / "valheim_Data/Managed/assembly_valheim.dll").is_file()), None)
    versions = []
    for base in (ROOT.parent / "tools", Path(r"C:\Program Files\Unity\Hub\Editor")):
        if base.is_dir():
            versions.extend(base.glob("*/Editor/Unity.exe"))
    version = unity_game_version(game) if game else ""
    unity = next((p for p in versions if version in str(p)), None)
    appdata = Path(os.environ.get("APPDATA", "")) / "r2modmanPlus-local/Valheim/profiles"
    profiles = list(appdata.glob("*/BepInEx")) if appdata.is_dir() else []
    if game:
        profiles.append(game / "BepInEx")
    bepinex = next((p for p in profiles if (p / "core/BepInEx.dll").is_file()
                    and list((p / "plugins").rglob("Jotunn.dll"))), None)
    return {"game": str(game or ""), "unity": str(unity or ""), "bepinex": str(bepinex or "")}


def validate(p, build=False):
    errors = []
    if not re.fullmatch(r"[a-z][a-z0-9_]{2,47}", p.get("id", "")):
        errors.append("Stable ID must be 3–48 lowercase letters, digits or underscores, beginning with a letter.")
    if not re.fullmatch(r"\d+\.\d+\.\d+", p.get("version", "")):
        errors.append("Version must use major.minor.patch, for example 1.0.0.")
    for key in ("name", "author", "greeting", "affection"):
        if not isinstance(p.get(key), str) or not p[key].strip() or len(p[key]) > 500:
            errors.append(f"{key.title()} must contain 1–500 characters.")
    for key, lo, hi in (("scale", .25, 3), ("yaw", -180, 180), ("offset_y", -3, 3)):
        try:
            if not lo <= float(p[key]) <= hi:
                raise ValueError()
        except (KeyError, TypeError, ValueError):
            errors.append(f"{key} must be between {lo} and {hi}.")
    if p.get("texture_limit") not in (1024, 2048, 4096, 8192):
        errors.append("Texture limit must be 1024, 2048, 4096 or 8192.")
    try:
        if not 1000 <= int(p["triangle_budget"]) <= 500000:
            raise ValueError()
    except (KeyError, TypeError, ValueError):
        errors.append("Triangle budget must be between 1,000 and 500,000.")
    if not re.fullmatch(r"#[0-9a-fA-F]{6}", p.get("tint", "")):
        errors.append("Tint must be a hex color such as #ffffff.")
    if not p.get("adult"):
        errors.append("Confirm that this companion represents an adult.")
    if not p.get("rights_confirmed") or not p.get("credits", "").strip():
        errors.append("Record asset sources, authors, licenses and credits, and confirm permission to bundle them.")
    def file_check(value, extensions, label):
        f = Path(value) if value else None
        if f is None or not f.is_file() or f.suffix.lower() not in extensions:
            errors.append(f"{label}: choose an existing {' / '.join(extensions)} file.")
    file_check(p.get("model"), (".fbx",), "Rigged character model")
    for role in ROLES:
        a = p.get("animations", {}).get(role, {})
        if a.get("path") or role in REQUIRED_ROLES:
            file_check(a.get("path"), (".fbx", ".anim"), role.title() + " animation")
    menu_ids = set()
    for item in p.get("animation_menu", []):
        key = item.get("id", "")
        if not re.fullmatch(r"[a-z][a-z0-9_]{0,47}", key) or key in menu_ids:
            errors.append("Animation menu entries need unique lowercase IDs.")
        menu_ids.add(key)
        if not isinstance(item.get("label"), str) or not 1 <= len(item['label']) <= 80:
            errors.append("Animation menu entries need a name of 1–80 characters.")
        file_check(item.get('path'), ('.fbx', '.anim'), 'Menu ' + key)
    names = []
    for m in p.get("materials", []):
        name = m.get("name", "")
        if not name or name in names:
            errors.append("Each material assignment needs a unique imported material name.")
        names.append(name)
        for key in ("base", "normal", "metallic", "emission"):
            if m.get(key):
                file_check(m[key], (".png", ".jpg", ".jpeg", ".tga"), f"{name} {key}")
    file_check(p.get("unity"), (".exe",), "Unity Editor executable")
    version = unity_game_version(p.get("game", ""))
    if not version:
        errors.append("Choose the Valheim installation folder containing valheim_Data/globalgamemanagers.")
    elif version not in str(Path(p.get("unity", ""))):
        errors.append(f"This Valheim installation uses Unity {version}. Select its matching Editor/Unity.exe.")
    if build:
        if not shutil.which("dotnet"):
            errors.append("Install the .NET SDK and restart Companion Forge.")
        core = Path(p.get("bepinex", "")) / "core"
        for dll in ("BepInEx.dll", "0Harmony.dll"):
            if not (core / dll).is_file():
                errors.append(f"BepInEx profile is missing core/{dll}.")
        if not list((Path(p.get("bepinex", "")) / "plugins").rglob("Jotunn.dll")):
            errors.append("Install Jötunn in the selected BepInEx profile.")
    return errors


def fingerprint(p):
    data = copy.deepcopy(p)
    files = [p["model"]] + [a["path"] for a in p["animations"].values() if a["path"]]
    files += [a['path'] for a in p.get('animation_menu', [])]
    files += [m[k] for m in p["materials"] for k in ("base", "normal", "metallic", "emission") if m.get(k)]
    files += [str(f) for f in Path(p["model"]).parent.iterdir()
              if f.is_file() and f.suffix.lower() in (".png", ".jpg", ".jpeg", ".tga")]
    files += [value + ".meta" for value in list(files) if Path(value + ".meta").is_file()]
    hashes = {}
    for value in sorted(set(files)):
        h = hashlib.sha256()
        with open(value, "rb") as f:
            for chunk in iter(lambda: f.read(1024 * 1024), b""):
                h.update(chunk)
        hashes[value] = h.hexdigest()
    data["asset_hashes"] = hashes
    data["unity_builder"] = hashlib.sha256((ROOT / "Unity/ForgeBuilder.cs").read_bytes()).hexdigest()
    data["game_unity_version"] = unity_game_version(p["game"])
    return hashlib.sha256(json.dumps(data, sort_keys=True).encode()).hexdigest()


def run(command, log_path, emit, cancel=None):
    """No shell expansion. Worker thread handles all blocking tools."""
    emit("Running " + Path(command[0]).name)
    with open(log_path, "w", encoding="utf-8") as log:
        proc = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT,
                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        try:
            while proc.poll() is None:
                if cancel and cancel.is_set():
                    proc.terminate()
                    try:
                        proc.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        proc.kill()
                        proc.wait()
                    raise RuntimeError("Build cancelled. Saved projects and previous packages are intact.")
                time.sleep(.2)
        finally:
            if proc.poll() is None:
                proc.kill()
                proc.wait()
    if proc.returncode:
        tail = Path(log_path).read_text(encoding="utf-8", errors="replace")[-5000:]
        raise RuntimeError(f"{Path(command[0]).name} exited with {proc.returncode}. Log: {log_path}\n{tail}")


def prepare(p, workspace, emit=print, cancel=None):
    errors = validate(p)
    if errors:
        raise ValueError("\n".join(errors))
    workspace = Path(workspace).resolve()
    # Every import has a fresh directory: no stale bundle or report can satisfy a later build.
    project = workspace / "runs" / uuid.uuid4().hex / "UnityProject"
    assets = project / "Assets"
    (assets / "Editor").mkdir(parents=True)
    (project / "ProjectSettings").mkdir()
    (project / "Packages").mkdir()
    version = unity_game_version(p["game"])
    (project / "ProjectSettings/ProjectVersion.txt").write_text(f"m_EditorVersion: {version}\n")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}')
    shutil.copy2(ROOT / "Unity/ForgeBuilder.cs", assets / "Editor/ForgeBuilder.cs")
    spec = copy.deepcopy(p)
    def stage(path, folder):
        if not path:
            return ""
        src = Path(path)
        target = assets / "Source" / folder / src.name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, target)
        meta = Path(str(src) + ".meta")
        if meta.is_file():
            # Keep manually corrected Humanoid bone mappings, but assign a fresh GUID for each staged copy.
            text = meta.read_text(encoding="utf-8")
            text = re.sub(r"(?m)^guid: [0-9a-fA-F]{32}$", "guid: " + uuid.uuid4().hex, text)
            Path(str(target) + ".meta").write_text(text, encoding="utf-8")
        return target.relative_to(project).as_posix()
    spec["model"] = stage(p["model"], "Model")
    # Copy nearby textures for FBX material extraction; explicit assignments still take precedence.
    for src in Path(p["model"]).parent.iterdir():
        if src.is_file() and src.suffix.lower() in (".png", ".jpg", ".jpeg", ".tga"):
            stage(str(src), "Model")
    spec["animations"] = [{"role": r, "path": stage(p["animations"][r]["path"], f"Animations/{r}"),
                            "clip": p["animations"][r]["clip"]} for r in ROLES if p["animations"][r]["path"]]
    spec['animations'] += [dict(role='menu_'+a['id'], path=stage(a['path'], 'Menu/'+a['id']),
                                clip=a.get('clip',''), menu=True, loop=bool(a.get('loop',True)),
                                hold=bool(a.get('hold',False))) for a in p.get('animation_menu',[])]
    for i, m in enumerate(spec["materials"]):
        for key in ("base", "normal", "metallic", "emission"):
            m[key] = stage(m.get(key, ""), f"Textures/{i}/{key}")
    spec["output"] = str(project.parent / "Bundle")
    (project / "forge.json").write_text(json.dumps(spec, ensure_ascii=False), encoding="utf-8")
    emit("Importing the model, validating its Humanoid avatar, and inspecting animation clips…")
    command = [p["unity"], "-batchmode", "-nographics", "-quit", "-projectPath", str(project),
               "-executeMethod", "ForgeBuilder.Build", "-logFile", str(project.parent / "unity.log")]
    try:
        run(command, project.parent / "unity-process.log", emit, cancel)
    except RuntimeError as e:
        unity_log = project.parent / "unity.log"
        tail = unity_log.read_text(encoding="utf-8", errors="replace")[-4000:] if unity_log.exists() else ""
        raise RuntimeError(f"{e}\nUnity Editor log: {unity_log}\n{tail}") from e
    report_path = project.parent / "Bundle/report.json"
    if not report_path.is_file():
        raise RuntimeError(f"Unity did not create an import report. Read {project.parent / 'unity.log'}")
    report = json.loads(report_path.read_text(encoding="utf-8"))
    if report.get("unity") != version:
        report["ok"] = False
        report.setdefault("errors", []).append(f"The editor actually used Unity {report.get('unity')}; Valheim requires {version}.")
    report["project"] = str(project)
    report["fingerprint"] = fingerprint(p)
    save_project(workspace / "last-import.json", report)
    if not report.get("ok"):
        raise ValueError("Unity import needs attention:\n" + "\n".join(report.get("errors", [])) +
                         f"\nFull report: {report_path}")
    emit(f"Imported {report['triangles']:,} triangles, {len(report['materials'])} materials. Preview is ready.")
    return report


def package(p, workspace, report=None, emit=print, cancel=None):
    errors = validate(p, build=True)
    if errors:
        raise ValueError("\n".join(errors))
    workspace = Path(workspace).resolve()
    if not report or not report.get("ok") or report.get("fingerprint") != fingerprint(p):
        report = prepare(p, workspace, emit, cancel)
    project = Path(report["project"])
    bundle = project.parent / "Bundle/companionassets"
    if not bundle.is_file() or bundle.read_bytes()[:7] != b"UnityFS":
        raise RuntimeError("The completed Unity asset bundle is missing or invalid. Import again.")
    package_run = project.parent / "packages" / uuid.uuid4().hex
    package_run.mkdir(parents=True)
    runtime = package_run / "Runtime"
    shutil.copytree(ROOT / "Runtime", runtime, ignore=shutil.ignore_patterns("bin", "obj", "Local.props"))
    jotunn = sorted((Path(p["bepinex"]) / "plugins").rglob("Jotunn.dll"))[0]
    import xml.sax.saxutils as xml
    props = (f'<Project><PropertyGroup><GameManaged>{xml.escape(str(Path(p["game"]) / "valheim_Data/Managed"))}</GameManaged>'
             f'<BepInExCore>{xml.escape(str(Path(p["bepinex"]) / "core"))}</BepInExCore>'
             f'<JotunnPath>{xml.escape(str(jotunn))}</JotunnPath></PropertyGroup></Project>')
    (runtime / "Local.props").write_text(props, encoding="utf-8")
    run(["dotnet", "build", str(runtime / "CompanionForge.Runtime.csproj"), "-c", "Release", "--nologo"],
        project.parent / "dotnet.log", emit, cancel)
    plugin_dir = package_run / "Install/BepInEx/plugins" / ("CompanionForge_" + p["id"])
    plugin_dir.mkdir(parents=True)
    # A single shared runtime discovers all generated packs at startup.
    shutil.copy2(runtime / "bin/Release/netstandard2.1/CompanionForge.Runtime.dll", plugin_dir / "CompanionForge.Runtime.dll")
    shutil.copy2(bundle, plugin_dir / "companionassets")
    config = {k: p[k] for k in ("id", "name", "version", "greeting", "affection", "adult")}
    sidecar = Path(p["model"]).with_suffix(".humanoid.json")
    if sidecar.is_file():
        motion = json.loads(sidecar.read_text(encoding="utf-8")).get("secondaryMotion")
        if motion:
            config["secondaryMotion"] = motion
    config["prefab"] = "CF_" + p["id"]
    config['animationMenu'] = [dict(id=a['id'], label=a['label'], category=a.get('category','Animations'),
                                   loop=bool(a.get('loop',True)), hold=bool(a.get('hold',False)))
                               for a in p.get('animation_menu',[])]
    config["bundlePrefab"] = "assets/generated/companionvisual.prefab"
    (plugin_dir / "companion.json").write_text(json.dumps(config, ensure_ascii=False, indent=2), encoding="utf-8")
    readme = (f'# {p["name"]}\n\nA custom adult companion for Valheim.\n\n'
              '## Installation\n\nInstall BepInEx and Jötunn. Extract the BepInEx folder into your Valheim installation '
              'or import this ZIP as a local mod into your mod manager. Replace the previous pack for this companion after closing Valheim.\n\n'
              '## Controls\n\nPress F8 to summon the selected companion beside you. '
              'Use the generated BepInEx configuration to change the summon key. '
              'Interact to use Valheim’s tame-creature follow/stay commands. '
              'Shift+interact opens Valheim’s rename dialog; Left Alt+interact also renames. '
              'Left Control+interact shows dialogue. A nearby following companion joins your bed when you get in '
              'and gets up when you leave. Sitting in a hot tub brings her to the nearest free seat, with relaxed glances toward your character. Alt+J opens the companion animation menu. '
              'Choose a category, scroll or use arrow keys, then click an animation or press Enter. Escape closes the menu. '
              'Stop / resume following ends the animation. Damage or moving away also ends a performance.\n\n'
              'While seated together in the hot tub, Alt+J opens Together. Choose Touch shoulder / hand on knee: '
              'she stays seated upright while your character reaches the front of her opposite shoulder. '
              'She rests her nearer hand on your knee, glances at your hand, then back toward you. '
              'Sitting on her left touches her right shoulder; sitting on her right touches her left. '
              'The gesture lasts five seconds. Stop touch or getting up ends it.\n\n'
              'The companion uses Valheim’s Dverger combat behavior with a custom character appearance.\n\n'
              'Secondary chest motion responds to movement and softly rises and falls on a six-second cycle in the hot tub. '
              'Use Appearance > Chest bounce multiplier in the Companion Forge configuration; 0 disables it.\n\n'
              '## Credits\n\n' + p['credits'] + '\n')
    install = package_run / "Install"
    (install / "README.md").write_text(readme, encoding="utf-8")
    shutil.copy2(ROOT / "LICENSE", install / "CompanionForge-LICENSE.txt")
    icon = Path(p.get("icon", ""))
    if icon.is_file():
        shutil.copy2(icon, install / "icon.png")
    manifest = {"name": re.sub(r"[^a-zA-Z0-9_]", "", p["name"]) or p["id"],
                "version_number": p["version"], "website_url": "",
                "description": "Custom Valheim companion: follow, fight and rest together.",
                "dependencies": ["denikson-BepInExPack_Valheim-5.4.2351", "ValheimModding-Jotunn-2.30.2"]}
    (install / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    out = workspace / "dist"
    out.mkdir(parents=True, exist_ok=True)
    target = out / f"{p['id']}-{p['version']}.zip"
    if target.exists():
        target = out / (target.stem + "-" + uuid.uuid4().hex[:8] + ".zip")
    temp = out / (target.stem + ".tmp.zip")
    with zipfile.ZipFile(temp, "w", zipfile.ZIP_DEFLATED) as z:
        for f in sorted(install.rglob("*")):
            if f.is_file():
                z.write(f, f.relative_to(install).as_posix())
    # Validate the actual finished archive before publishing the result.
    with zipfile.ZipFile(temp) as z:
        if z.testzip():
            raise RuntimeError("Package integrity check failed.")
        if z.read(plugin_dir.relative_to(install).as_posix() + "/companionassets") != bundle.read_bytes():
            raise RuntimeError("Packaged bundle does not match the built bundle.")
    checker = ROOT.parent / "tools/check_icon_backgrounds.py"
    if icon.is_file() and checker.is_file():
        import sys
        run([sys.executable, str(checker), str(temp)], package_run / "icon-check.log", emit, cancel)
    temp.replace(target)
    emit(f"Created install package: {target}")
    return target


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Companion Forge command-line build")
    parser.add_argument("project", type=Path)
    parser.add_argument("--inspect", action="store_true")
    parser.add_argument("--workspace", type=Path)
    args = parser.parse_args()
    project = load_project(args.project)
    workspace = args.workspace or args.project.resolve().parent / (args.project.stem + "-build")
    if args.inspect:
        prepare(project, workspace)
    else:
        package(project, workspace)
