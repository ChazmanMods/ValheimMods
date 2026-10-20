import json
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import forge


class PipelineTests(unittest.TestCase):
    def setUp(self):
        temp_root = Path(__file__).resolve().parent / ".tmp"
        temp_root.mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=temp_root)
        Path(self.temp.name).resolve().relative_to(temp_root.resolve())
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def project(self):
        p = forge.default_project()
        p.update(forge.detect_tools())
        f = self.root / "model.fbx"
        f.write_bytes(b"fixture")
        p["model"] = str(f)
        for role in forge.REQUIRED_ROLES:
            p["animations"][role]["path"] = str(f)
        p["adult"] = p["rights_confirmed"] = True
        p["credits"] = "Test fixture, locally generated; not redistributed."
        return p

    def test_missing_assets_and_rights_are_actionable(self):
        problems = forge.validate(forge.default_project())
        self.assertTrue(any("adult" in p for p in problems))
        self.assertTrue(any("permission" in p for p in problems))
        self.assertTrue(any("Rigged character" in p for p in problems))
        self.assertTrue(any("Idle" in p for p in problems))

    def test_injection_ids_and_bad_quality_values_rejected(self):
        p = self.project()
        p.update(id="../escaped", tint="red", scale=-1, texture_limit=123, triangle_budget=1)
        problems = forge.validate(p)
        self.assertGreaterEqual(len(problems), 5)

    def test_saved_project_resolves_relative_assets(self):
        p = self.project()
        p["model"] = "model.fbx"
        p["animations"]["idle"]["path"] = "model.fbx"
        target = self.root / "project.json"
        forge.save_project(target, p)
        restored = forge.load_project(target)
        self.assertEqual(restored["model"], str(self.root / "model.fbx"))
        self.assertEqual(restored["animations"]["idle"]["path"], restored["model"])
        self.assertEqual(restored["id"], p["id"])

    def test_old_project_schema_rejected(self):
        target = self.root / "old.json"
        target.write_text('{"schema": 99}')
        with self.assertRaisesRegex(ValueError, "Unsupported project"):
            forge.load_project(target)

    def test_content_changes_invalidate_bundle_even_if_size_unchanged(self):
        p = self.project()
        first = forge.fingerprint(p)
        Path(p["model"]).write_bytes(b"changed")
        self.assertNotEqual(first, forge.fingerprint(p))
        texture = self.root / "skin.png"
        texture.write_bytes(b"texture-a")
        second = forge.fingerprint(p)
        texture.write_bytes(b"texture-b")
        self.assertNotEqual(second, forge.fingerprint(p))

    def test_material_paths_and_duplicates_rejected(self):
        p = self.project()
        p["materials"] = [{"name": "Skin", "base": "missing.png"}, {"name": "Skin"}]
        problems = forge.validate(p)
        self.assertTrue(any("unique" in x for x in problems))
        self.assertTrue(any("Skin base" in x for x in problems))

    def test_wrong_unity_version_rejected(self):
        p = self.project()
        with patch.object(forge, "unity_game_version", return_value="2022.3.62f3"):
            self.assertTrue(any("matching" in x for x in forge.validate(p)))

    def test_cancel_stops_worker_and_preserves_previous_output(self):
        cancelled = threading.Event()
        cancelled.set()
        sentinel = self.root / "existing.zip"
        sentinel.write_bytes(b"previous-good-package")
        start = time.monotonic()
        with self.assertRaisesRegex(RuntimeError, "cancelled"):
            forge.run([sys.executable, "-c", "import time; time.sleep(30)"], self.root / "process.log", lambda x: None, cancelled)
        self.assertLess(time.monotonic() - start, 5)
        self.assertEqual(sentinel.read_bytes(), b"previous-good-package")

    def test_build_failures_include_log_and_leave_inputs(self):
        source = self.root / "source.fbx"
        source.write_bytes(b"source")
        with self.assertRaisesRegex(RuntimeError, "exit"):
            forge.run([sys.executable, "-c", "raise SystemExit(7)"], self.root / "failure.log", lambda x: None)
        self.assertEqual(source.read_bytes(), b"source")

    def test_manual_bone_mapping_metadata_survives_staging(self):
        p = self.project()
        meta = Path(p["model"] + ".meta")
        old_guid = "1" * 32
        meta.write_text("fileFormatVersion: 2\nguid: " + old_guid + "\nModelImporter:\n  humanDescription:\n    human:\n    - boneName: CustomHips\n      humanName: Hips\n")
        def fake_run(command, log, emit, cancel=None):
            project = Path(command[command.index("-projectPath") + 1])
            spec = json.loads((project / "forge.json").read_text())
            output = Path(spec["output"])
            output.mkdir()
            (output / "report.json").write_text(json.dumps({"ok": True, "unity": forge.unity_game_version(p["game"]), "triangles": 12, "materials": []}))
        with patch.object(forge, "run", side_effect=fake_run):
            report = forge.prepare(p, self.root / "build", lambda x: None)
        staged = Path(report["project"]) / "Assets/Source/Model/model.fbx.meta"
        self.assertIn("CustomHips", staged.read_text())
        self.assertNotIn(old_guid, staged.read_text())
        self.assertIn(old_guid, meta.read_text())

    def test_wizard_tabs_roundtrip_and_results(self):
        import tkinter as tk
        import wizard
        root = tk.Tk()
        root.withdraw()
        self.addCleanup(root.destroy)
        ui = wizard.Wizard(root)
        self.assertEqual(len(ui.tabs.tabs()), 5)
        ui.example()
        self.assertTrue(Path(ui.project["model"]).is_file())
        self.assertTrue(ui.project["adult"])
        ui.fields["name"].set("Custom Character")
        ui.clip_fields["walk"][1].set("Walking")
        self.assertEqual(ui.collect()["name"], "Custom Character")
        self.assertEqual(ui.project["animations"]["walk"]["clip"], "Walking")
        ui.events.put(("report", {"ok": False, "materials": ["Skin", "Hair"], "animations": [
            {"role": "walk", "clips": ["Walking", "FastWalk"]}], "project": str(self.root)}))
        ui.drain()
        self.assertEqual(ui.material_list.size(), 2)
        self.assertIn("FastWalk", ui.clip_report.get("1.0", "end"))
        ui.material_list.selection_set(0)
        ui.select_material()
        ui.material_vars["base"].set("skin.png")
        ui.store_material()
        self.assertEqual(ui.project["materials"][0]["base"], "skin.png")
        ui.set_busy(True)
        self.assertEqual(str(ui.build_button["state"]), "disabled")
        ui.set_busy(False)
        self.assertEqual(str(ui.build_button["state"]), "normal")


if __name__ == "__main__":
    unittest.main(verbosity=2)
