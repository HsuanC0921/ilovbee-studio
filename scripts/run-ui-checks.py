"""Run CLI-first browser regressions against a fresh isolated demo executable.

Install @playwright/cli@0.1.23 in .test-tools or pass --cli-path. Chrome must exist.
Never uses the user's running host, physical HID, camera, or audio endpoint.
"""
import argparse
import json
import os
import pathlib
import subprocess
import tempfile
import time
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--binary", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--cli-path", default=".test-tools/node_modules/@playwright/cli/playwright-cli.js")
    args = parser.parse_args()
    root = pathlib.Path(__file__).resolve().parent.parent
    cli = pathlib.Path(args.cli_path).resolve()
    report, host = {"status": "fail", "browser": "Chrome, isolated demo", "suites": []}, None
    with tempfile.TemporaryDirectory(prefix="ilovbee-browser-") as temporary:
        data = pathlib.Path(temporary)
        try:
            host = subprocess.Popen([str(pathlib.Path(args.binary).resolve()), "--demo", "--headless",
                "--data-dir", temporary], stdin=subprocess.PIPE, stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
            deadline = time.monotonic() + 15
            while not (data / "server.txt").exists():
                assert host.poll() is None and time.monotonic() < deadline, "demo startup failed"
                time.sleep(.02)
            port, token = (data / "server.txt").read_text().splitlines()
            session = "gate-" + str(host.pid)

            def browser(*arguments, result=False):
                completed = subprocess.run(["node", str(cli), "-s=" + session, "--raw", *arguments],
                    cwd=root, capture_output=True, text=True, encoding="utf8", errors="replace", timeout=90)
                output = (completed.stdout + completed.stderr).replace(token, "[redacted]")
                if completed.returncode or "### Error" in output:
                    raise RuntimeError(output[-3000:])
                if result:
                    # Modal output or a missing return is an unfinished test, not a pass.
                    for offset, character in enumerate(output):
                        if character != '{':
                            continue
                        try:
                            value, _ = json.JSONDecoder().raw_decode(output[offset:])
                            if isinstance(value, dict) and value.get('status') == 'pass':
                                return value
                        except json.JSONDecodeError:
                            continue
                    raise AssertionError("browser test did not return a pass result: " + output[:1500])
                return output

            def api(path, value=None):
                request = urllib.request.Request(f"http://127.0.0.1:{port}" + path,
                    data=None if value is None else json.dumps(value).encode(), headers={"Cookie": "ilv=" + token})
                with urllib.request.urlopen(request, timeout=10) as response:
                    return json.load(response)

            browser("open", f"http://127.0.0.1:{port}/?t={token}", "--browser=chrome")
            assert api("/api/state")["demo"]
            # Native file-picker boundary is kept out of run-code suites: CLI
            # intentionally hands control back when a native modal opens.
            macro = data / "macros.json"
            macro.write_text(json.dumps([{"Name": "原生匯入測試", "Events": [
                {"Code": 4, "Up": False, "Delay": 10}, {"Code": 4, "Up": True, "Delay": 0}]}]), encoding="utf8")
            browser("run-code", "async page => {await page.getByRole('navigation').getByRole('button',{name:'巨集',exact:true}).click();await page.getByRole('button',{name:'匯入巨集（加到清單後面）',exact:true}).click();}")
            browser("upload", str(macro))
            deadline = time.monotonic() + 5
            while not any(m["Name"] == "原生匯入測試" for m in api("/api/state")["macros"]):
                assert time.monotonic() < deadline, "native file upload did not import macro"
                time.sleep(.05)
            api("/api/macros/save", [])
            original = api("/api/profile")
            profile = data / "profile.json"
            profile.write_text(json.dumps(original), encoding="utf8")
            changed = bytearray.fromhex(original["Lighting"])
            changed[18] = 3
            api("/api/lights", {"expected": original["Lighting"], "after": changed.hex()})
            for decision in ["dialog-dismiss", "dialog-accept"]:
                browser("run-code", "async page => {await page.getByRole('navigation').getByRole('button',{name:'設定檔',exact:true}).click();await page.getByRole('button',{name:'從檔案還原',exact:true}).click();}")
                browser("upload", str(profile))
                browser(decision)
                deadline = time.monotonic() + 5
                wanted = changed.hex().upper() if decision == "dialog-dismiss" else original["Lighting"]
                while api("/api/profile")["Lighting"] != wanted:
                    assert time.monotonic() < deadline, "native restore dialog result mismatch"
                    time.sleep(.05)
            report["suites"].append({"status": "pass", "checks": ["native macro file chooser/import", "native profile restore cancel", "native profile restore accept"]})
            for name in ["check-workflows-ui.js", "check-keyboard-ui.js", "check-lighting-ui.js", "check-theme-ui.js"]:
                (root / ".impeccable/review").mkdir(parents=True, exist_ok=True)
                outcome = browser("run-code", "--filename", str(root / "scripts" / name), result=True)
                report["suites"].append({"script": name, **outcome})
            report["status"] = "pass"
            browser("close")
        except Exception as error:
            report["error"] = str(error)
        finally:
            if 'browser' in locals():
                try:
                    browser('close')
                except Exception:
                    # An already-closed browser has no application state to recover.
                    pass
            if host and host.poll() is None:
                try:
                    host.communicate(b"quit\n", timeout=10)
                except subprocess.TimeoutExpired:
                    host.kill()
                    host.wait()
            pathlib.Path(args.output).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf8")
            # Keep console JSON readable by Windows runners with non-UTF-8 stdout.
            print(json.dumps(report))
    return report["status"] != "pass"


if __name__ == "__main__":
    raise SystemExit(main())
