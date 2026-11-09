//! Win32 handles are scoped here; protocol and application code remain safe Rust.
use crate::protocol::*;
use anyhow::{bail, ensure, Context, Result};
use std::{
    mem::{size_of, zeroed},
    ptr::{null, null_mut},
    thread,
    time::{Duration, Instant},
};
use windows_sys::{
    core::GUID,
    Win32::{
        Devices::{DeviceAndDriverInstallation::*, HumanInterfaceDevice::*},
        Foundation::*,
        Storage::FileSystem::*,
        System::{Diagnostics::ToolHelp::*, Threading::*},
        UI::Input::KeyboardAndMouse::GetAsyncKeyState,
    },
};

pub fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(Some(0)).collect()
}
fn os_error() -> anyhow::Error {
    std::io::Error::last_os_error().into()
}
struct DeviceSet(HDEVINFO);
impl Drop for DeviceSet {
    fn drop(&mut self) {
        unsafe {
            SetupDiDestroyDeviceInfoList(self.0);
        }
    }
}
pub struct Handle(pub HANDLE);
// Win32 kernel handles may be transferred between threads. The service mutex serializes HID I/O.
unsafe impl Send for Handle {}
impl Drop for Handle {
    fn drop(&mut self) {
        if !self.0.is_null() && self.0 != INVALID_HANDLE_VALUE {
            unsafe {
                CloseHandle(self.0);
            }
        }
    }
}

pub struct Hid {
    handle: Handle,
}
impl Transport for Hid {
    fn read(&mut self, c: u8, l: u8, n: usize) -> Result<Vec<u8>> {
        let req = read_request(c, l, n)?;
        unsafe {
            ensure!(
                HidD_SetFeature(self.handle.0, req.as_ptr().cast(), REPORT_BYTES as u32),
                "裝置查詢失敗：{}",
                os_error()
            );
        }
        thread::sleep(Duration::from_millis(40));
        let mut res = [0; REPORT_BYTES];
        res[0] = 6;
        unsafe {
            ensure!(
                HidD_GetFeature(self.handle.0, res.as_mut_ptr().cast(), REPORT_BYTES as u32),
                "裝置未回應：{}",
                os_error()
            );
        }
        reply(&req, &res)
    }
    fn write(&mut self, c: u8, l: u8, data: &[u8]) -> Result<()> {
        let req = write_request(c, l, data)?;
        unsafe {
            ensure!(
                HidD_SetFeature(self.handle.0, req.as_ptr().cast(), REPORT_BYTES as u32),
                "寫入失敗：{}",
                os_error()
            );
        }
        thread::sleep(Duration::from_millis(match c {
            5 => 200,
            8 => 0,
            _ => 80,
        }));
        Ok(())
    }
}
pub fn connect() -> Result<Option<Box<dyn Transport>>> {
    unsafe {
        let mut guid = zeroed();
        HidD_GetHidGuid(&mut guid);
        let set = DeviceSet(SetupDiGetClassDevsW(
            &guid,
            null(),
            null_mut(),
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE,
        ));
        ensure!(set.0 != -1, "無法列舉 USB 裝置：{}", os_error());
        let mut found: Vec<Box<dyn Transport>> = vec![];
        for i in 0..256 {
            let mut iface: SP_DEVICE_INTERFACE_DATA = zeroed();
            iface.cbSize = size_of::<SP_DEVICE_INTERFACE_DATA>() as u32;
            if SetupDiEnumDeviceInterfaces(set.0, null(), &guid, i, &mut iface) == 0 {
                if GetLastError() == ERROR_NO_MORE_ITEMS {
                    break;
                }
                return Err(os_error());
            }
            let mut n = 0;
            SetupDiGetDeviceInterfaceDetailW(set.0, &iface, null_mut(), 0, &mut n, null_mut());
            if !(8..=65536).contains(&n) {
                continue;
            }
            // u64 storage keeps the detail structure aligned on x64.
            let mut buf = vec![0u64; (n as usize).div_ceil(8)];
            let detail = buf.as_mut_ptr().cast::<SP_DEVICE_INTERFACE_DETAIL_DATA_W>();
            (*detail).cbSize = size_of::<SP_DEVICE_INTERFACE_DETAIL_DATA_W>() as u32;
            if SetupDiGetDeviceInterfaceDetailW(set.0, &iface, detail, n, &mut n, null_mut()) == 0 {
                continue;
            }
            let ptr = std::ptr::addr_of!((*detail).DevicePath).cast::<u16>();
            let max = (n as usize - 4) / 2;
            let path = std::slice::from_raw_parts(ptr, max);
            let len = path.iter().position(|&c| c == 0).unwrap_or(max);
            if !String::from_utf16_lossy(&path[..len])
                .to_lowercase()
                .contains("vid_258a&pid_0199")
            {
                continue;
            }
            let h = Handle(CreateFileW(
                ptr,
                0,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                null(),
                OPEN_EXISTING,
                0,
                null_mut(),
            ));
            if h.0 == INVALID_HANDLE_VALUE {
                return Err(os_error()).context("無法開啟 B87 設定介面");
            }
            let mut attrs: HIDD_ATTRIBUTES = zeroed();
            attrs.Size = size_of::<HIDD_ATTRIBUTES>() as u32;
            if !HidD_GetAttributes(h.0, &mut attrs)
                || (attrs.VendorID, attrs.ProductID, attrs.VersionNumber)
                    != (0x258a, 0x0199, 0x2010)
            {
                continue;
            }
            let mut pp = 0;
            if !HidD_GetPreparsedData(h.0, &mut pp) {
                continue;
            }
            let mut caps: HIDP_CAPS = zeroed();
            let result = HidP_GetCaps(pp, &mut caps);
            HidD_FreePreparsedData(pp);
            if result >= 0
                && caps.UsagePage == 0xff00
                && caps.Usage == 1
                && caps.FeatureReportByteLength == 520
            {
                found.push(Box::new(Hid { handle: h }));
            }
        }
        ensure!(found.len() <= 1, "偵測到多個相容 B87，請只連接一把再操作。");
        Ok(found.pop())
    }
}
pub fn wait_keys_idle() -> Result<()> {
    let start = Instant::now();
    let mut quiet = 0;
    while start.elapsed() < Duration::from_millis(1500) {
        let down = (8..255)
            .filter(|k| ![0x10, 0x11, 0x12].contains(k))
            .any(|k| unsafe { GetAsyncKeyState(k) as u16 & 0x8000 != 0 });
        quiet = if down { 0 } else { quiet + 10 };
        if quiet >= 150 {
            return Ok(());
        }
        thread::sleep(Duration::from_millis(10));
    }
    bail!("請先放開所有按鍵，再套用設定。")
}
pub fn check_conflict() -> Result<()> {
    unsafe {
        let snap = Handle(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0));
        ensure!(
            snap.0 != INVALID_HANDLE_VALUE,
            "無法檢查其他鍵盤程式：{}",
            os_error()
        );
        let mut p: PROCESSENTRY32W = zeroed();
        p.dwSize = size_of::<PROCESSENTRY32W>() as u32;
        let mut ok = Process32FirstW(snap.0, &mut p);
        while ok != 0 {
            let len = p
                .szExeFile
                .iter()
                .position(|&c| c == 0)
                .unwrap_or(p.szExeFile.len());
            let name = String::from_utf16_lossy(&p.szExeFile[..len]);
            if name.eq_ignore_ascii_case("DeviceDriver.exe") {
                bail!("請先結束 DeviceDriver 原廠鍵盤程式，再套用設定。");
            }
            ok = Process32NextW(snap.0, &mut p);
        }
        Ok(())
    }
}

#[derive(Clone, serde::Serialize)]
pub struct Battery {
    pub percent: u8,
    pub reported_at: Option<String>,
    pub name: String,
}
pub fn battery() -> Result<Option<Battery>> {
    unsafe {
        let set = DeviceSet(SetupDiGetClassDevsW(
            null(),
            wide("BTHLE").as_ptr(),
            null_mut(),
            DIGCF_PRESENT | DIGCF_ALLCLASSES,
        ));
        ensure!(set.0 != -1, "無法讀取藍牙電量：{}", os_error());
        let mut found = None;
        let props = GUID::from_u128(0xa45c254edf1c4efd802067d146a850e0);
        let power = GUID::from_u128(0x104ea3196ee24701bd478ddbf425bbe5);
        for i in 0..1024 {
            let mut d: SP_DEVINFO_DATA = zeroed();
            d.cbSize = size_of::<SP_DEVINFO_DATA>() as u32;
            if SetupDiEnumDeviceInfo(set.0, i, &mut d) == 0 {
                break;
            }
            let get = |format: GUID, id: u32, expected: u32| -> Option<Vec<u8>> {
                let key = DEVPROPKEY {
                    fmtid: format,
                    pid: id,
                };
                let (mut kind, mut n) = (0, 0);
                let mut buf = [0u8; 1024];
                if SetupDiGetDevicePropertyW(
                    set.0,
                    &d,
                    &key,
                    &mut kind,
                    buf.as_mut_ptr(),
                    1024,
                    &mut n,
                    0,
                ) == 0
                    || kind != expected
                    || n > 1024
                {
                    return None;
                }
                Some(buf[..n as usize].to_vec())
            };
            let Some(b) = get(props, 14, 18).or_else(|| get(props, 2, 18)) else {
                continue;
            };
            let utf: Vec<_> = b
                .as_chunks::<2>()
                .0
                .iter()
                .map(|c| u16::from_le_bytes([c[0], c[1]]))
                .take_while(|&c| c != 0)
                .collect();
            let name = String::from_utf16_lossy(&utf);
            if !name.to_lowercase().contains("b87") && !name.to_lowercase().contains("ilovbee") {
                continue;
            }
            let Some(level) = get(power, 2, 3) else {
                continue;
            };
            if level.len() != 1 || level[0] > 100 {
                continue;
            }
            let stamp = get(power, 7, 16).filter(|x| x.len() == 8).and_then(|x| {
                let ticks = i64::from_le_bytes(x.try_into().ok()?);
                chrono::DateTime::from_timestamp(
                    (ticks / 10_000_000) - 11_644_473_600,
                    ((ticks % 10_000_000) * 100) as u32,
                )
                .map(|t| t.to_rfc3339())
            });
            if found.is_some() {
                return Ok(None);
            }
            found = Some(Battery {
                percent: level[0],
                reported_at: stamp,
                name,
            });
        }
        Ok(found)
    }
}

/// Same mutex name as the C# host prevents two generations writing at once.
pub fn single_instance() -> Result<Option<Handle>> {
    unsafe {
        let h = CreateMutexW(null(), 1, wide("Local\\iLovbeeStudioWidget").as_ptr());
        ensure!(!h.is_null(), "無法建立執行個體鎖：{}", os_error());
        let existing = GetLastError() == ERROR_ALREADY_EXISTS;
        let handle = Handle(h);
        Ok(if existing { None } else { Some(handle) })
    }
}
