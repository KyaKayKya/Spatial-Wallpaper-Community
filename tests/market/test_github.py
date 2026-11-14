import base64
import io
import unittest
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlsplit

from tools.market.github import GitHub, JsonTransport, AssetRedirects
from tools.market.common import MarketError
from fixtures import manifest, pr, repo


class GitHubTests(unittest.TestCase):
    def test_live_registration_rechecks_head_after_all_paginated_files(self):
        import json
        reads = []; current = pr()
        registration = {"schemaVersion": 1, "repositoryId": 101, "repository": "alice/windows"}
        def get(path):
            reads.append(path)
            if path.endswith("/pulls/9"):
                value = dict(current, changed_files=1)
                if reads.count(path) > 1: value["head"] = {"sha": "b" * 40}
                else: value["head"] = {"sha": "a" * 40, "repo": {"full_name": "alice/fork"}}
                return value, {}
            if "/files?" in path: return [{"filename": "market/registry/101.json", "status": "added"}], {}
            if path == "/repos/alice/windows/commits/main": return {"sha": "c" * 40}, {}
            if "/contents/" in path:
                data = manifest() if "/wallpaper.json?" in path else registration
                return {"type": "file", "encoding": "base64", "content": base64.b64encode(json.dumps(data).encode()).decode()}, {}
            if path == "/repositories/101": return repo(), {}
            raise AssertionError(path)
        with self.assertRaisesRegex(MarketError, "head changed"):
            GitHub(get).registration(9, "a" * 40, None, "2026-10-26T00:00:00Z")
        self.assertTrue(any("ref=" + "a" * 40 in p for p in reads))

    def test_release_pagination_and_search_link(self):
        seen = []
        def get(path):
            seen.append(path)
            if "search/repositories" in path:
                return {"items": [], "total_count": 0, "incomplete_results": False}, {"Link": '<https://api.github.com/search/repositories?page=2>; rel="next"'}
            if parse_qs(urlsplit(path).query)["page"] == ["1"]: return [{"id": 1}], {"Link": '<https://api.github.com/unused>; rel="next"'}
            return [{"id": 2}], {}
        gh = GitHub(get)
        self.assertEqual(gh.releases("alice/windows"), [{"id": 1}, {"id": 2}])
        self.assertTrue(gh.search("topic:gameai-wallpaper", 1)["has_next"])
        self.assertTrue(all(path.startswith("/") for path in seen))

    def test_manifest_is_read_from_resolved_default_commit_not_pr(self):
        seen = []
        def get(path):
            seen.append(path)
            if "/commits/" in path: return {"sha": "a" * 40}, {}
            return {"type": "file", "encoding": "base64", "content": base64.b64encode(b'{"listingConsent":true}').decode()}, {}
        gh = GitHub(get)
        self.assertEqual(gh.manifest({"full_name": "alice/windows", "default_branch": "main"}), {"listingConsent": True})
        self.assertIn("ref=" + "a" * 40, seen[1])

    def test_rate_error_is_explicit_and_never_becomes_empty_response(self):
        def opener(request, timeout):
            raise HTTPError(request.full_url, 429, "limited", {"Retry-After": "60", "X-RateLimit-Reset": "123"}, io.BytesIO(b"error"))
        with self.assertRaisesRegex(MarketError, "retry-after=60"):
            JsonTransport(opener=opener)("/search/repositories?q=x")

    def test_api_absolute_urls_and_asset_redirects_do_not_leak_credentials(self):
        called = []
        def opener(request, timeout): called.append(request); raise AssertionError("should not send")
        for path in ["https://evil.test/a", "//evil.test/a", "/repos/../a"]:
            with self.assertRaises(MarketError): JsonTransport(token="sensitive", opener=opener)(path)
        self.assertEqual(called, [])
        from urllib.request import Request
        handler = AssetRedirects()
        with self.assertRaises(MarketError):
            handler.redirect_request(Request("https://github.com/a/b/releases/download/v/a.zip"), None, 302, "", {}, "https://evil.test/a")
        redirected = handler.redirect_request(Request("https://github.com/a/b/releases/download/v/a.zip"), None, 302, "", {}, "https://release-assets.githubusercontent.com/a")
        self.assertNotIn("Authorization", redirected.headers)


if __name__ == "__main__": unittest.main()
