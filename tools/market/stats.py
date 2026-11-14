"""Daily asset counters, conservative window attribution and explicit coverage."""
import copy
import re
from datetime import date, timedelta

from .common import require


def observe(previous, day, samples, statuses=None, revoked_builds=None):
    current_day = date.fromisoformat(day)
    require(isinstance(samples, list), "API observation failed; keep last successful state")
    state = copy.deepcopy(previous) if previous else {"schemaVersion": 1, "latestDay": day, "observedDays": [], "assets": {}, "works": {}, "anomalies": [], "revokedBuilds": []}
    require(day >= state["latestDay"], "out-of-order observation")
    seen = set()
    for sample in samples:
        asset_key = str(sample["repositoryId"]) + ":" + str(sample["assetId"])
        require(asset_key not in seen, "asset counted more than once/cross-work reference")
        seen.add(asset_key)
        count = sample["downloadCount"]
        require(re.fullmatch(r"[0-9a-f]{64}", sample["buildId"]) is not None, "invalid checked build identity")
        require(type(count) is int and count >= 0, "invalid download counter")
        require(sample["status"] in {"installable", "awaiting-sdk-validation", "revoked"}, "invalid work status")
        state["works"][sample["work"]] = sample["status"]
        asset = state["assets"].get(asset_key)
        if asset:
            require(asset["work"] == sample["work"] and asset["volumeGroup"] == sample["volumeGroup"]
                    and asset["buildId"] == sample["buildId"], "asset reassigned to another work/group/build")
            delta = max(0, count - asset["lastCount"])
            if count < asset["lastCount"]:
                anomaly = {"asset": asset_key, "day": day, "reason": "counter-decreased"}
                if anomaly not in state["anomalies"]:
                    state["anomalies"].append(anomaly)
            if asset["lastDay"] == day:
                asset["events"][-1]["delta"] += delta
            else:
                asset["events"].append({"from": asset["lastDay"], "to": day, "delta": delta})
            # Keep the high-water count: a reset cannot cause the next sample to count again.
            asset["lastCount"] = max(asset["lastCount"], count)
            asset["lastDay"] = day
        else:
            state["assets"][asset_key] = {"work": sample["work"], "volumeGroup": sample["volumeGroup"], "buildId": sample["buildId"],
                                          "firstDay": day, "lastDay": day, "lastCount": count,
                                          "events": [{"from": None, "to": day, "delta": count}]}
    if statuses:
        require(all(value in {"installable", "awaiting-sdk-validation", "revoked"} for value in statuses.values()), "invalid status overlay")
        state["works"].update(statuses)
    if revoked_builds is not None:
        require(all(re.fullmatch(r"[0-9a-f]{64}", build) is not None for build in revoked_builds), "invalid revoked build")
        state["revokedBuilds"] = sorted(set(revoked_builds))
    state["latestDay"] = current_day.isoformat()
    state["observedDays"] = sorted(set(state["observedDays"]) | {day})
    return state


def rank(state, day, days, public=True):
    require(days in (7, 30), "only 7/30-day windows supported")
    end = date.fromisoformat(day); start = end - timedelta(days=days - 1)
    require(day == state["latestDay"], "rank must use latest successful observation")
    rows = []
    for work, status in state["works"].items():
        if status == "revoked" or (public and status != "installable"):
            continue
        assets = [(k, a) for k, a in state["assets"].items() if a["work"] == work and a["buildId"] not in state["revokedBuilds"]]
        groups, unknown, observed_since, complete = {}, 0, day, True
        volumes = False
        for asset_key, asset in assets:
            observed_since = min(observed_since, asset["firstDay"])
            total, unallocated = 0, 0
            for event in asset["events"]:
                to = date.fromisoformat(event["to"])
                if not start <= to <= end:
                    continue
                if event["from"] is None or date.fromisoformat(event["from"]) >= start - timedelta(days=1):
                    total += event["delta"]
                else:
                    unallocated += event["delta"]
            group = asset["volumeGroup"] or asset_key
            volumes |= asset["volumeGroup"] is not None
            groups.setdefault(group, []).append(total)
            unknown += unallocated
            # Each asset needs observations throughout its active interval; sparse observations are not zeros.
            observed = {e["to"] for e in asset["events"]}
            complete &= all((start + timedelta(days=i)).isoformat() in observed for i in range(days))
        rows.append({"work": work, "downloads": sum(min(counts) for counts in groups.values()),
                     "windowDays": days, "observedSince": observed_since, "complete": bool(complete),
                     "unallocatedDownloads": unknown, "status": status,
                     "metric": "下载次数（分卷取最小计数，非安装人数）" if volumes else "下载次数"})
    return sorted(rows, key=lambda row: (-row["downloads"], row["work"]))


def featured(configured_keys, state):
    return [key for key in configured_keys if state["works"].get(key) == "installable"]
