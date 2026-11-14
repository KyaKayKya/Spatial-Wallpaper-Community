"""Inert ZIP/rights tests; real NativeValidator acceptance is a separate CLI run."""
import copy
import tempfile
import unittest
import warnings
import zipfile
from pathlib import Path
from unittest.mock import patch

from tools.wallpapers import prepare_shared_external as shared


def fixture():
    inner = {"scene.json": b'{"scene":"inert-test-data"}', "meshes/test.smesh": b"inert-mesh"}
    license = {"mode": "local-validation", "records": [{"name": "Test", "source": "test-original",
               "license": "CC0-1.0", "redistribution": "unverified", "attribution": "Test author"}]}
    manifest = {"schema": 2, "template_id": "11111111-2222-4333-8444-555555555555", "license": license,
                "reshare": False, "files": {p: {"size": len(b), "sha256": shared.sha(b)} for p, b in inner.items()}}
    manifest["build_id"] = shared.identity(manifest)
    inner["manifest.json"] = shared.canonical(manifest)
    outer = {"script.json": b'{"inert-script":true}', "assets.spatial": shared.archive_bytes(inner)}
    wrapper = {"format": shared.EXTERNAL, "schema": 5, "sdk": "0.4.0-preview.1", "command_abi": 1,
               "work_id": manifest["template_id"], "revision": "1.0.0", "name": "Original inert fixture",
               "assets": {"format": "canonical-static-data-v1", "build_id": manifest["build_id"]},
               "license": license, "files": {p: {"size": len(b), "sha256": shared.sha(b)} for p, b in outer.items()}}
    wrapper["build_id"] = shared.identity(wrapper)
    outer["manifest.json"] = shared.canonical(wrapper)
    return shared.archive_bytes(outer)


def inert_validator(validator, path):
    _, manifest, _, _ = shared.read_external(path.read_bytes())
    return {"valid": True, "policy": shared.POLICY, "build_id": manifest["build_id"],
            "sha256": shared.sha(path.read_bytes()), "license_records": manifest["license"]["records"],
            "license_share": manifest["license"]["mode"] == "share"}


class PrepareSharedExternalTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "local.spatial"
        self.source.write_bytes(fixture())
        self.validator = self.root / "trusted-validator.exe"
        self.validator.write_bytes(b"inert-validator-placeholder")
        self.output = self.root / "shared.spatial"
        outer, _, inner, _ = shared.read_external(self.source.read_bytes())
        self.rights = {"schemaVersion": 1, "records": [{"name": "Original fixture", "source": "https://example.test/source",
            "license": "CC0-1.0", "redistribution": "verified", "attribution": "Test author",
            "resources": list(shared.resource_inventory(outer, inner))}]}

    def prepare(self, **kwargs):
        return shared.prepare(self.source, self.rights, True, self.validator, self.output, **kwargs)

    def test_only_permission_and_cascaded_identities_change(self):
        original = self.source.read_bytes()
        with patch.object(shared, "run_validator", side_effect=inert_validator) as validator:
            receipt = self.prepare()
        self.assertEqual(validator.call_count, 2)
        self.assertEqual(self.source.read_bytes(), original)
        before, bm, bi, bim = shared.read_external(original)
        after, am, ai, aim = shared.read_external(self.output.read_bytes())
        self.assertEqual(before["script.json"], after["script.json"])
        self.assertEqual({k: v for k, v in bi.items() if k != "manifest.json"}, {k: v for k, v in ai.items() if k != "manifest.json"})
        self.assertEqual(aim["license"], am["license"])
        self.assertTrue(aim["reshare"])
        self.assertEqual(am["build_id"], shared.identity(am))
        self.assertEqual(aim["build_id"], shared.identity(aim))
        self.assertEqual(am["assets"]["build_id"], aim["build_id"])
        self.assertEqual(am["files"]["assets.spatial"], {"size": len(after["assets.spatial"]), "sha256": shared.sha(after["assets.spatial"])})
        for k in set(bm) - {"license", "assets", "files", "build_id"}: self.assertEqual(bm[k], am[k])
        for k in set(bim) - {"license", "reshare", "build_id"}: self.assertEqual(bim[k], aim[k])
        self.assertTrue(receipt["after"]["license_share"])
        self.assertEqual(receipt["sha256"], shared.sha(self.output.read_bytes()))

    def test_requires_confirmation_complete_verified_per_resource_records(self):
        with patch.object(shared, "run_validator") as validator:
            with self.assertRaisesRegex(ValueError, "confirm-rights"):
                shared.prepare(self.source, self.rights, False, self.validator, self.output)
            variants = []
            for field, value in [("redistribution", "unverified"), ("source", ""), ("resources", []),
                                 ("resources", ["assets/not-listed"]), ("resources", ["script.json"] * 2)]:
                bad = copy.deepcopy(self.rights); bad["records"][0][field] = value; variants.append(bad)
            bad = copy.deepcopy(self.rights); bad["records"][0]["extra"] = True; variants.append(bad)
            bad = copy.deepcopy(self.rights); bad["schemaVersion"] = True; variants.append(bad)
            for bad in variants:
                with self.subTest(value=bad), self.assertRaises(ValueError):
                    shared.prepare(self.source, bad, True, self.validator, self.output)
            validator.assert_not_called()
        self.assertFalse(self.output.exists())

    def test_read_only_inventory_requires_actual_validator_but_no_rights_or_output(self):
        with patch.object(shared, "run_validator", side_effect=inert_validator) as validator:
            receipt = shared.prepare(self.source, None, False, self.validator, list_only=True)
        self.assertEqual(validator.call_count, 1)
        self.assertEqual(set(receipt["resources"]), set(self.rights["records"][0]["resources"]))
        self.assertFalse(self.output.exists())

    def test_existing_output_and_failed_post_admission_leave_original_untouched(self):
        original = self.source.read_bytes()
        self.output.write_bytes(b"existing-user-file")
        with self.assertRaises(ValueError): self.prepare()
        self.assertEqual(self.output.read_bytes(), b"existing-user-file")
        self.output.unlink()
        def reject_shared(validator, path):
            if path.name == "shared.spatial": raise ValueError("actual validator rejected")
            return inert_validator(validator, path)
        with patch.object(shared, "run_validator", side_effect=reject_shared), self.assertRaises(ValueError):
            self.prepare()
        self.assertFalse(self.output.exists())
        self.assertEqual(self.source.read_bytes(), original)

    def test_platform_schema4_and_duplicate_json_never_reach_validator(self):
        outer, manifest, _, _ = shared.read_external(self.source.read_bytes())
        for change in ({"format": "spatial-native-wallpaper"}, {"schema": 4}, {"schema": True}):
            bad = dict(manifest, **change); files = dict(outer, **{"manifest.json": shared.canonical(bad)})
            self.source.write_bytes(shared.archive_bytes(files))
            with patch.object(shared, "run_validator") as validator, self.assertRaises(ValueError): self.prepare()
            validator.assert_not_called()
        with self.assertRaises(ValueError): shared.loads(b'{"license":1,"license":2}')

    def test_unsafe_zip_paths_aliases_links_and_budgets_rejected(self):
        for name in ("../x", "/x", "a\\x", "a:x", "a//x", "NUL.txt", "a/../x", "a%2fx", "a ", "a."):
            with self.subTest(path=name), self.assertRaises(ValueError): shared.entry_path(name)
        import io
        for entries in (["manifest.json", "manifest.json"], ["x", "X"]):
            stream = io.BytesIO()
            with warnings.catch_warnings(), zipfile.ZipFile(stream, "w") as archive:
                warnings.filterwarnings("ignore", message="Duplicate name:", category=UserWarning)
                for name in entries: archive.writestr(name, b"x")
            with self.assertRaises(ValueError): shared.read_zip(stream.getvalue())
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            info = zipfile.ZipInfo("link"); info.external_attr = 0o120777 << 16; archive.writestr(info, b"target")
        with self.assertRaises(ValueError): shared.read_zip(stream.getvalue())
        with patch.object(shared, "MAX_PACKAGE_BYTES", 10), self.assertRaises(ValueError): self.prepare()
        with patch.object(shared, "MAX_EXPANDED_BYTES", 10), self.assertRaises(ValueError): shared.read_external(self.source.read_bytes())

    def test_public_snapshot_keeps_author_and_release_guides(self):
        from tools.market.prepare_public_source import category
        for name in ("docs/guides/外来原生作品导出.md", "docs/guides/SDK0.4发行与体验.md"):
            self.assertIsNone(category(name))


if __name__ == "__main__":
    unittest.main()
