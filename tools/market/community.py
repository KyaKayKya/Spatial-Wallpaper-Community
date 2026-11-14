"""Official Discussion reconciliation plans and bounded README generation."""
import copy
import html
import re

from .common import OFFICIAL, require

START = "<!-- market-creators:start -->"
END = "<!-- market-creators:end -->"


def discussion_plan(entries, mapping, existing, category_id, trusted_actor_id):
    require(category_id and isinstance(existing, list), "authoritative Discussions response/category required")
    require(type(trusted_actor_id) is int and trusted_actor_id > 0, "trusted discussion creator ID required")
    result, operations = copy.deepcopy(mapping), []
    for entry in entries:
        key = entry["key"]
        require(re.fullmatch(r"[1-9][0-9]*:[a-z0-9][a-z0-9-]{0,63}", key) is not None, "invalid stable discussion identity")
        marker = "<!-- spatial-market:" + key + " -->"
        matches = [d for d in existing if marker in d["body"].splitlines() and d.get("authorId") == trusted_actor_id]
        require(len(matches) <= 1, "duplicate official discussions; reconcile manually")
        if key in result:
            record = result[key]
            require(record["repository"] == OFFICIAL and record["categoryId"] == category_id
                    and record.get("authorId") == trusted_actor_id, "foreign/untrusted discussion mapping")
            require(len(matches) == 1 and matches[0]["id"] == record["id"] and matches[0]["number"] == record["number"], "mapped discussion missing or mismatched; do not recreate")
        if matches:
            record = matches[0]
            require(record["repository"] == OFFICIAL and record["categoryId"] == category_id, "foreign discussion marker")
            require(type(record["number"]) is int and record["number"] > 0, "invalid discussion number")
            result[key] = {k: record[k] for k in ["id", "number", "repository", "categoryId", "authorId"]}
            result[key]["url"] = "https://github.com/" + OFFICIAL + "/discussions/" + str(record["number"])
        elif entry["status"] != "revoked":
            operations.append({"dry_run": True, "operation": "createDiscussion", "repository": OFFICIAL,
                               "categoryId": category_id, "key": key, "marker": marker,
                               "title": "Wallpaper " + key, "body": marker + "\n作品交流；产品故障请使用官方 Issues。",
                               "idempotencyKey": key})
    return {"mapping": result, "operations": operations}


def markdown_text(value):
    text = " ".join(value.split())
    text = html.escape(text, quote=True).replace("@", "&#64;")
    return re.sub(r"([\\`*_{}\[\]()#+.!|~-])", r"\\\1", text)


def render_credits(readme, entries, identities):
    require(readme.count(START) == readme.count(END) == 1 and readme.index(START) < readme.index(END), "exact README generated markers required")
    lines = ["", "以下为已声明收录的创作者；候选作品仍待 SDK 核查，署名不代表内容准入或原创认证。", ""]
    for entry in sorted(entries, key=lambda e: e["key"]):
        if entry["status"] == "revoked":
            continue
        require(re.fullmatch(r"[A-Za-z0-9-]+/[A-Za-z0-9_.-]+", entry["repository"]) is not None, "invalid repository path")
        authors = []
        for author in entry["attribution"]:
            identity = identities.get(author["githubUserId"])
            require(identity is not None and identity["id"] == author["githubUserId"]
                    and re.fullmatch(r"[A-Za-z0-9-]+", identity["login"]) is not None, "author profile not resolved by numeric ID")
            authors.append("[" + markdown_text(author["name"]) + "](https://github.com/" + identity["login"] + ")")
        lines.append("- [" + markdown_text(entry["title"]) + "](https://github.com/" + entry["repository"] + ") — " + "、".join(authors) + "（候选）")
    if len(lines) == 3:
        lines.append("尚无已登记的创作者。")
    lines.append("")
    return readme[:readme.index(START) + len(START)] + "\n".join(lines) + readme[readme.index(END):]
