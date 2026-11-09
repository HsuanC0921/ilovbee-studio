"""Security and stress gate for a real executable with an isolated --demo device.

Never connects to an existing instance, opens audio/camera, or writes real HID.
"""
import argparse
import concurrent.futures
import contextlib
import ctypes
import datetime
import http.client
import hashlib
import json
import os
import pathlib
import socket
import statistics
import subprocess
import tempfile
import time


def process_resources(pid):
    if os.name != "nt":
        return None
    from ctypes import wintypes

    class Memory(ctypes.Structure):
        _fields_ = [("cb", wintypes.DWORD), ("PageFaultCount", wintypes.DWORD)] + [
            (name, ctypes.c_size_t) for name in ["PeakWorkingSetSize", "WorkingSetSize",
             "QuotaPeakPagedPoolUsage", "QuotaPagedPoolUsage", "QuotaPeakNonPagedPoolUsage",
             "QuotaNonPagedPoolUsage", "PagefileUsage", "PeakPagefileUsage", "PrivateUsage"]]

    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.GetProcessHandleCount.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    psapi = ctypes.WinDLL("psapi", use_last_error=True)
    psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(Memory), wintypes.DWORD]
    handle = kernel.OpenProcess(0x410, False, pid)
    assert handle, "cannot measure test process"
    try:
        memory, handles = Memory(), wintypes.DWORD()
        memory.cb = ctypes.sizeof(memory)
        assert psapi.GetProcessMemoryInfo(handle, ctypes.byref(memory), memory.cb)
        assert kernel.GetProcessHandleCount(handle, ctypes.byref(handles))
        return {"private_bytes": memory.PrivateUsage, "handles": handles.value}
    finally:
        kernel.CloseHandle(handle)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--binary", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--requests", type=int, default=4000, help="requests per stress round")
    parser.add_argument("--clients", type=int, default=32)
    args = parser.parse_args()
    report = {"at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "device": "isolated demo; no HID/audio/camera", "checks": [], "stress": [],
              "binary_sha256": hashlib.sha256(pathlib.Path(args.binary).read_bytes()).hexdigest()}
    started = time.monotonic()
    host = None
    try:
        with contextlib.ExitStack() as resources:
            temp = resources.enter_context(tempfile.TemporaryDirectory(prefix="ilovbee-release-"))
            data = pathlib.Path(temp)
            with (data / "host.log").open("wb") as log:
                host = subprocess.Popen([str(pathlib.Path(args.binary).resolve()), "--demo", "--headless",
                    "--data-dir", temp], stdin=subprocess.PIPE, stdout=log, stderr=log,
                    creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
                def stop_host():
                    if host.poll() is None:
                        host.kill()
                        host.wait()
                resources.callback(stop_host)
                connection = data / "server.txt"
                deadline = time.monotonic() + 15
                while not connection.exists():
                    assert host.poll() is None and time.monotonic() < deadline, "host startup failed"
                    time.sleep(.02)
                port, token = connection.read_text().splitlines()
                port = int(port)

                def request(method="GET", path="/api/ping", body=None, headers=None, auth=True):
                    connection = http.client.HTTPConnection("127.0.0.1", port, timeout=6)
                    fields = {"Cookie": "ilv=" + token} if auth else {}
                    fields.update(headers or {})
                    raw = json.dumps(body).encode() if isinstance(body, (dict, list)) else body
                    try:
                        connection.request(method, path, body=raw, headers=fields)
                        response = connection.getresponse()
                        return response.status, dict(response.getheaders()), response.read()
                    finally:
                        connection.close()

                def call(method="GET", path="/api/ping", body=None):
                    status, _, raw = request(method, path, body)
                    assert status == 200, (method, path, status, raw[:200])
                    return json.loads(raw)

                def check(name, result):
                    assert result, name
                    report["checks"].append(name)

                state = call(path="/api/state")
                check("isolated demo backend", state["demo"] is True)
                report["version"] = state["version"]
                original = call(path="/api/profile")
                check("unauthenticated read", request(auth=False)[0] == 403)
                check("unauthenticated write", request("POST", "/api/backup", {}, auth=False)[0] == 403)
                check("invalid cookie", request(headers={"Cookie": "ilv=wrong"})[0] == 403)
                check("token is not API authorization", request(path="/api/state?t=" + token, auth=False)[0] == 403)
                for origin in ["https://example.invalid", "null", f"http://localhost:{port}", "http://127.0.0.1:1"]:
                    check("reject Origin " + origin, request("POST", "/api/backup", {}, {"Origin": origin})[0] == 403)
                for authority in ["example.invalid", f"localhost:{port}", "127.0.0.1:1"]:
                    check("reject Host " + authority, request(headers={"Host": authority})[0] == 403)
                status, headers, _ = request(path="/?t=" + token, auth=False)
                lower = {key.lower(): value for key, value in headers.items()}
                check("bootstrap cookie restrictions", status == 200 and "HttpOnly; SameSite=Strict" in lower["set-cookie"])
                check("no token referrer or cache", lower.get("referrer-policy") == "no-referrer" and lower.get("cache-control") == "no-store")
                check("script and frame CSP", "script-src 'self';" in lower.get("content-security-policy", "") and "frame-ancestors 'none'" in lower["content-security-policy"])
                check("camera microphone disabled", "camera=()" in lower.get("permissions-policy", "") and "microphone=()" in lower["permissions-policy"])
                check("no permissive CORS", "access-control-allow-origin" not in lower)
                for path in ["/../Cargo.toml", "/%2e%2e/Cargo.toml", "/server.txt", "/api/backups/../../live.ini"]:
                    check("reject file access " + path, request(path=path)[0] >= 400)
                for name in ["../live.ini", "..%2flive.ini", "C:%5cWindows%5cwin.ini"]:
                    check("reject backup traversal " + name, request("POST", "/api/restore?name=" + name, {})[0] == 400)
                for path, invalid in [
                    ("/api/macros/save", b"{"),
                    ("/api/restore", {**original, "Device": "342D:E469"}),
                    ("/api/keyboard", {"expected": original["Lighting"], "settings": {"sideColor": 255}}),
                    ("/api/keyboard", {"expected": original["Lighting"], "settings": {"winLock": "true"}}),
                    ("/api/keyboard", {"expected": original["Lighting"], "settings": {"recording": True}}),
                    ("/api/layouts/put", {"name": "bad", "frame": "GG"}),
                    ("/api/live", {"mode": "firmware"}),
                    ("/api/schemes/delete", {"index": -1}),
                ]:
                    check("reject malformed " + path + " " + str(len(report["checks"])), request("POST", path, invalid)[0] == 400)
                expected = {**original["Keymaps"]}
                desired = {**expected, "0": "00" * 416}
                check("FN protection", request("POST", "/api/keymap", {"expected": expected, "desired": desired})[0] == 400)
                check("request body bound", request("POST", "/api/macros/save", b" " * (2 * 1024 * 1024 + 1))[0] == 413)
                current = call(path="/api/profile")
                check("invalid requests preserve keyboard", current["Keymaps"] == original["Keymaps"] and current["Lighting"] == original["Lighting"])
                if os.name == "nt":
                    stalled = socket.create_connection(("127.0.0.1", port), timeout=6)
                    try:
                        stalled.sendall((f"POST /api/keyboard HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nCookie: ilv={token}\r\nContent-Length: 10000\r\nConnection: close\r\n\r\n{{").encode())
                        time.sleep(.1)
                        recovery = time.monotonic()
                        check("stalled body timeout and service recovery", request()[0] == 200 and time.monotonic() - recovery < 5.5)
                    finally:
                        stalled.close()
                # Baseline after connection/thread allocation has warmed up.
                # Warm the same concurrency that will be measured. A serial
                # warmup measures pool allocation as a leak instead of retention.
                with concurrent.futures.ThreadPoolExecutor(max_workers=args.clients) as pool:
                    list(pool.map(lambda _: call(), range(args.clients * 20)))
                time.sleep(6)
                baseline = process_resources(host.pid)
                for round_number in range(2):
                    latencies = []
                    def worker(index):
                        t = time.monotonic()
                        if index % 20 == 0:
                            value = call("POST", "/api/live", {"mode": "off", "style": index % 12})
                            assert value["ok"]
                        elif index % 5 == 0:
                            assert call(path="/api/state")["demo"]
                        elif index % 3 == 0:
                            assert len(call(path="/api/profile")["Keymaps"]) == 4
                        else:
                            assert call()["connected"]
                        return (time.monotonic() - t) * 1000
                    t = time.monotonic()
                    with concurrent.futures.ThreadPoolExecutor(max_workers=args.clients) as pool:
                        latencies = list(pool.map(worker, range(args.requests)))
                    elapsed = time.monotonic() - t
                    # tiny_http retires surplus idle workers after five seconds.
                    # Measure retained resources after retirement, not its active pool.
                    time.sleep(6)
                    retained = process_resources(host.pid)
                    p95 = sorted(latencies)[int(len(latencies) * .95)]
                    report["stress"].append({"requests": args.requests, "clients": args.clients,
                        "elapsed_seconds": round(elapsed, 3), "p95_ms": round(p95, 3),
                        "mean_ms": round(statistics.mean(latencies), 3), "resources": retained,
                        "baseline": baseline})
                    check("stress round " + str(round_number + 1), p95 < 1000 and elapsed < 90)
                    if retained and baseline:
                        check("bounded memory round " + str(round_number + 1), retained["private_bytes"] - baseline["private_bytes"] < 64 * 1024 * 1024)
                        check("bounded handles round " + str(round_number + 1), retained["handles"] - baseline["handles"] < 24)
                call("POST", "/api/restore", original)
                restored = call(path="/api/profile")
                check("stress leaves keyboard restorable", restored["Keymaps"] == original["Keymaps"] and restored["Lighting"] == original["Lighting"])
                host.communicate(b"quit\n", timeout=10)
                check("clean shutdown", host.returncode == 0 and not connection.exists())
                report["status"] = "pass"
    except Exception as error:
        report["status"] = "fail"
        report["error"] = str(error)
    finally:
        if host and host.poll() is None:
            host.kill()
            host.wait()
        report["elapsed_seconds"] = round(time.monotonic() - started, 3)
        pathlib.Path(args.output).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf8")
        print(json.dumps(report, ensure_ascii=False))
    return report["status"] != "pass"


if __name__ == "__main__":
    raise SystemExit(main())
