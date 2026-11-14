import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("local_assets", Path(__file__).with_name("local_assets.py"))
assets = importlib.util.module_from_spec(spec)
spec.loader.exec_module(assets)


class RecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.store = self.root / "store"
        self.target = self.root / "target"
        (self.store / "payload").mkdir(parents=True)
        self.target.mkdir()
        self.manifest = {"schema": 1, "files": []}

    def add(self, name, data=b"approved"):
        path = self.store / "payload" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        self.manifest["files"].append({"path": name, "group": "source",
                                      "size": len(data), "sha256": hashlib.sha256(data).hexdigest()})

    def test_restore_and_repeat_preserve_bytes(self):
        self.add("art/scene.blend")
        result = assets.restore(self.store, self.target, self.manifest, "all")
        self.assertEqual(result, {"copied": 1, "existing": 0})
        self.assertEqual(assets.restore(self.store, self.target, self.manifest, "all"),
                         {"copied": 0, "existing": 1})
        self.assertEqual((self.target / "art/scene.blend").read_bytes(), b"approved")

    def test_corrupt_store_prevents_all_copies(self):
        self.add("art/a.blend")
        self.add("art/b.blend")
        (self.store / "payload/art/b.blend").write_bytes(b"corrupt")
        with self.assertRaises(ValueError):
            assets.restore(self.store, self.target, self.manifest, "all")
        self.assertFalse((self.target / "art/a.blend").exists())

    def test_conflict_prevents_all_copies_and_preserves_edit(self):
        self.add("art/a.blend")
        self.add("art/b.blend")
        (self.target / "art").mkdir()
        (self.target / "art/b.blend").write_bytes(b"local edit")
        with self.assertRaises(ValueError):
            assets.restore(self.store, self.target, self.manifest, "all")
        self.assertFalse((self.target / "art/a.blend").exists())
        self.assertEqual((self.target / "art/b.blend").read_bytes(), b"local edit")

    def test_meta_mismatch_prevents_restore(self):
        self.add("Prototype/Assets/model.fbx")
        self.manifest["files"][0]["meta_sha256_lf"] = hashlib.sha256(b"guid: expected\n").hexdigest()
        p = self.target / "Prototype/Assets/model.fbx.meta"
        p.parent.mkdir(parents=True)
        p.write_text("guid: different\n")
        with self.assertRaises(ValueError):
            assets.restore(self.store, self.target, self.manifest, "all")

    def test_traversal_is_rejected(self):
        self.manifest["files"] = [{"path": "../outside", "group": "source"}]
        with self.assertRaises(ValueError):
            assets.verify(self.store, self.manifest)

    def test_check_does_not_create_missing_files(self):
        self.add("art/scene.blend")
        with self.assertRaises(ValueError):
            assets.restore(self.store, self.target, self.manifest, "all", check=True)
        self.assertEqual(list(self.target.iterdir()), [])


if __name__ == "__main__":
    unittest.main()
