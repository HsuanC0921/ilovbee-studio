//! Starts the shipped Rust binary with an isolated demo device and drives real HTTP requests.
use serde_json::{json, Value};
use std::{
    fs,
    io::{Read, Write},
    net::TcpStream,
    process::{Child, Command, Stdio},
    thread,
    time::{Duration, Instant},
};
struct Running(Child);
impl Drop for Running {
    fn drop(&mut self) {
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}
fn request(
    port: u16,
    method: &str,
    path: &str,
    body: &str,
    cookie: Option<&str>,
    origin: Option<&str>,
) -> (u16, String, String) {
    let mut stream = TcpStream::connect(("127.0.0.1", port)).unwrap();
    stream
        .set_read_timeout(Some(Duration::from_secs(5)))
        .unwrap();
    let mut head=format!("{method} {path} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\nContent-Length: {}\r\n",body.len());
    if let Some(c) = cookie {
        head.push_str(&format!("Cookie: {c}\r\n"));
    }
    if let Some(o) = origin {
        head.push_str(&format!("Origin: {o}\r\n"));
    }
    head.push_str("\r\n");
    stream.write_all(head.as_bytes()).unwrap();
    stream.write_all(body.as_bytes()).unwrap();
    let mut response = String::new();
    stream.read_to_string(&mut response).unwrap();
    let (headers, body) = response.split_once("\r\n\r\n").unwrap();
    let status = headers.split_whitespace().nth(1).unwrap().parse().unwrap();
    (status, headers.into(), body.into())
}
#[test]
fn shipped_binary_auth_assets_edit_backup_restore_and_live_recovery() {
    let dir = tempfile::tempdir().unwrap();
    let child = Command::new(env!("CARGO_BIN_EXE_iLovbeeStudio"))
        .args(["--demo", "--headless", "--data-dir"])
        .arg(dir.path())
        .stdin(Stdio::piped())
        .stdout(Stdio::null())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    let mut running = Running(child);
    let start = Instant::now();
    let connection = loop {
        if let Ok(text) = fs::read_to_string(dir.path().join("server.txt")) {
            break text;
        }
        assert!(
            start.elapsed() < Duration::from_secs(10),
            "host did not start"
        );
        assert!(running.0.try_wait().unwrap().is_none(), "host exited early");
        thread::sleep(Duration::from_millis(20));
    };
    let mut lines = connection.lines();
    let port: u16 = lines.next().unwrap().parse().unwrap();
    let token = lines.next().unwrap();
    let cookie = format!("ilv={token}");
    assert_eq!(request(port, "GET", "/api/state", "", None, None).0, 403);
    let bootstrap = request(port, "GET", &format!("/?t={token}"), "", None, None);
    assert_eq!(bootstrap.0, 200);
    assert!(bootstrap.1.contains("HttpOnly; SameSite=Strict"));
    assert!(bootstrap.1.contains("no-referrer"));
    assert_eq!(
        request(
            port,
            "POST",
            "/api/backup",
            "{}",
            Some(&cookie),
            Some("https://example.invalid")
        )
        .0,
        403
    );
    for path in [
        "/",
        "/keyboard.json",
        "/core.js",
        "/manual.js",
        "/effects.js",
        "/remap.js",
        "/lighting.js",
        "/custom.js",
        "/macros.js",
        "/misc.js",
        "/main.js",
        "/style.css",
    ] {
        assert_eq!(
            request(port, "GET", path, "", Some(&cookie), None).0,
            200,
            "asset {path}"
        );
    }
    let call = |method: &str, path: &str, body: Value| -> Value {
        let (status, _, text) = request(port, method, path, &body.to_string(), Some(&cookie), None);
        assert_eq!(status, 200, "{path}: {text}");
        serde_json::from_str(&text).unwrap()
    };
    let state = call("GET", "/api/state", Value::Null);
    assert_eq!(state["demo"], true);
    assert_eq!(state["backend"], "rust");
    assert_eq!(state["maps"].as_object().unwrap().len(), 4);
    let original = call("GET", "/api/profile", Value::Null);
    let mut desired = state["maps"].clone();
    let map = desired["3"].as_str().unwrap().to_string();
    desired["3"] = json!(format!("00080007{}", &map[8..]));
    call(
        "POST",
        "/api/keymap",
        json!({"expected":state["maps"],"desired":desired}),
    );
    assert_eq!(call("GET", "/api/state", Value::Null)["maps"], desired);
    call("POST", "/api/schemes/create", json!({"name":"整合測試"}));
    let scheme = call("GET", "/api/schemes/export?index=0", Value::Null);
    assert_eq!(scheme["Layers"], desired);
    call(
        "POST",
        "/api/schemes/rename",
        json!({"index":0,"name":"保留設定"}),
    );
    call(
        "POST",
        "/api/macros/save",
        json!([{"Name":"A","Events":[{"Code":4,"Up":false,"Delay":10},{"Code":4,"Up":true,"Delay":0}]}]),
    );
    call("POST", "/api/macros/write", json!({}));
    call(
        "POST",
        "/api/layouts/put",
        json!({"name":"紅色","frame":"FF0000".repeat(126),"fx":"01".repeat(126)}),
    );
    call("POST", "/api/color", json!({"mode":2,"rgb":"0011FF"}));
    call("POST", "/api/backup", json!({}));
    assert!(!call("GET", "/api/backups", Value::Null)["files"]
        .as_array()
        .unwrap()
        .is_empty());
    assert_eq!(
        request(port, "POST", "/api/macros/save", "{", Some(&cookie), None).0,
        400
    );
    assert_eq!(
        request(
            port,
            "POST",
            "/api/schemes/delete",
            "{\"index\":999}",
            Some(&cookie),
            None
        )
        .0,
        400
    );
    assert_eq!(
        request(
            port,
            "POST",
            "/api/layouts/put",
            "{\"name\":\"bad\",\"frame\":\"GG\"}",
            Some(&cookie),
            None
        )
        .0,
        400
    );
    assert_eq!(
        request(
            port,
            "POST",
            "/api/backup",
            &" ".repeat(2 * 1024 * 1024 + 1),
            Some(&cookie),
            None
        )
        .0,
        413
    );
    call("POST", "/api/restore", original.clone());
    assert_eq!(
        call("GET", "/api/profile", Value::Null)["Keymaps"],
        original["Keymaps"]
    );
    call(
        "POST",
        "/api/live",
        json!({"mode":"custom","frame":"0000FF".repeat(126),"fx":"02".repeat(126)}),
    );
    let start = Instant::now();
    loop {
        if call("GET", "/api/live", Value::Null)["status"] == "自訂燈光" {
            break;
        }
        assert!(start.elapsed() < Duration::from_secs(3));
        thread::sleep(Duration::from_millis(30));
    }
    assert!(dir.path().join("live-restore.txt").exists());
    // Graceful shutdown must restore lighting and clear connection/restore markers.
    running
        .0
        .stdin
        .as_mut()
        .unwrap()
        .write_all(b"quit\n")
        .unwrap();
    let start = Instant::now();
    loop {
        if let Some(status) = running.0.try_wait().unwrap() {
            assert!(status.success());
            break;
        }
        assert!(start.elapsed() < Duration::from_secs(5));
        thread::sleep(Duration::from_millis(20));
    }
    assert!(!dir.path().join("server.txt").exists());
    assert!(!dir.path().join("live-restore.txt").exists());
}
