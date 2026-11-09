"""Query OSV for the reachable Windows dependency graph from locked cargo metadata.

Generate each input with cargo metadata --locked --format-version 1
--filter-platform x86_64-pc-windows-msvc (root and ui/Cargo.toml).
Only package names/versions are sent. Network failure is a failed gate, never a pass.
"""
import argparse
import datetime
import json
import pathlib
import urllib.request


def request(path, body=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request("https://api.osv.dev/v1/" + path, data=data,
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=40) as response:
        return json.load(response)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--metadata", nargs="+", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    packages = set()
    for name in args.metadata:
        data = json.loads(pathlib.Path(name).read_text(encoding="utf-8-sig"))
        nodes = {n["id"]: n for n in data["resolve"]["nodes"]}
        reached, pending = set(), [data["resolve"]["root"]]
        while pending:
            key = pending.pop()
            if key in reached:
                continue
            reached.add(key)
            pending.extend(d["pkg"] for d in nodes[key]["deps"])
        packages.update((p["name"], p["version"]) for p in data["packages"]
                        if p["id"] in reached and (p["source"] or "").startswith("registry+"))
    findings = []
    packages = sorted(packages)
    for offset in range(0, len(packages), 100):
        chunk = packages[offset:offset + 100]
        pending = [(name, version, {"package": {"name": name, "ecosystem": "crates.io"},
                                    "version": version}) for name, version in chunk]
        while pending:
            result = request("querybatch", {"queries": [p[2] for p in pending]})["results"]
            assert len(result) == len(pending), "incomplete advisory response"
            next_page = []
            for (name, version, query), item in zip(pending, result):
                for vuln in item.get("vulns", []):
                    detail = request("vulns/" + vuln["id"])
                    if not detail.get("withdrawn"):
                        findings.append({"package": name, "version": version,
                                         "id": vuln["id"], "summary": detail.get("summary"),
                                         "url": "https://osv.dev/vulnerability/" + vuln["id"]})
                if item.get("next_page_token"):
                    next_page.append((name, version, {**query, "page_token": item["next_page_token"]}))
            pending = next_page
    report = {"at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "target": "Windows x64 MSVC/GNU graphs supplied by release build (runtime and build dependencies)",
              "packages": len(packages), "findings": findings,
              "status": "fail" if findings else "pass"}
    pathlib.Path(args.output).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf8")
    print(json.dumps(report, ensure_ascii=False))
    return bool(findings)


if __name__ == "__main__":
    raise SystemExit(main())
