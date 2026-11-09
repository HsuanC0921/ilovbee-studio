use anyhow::{ensure, Context, Result};
use ilovbee_studio::{
    api::App,
    windows::{self, wide},
};
use std::{
    cell::RefCell,
    mem::{size_of, zeroed},
    ptr::{null, null_mut},
    sync::{Arc, Mutex},
};
use windows_sys::Win32::{
    Foundation::*,
    System::{LibraryLoader::GetModuleHandleW, Registry::*},
    UI::{Shell::*, WindowsAndMessaging::*},
};

thread_local! {static DESKTOP:RefCell<Option<Desktop>>=const{RefCell::new(None)};}
struct Desktop {
    app: Arc<Mutex<App>>,
    url: String,
    tray: NOTIFYICONDATAW,
    taskbar: u32,
    last_notice: Option<u8>,
}
fn put(dst: &mut [u16], s: &str) {
    dst.fill(0);
    for (d, c) in dst.iter_mut().zip(s.encode_utf16()) {
        *d = c;
    }
    dst[dst.len() - 1] = 0;
}

pub fn open_ui(url: &str) -> Result<()> {
    let exe = std::env::current_exe()?;
    let shell = exe.parent().context("缺少執行路徑")?.join("iLovbeeUI.exe");
    if shell.exists() {
        std::process::Command::new(shell)
            .args(["--url", url])
            .spawn()?;
        return Ok(());
    }
    for base in [
        std::env::var_os("ProgramFiles(x86)"),
        std::env::var_os("ProgramFiles"),
    ]
    .into_iter()
    .flatten()
    {
        let edge = std::path::PathBuf::from(base).join("Microsoft/Edge/Application/msedge.exe");
        if edge.exists() {
            std::process::Command::new(edge)
                .args([format!("--app={url}"), "--window-size=1200,860".into()])
                .spawn()?;
            return Ok(());
        }
    }
    open_url(url)
}
fn open_url(url: &str) -> Result<()> {
    let r = unsafe {
        ShellExecuteW(
            null_mut(),
            wide("open").as_ptr(),
            wide(url).as_ptr(),
            null(),
            null(),
            SW_SHOWNORMAL,
        )
    };
    ensure!(r as usize > 32, "無法開啟設定視窗。");
    Ok(())
}
fn autostart() -> Result<bool> {
    unsafe {
        let mut key = null_mut();
        let r = RegOpenKeyExW(
            HKEY_CURRENT_USER,
            wide("Software\\Microsoft\\Windows\\CurrentVersion\\Run").as_ptr(),
            0,
            KEY_QUERY_VALUE,
            &mut key,
        );
        if r == ERROR_FILE_NOT_FOUND {
            return Ok(false);
        }
        ensure!(r == 0, "無法讀取開機設定：{r}");
        let mut n = 0;
        let r = RegQueryValueExW(
            key,
            wide("iLovbeeStudio").as_ptr(),
            null(),
            null_mut(),
            null_mut(),
            &mut n,
        );
        RegCloseKey(key);
        ensure!(r == 0 || r == ERROR_FILE_NOT_FOUND, "無法讀取開機設定：{r}");
        Ok(r == 0)
    }
}
fn set_autostart(enabled: bool) -> Result<()> {
    unsafe {
        let mut key = null_mut();
        let r = RegCreateKeyExW(
            HKEY_CURRENT_USER,
            wide("Software\\Microsoft\\Windows\\CurrentVersion\\Run").as_ptr(),
            0,
            null(),
            0,
            KEY_SET_VALUE,
            null(),
            &mut key,
            null_mut(),
        );
        ensure!(r == 0, "無法寫入開機設定：{r}");
        let name = wide("iLovbeeStudio");
        let r = if enabled {
            let value = wide(&format!(
                "\"{}\" --tray",
                std::env::current_exe()?.display()
            ));
            RegSetValueExW(
                key,
                name.as_ptr(),
                0,
                REG_SZ,
                value.as_ptr().cast(),
                (value.len() * 2) as u32,
            )
        } else {
            RegDeleteValueW(key, name.as_ptr())
        };
        RegCloseKey(key);
        ensure!(
            r == 0 || (!enabled && r == ERROR_FILE_NOT_FOUND),
            "無法寫入開機設定：{r}"
        );
        Ok(())
    }
}
impl Desktop {
    unsafe fn update(&mut self, command: u32) -> Result<()> {
        let demo = self.app.lock().unwrap().service.demo;
        let reading = if demo { None } else { windows::battery()? };
        let percent = reading.as_ref().map(|b| b.percent);
        let size = windows_sys::Win32::UI::HiDpi::GetSystemMetricsForDpi(
            SM_CXSMICON,
            windows_sys::Win32::UI::HiDpi::GetDpiForSystem(),
        )
        .max(16) as usize;
        let mut rgba = vec![0u8; size * size * 4];
        let mask_stride = size.div_ceil(16) * 2;
        let mut mask = vec![255u8; size * mask_stride];
        for py in 0..size {
            for px in 0..size {
                let (x, y) = (px * 16 / size, py * 16 / size);
                let border = (x == 1 || x == 13 || y == 3 || y == 12)
                    && (1..=13).contains(&x)
                    && (3..=12).contains(&y);
                let cap = x == 14 && (6..=9).contains(&y);
                let fill = percent.is_some_and(|p| {
                    x >= 3 && x < 3 + (p as usize * 9 + 50) / 100 && (5..=10).contains(&y)
                });
                if border || cap || fill {
                    let at = (py * size + px) * 4;
                    let col = if percent.is_some_and(|p| p <= 20) {
                        [45, 55, 200, 255]
                    } else {
                        [189, 90, 24, 255]
                    };
                    rgba[at..at + 4].copy_from_slice(&col);
                    mask[py * mask_stride + px / 8] &= !(0x80 >> (px % 8));
                }
            }
        }
        let icon = CreateIcon(
            null_mut(),
            size as i32,
            size as i32,
            1,
            32,
            mask.as_ptr(),
            rgba.as_ptr(),
        );
        ensure!(!icon.is_null(), "無法建立系統匣圖示。");
        let old = self.tray.hIcon;
        self.tray.hIcon = icon;
        let text = reading.as_ref().map_or_else(
            || "B87 · 尚未取得電量".into(),
            |b| {
                format!(
                    "B87 · {}% · Windows 最近回報 {}",
                    b.percent,
                    b.reported_at.as_deref().unwrap_or("時間不明")
                )
            },
        );
        put(&mut self.tray.szTip, &text);
        self.tray.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
        let success = Shell_NotifyIconW(command, &self.tray);
        if !old.is_null() {
            DestroyIcon(old);
        }
        ensure!(success != 0, "無法更新系統匣圖示。");
        if let Some(b) = reading {
            let recent = b
                .reported_at
                .as_ref()
                .and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok())
                .is_some_and(|t| {
                    let age = chrono::Utc::now().signed_duration_since(t);
                    age >= chrono::Duration::zero() && age < chrono::Duration::minutes(10)
                });
            if b.percent <= 20
                && recent
                && self
                    .last_notice
                    .is_none_or(|p| p.saturating_sub(b.percent) >= 5)
            {
                self.notice(
                    "B87 電量偏低",
                    &format!("Windows 最近回報 {}%，請連接 USB 充電。", b.percent),
                );
                self.last_notice = Some(b.percent);
            } else if b.percent > 20 {
                self.last_notice = None;
            }
        }
        Ok(())
    }
    unsafe fn notice(&mut self, title: &str, body: &str) {
        put(&mut self.tray.szInfoTitle, title);
        put(&mut self.tray.szInfo, body);
        self.tray.uFlags = NIF_INFO;
        self.tray.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, &self.tray);
    }
    unsafe fn menu(&mut self, hwnd: HWND) -> Result<()> {
        let menu = CreatePopupMenu();
        ensure!(!menu.is_null(), "無法建立系統匣選單。");
        let add = |flags: u32, id: usize, text: &str| {
            AppendMenuW(menu, flags, id, wide(text).as_ptr());
        };
        let a = self.app.lock().unwrap();
        add(MF_GRAYED, 0, &format!("燈光：{}", a.status));
        add(MF_STRING, 1, "開啟設定");
        add(MF_STRING, 2, "更新電量");
        add(MF_STRING, 3, "Windows 藍牙設定");
        add(MF_SEPARATOR, 0, "");
        for (i, name) in ilovbee_studio::lighting::STYLES.iter().enumerate() {
            add(
                if a.config.mode == "music" && a.config.style == i {
                    MF_CHECKED
                } else {
                    0
                },
                20 + i,
                &format!("音樂 · {name}"),
            );
        }
        let layouts = a.layouts.clone();
        for (i, l) in layouts.iter().enumerate() {
            add(MF_STRING, 100 + i, &format!("燈光 · {}", l.name));
        }
        add(MF_STRING, 41, "停止並恢復原本燈效");
        add(MF_SEPARATOR, 0, "");
        add(
            if autostart()? { MF_CHECKED } else { 0 },
            42,
            "開機自動啟動",
        );
        add(MF_STRING, 4, "結束 iLovbee Studio");
        drop(a);
        let mut point = zeroed();
        GetCursorPos(&mut point);
        SetForegroundWindow(hwnd);
        let chosen = TrackPopupMenu(
            menu,
            TPM_RETURNCMD | TPM_RIGHTBUTTON,
            point.x,
            point.y,
            0,
            hwnd,
            null(),
        ) as usize;
        DestroyMenu(menu);
        match chosen {
            1 => open_ui(&self.url)?,
            2 => self.update(NIM_MODIFY)?,
            3 => open_url("ms-settings:bluetooth")?,
            4 => {
                DestroyWindow(hwnd);
            }
            42 => {
                set_autostart(!autostart()?)?;
            }
            _ => {
                let mut a = self.app.lock().unwrap();
                let mut c = a.config.clone();
                if (20..32).contains(&chosen) {
                    c.mode = "music".into();
                    c.style = chosen - 20;
                } else if chosen >= 100 && chosen - 100 < layouts.len() {
                    let l = &layouts[chosen - 100];
                    c.mode = "custom".into();
                    c.frame = hex::decode(&l.frame)?;
                    c.fx = if let Some(f) = &l.fx {
                        hex::decode(f)?
                    } else {
                        vec![0; 126]
                    };
                } else if chosen == 41 {
                    c.mode = "off".into();
                } else {
                    return Ok(());
                }
                a.set_config(c)?;
            }
        }
        Ok(())
    }
}
unsafe extern "system" fn proc(hwnd: HWND, msg: u32, w: WPARAM, l: LPARAM) -> LRESULT {
    if msg == WM_DESTROY {
        PostQuitMessage(0);
        return 0;
    }
    let handled = DESKTOP.with(|cell| {
        let mut d = cell.borrow_mut();
        let Some(d) = d.as_mut() else {
            return false;
        };
        let result = if msg == d.taskbar {
            Some(d.update(NIM_ADD))
        } else if msg == WM_TIMER {
            Some(d.update(NIM_MODIFY))
        } else if msg == WM_APP + 1 {
            match l as u32 {
                WM_LBUTTONDBLCLK => Some(open_ui(&d.url)),
                WM_RBUTTONUP => Some(d.menu(hwnd)),
                _ => None,
            }
        } else {
            None
        };
        if let Some(r) = result {
            if let Err(e) = r {
                d.notice("操作未完成", &format!("{e:#}"));
            }
            true
        } else {
            false
        }
    });
    if handled {
        0
    } else {
        DefWindowProcW(hwnd, msg, w, l)
    }
}
pub fn run(app: Arc<Mutex<App>>, url: String, only_tray: bool) -> Result<()> {
    unsafe {
        let class = wide("iLovbeeStudioRust");
        let instance = GetModuleHandleW(null());
        let mut wc: WNDCLASSW = zeroed();
        wc.lpfnWndProc = Some(proc);
        wc.hInstance = instance;
        wc.lpszClassName = class.as_ptr();
        ensure!(RegisterClassW(&wc) != 0, "無法註冊桌面視窗。");
        let hwnd = CreateWindowExW(
            0,
            class.as_ptr(),
            wide("iLovbee Studio").as_ptr(),
            0,
            0,
            0,
            0,
            0,
            null_mut(),
            null_mut(),
            instance,
            null(),
        );
        ensure!(!hwnd.is_null(), "無法建立桌面視窗。");
        let mut tray: NOTIFYICONDATAW = zeroed();
        tray.cbSize = size_of::<NOTIFYICONDATAW>() as u32;
        tray.hWnd = hwnd;
        tray.uID = 1;
        tray.uCallbackMessage = WM_APP + 1;
        let mut desktop = Desktop {
            app,
            url: url.clone(),
            tray,
            taskbar: RegisterWindowMessageW(wide("TaskbarCreated").as_ptr()),
            last_notice: None,
        };
        desktop.update(NIM_ADD)?;
        DESKTOP.with(|d| *d.borrow_mut() = Some(desktop));
        SetTimer(hwnd, 1, 60000, None);
        if !only_tray {
            if let Err(e) = open_ui(&url) {
                DESKTOP.with(|d| {
                    d.borrow_mut()
                        .as_mut()
                        .unwrap()
                        .notice("開啟設定失敗", &format!("{e:#}"))
                });
            }
        }
        let mut msg = zeroed();
        loop {
            let r = GetMessageW(&mut msg, null_mut(), 0, 0);
            if r <= 0 {
                break;
            }
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        KillTimer(hwnd, 1);
        DESKTOP.with(|d| {
            if let Some(d) = d.borrow_mut().take() {
                Shell_NotifyIconW(NIM_DELETE, &d.tray);
                DestroyIcon(d.tray.hIcon);
            }
        });
        Ok(())
    }
}
