"""Synthetic CC0 metadata and inert bytes; no executable wallpaper packages."""
import copy

REPO = {"id": 101, "full_name": "alice/windows", "owner": {"id": 11, "login": "alice", "type": "User"}, "default_branch": "main", "private": False}
MANIFEST = {
    "schemaVersion": 1, "repositoryId": 101, "listingConsent": True,
    "works": [{"id": "rain", "title": "Rain", "version": "1.0.0", "releaseTag": "v1", "sdk": "declared-only",
               "capabilities": ["static"], "assets": ["rain.zip"],
               "attribution": [{"name": "Alice", "githubUserId": 11, "license": "CC0-1.0"}]}]
}


def repo():
    return copy.deepcopy(REPO)


def manifest():
    return copy.deepcopy(MANIFEST)


def pr():
    return {"number": 9, "state": "open", "draft": False,
            "head": {"sha": "a" * 40}, "base": {"ref": "main", "repo": {"full_name": "KyaKayKya/Spatial-Wallpaper"}},
            "user": {"id": 11}}


def releases():
    return [{"id": 301, "tag_name": "v1", "draft": False, "prerelease": False,
             "assets": [{"id": 401, "name": "rain.zip", "size": 4, "download_count": 10,
                         "browser_download_url": "https://github.com/alice/windows/releases/download/v1/rain.zip"}]}]
