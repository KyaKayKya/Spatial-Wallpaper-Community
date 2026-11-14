import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from tools.market import prepare_public_source as export
from tools.market import sync_public_source as sync


class PublicSyncTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "private"
        self.public = self.root / "public"
        for repo in [self.source, self.public]:
            repo.mkdir()
            self.git(repo, "init", "-b", "dev/v0.0")
            self.git(repo, "config", "user.name", "Fixture")
            self.git(repo, "config", "user.email", "fixture@example.invalid")
            self.git(repo, "config", "core.autocrlf", "false")
        self.write(self.source, "docs/public/README.md", b"# Maintained public homepage\n\n![cover](docs/public/assets/cover.png)\n")
        self.write(self.source, "docs/public/README.en.md", b"# Maintained English homepage\n")
        self.write(self.source, "docs/public/DEVELOPMENT.md", b"Development instructions\n")
        self.write(self.source, "tools/example.py", b"print('source')\n")
        self.write(self.source, "tools/retired.py", b"# removed next time\n")
        self.write(self.source, "market/revocations.json", b'{"works": []}\n')
        self.write(self.source, "market/featured.json", b'{"works": []}\n')
        self.write(self.source, "docs/private.md", b"private development notes\n")
        self.first = self.commit(self.source)

    @staticmethod
    def git(repo, *args):
        return subprocess.check_output(["git", "-C", str(repo), *args], stderr=subprocess.PIPE)

    @staticmethod
    def write(repo, path, data):
        target = repo / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)

    def commit(self, repo):
        self.git(repo, "add", "--all")  # Isolated fixture repositories only.
        self.git(repo, "commit", "-m", "fixture")
        return self.git(repo, "rev-parse", "HEAD").decode().strip()

    def seed_public(self, legacy_operational=False):
        snapshot = self.root / "seed"
        export.prepare(self.source, self.first, snapshot)
        if legacy_operational:
            manifest = json.loads((snapshot / "SOURCE_MANIFEST.json").read_text("utf-8"))
            for name in ["market/featured.json", "market/revocations.json"]:
                raw = b'{"public_operational_state": true}\n'
                self.write(snapshot, name, raw)
                manifest["files"].append({"path": name, "sha256": sync.digest(raw), "bytes": len(raw), "git_mode": "100644", "git_blob": "0" * 40})
            self.write(snapshot, "SOURCE_MANIFEST.json", (json.dumps(manifest) + "\n").encode())
            sums = [sync.digest(p.read_bytes()) + "  " + p.relative_to(snapshot).as_posix() for p in sorted(snapshot.rglob("*")) if p.is_file() and p.name != "SHA256SUMS"]
            self.write(snapshot, "SHA256SUMS", ("\n".join(sums) + "\n").encode())
        shutil.copytree(snapshot, self.public, dirs_exist_ok=True)
        for name in ["market/submissions/145/example/1.0.0.json", "market/registry/145.json", "market/details/author.md", "market/previews/author.png", "wallpaper.json", ".gitignore"]:
            self.write(self.public, name, b"public-only retained\n")
        return self.commit(self.public)

    def apply(self, commit, apply=False, branch="task/snapshot"):
        base = self.git(self.public, "rev-parse", "HEAD").decode().strip()
        with patch.object(sync, "refresh_base", return_value=base):
            return sync.synchronize(self.source, commit, self.public, "dev/v0.0", branch, apply)

    def test_readme_is_maintained_git_bytes_and_private_inputs_stay_out(self):
        destination = self.root / "snapshot"
        receipt = export.prepare(self.source, self.first, destination)
        manifest, files = sync.verify_snapshot(destination, self.first)
        self.assertEqual(files["README.md"], (self.source / "docs/public/README.md").read_bytes())
        self.assertEqual(files["README.en.md"], (self.source / "docs/public/README.en.md").read_bytes())
        self.assertNotIn("docs/public/README.md", files)
        self.assertNotIn("docs/private.md", files)
        self.assertNotIn("market/revocations.json", files)
        self.assertEqual(receipt["sha256sums"], sync.digest(files["SHA256SUMS"]))
        readme = next(x for x in manifest["files"] if x["path"] == "README.md")
        self.assertEqual(readme["source_path"], "docs/public/README.md")
        (destination / "README.md").write_bytes(b"tampered")
        with self.assertRaisesRegex(ValueError, "checksum mismatch"):
            sync.verify_snapshot(destination, self.first)

    def test_dry_run_then_apply_exact_index_bytes_with_crlf_and_idempotence(self):
        self.seed_public()
        self.git(self.public, "config", "core.autocrlf", "true")
        self.write(self.source, "tools/example.py", b"print('new source')\n")
        self.write(self.source, "docs/public/README.md", b"# New maintained homepage\n")
        self.write(self.source, "tools/中文示例.py", "# 中文 UTF-8 路径与字节\n".encode("utf-8"))
        image = b"\x89PNG\r\n\x1a\n" + b"\0\0\0\rIHDR" + struct.pack(">II", 16, 16) + b"\x08\x06\0\0\0" + b"\0" * 4
        self.write(self.source, "docs/public/assets/真实预览.png", image)
        (self.source / "tools/retired.py").unlink()
        second = self.commit(self.source)
        before = self.git(self.public, "rev-parse", "HEAD")
        report = self.apply(second)
        self.assertIsNone(report["commit"])
        self.assertEqual(before, self.git(self.public, "rev-parse", "HEAD"))
        self.assertIn("tools/retired.py", report["removed"])
        self.assertEqual(b"", self.git(self.public, "status", "--porcelain"))
        report = self.apply(second, True)
        self.assertIsNotNone(report["commit"])
        self.assertEqual(self.git(self.public, "show", "HEAD:tools/example.py"), b"print('new source')\n")
        self.assertEqual(self.git(self.public, "show", "HEAD:README.md"), b"# New maintained homepage\n")
        self.assertEqual(self.git(self.public, "show", "HEAD:tools/中文示例.py"), "# 中文 UTF-8 路径与字节\n".encode("utf-8"))
        self.assertEqual(self.git(self.public, "show", "HEAD:docs/public/assets/真实预览.png"), image)
        self.assertFalse((self.public / "tools/retired.py").exists())
        self.assertTrue((self.public / "market/submissions/145/example/1.0.0.json").exists())
        self.assertEqual(b"", self.git(self.public, "status", "--porcelain"))
        again = self.apply(second, True, "task/snapshot-again")
        self.assertEqual([], again["changed"])
        self.assertEqual([], again["removed"])
        self.assertIsNone(again["commit"])

    def test_legacy_operational_manifest_entries_are_relinquished_never_deleted(self):
        self.seed_public(legacy_operational=True)
        report = self.apply(self.first, True)
        for name in ["market/featured.json", "market/revocations.json"]:
            self.assertNotIn(name, report["removed"])
            self.assertNotIn(name, report["changed"])
            self.assertEqual((self.public / name).read_bytes(), b'{"public_operational_state": true}\n')
        public_manifest = json.loads((self.public / "SOURCE_MANIFEST.json").read_text("utf-8"))
        self.assertFalse(any(x["path"] == "market/revocations.json" for x in public_manifest["files"]))

    def test_rejects_unknown_overwrite_public_modification_and_dirty_checkout(self):
        self.seed_public()
        self.write(self.public, "tools/unknown.py", b"public author code\n")
        self.commit(self.public)
        self.write(self.source, "tools/unknown.py", b"different private source\n")
        second = self.commit(self.source)
        with self.assertRaisesRegex(ValueError, "unowned or publicly modified"):
            self.apply(second, True)
        self.write(self.public, "tools/example.py", b"public fix\n")
        self.commit(self.public)
        with self.assertRaisesRegex(ValueError, "publicly modified"):
            self.apply(self.first, True)
        self.write(self.public, "uncommitted.txt", b"user work\n")
        with self.assertRaisesRegex(ValueError, "uncommitted"):
            self.apply(self.first, True)

    def test_missing_public_readme_never_falls_back_to_generated_prose(self):
        (self.source / "docs/public/README.md").unlink()
        commit = self.commit(self.source)
        with self.assertRaisesRegex(ValueError, "reviewed docs/public/README.md"):
            export.prepare(self.source, commit, self.root / "absent")

    def test_explicit_public_image_scope_and_passive_svg(self):
        png = b"\x89PNG\r\n\x1a\n" + b"\0\0\0\rIHDR" + struct.pack(">II", 16, 16) + b"\x08\x06\0\0\0" + b"\0" * 4
        self.write(self.source, "docs/public/assets/cover.png", png)
        self.write(self.source, "docs/public/assets/parallax.svg", b'<svg xmlns="http://www.w3.org/2000/svg"><path fill="#123" d="M0 0L1 1"/></svg>')
        self.write(self.source, "docs/private.png", png)
        self.write(self.source, "docs/public/assets/payload.exe", b"MZpayload")
        commit = self.commit(self.source)
        export.prepare(self.source, commit, self.root / "images")
        _, files = sync.verify_snapshot(self.root / "images", commit)
        self.assertEqual(png, files["docs/public/assets/cover.png"])
        self.assertNotIn("docs/private.png", files)
        self.assertNotIn("docs/public/assets/payload.exe", files)
        for svg in [b'<svg><script>evil()</script></svg>', b'<svg><path onload="evil()"/></svg>', b'<svg><image href="https://example.com/a"/></svg>', b'<!DOCTYPE svg><svg/>', b'<svg><path fill="url(https://x)"/></svg>']:
            with self.subTest(svg=svg), self.assertRaises(ValueError):
                export.validate_public_image("docs/public/assets/x.svg", svg)
        with self.assertRaises(ValueError):
            export.validate_public_image("docs/public/assets/x.png", b"MZnotimage")

    def test_passive_svg_text_still_requires_credential_scan(self):
        # Construct inert fixture markers; never use real credentials.
        markers = ["gh" + "p_" + "a" * 36,
                   "-----BEGIN " + "PRIVATE KEY-----"]
        for tag in ["text", "title", "desc"]:
            for marker in markers:
                with self.subTest(tag=tag, marker_kind=marker[:6]):
                    data = ("<svg xmlns=\"http://www.w3.org/2000/svg\"><" + tag + ">" + marker + "</" + tag + "></svg>").encode("utf-8")
                    with self.assertRaisesRegex(ValueError, "credential-content-review-required"):
                        export.validate_public_image("docs/public/assets/credential.svg", data)
        self.write(self.source, "docs/public/assets/diagram.svg", ("<svg><title>" + markers[0] + "</title></svg>").encode())
        commit = self.commit(self.source)
        with self.assertRaisesRegex(ValueError, "credential-content-review-required"):
            export.prepare(self.source, commit, self.root / "credential-output")
        self.assertFalse((self.root / "credential-output").exists())

    def test_apply_never_overwrites_ignored_files_during_base_switch(self):
        self.seed_public()
        self.write(self.public, ".gitignore", b"public-collision.txt\n")
        current = self.commit(self.public)
        self.git(self.public, "switch", "-c", "task/fresh-public-base")
        self.write(self.public, "public-collision.txt", b"new public non-snapshot file\n")
        self.git(self.public, "add", "-f", "public-collision.txt")
        self.commit(self.public)
        fresh_base = self.git(self.public, "rev-parse", "HEAD").decode().strip()
        self.git(self.public, "switch", "dev/v0.0")
        self.write(self.public, "public-collision.txt", b"ignored local unique bytes\n")
        self.write(self.source, "tools/example.py", b"# new snapshot\n")
        source = self.commit(self.source)
        self.assertEqual(b"", self.git(self.public, "status", "--porcelain"))
        with patch.object(sync, "refresh_base", return_value=fresh_base):
            with self.assertRaises(subprocess.CalledProcessError):
                sync.synchronize(self.source, source, self.public, "dev/v0.0", "task/sync-collision", True)
        self.assertEqual((self.public / "public-collision.txt").read_bytes(), b"ignored local unique bytes\n")
        self.assertEqual(self.git(self.public, "rev-parse", "HEAD").decode().strip(), current)
        self.assertEqual(self.git(self.public, "branch", "--show-current").decode().strip(), "dev/v0.0")

    def test_rejects_traversal_checksums_and_non_task_branch(self):
        with self.assertRaises(ValueError):
            sync.parse_sums(b"0" * 64 + b"  ../secret\n")
        self.seed_public()
        with self.assertRaisesRegex(ValueError, "task/\\*"):
            self.apply(self.first, True, "main")

    def test_210_files_use_constant_batch_processes_and_preserve_exact_bytes(self):
        expected = {}
        for index in range(210):
            name = "tools/批读/example%03d.py" % index
            expected[name] = ("# 中文 original %d\r\n" % index).encode("utf-8")
            self.write(self.source, name, expected[name])
        self.first = self.commit(self.source)
        self.seed_public()
        self.write(self.public, "unowned-large.bin", bytes(range(256)) * 1024)
        self.commit(self.public)
        unknown_oid = self.git(self.public, "rev-parse", "HEAD:unowned-large.bin").strip()
        (self.source / "tools/retired.py").unlink()
        expected["tools/批读/example000.py"] = b"# updated exact LF bytes\n"
        self.write(self.source, "tools/批读/example000.py", expected["tools/批读/example000.py"])
        source = self.commit(self.source)
        for apply, expected_batches in [(False, 1), (True, 2)]:
            with self.subTest(apply=apply), patch.object(sync, "git", wraps=sync.git) as calls:
                report = self.apply(source, apply)
            cat_calls = [call for call in calls.call_args_list if call.args[1] == "cat-file"]
            batch_calls = [call for call in cat_calls if call.args[2] == "--batch"]
            self.assertEqual(len(batch_calls), expected_batches)
            self.assertEqual(len(cat_calls), expected_batches + 3)  # prior manifest and checksums only
            for call in batch_calls:
                self.assertNotIn(unknown_oid + b"\n", call.kwargs["input_data"])
            self.assertIn("tools/retired.py", report["removed"])
        entries = sync.read_tree(self.public, "HEAD")
        actual = sync.batch_blobs(self.public, entries, expected)
        self.assertEqual(actual, expected)
        self.assertEqual((self.public / "unowned-large.bin").read_bytes(), bytes(range(256)) * 1024)

    def test_batch_response_requires_exact_oid_type_size_bytes_and_no_tail(self):
        raw = b"exact\r\nUTF-8 \xe4\xb8\xad\x00binary\n"
        oid = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
        entries = {"selected.bin": ("100644", oid)}
        valid = oid.encode() + b" blob " + str(len(raw)).encode() + b"\n" + raw + b"\n"
        with patch.object(sync, "git", return_value=valid):
            self.assertEqual(sync.batch_blobs(self.public, entries, entries), {"selected.bin": raw})
        invalid = [valid.replace(oid.encode(), b"0" * 40, 1),
                   valid.replace(b" blob ", b" tree ", 1),
                   valid.replace(b" blob " + str(len(raw)).encode(), b" blob invalid", 1),
                   valid[:-2], valid + b"unexpected", valid.replace(b"exact", b"wrong", 1)]
        for response in invalid:
            with self.subTest(response=response[:50]), patch.object(sync, "git", return_value=response):
                with self.assertRaises(ValueError):
                    sync.batch_blobs(self.public, entries, entries)


if __name__ == "__main__":
    unittest.main()
