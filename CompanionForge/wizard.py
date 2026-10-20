"""Companion Forge: a guided desktop workflow, with builds off the UI thread."""
import json
import os
from pathlib import Path
import queue
import subprocess
import threading
import time
import tkinter as tk
from tkinter import ttk, filedialog, messagebox, colorchooser
import webbrowser

import forge

GUIDES = {
    "Get a rigged character": "https://www.mixamo.com/",
    "Export FBX from Blender": "https://docs.blender.org/manual/en/latest/addons/import_export/scene_fbx.html",
    "Unity Humanoid rig mapping": "https://docs.unity3d.com/6000.0/Documentation/Manual/ConfiguringtheAvatar.html",
    "Find matching animations": "https://www.mixamo.com/",
    "Texture and material workflow": "https://docs.unity3d.com/6000.0/Documentation/Manual/StandardShaderMaterialParameters.html",
    "Valheim creature integration": "https://valheim-modding.github.io/Jotunn/tutorials/creatures.html",
}


class Wizard:
    def __init__(self, root):
        self.root = root
        root.title("Companion Forge · Character to Valheim")
        root.geometry("1120x820")
        root.minsize(940, 720)
        style = ttk.Style()
        style.theme_use("clam")
        style.configure("TFrame", background="#f3f5f7")
        style.configure("TLabel", background="#f3f5f7", foreground="#233245", font=("Segoe UI", 10))
        style.configure("Title.TLabel", font=("Segoe UI", 23, "bold"))
        style.configure("Sub.TLabel", font=("Segoe UI", 11), foreground="#506176")
        style.configure("TButton", padding=(10, 7), font=("Segoe UI", 10))
        style.configure("TNotebook.Tab", padding=(14, 9))
        self.project = forge.default_project()
        self.project.update(forge.detect_tools())
        self.path = None
        self.report = None
        self.busy = False
        self.cancel = threading.Event()
        self.events = queue.Queue()
        self.fields = {}
        self.clip_fields = {}
        self.material_vars = {}
        self.package_path = None
        outer = ttk.Frame(root, padding=20)
        outer.pack(fill="both", expand=True)
        ttk.Label(outer, text="Companion Forge", style="Title.TLabel").pack(anchor="w")
        ttk.Label(outer, text="Gather your character assets. Check the fit. Build a Valheim companion.", style="Sub.TLabel").pack(anchor="w", pady=(2, 16))
        toolbar = ttk.Frame(outer)
        toolbar.pack(fill="x", pady=(0, 10))
        self.new_button = ttk.Button(toolbar, text="New project", command=self.new)
        self.new_button.pack(side="left")
        self.open_button = ttk.Button(toolbar, text="Open project", command=self.open)
        self.open_button.pack(side="left", padx=6)
        self.save_button = ttk.Button(toolbar, text="Save project", command=self.save)
        self.save_button.pack(side="left")
        self.example_button = ttk.Button(toolbar, text="Load practice character", command=self.example)
        self.example_button.pack(side="left", padx=6)
        self.project_label = ttk.Label(toolbar, text="Unsaved project")
        self.project_label.pack(side="right")
        self.tabs = ttk.Notebook(outer)
        self.tabs.pack(fill="both", expand=True)
        self.pages = []
        self.canvases = []
        for name in ("1 · Gather", "2 · Character", "3 · Animations", "4 · Materials", "5 · Build"):
            holder = ttk.Frame(self.tabs)
            self.tabs.add(holder, text=name)
            canvas = tk.Canvas(holder, background="#f3f5f7", highlightthickness=0)
            scrollbar = ttk.Scrollbar(holder, orient="vertical", command=canvas.yview)
            scrollbar.pack(side="right", fill="y")
            canvas.pack(side="left", fill="both", expand=True)
            canvas.configure(yscrollcommand=scrollbar.set)
            frame = ttk.Frame(canvas, padding=16)
            window = canvas.create_window((0, 0), window=frame, anchor="nw")
            frame.bind("<Configure>", lambda event, c=canvas: c.configure(scrollregion=c.bbox("all")))
            def resize(event, c=canvas, w=window, f=frame):
                c.itemconfigure(w, width=event.width)
                def wrap(parent):
                    for child in parent.winfo_children():
                        if isinstance(child, ttk.Label) and int(child.cget("wraplength")) > 0:
                            child.configure(wraplength=max(200, parent.winfo_width() - 40))
                        wrap(child)
                wrap(f)
            canvas.bind("<Configure>", resize)
            self.pages.append(frame)
            self.canvases.append(canvas)
        def wheel(event):
            if not isinstance(event.widget, (tk.Text, tk.Listbox, ttk.Combobox)):
                self.canvases[self.tabs.index("current")].yview_scroll(-int(event.delta / 120), "units")
        root.bind("<MouseWheel>", wheel)
        self.gather_page()
        self.character_page()
        self.animation_page()
        self.material_page()
        self.build_page()
        footer = ttk.Frame(outer)
        footer.pack(fill="x", pady=(12, 0))
        self.status = ttk.Label(footer, text="Start with the asset checklist in Gather.")
        self.status.pack(side="left")
        ttk.Button(footer, text="Next →", command=self.next).pack(side="right")
        ttk.Button(footer, text="← Back", command=self.back).pack(side="right", padx=6)
        self.populate()
        root.protocol("WM_DELETE_WINDOW", self.close)
        root.after(150, self.drain)

    def note(self, parent, text):
        ttk.Label(parent, text=text, wraplength=995, justify="left").pack(anchor="w", fill="x", pady=(0, 12))

    def entry(self, parent, key, label, browse=None, filetypes=None):
        row = ttk.Frame(parent)
        row.pack(fill="x", pady=4)
        ttk.Label(row, text=label, width=23).pack(side="left")
        var = tk.StringVar()
        self.fields[key] = var
        ttk.Entry(row, textvariable=var).pack(side="left", fill="x", expand=True)
        if browse:
            def choose():
                value = filedialog.askdirectory(title=label) if browse == "folder" else filedialog.askopenfilename(title=label, filetypes=filetypes or [("All files", "*.*")])
                if value:
                    var.set(value)
            ttk.Button(row, text="Browse…", command=choose).pack(side="left", padx=(6, 0))
        return var

    def open_animation_studio(self):
        studio = Path(r"C:\Users\Charles Sammons\OneDrive\Documents\ChatGPT\Verdant Machina\LocalAnimationStudio")
        import socket
        try:
            with socket.create_connection(("127.0.0.1",7862),timeout=.2): pass
        except OSError:
            python = studio / ".venv/Scripts/pythonw.exe"
            if not python.is_file():
                messagebox.showerror("Animation Studio", "Existing Animation Studio environment was not found.")
                return
            subprocess.Popen([str(python),str(studio/"app.py"),"--no-browser"],cwd=studio,creationflags=subprocess.CREATE_NO_WINDOW)
        webbrowser.open("http://127.0.0.1:7862/#valheim-builder")

    def gather_page(self):
        page = self.pages[0]
        self.note(page, "You will need an adult humanoid character exported as FBX with a skinned mesh, a skeleton, and base-color textures. Start with idle, walk and run animations. Use the same character/skeleton when exporting animation FBXs, or use Humanoid clips that Unity can retarget.")
        ttk.Button(page, text="Open Animation Studio · Import, rig and animate", command=self.open_animation_studio).pack(anchor="w", pady=8)
        box = ttk.LabelFrame(page, text="What to gather", padding=12)
        box.pack(fill="x", pady=8)
        for text in (
            "✓ Model: rigged FBX, metres, upright and forward-facing, neutral T-pose; skin/clothing included.",
            "✓ Motion: idle, walk and run. Choose in-place movement. Optional attack and greeting/wave clips.",
            "✓ Textures: base color; optional tangent-space normal, metallic + gloss, and emission maps.",
            "✓ Materials: record which texture belongs to each skin, clothing, hair and eye material.",
            "✓ Credits: source URL, creator, asset title, license and redistribution permission for every asset.",
            "✓ Tools: matching Unity Editor, .NET SDK, Valheim, BepInEx and Jötunn in a mod profile.",
        ):
            ttk.Label(box, text=text, wraplength=940).pack(anchor="w", pady=4)
        self.note(page, "Gather assets in a folder you can keep. The tool copies inputs to its own build folder. High resolution comes from mesh detail and textures; raising a texture limit cannot add detail to a low-resolution source. A 4K texture limit and 80,000-triangle budget are the starting defaults.")
        self.note(page, "For a Mixamo workflow: upload or select your adult character, complete rigging, and download the character as FBX with skin. Select idle, walking and running motions; choose In Place where available and download each as an FBX animation. Retain textures from the model export. For another asset provider, request equivalent skinned FBX and Humanoid animation exports.")
        links = ttk.Frame(page)
        links.pack(fill="x")
        for i, (title, url) in enumerate(GUIDES.items()):
            ttk.Button(links, text=title + " ↗", command=lambda u=url: webbrowser.open(u)).grid(row=i // 2, column=i % 2, sticky="ew", padx=4, pady=4)
        links.columnconfigure(0, weight=1)
        links.columnconfigure(1, weight=1)
        self.note(page, "Use Blender or the asset provider’s export controls to prepare FBX files. Unity can auto-map conventional humanoid bones; the import report identifies rigs needing correction. The tool does not purchase or download third-party assets for you.")

    def character_page(self):
        page = self.pages[1]
        self.note(page, "Name your companion and choose its model. Keep the stable ID unchanged when updating a companion already saved in a world. Scale, facing and vertical offset affect the visible character; preview them before building.")
        for key, label in (("name", "Companion name"), ("author", "Creator"), ("id", "Stable ID"), ("version", "Pack version")):
            self.entry(page, key, label)
        self.entry(page, "model", "Rigged model (FBX)", "file", [("FBX model", "*.fbx *.FBX")])
        self.entry(page, "icon", "Transparent 256×256 mod icon", "file", [("PNG icon", "*.png")])
        options = ttk.Frame(page)
        options.pack(fill="x", pady=8)
        for key, label in (("scale", "Scale"), ("yaw", "Facing degrees"), ("offset_y", "Height offset (m)"), ("triangle_budget", "Triangle budget")):
            ttk.Label(options, text=label).pack(side="left", padx=(0, 4))
            var = tk.StringVar()
            self.fields[key] = var
            ttk.Entry(options, textvariable=var, width=8).pack(side="left", padx=(0, 12))
        self.fields["adult"] = tk.BooleanVar()
        ttk.Checkbutton(page, text="This character represents an adult; affection and dialogue remain non-explicit.", variable=self.fields["adult"]).pack(anchor="w", pady=6)
        self.entry(page, "greeting", "Greeting dialogue")
        self.entry(page, "affection", "Affection dialogue")
        ttk.Label(page, text="Asset sources / authors / licenses / required credits (one entry per asset)").pack(anchor="w", pady=(10, 4))
        self.credits = tk.Text(page, height=5, font=("Segoe UI", 10), wrap="word")
        self.credits.pack(fill="x")
        self.fields["rights_confirmed"] = tk.BooleanVar()
        ttk.Checkbutton(page, text="I have permission to include these assets in my generated mod and have recorded their credits.", variable=self.fields["rights_confirmed"]).pack(anchor="w", pady=6)

    def animation_page(self):
        page = self.pages[2]
        self.note(page, "Idle, walk and run are required. Select an animation FBX or standalone Unity .anim for each role. Leave Clip name blank only if that file contains exactly one clip. An import attempt lists available clip names below and in its report, including when validation fails.")
        grid = ttk.Frame(page)
        grid.pack(fill="x")
        for c, title in enumerate(("Role", "Animation file", "", "Exact clip name")):
            ttk.Label(grid, text=title).grid(row=0, column=c, sticky="w", pady=6)
        for row, role in enumerate(forge.ROLES, 1):
            ttk.Label(grid, text=role.title() + (" *" if role in forge.REQUIRED_ROLES else "")).grid(row=row, column=0, sticky="w", padx=(0, 12))
            path = tk.StringVar()
            clip = tk.StringVar()
            self.clip_fields[role] = (path, clip)
            ttk.Entry(grid, textvariable=path).grid(row=row, column=1, sticky="ew", pady=6)
            def browse(v=path):
                f = filedialog.askopenfilename(filetypes=[("Humanoid animation", "*.fbx *.FBX *.anim")])
                if f:
                    v.set(f)
            ttk.Button(grid, text="Browse…", command=browse).grid(row=row, column=2, padx=6)
            ttk.Entry(grid, textvariable=clip, width=25).grid(row=row, column=3, sticky="ew")
        grid.columnconfigure(1, weight=1)
        self.note(page, "Animations are retargeted through Unity Humanoid. Root motion and imported animation events are disabled: Valheim drives movement and native combat timing. Sleep joins your bed; sit joins a free hot-tub seat. Alt+C plays the optional pose. Shift+E renames; E commands follow/stay. Attack is recommended for visible combat motion.")
        ttk.Label(page, text="Imported clip choices").pack(anchor="w", pady=(10, 4))
        self.clip_report = tk.Text(page, height=11, wrap="word", font=("Consolas", 10), state="disabled")
        self.clip_report.pack(fill="both", expand=True)

    def material_page(self):
        page = self.pages[3]
        self.note(page, "Each imported material gets its own texture assignment. Use Inspect assets in Build to discover material names; then return here. Textures already linked in the FBX can be retained. The shader uses Unity’s built-in Standard workflow, suitable for Valheim’s built-in rendering pipeline.")
        options = ttk.Frame(page)
        options.pack(fill="x", pady=6)
        ttk.Label(options, text="Maximum texture size").pack(side="left")
        self.fields["texture_limit"] = tk.StringVar()
        ttk.Combobox(options, textvariable=self.fields["texture_limit"], values=(1024, 2048, 4096, 8192), width=8, state="readonly").pack(side="left", padx=8)
        ttk.Label(options, text="Global tint").pack(side="left", padx=(18, 0))
        self.fields["tint"] = tk.StringVar()
        ttk.Entry(options, textvariable=self.fields["tint"], width=10).pack(side="left", padx=8)
        def color():
            value = colorchooser.askcolor(self.fields["tint"].get())[1]
            if value:
                self.fields["tint"].set(value)
        ttk.Button(options, text="Choose color", command=color).pack(side="left")
        self.note(page, "Normal maps must be tangent-space. Metallic maps use the red channel for metalness and alpha for smoothness; roughness maps need conversion to smoothness before assignment. Base-color textures with alpha use cutout rendering for hair and eyelashes. Mipmaps are generated for stable distant rendering.")
        split = ttk.Frame(page)
        split.pack(fill="both", expand=True)
        self.material_list = tk.Listbox(split, width=28, exportselection=False, font=("Segoe UI", 10))
        self.material_list.pack(side="left", fill="y", padx=(0, 12))
        self.material_list.bind("<<ListboxSelect>>", self.select_material)
        editor = ttk.Frame(split)
        editor.pack(side="left", fill="both", expand=True)
        self.material_name = ttk.Label(editor, text="Inspect the model to discover its materials.", wraplength=600)
        self.material_name.pack(anchor="w", pady=8)
        for key, title in (("base", "Base color"), ("normal", "Normal map"), ("metallic", "Metallic / gloss"), ("emission", "Emission")):
            ttk.Label(editor, text=title).pack(anchor="w", pady=(10, 2))
            row = ttk.Frame(editor)
            row.pack(fill="x")
            var = tk.StringVar()
            self.material_vars[key] = var
            ttk.Entry(row, textvariable=var).pack(side="left", fill="x", expand=True)
            def browse(k=key, v=var):
                f = filedialog.askopenfilename(filetypes=[("Texture", "*.png *.jpg *.jpeg *.tga")])
                if f:
                    v.set(f)
                    self.store_material()
            ttk.Button(row, text="Browse…", command=browse).pack(side="left", padx=5)
        ttk.Button(editor, text="Apply this material assignment", command=self.store_material).pack(anchor="w", pady=16)
        self.current_material = None

    def build_page(self):
        page = self.pages[4]
        self.note(page, "Inspect assets imports them in an isolated Unity project and reports rig, clips, material names, size and triangle count. Once it passes, open the generated Preview scene in Unity to inspect shape and materials, then build the install ZIP. Build results and logs live beside your saved project.")
        self.entry(page, "unity", "Matching Unity.exe", "file", [("Unity Editor", "Unity.exe")])
        self.entry(page, "game", "Valheim install folder", "folder")
        self.entry(page, "bepinex", "BepInEx profile folder", "folder")
        ttk.Button(page, text="Detect installed tools again", command=self.detect).pack(anchor="w", pady=6)
        buttons = ttk.Frame(page)
        buttons.pack(fill="x", pady=8)
        self.inspect_button = ttk.Button(buttons, text="Inspect assets", command=lambda: self.work(False))
        self.inspect_button.pack(side="left")
        self.preview_button = ttk.Button(buttons, text="Open Unity preview", command=self.preview)
        self.preview_button.pack(side="left", padx=6)
        self.build_button = ttk.Button(buttons, text="Build install ZIP", command=lambda: self.work(True))
        self.build_button.pack(side="left")
        self.cancel_button = ttk.Button(buttons, text="Cancel build", command=self.cancel.set, state="disabled")
        self.cancel_button.pack(side="left", padx=6)
        self.result_button = ttk.Button(buttons, text="Open output folder", command=self.open_output)
        self.result_button.pack(side="right")
        self.progress = ttk.Progressbar(page, mode="indeterminate")
        self.progress.pack(fill="x", pady=6)
        self.log = tk.Text(page, height=14, wrap="word", font=("Consolas", 10), state="disabled")
        self.log.pack(fill="both", expand=True)
        self.note(page, "The ZIP contains your character bundle, companion settings, runtime plugin and credits. It uses BepInEx and Jötunn, with no dependency on Wife Companion or another Runic mod. Import the ZIP into a mod manager or extract its BepInEx folder into the game. Live visual, movement and combat checks happen in Valheim.")

    def populate(self):
        for key, var in self.fields.items():
            var.set(self.project[key])
        for role, (path, clip) in self.clip_fields.items():
            path.set(self.project["animations"][role]["path"])
            clip.set(self.project["animations"][role]["clip"])
        self.credits.delete("1.0", "end")
        self.credits.insert("1.0", self.project["credits"])
        self.current_material = None
        self.material_list.delete(0, "end")
        for m in self.project["materials"]:
            self.material_list.insert("end", m["name"])
        for v in self.material_vars.values():
            v.set("")
        self.project_label.configure(text=self.path.name if self.path else "Unsaved project")

    def collect(self):
        self.store_material()
        for key, var in self.fields.items():
            value = var.get()
            if key in ("scale", "yaw", "offset_y"):
                value = float(value)
            if key in ("texture_limit", "triangle_budget"):
                value = int(value)
            self.project[key] = value
        for role, (path, clip) in self.clip_fields.items():
            self.project["animations"][role] = {"path": path.get(), "clip": clip.get()}
        self.project["credits"] = self.credits.get("1.0", "end").strip()
        return self.project

    def store_material(self):
        if self.current_material:
            for m in self.project["materials"]:
                if m["name"] == self.current_material:
                    m.update({k: v.get() for k, v in self.material_vars.items()})

    def select_material(self, event=None):
        selection = self.material_list.curselection()
        if not selection:
            return
        self.store_material()
        self.current_material = self.material_list.get(selection[0])
        self.material_name.configure(text=self.current_material)
        m = next(x for x in self.project["materials"] if x["name"] == self.current_material)
        for key, var in self.material_vars.items():
            var.set(m.get(key, ""))

    def save(self):
        if self.busy:
            return False
        try:
            self.collect()
            if self.path is None:
                value = filedialog.asksaveasfilename(defaultextension=".json", initialfile=self.project["id"] + ".json", filetypes=[("Companion Forge project", "*.json")])
                if not value:
                    return False
                self.path = Path(value).resolve()
            forge.save_project(self.path, self.project)
            self.project_label.configure(text=self.path.name)
            self.status.configure(text="Project saved.")
            return True
        except Exception as e:
            messagebox.showerror("Project needs attention", str(e))
            return False

    def new(self):
        if self.busy:
            return
        if not messagebox.askyesno("New project", "Start a new project? Save your current changes first if you want to keep them."):
            return
        self.project = forge.default_project()
        self.project.update(forge.detect_tools())
        self.path = self.report = self.package_path = None
        self.populate()
        self.tabs.select(0)

    def open(self):
        if self.busy:
            return
        value = filedialog.askopenfilename(filetypes=[("Companion Forge project", "*.json")])
        if not value:
            return
        try:
            self.project = forge.load_project(value)
            self.path = Path(value).resolve()
            self.report = self.package_path = None
            self.populate()
        except Exception as e:
            messagebox.showerror("Cannot open project", str(e))

    def example(self):
        if self.busy:
            return
        try:
            self.project = forge.load_project(forge.ROOT / "Examples/Mannequin/mannequin.json")
            self.project.update(forge.detect_tools())
            self.path = self.report = self.package_path = None
            self.populate()
            self.tabs.select(1)
            self.status.configure(text="Practice character loaded. Save it to begin an import or build.")
        except Exception as e:
            messagebox.showerror("Cannot load practice character", str(e))

    def detect(self):
        for key, value in forge.detect_tools().items():
            if value:
                self.fields[key].set(value)
        self.status.configure(text="Detected local tools. Review paths before importing.")

    def workspace(self):
        return self.path.parent / (self.path.stem + "-build")

    def append(self, widget, text):
        widget.configure(state="normal")
        widget.insert("end", text + "\n")
        widget.see("end")
        widget.configure(state="disabled")

    def work(self, build):
        if self.busy or not self.save():
            return
        problems = forge.validate(self.project, build=build)
        if problems:
            messagebox.showerror("Complete these steps first", "\n\n".join(problems))
            return
        snapshot = json.loads(json.dumps(self.project))
        self.busy = True
        self.cancel.clear()
        self.set_busy(True)
        self.progress.start(12)
        self.status.configure(text="Building… you can cancel while the worker is running.")
        workspace = self.workspace()
        latest = workspace / "last-import.json"
        previous_report_time = latest.stat().st_mtime_ns if latest.exists() else 0
        def worker():
            try:
                emit = lambda msg: self.events.put(("log", msg))
                result = forge.package(snapshot, workspace, self.report, emit, self.cancel) if build else forge.prepare(snapshot, workspace, emit, self.cancel)
                self.events.put(("package" if build else "report", result))
            except Exception as e:
                self.events.put(("error", str(e)))
                # Reports still contain material and clip discoveries when rig/material checks fail.
                if latest.exists() and latest.stat().st_mtime_ns != previous_report_time:
                    try:
                        self.events.put(("report", json.loads(latest.read_text(encoding="utf-8"))))
                    except (ValueError, OSError):
                        pass
            finally:
                self.events.put(("done", None))
        threading.Thread(target=worker, daemon=True).start()

    def set_busy(self, busy):
        for button in (self.new_button, self.open_button, self.save_button, self.example_button, self.inspect_button, self.build_button, self.preview_button):
            button.configure(state="disabled" if busy else "normal")
        self.cancel_button.configure(state="normal" if busy else "disabled")
        # Snapshotting protects the worker; disable tab switching while it owns this project.
        for i in range(4):
            self.tabs.tab(i, state="disabled" if busy else "normal")

    def drain(self):
        try:
            while True:
                kind, value = self.events.get_nowait()
                if kind == "log":
                    self.append(self.log, value)
                elif kind == "error":
                    self.append(self.log, "NEEDS ATTENTION\n" + value)
                    self.status.configure(text="Check the build report and correct the listed inputs.")
                elif kind == "report":
                    self.report = value
                    self.clip_report.configure(state="normal")
                    self.clip_report.delete("1.0", "end")
                    self.clip_report.configure(state="disabled")
                    for a in value.get("animations", []):
                        self.append(self.clip_report, a["role"] + ": " + ", ".join(a["clips"]))
                    existing = {m["name"]: m for m in self.project["materials"]}
                    self.project["materials"] = [existing.get(n, {"name": n}) for n in value.get("materials", [])]
                    self.current_material = None
                    self.material_list.delete(0, "end")
                    for m in self.project["materials"]:
                        self.material_list.insert("end", m["name"])
                    for note in value.get("notes", []):
                        self.append(self.log, note)
                    self.append(self.log, "Unity preview project: " + value["project"])
                    if value.get("ok"):
                        self.status.configure(text="Import passed. Preview in Unity, then build the ZIP.")
                elif kind == "package":
                    self.package_path = value
                    self.status.configure(text="Install ZIP built: " + value.name)
                elif kind == "done":
                    self.busy = False
                    self.progress.stop()
                    self.set_busy(False)
        except queue.Empty:
            pass
        self.root.after(150, self.drain)

    def preview(self):
        if not self.report:
            messagebox.showinfo("Inspect assets first", "Run Inspect assets to create a Unity preview project.")
            return
        scene = Path(self.report["project"]) / "Assets/Generated/Preview.unity"
        if not scene.exists():
            messagebox.showinfo("Complete import first", "Correct the import errors and inspect again to create the preview scene.")
            return
        subprocess.Popen([self.fields["unity"].get(), "-projectPath", self.report["project"], "-openfile", str(scene)])
        self.status.configure(text="Unity preview opened. Select Game view and Play to inspect idle motion.")

    def open_output(self):
        if not self.path:
            return
        folder = self.workspace() / "dist"
        folder.mkdir(parents=True, exist_ok=True)
        os.startfile(folder)

    def next(self):
        if not self.busy:
            self.tabs.select(min(4, self.tabs.index("current") + 1))

    def back(self):
        if not self.busy:
            self.tabs.select(max(0, self.tabs.index("current") - 1))

    def close(self):
        if self.busy:
            self.cancel.set()
            self.status.configure(text="Cancelling the build. Close the tool again after it stops.")
            return
        self.root.destroy()


if __name__ == "__main__":
    Wizard(tk.Tk()).root.mainloop()
