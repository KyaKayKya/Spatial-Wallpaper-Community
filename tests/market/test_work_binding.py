import copy
import tempfile
import unittest
from unittest.mock import patch

from fixtures import manifest, releases, repo
from test_published import BODY, NOW, WORK, promote, report
from tools.market import catalog, publish
from tools.market.common import MarketError, canonical


def inputs(repository_id):
    repository, m, rs = repo(), manifest(), releases()
    repository["id"] = m["repositoryId"] = repository_id
    repository["full_name"] = "alice/windows" if repository_id == 101 else "bob/windows"
    m["works"][0].update(id=WORK, sdk=publish.SDK, capabilities=["view-data"], assets=["rain.spatial"])
    rs[0]["assets"][0].update(name="rain.spatial", size=len(BODY), browser_download_url="https://github.com/" + repository["full_name"] + "/releases/download/v1/rain.spatial")
    return repository, m, rs


def candidate(repository_id=101, previous=None):
    repository, m, rs = inputs(repository_id)
    return catalog.build_snapshot(repository, m, rs, lambda a: (a["browser_download_url"], [BODY]), previous,
                                  previous["generation"] + 1 if previous else 1, NOW, {"works": [], "builds": []})


class WorkBindingTests(unittest.TestCase):
    def test_candidate_rejects_foreign_guid_even_if_previous_is_revoked(self):
        for revoked in (False, True):
            previous = candidate()
            if revoked: previous["entries"][0]["status"] = "revoked"
            with self.assertRaisesRegex(MarketError, "another repository"):
                candidate(202, previous)
        self.assertEqual(candidate(101, candidate())["entries"][0]["repositoryId"], 101)

    def test_final_published_rejects_validly_bound_foreign_entry_and_tombstone(self):
        previous, _ = promote()
        foreign = copy.deepcopy(previous["entries"][0])
        foreign.update(repositoryId=202, repository="bob/windows", key="202:" + WORK)
        build = foreign["build"]
        build["assets"][0]["url"] = "https://github.com/bob/windows/releases/download/v1/rain.spatial"
        binding = {k: build[k] for k in ("version", "releaseId", "tag", "checker")}
        binding["assets"] = [catalog.asset_binding(a) for a in build["assets"]]
        build["identity"] = publish.digest(canonical({"repositoryId": 202, "ownerId": foreign["ownerId"], "workId": WORK, **binding}))
        foreign["history"] = [copy.deepcopy(build)]
        foreign["admission"]["buildIdentity"] = build["identity"]
        for revoked in (False, True):
            value = copy.deepcopy(previous)
            if revoked: value["entries"][0]["status"] = "revoked"
            value["entries"].append(copy.deepcopy(foreign))
            with self.assertRaisesRegex(MarketError, "another repository"):
                publish.validate_published(value)
        only_foreign = copy.deepcopy(previous); only_foreign["entries"] = [foreign]
        with self.assertRaisesRegex(MarketError, "another repository"):
            publish.validate_published(only_foreign, previous)

    def test_collection_detects_collision_across_current_repositories(self):
        class Source:
            def resolve(self, rid): return inputs(rid)[0]
            def manifest(self, repository): return inputs(repository["id"])[1]
            def releases(self, name): return inputs(101 if name.startswith("alice/") else 202)[2]
            def asset(self, asset): return asset["browser_download_url"], [BODY]
        registrations = {rid: {"repository": inputs(rid)[0]["full_name"]} for rid in (101, 202)}
        approvals = {(rid, WORK, "1.0.0"): {"repositoryId": rid, "repository": inputs(rid)[0]["full_name"], "workId": WORK, "revision": "1.0.0", "packageSha256": publish.digest(BODY)} for rid in (101, 202)}
        with patch.object(publish, "registrations", return_value=registrations), patch.object(publish, "submissions", return_value=approvals), self.assertRaisesRegex(MarketError, "another repository"):
            publish.collect(Source(), None, NOW, {"works": [], "builds": []})

    def test_promotion_cannot_discard_previous_guid_ownership(self):
        previous, _ = promote(); previous["entries"][0]["status"] = "revoked"
        with tempfile.TemporaryDirectory() as directory, self.assertRaisesRegex(MarketError, "another repository"):
            publish.promote(candidate(202), lambda a: (a["browser_download_url"], [BODY]), report, directory, previous=previous)


if __name__ == "__main__": unittest.main()
