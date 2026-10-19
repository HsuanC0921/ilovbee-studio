#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::{env, fs, path::PathBuf};
use tauri::{WebviewUrl, WebviewWindowBuilder};

/// 取得常駐程式的連線資訊：命令列 `--url <網址>`，或讀取 `%LOCALAPPDATA%\iLovbeeStudio\server.txt`。
fn server_url() -> Option<String> {
    let args: Vec<String> = env::args().collect();
    if let Some(i) = args.iter().position(|a| a == "--url") {
        return args.get(i + 1).cloned();
    }
    let path = PathBuf::from(env::var_os("LOCALAPPDATA")?).join("iLovbeeStudio").join("server.txt");
    let text = fs::read_to_string(path).ok()?;
    let mut lines = text.lines();
    let port = lines.next()?.trim();
    let token = lines.next()?.trim();
    Some(format!("http://127.0.0.1:{port}/?t={token}"))
}

fn main() {
    let Some(url) = server_url() else { return };
    tauri::Builder::default()
        .setup(move |app| {
            let window = WebviewWindowBuilder::new(app, "main", WebviewUrl::External(url.parse().expect("url")))
                .title("iLovbee Studio")
                .inner_size(1200.0, 860.0)
                .min_inner_size(900.0, 620.0)
                .decorations(false)
                .transparent(true)
                .shadow(true)
                .center()
                .build()?;
            #[cfg(target_os = "windows")]
            {
                let _ = window_vibrancy::apply_mica(&window, None);
            }
            let _ = window;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("failed to run");
}
