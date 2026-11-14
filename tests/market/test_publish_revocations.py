import copy
import unittest
from unittest.mock import patch

from fixtures import manifest, releases, repo
from tools.market import publish
from tools.market.common import ROOT, MarketError, read_json, validate

NOW = "2026-11-14T00:00:00Z"


class CollectionSource:
    def resolve(self, repository_id): return repo()
    def manifest(self, repository): return manifest()
    def releases(self, repository): return releases()
    def asset(self, asset): return asset["browser_download_url"], [b"rain"]


class PublishedRevocationTests(unittest.TestCase):
    def collect(self, revocations, registrations=None):
        registrations = {101: {"repository": "alice/windows"}} if registrations is None else registrations
        with patch.object(publish, "registrations", return_value=registrations):
            return publish.collect(CollectionSource(), None, NOW, revocations)[0]

    def test_real_repository_envelope_becomes_valid_nested_candidate(self):
        envelope = read_json(ROOT / "market/revocations.json")
        before = copy.deepcopy(envelope)
        candidate = self.collect(envelope)
        self.assertEqual(candidate["revocations"], {k: envelope[k] for k in ("works", "builds")})
        self.assertEqual(len(candidate["entries"]), 1)
        validate("catalog", candidate)
        self.assertEqual(envelope, before)
        # An empty registry must not let the versioned envelope leak into output.
        validate("catalog", self.collect(envelope, {}))

    def test_internal_unversioned_revocations_still_apply_and_are_copied(self):
        for versioned in (False, True):
            value = {"works": ["101:rain"], "builds": ["a" * 64]}
            if versioned: value["schemaVersion"] = 1
            candidate = self.collect(value)
            self.assertEqual(candidate["entries"][0]["status"], "revoked")
            self.assertEqual(candidate["revocations"], {"works": ["101:rain"], "builds": ["a" * 64]})
            candidate["revocations"]["works"].append("another")
            self.assertEqual(value["works"], ["101:rain"])

    def test_invalid_envelopes_rejected_before_repository_or_network_work(self):
        valid = {"schemaVersion": 1, "works": [], "builds": []}
        invalid = [dict(valid, schemaVersion=v) for v in (0, 2, True, "1", None)]
        invalid += [dict(valid, unexpected=True), {"works": []}, {"builds": []}, [], None,
                    dict(valid, works="101:rain"), dict(valid, works=[1]),
                    dict(valid, works=["101:rain", "101:rain"]),
                    dict(valid, builds=["not-a-digest"]), dict(valid, builds=["a" * 64] * 2)]
        for value in invalid:
            with self.subTest(value=value), patch.object(publish, "registrations") as registrations:
                with self.assertRaises(MarketError):
                    publish.collect(CollectionSource(), None, NOW, value)
                registrations.assert_not_called()


if __name__ == "__main__":
    unittest.main()
