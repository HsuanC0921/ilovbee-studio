#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::{env, fs, path::PathBuf};
use tauri::{WebviewUrl, WebviewWindowBuilder};

/// 取得常駐程式的連線資訊：命令列 `--url <網址>`，或讀取 `%LOCALAPPDATA%\iLovbeeStudio\server.txt`。
fn server_url() -> Option<String> {
    let args: Vec<String> = env::args().collect();
    if let Some(i) = args.iter().position(|a| a == "--url") {
        return args.get(i + 1).cloned();
    }
    let path = PathBuf::from(env::var_os("LOCALAPPDATA")?)
        .join("iLovbeeStudio")
        .join("server.txt");
    let text = fs::read_to_string(path).ok()?;
    let mut lines = text.lines();
    let port = lines.next()?.trim();
    let token = lines.next()?.trim();
    Some(format!("http://127.0.0.1:{port}/?t={token}"))
}

fn valid_server_url(url: &url::Url) -> bool {
    url.scheme() == "http"
        && url.host_str() == Some("127.0.0.1")
        && url.port().is_some_and(|p| p != 0)
        && url.username().is_empty()
        && url.password().is_none()
        && url.path() == "/"
}
fn allowed_navigation(origin: &url::Origin, target: &url::Url) -> bool {
    &target.origin() == origin || target.as_str() == "ms-settings:bluetooth"
}
fn main() {
    let Some(url) = server_url() else { return };
    let Ok(url) = url::Url::parse(&url) else {
        return;
    };
    if !valid_server_url(&url) {
        return;
    }
    tauri::Builder::default()
        .setup(move |app| {
            let origin = url.origin();
            let window = WebviewWindowBuilder::new(app, "main", WebviewUrl::External(url))
                .on_navigation(move |target| allowed_navigation(&origin, target))
                .title("iLovbee Studio")
                .inner_size(1200.0, 860.0)
                .min_inner_size(900.0, 620.0)
                .decorations(false)
                .transparent(false)
                .shadow(true)
                .center()
                .build()?;
            let _ = window;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("failed to run");
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn server_entry_rejects_external_hosts_credentials_and_paths() {
        assert!(valid_server_url(
            &url::Url::parse("http://127.0.0.1:4321/?t=example").unwrap()
        ));
        for value in [
            "https://127.0.0.1:4321/",
            "http://localhost:4321/",
            "http://127.0.0.1:0/",
            "http://user@127.0.0.1:4321/",
            "http://127.0.0.1:4321/other",
            "https://example.com/",
        ] {
            assert!(
                !valid_server_url(&url::Url::parse(value).unwrap()),
                "{value}"
            );
        }
    }
    #[test]
    fn navigation_stays_on_exact_server_origin_except_bluetooth_settings() {
        let origin = url::Url::parse("http://127.0.0.1:4321/").unwrap().origin();
        for value in ["http://127.0.0.1:4321/", "ms-settings:bluetooth"] {
            assert!(allowed_navigation(
                &origin,
                &url::Url::parse(value).unwrap()
            ));
        }
        for value in [
            "http://127.0.0.1:4322/",
            "https://example.com/",
            "javascript:alert(1)",
            "file:///C:/Windows/win.ini",
            "ms-settings:privacy-webcam",
        ] {
            assert!(
                !allowed_navigation(&origin, &url::Url::parse(value).unwrap()),
                "{value}"
            );
        }
    }
}
