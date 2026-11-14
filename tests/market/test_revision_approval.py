import copy
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from test_published import BODY, NOW, WORK, promote, report
from test_work_binding import inputs
from tools.market import publish
from tools.market.common import ROOT, MarketError, atomic_json, read_json, validate

OTHER_WORK = "22222222-2222-4333-8444-555555555555"


def approval(revision="1.0.0", body=BODY):
    return {"schemaVersion": 1, "repositoryId": 101, "repository": "alice/windows", "workId": WORK,
            "revision": revision, "packageSha256": publish.digest(body)}


class Source:
    def __init__(self):
        self.repository, self.value, self.release_list = inputs(101)
        self.calls = []
        self.bodies = {401: BODY}
    def resolve(self, rid): return copy.deepcopy(self.repository)
    def manifest(self, repository): return copy.deepcopy(self.value)
    def releases(self, name): return copy.deepcopy(self.release_list)
    def asset(self, asset):
        self.calls.append(asset["id"])
        return asset["browser_download_url"], [self.bodies[asset["id"]]]
    def revise(self):
        self.value["works"][0].update(version="1.0.1", releaseTag="v2")
        release = copy.deepcopy(self.release_list[0]); release.update(id=302, tag_name="v2")
        release["assets"][0].update(id=402, browser_download_url="https://github.com/alice/windows/releases/download/v2/rain.spatial")
        self.bodies[402] = BODY
        self.release_list.append(release)
    def add_unapproved_work(self):
        self.value["works"].append(dict(self.value["works"][0], id=OTHER_WORK, assets=["not-released.spatial"]))


class RevisionApprovalTests(unittest.TestCase):
    def collect(self, source, previous=None, approved=(), revocations=None):
        records = {(v["repositoryId"], v["workId"], v["revision"]): v for v in approved}
        with patch.object(publish, "registrations", return_value={101: {"repository": "alice/windows"}}), patch.object(publish, "submissions", return_value=records):
            return publish.collect(source, previous, NOW, revocations or {"works": [], "builds": []})

    def test_registry_alone_never_publishes_a_new_work(self):
        source = Source()
        candidate, _, _ = self.collect(source)
        self.assertEqual(candidate["entries"], [])
        self.assertEqual(source.calls, [])

    def test_unapproved_revision_keeps_exact_signed_build_and_presentation(self):
        previous, _ = promote()
        previous["entries"][0]["description"] = "reviewed description"
        previous["entries"][0]["comments"] = {"kind": "issues", "number": 3, "url": "https://github.com/" + publish.MARKET_REPOSITORY + "/issues/3"}
        source = Source(); source.revise(); source.add_unapproved_work()
        candidate, _, presentation = self.collect(source, previous)
        self.assertEqual(len(candidate["entries"]), 1)
        self.assertEqual(candidate["entries"][0]["build"], previous["entries"][0]["build"])
        self.assertEqual(candidate["entries"][0]["history"], previous["entries"][0]["history"])
        self.assertEqual(candidate["entries"][0]["status"], "awaiting-sdk-validation")
        self.assertEqual(source.calls, [])
        with tempfile.TemporaryDirectory() as directory:
            final, _ = publish.promote(candidate, source.asset, report, directory, presentation, previous)
        self.assertEqual(final["entries"][0]["status"], "published")
        self.assertEqual(final["entries"][0]["build"], previous["entries"][0]["build"])
        self.assertEqual(final["entries"][0]["comments"], previous["entries"][0]["comments"])
        self.assertEqual(final["entries"][0]["description"], "reviewed description")

    def test_approved_revision_downloads_exact_hash_and_retains_old_history(self):
        previous, _ = promote(); source = Source(); source.revise(); source.add_unapproved_work()
        candidate, _, presentation = self.collect(source, previous, [approval("1.0.1")])
        self.assertEqual(len(candidate["entries"]), 1)
        self.assertEqual(candidate["entries"][0]["build"]["version"], "1.0.1")
        self.assertIn(previous["entries"][0]["build"], candidate["entries"][0]["history"])
        self.assertEqual(source.calls, [402])
        def updated_report(path):
            value = report(path); value["revision"] = "1.0.1"; return value
        with tempfile.TemporaryDirectory() as directory:
            final, _ = publish.promote(candidate, source.asset, updated_report, directory, presentation, previous)
        self.assertEqual(final["entries"][0]["status"], "published")
        self.assertEqual(final["entries"][0]["build"]["version"], "1.0.1")

    def test_bytes_replaced_after_approval_or_collection_are_rejected(self):
        source = Source(); source.revise()
        source.bodies[402] = b"x" * len(BODY)
        with self.assertRaisesRegex(MarketError, "SHA256"):
            self.collect(source, approved=[approval("1.0.1")])
        source.bodies[402] = BODY
        candidate, _, _ = self.collect(source, approved=[approval("1.0.1")])
        source.bodies[402] = b"x" * len(BODY)
        with tempfile.TemporaryDirectory() as directory, self.assertRaisesRegex(MarketError, "bytes changed"):
            publish.promote(candidate, source.asset, report, directory)

    def test_explicit_revocations_apply_without_approved_current_updates(self):
        previous, _ = promote(); source = Source(); source.revise()
        entry = previous["entries"][0]
        for revocations in ({"works": [entry["key"]], "builds": []}, {"works": [], "builds": [entry["build"]["identity"]]}):
            candidate, _, _ = self.collect(source, previous, revocations=revocations)
            self.assertEqual(candidate["entries"][0]["status"], "revoked")
            self.assertEqual(candidate["entries"][0]["history"], entry["history"])
        with patch.object(publish, "registrations", return_value={}), patch.object(publish, "submissions", return_value={}):
            candidate, _, _ = publish.collect(source, previous, NOW, {"works": [entry["key"]], "builds": []})
        self.assertEqual(candidate["entries"][0]["status"], "revoked")
        previous["entries"][0]["status"] = "revoked"
        source.value["works"][0]["id"] = OTHER_WORK
        candidate, _, _ = self.collect(source, previous)
        self.assertEqual(candidate["entries"][0]["status"], "revoked")
        self.assertEqual(candidate["entries"][0]["build"], entry["build"])

    def test_merged_submission_file_approves_exact_bytes_and_registers_source(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(publish, "ROOT", Path(directory)):
            value = approval()
            atomic_json(Path(directory) / "market/schemas/catalog.schema.json", read_json(ROOT / "market/schemas/catalog.schema.json"))
            atomic_json(Path(directory) / "market" / publish.submission_path(value), value)
            self.assertEqual(publish.submissions()[(101, WORK, "1.0.0")], value)
            self.assertEqual(publish.registrations()[101]["repository"], "alice/windows")
            candidate, _, _ = publish.collect(Source(), None, NOW, {"works": [], "builds": []})
            self.assertEqual(candidate["entries"][0]["build"]["assets"][0]["sha256"], value["packageSha256"])

    def test_submission_schema_requires_exact_sha_not_safety_self_report(self):
        value = approval(); validate("submission", value)
        bad = copy.deepcopy(value); bad.pop("packageSha256")
        for patch_value in ({"packageSha256": "a" * 63}, {"packageSha256": "A" * 64}, {"packageSha256": "a" * 64 + "\n"}, {"safe": True}):
            with self.subTest(patch=patch_value), self.assertRaises(MarketError): validate("submission", dict(value, **patch_value))
        with self.assertRaises(MarketError): validate("submission", bad)


class SubmissionPrTests(unittest.TestCase):
    def source(self):
        source = Source(); source.add_unapproved_work()
        value = approval()
        pull = {"state": "open", "draft": False, "head": {"sha": "a" * 40, "repo": {"full_name": "alice/fork"}},
                "base": {"repo": {"full_name": publish.MARKET_REPOSITORY}, "ref": "dev/v0.0"}, "user": {"id": 11}, "changed_files": 1}
        source.get = lambda path: (copy.deepcopy(pull), {})
        source.pages = lambda path: [{"filename": "market/" + publish.submission_path(value), "status": "added"}]
        source.file_at = lambda repository, filename, head: copy.deepcopy(value)
        return source, value

    def test_pr_checks_only_requested_work_and_actual_package_hash(self):
        source, value = self.source()
        with tempfile.TemporaryDirectory() as directory:
            result = publish.check_pr(source, 1, "a" * 40, NOW, report, directory)
        self.assertEqual(result["works"], 1)
        self.assertEqual(result["packageSha256"], value["packageSha256"])
        self.assertEqual(source.calls, [401, 401])

    def test_pr_declared_hash_and_post_check_replaced_bytes_are_rejected(self):
        source, value = self.source(); value["packageSha256"] = "0" * 64
        with self.assertRaisesRegex(MarketError, "SHA256"):
            publish.check_pr(source, 1, "a" * 40, NOW)
        source, value = self.source(); original = source.asset
        def changed(asset):
            if source.calls: source.bodies[401] = b"x" * len(BODY)
            return original(asset)
        source.asset = changed
        with tempfile.TemporaryDirectory() as directory, self.assertRaisesRegex(MarketError, "bytes changed"):
            publish.check_pr(source, 1, "a" * 40, NOW, report, directory)


if __name__ == "__main__": unittest.main()
