#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
use anyhow::{bail, Context, Result};
use ilovbee_studio::{
    api::{self, App, Session},
    live,
    store::Store,
};
use std::{
    path::PathBuf,
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc, Mutex,
    },
    time::Duration,
};
#[cfg(windows)]
mod desktop;
fn main() {
    if let Err(e) = run() {
        eprintln!("{e:#}");
        #[cfg(windows)]
        if !std::env::args().any(|a| a == "--headless") {
            unsafe {
                windows_sys::Win32::UI::WindowsAndMessaging::MessageBoxW(
                    std::ptr::null_mut(),
                    ilovbee_studio::windows::wide(&format!("{e:#}")).as_ptr(),
                    ilovbee_studio::windows::wide("iLovbee Studio 無法啟動").as_ptr(),
                    windows_sys::Win32::UI::WindowsAndMessaging::MB_OK
                        | windows_sys::Win32::UI::WindowsAndMessaging::MB_ICONERROR,
                );
            }
        }
        std::process::exit(1);
    }
}
fn run() -> Result<()> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let mut demo = false;
    let mut headless = false;
    let mut tray = false;
    let mut root = None;
    let mut i = 0;
    while i < args.len() {
        match args[i].as_str() {
            "--demo" => demo = true,
            "--headless" => headless = true,
            "--tray" => tray = true,
            "--data-dir" => {
                i += 1;
                root = Some(PathBuf::from(
                    args.get(i).context("--data-dir 需要資料夾路徑。")?,
                ));
            }
            "--help" | "-h" => {
                println!("iLovbee Studio (Rust)\n  --demo       離線示範，不存取 USB\n  --headless   僅啟動本機 API\n  --tray       僅啟動系統匣\n  --data-dir <path>  指定設定資料夾");
                return Ok(());
            }
            _ => bail!("未知的選項：{}", args[i]),
        }
        i += 1;
    }
    let root = root.unwrap_or_else(|| {
        PathBuf::from(std::env::var_os("LOCALAPPDATA").unwrap_or_else(|| ".".into())).join(
            if demo {
                "iLovbeeStudio-Demo"
            } else {
                "iLovbeeStudio"
            },
        )
    });
    #[cfg(windows)]
    let _instance = if !demo {
        match ilovbee_studio::windows::single_instance()? {
            Some(h) => Some(h),
            None => {
                if !headless && !tray {
                    if let Ok(text) = std::fs::read_to_string(root.join("server.txt")) {
                        let mut l = text.lines();
                        if let (Some(port), Some(token)) = (l.next(), l.next()) {
                            let port: u16 = port.parse()?;
                            anyhow::ensure!(
                                token.len() >= 32 && token.bytes().all(|c| c.is_ascii_hexdigit()),
                                "無效的連線資料。"
                            );
                            desktop::open_ui(&format!("http://127.0.0.1:{port}/?t={token}"))?;
                        }
                    }
                }
                return Ok(());
            }
        }
    } else {
        None
    };
    let store = Store::new(root)?;
    let app = Arc::new(Mutex::new(App::new(store.clone(), demo)?));
    // USB enumeration is read-only. No settings are changed at launch unless recovering/starting live lighting.
    if let Err(e) = app.lock().unwrap().service.scan() {
        eprintln!("USB：{e:#}");
    }
    let server = Arc::new(api::listen()?);
    let port = server.server_addr().to_ip().context("缺少監聽埠")?.port();
    let session = Session::new(port)?;
    let url = format!("http://127.0.0.1:{port}/?t={}", session.token);
    store.write_text("server.txt", &format!("{port}\n{}\n", session.token))?;
    println!(
        "iLovbee Studio Rust listening on 127.0.0.1:{port}{}",
        if demo { " (demo)" } else { "" }
    );
    let stop = Arc::new(AtomicBool::new(false));
    let live = live::spawn(app.clone(), stop.clone());
    let srv = server.clone();
    let http_app = app.clone();
    let done = stop.clone();
    let http = std::thread::spawn(move || {
        while !done.load(Ordering::Relaxed) {
            match srv.recv_timeout(Duration::from_millis(100)) {
                Ok(Some(r)) => api::serve_one(r, &http_app, &session),
                Ok(None) => (),
                Err(e) => {
                    eprintln!("HTTP：{e}");
                    break;
                }
            }
        }
    });
    let result = if headless {
        // EOF or "quit" exits cleanly, including live lighting restoration. Useful for scripted integration tests.
        let mut input = String::new();
        loop {
            input.clear();
            if std::io::stdin().read_line(&mut input)? == 0 || input.trim() == "quit" {
                break;
            }
        }
        Ok(())
    } else {
        #[cfg(windows)]
        {
            desktop::run(app, url, tray)
        }
        #[cfg(not(windows))]
        {
            let _ = (app, url, tray);
            bail!("桌面程式需要 Windows；可使用 --demo --headless 測試。");
        }
    };
    stop.store(true, Ordering::Relaxed);
    let _ = live.join();
    server.unblock();
    let _ = http.join();
    std::fs::remove_file(store.root.join("server.txt"))?;
    result
}
