"""Read-only GitHub adapter. External data is never imported or executed."""
import base64
import re
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener

from .catalog import CDN_HOSTS
from .common import OFFICIAL, MarketError, loads, require, validate


class ApiNoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise MarketError("API redirect refused; resolve repository by numeric ID")


class AssetRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        target = urlsplit(newurl)
        require(target.scheme == "https" and target.hostname in CDN_HOSTS
                and target.port in (None, 443) and target.username is None and target.password is None,
                "untrusted asset redirect")
        require(code in (301, 302, 303, 307, 308), "unexpected asset redirect")
        return Request(newurl, headers={"User-Agent": "Spatial-Wallpaper-market/1"})


class JsonTransport:
    def __init__(self, token=None, opener=None):
        self.token = token
        self.opener = opener or build_opener(ApiNoRedirect()).open

    def __call__(self, path):
        require(path.startswith("/") and not path.startswith("//") and ".." not in path
                and "\\" not in path and not any(ord(c) < 32 for c in path), "unsafe GitHub API path")
        headers = {"Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28",
                   "User-Agent": "Spatial-Wallpaper-market/1"}
        if self.token:
            headers["Authorization"] = "Bearer " + self.token
        try:
            with self.opener(Request("https://api.github.com" + path, headers=headers), timeout=30) as response:
                require(response.geturl().startswith("https://api.github.com/"), "API origin changed")
                raw = response.read(8 * 1024 * 1024 + 1)
                require(len(raw) <= 8 * 1024 * 1024, "API response budget exceeded")
                return loads(raw), dict(response.headers)
        except HTTPError as error:
            raise MarketError("GitHub HTTP " + str(error.code) + "; retry-after=" + str(error.headers.get("Retry-After"))
                              + "; reset=" + str(error.headers.get("X-RateLimit-Reset"))) from error
        except URLError as error:
            raise MarketError("GitHub transport failed: " + str(error.reason)) from error


def has_next(headers):
    return 'rel="next"' in next((v for k, v in headers.items() if k.lower() == "link"), "")


def repository_path(full_name):
    require(re.fullmatch(r"[A-Za-z0-9-]+/[A-Za-z0-9_.-]+", full_name) is not None, "invalid repository name")
    return "/repos/" + full_name


class GitHub:
    def __init__(self, get=None):
        self.get = get or JsonTransport()

    def resolve(self, repository_id):
        require(type(repository_id) is int and repository_id > 0, "invalid repository ID")
        return self.get("/repositories/" + str(repository_id))[0]

    def pages(self, path):
        items = []
        for page in range(1, 101):
            data, headers = self.get(path + ("&" if "?" in path else "?") + urlencode({"per_page": 100, "page": page}))
            require(isinstance(data, list), "expected complete API list")
            items.extend(data)
            if not has_next(headers):
                return items
        raise MarketError("API pagination budget exhausted; retain previous snapshot")

    def search(self, query, page):
        data, headers = self.get("/search/repositories?" + urlencode({"q": query, "per_page": 100, "page": page}))
        return {**data, "has_next": has_next(headers)}

    def default_file(self, repository, filename):
        base = repository_path(repository["full_name"])
        head = self.get(base + "/commits/" + quote(repository["default_branch"], safe=""))[0]["sha"]
        require(re.fullmatch(r"[0-9a-f]{40}", head) is not None, "invalid default branch commit")
        return self.file_at(repository["full_name"], filename, head)

    def file_at(self, full_name, filename, ref):
        data = self.get(repository_path(full_name) + "/contents/" + quote(filename, safe="/") + "?ref=" + quote(ref, safe=""))[0]
        require(data["type"] == "file" and data["encoding"] == "base64", "expected small default-branch JSON file")
        content = base64.b64decode("".join(data["content"].split()), validate=True)
        require(len(content) <= 1024 * 1024, "manifest budget exceeded")
        return loads(content)

    def registration(self, number, checked_head, challenge, now):
        from .registry import registration_plan
        require(type(number) is int and number > 0, "invalid PR number")
        path = repository_path(OFFICIAL) + "/pulls/" + str(number)
        pull = self.get(path)[0]
        require(pull["head"]["sha"] == checked_head, "PR head changed before collection")
        files = self.pages(path + "/files")
        require(len(files) == pull["changed_files"] == 1, "all PR files required; only one new registration allowed")
        require(re.fullmatch(r"market/registry/[1-9][0-9]*\.json", files[0]["filename"]) is not None
                and files[0]["status"] == "added", "non-registration PR rejected")
        registration = self.file_at(pull["head"]["repo"]["full_name"], files[0]["filename"], checked_head)
        validate("registration", registration)
        repository = self.resolve(registration["repositoryId"])
        # Consent and target identity come from target default branch, never the fork.
        from .registry import validate_manifest
        validate_manifest(repository, self.manifest(repository))
        declaration = None
        if repository["owner"]["type"] != "User" or pull["user"]["id"] != repository["owner"]["id"]:
            declaration = {"sourceRepositoryId": repository["id"], "sourceRef": repository["default_branch"],
                           "value": self.default_file(repository, "market-control/" + str(number) + ".json")}
        current = self.get(path)[0]
        return registration_plan(pull, checked_head, current["head"]["sha"], files, registration,
                                 repository, declaration, challenge, now)

    def manifest(self, repository):
        return self.default_file(repository, "wallpaper.json")

    def releases(self, full_name):
        return self.pages(repository_path(full_name) + "/releases")

    def asset(self, asset):
        # Caller validates exact repo/tag/name before entry. No API credential used here.
        response = build_opener(AssetRedirects()).open(Request(asset["browser_download_url"], headers={"User-Agent": "Spatial-Wallpaper-market/1"}), timeout=30)
        def chunks():
            try:
                while True:
                    chunk = response.read(64 * 1024)
                    if not chunk:
                        return
                    yield chunk
            finally:
                response.close()
        return response.geturl(), chunks()
