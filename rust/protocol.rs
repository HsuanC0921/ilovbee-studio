//! Only the B87 configuration protocol. Firmware/ISP commands are not exposed.
use anyhow::{ensure, Result};

pub const REPORT_BYTES: usize = 520;
pub const MAP_BYTES: usize = 416;
pub const LIGHT_BYTES: usize = 128;
pub const FRAME_BYTES: usize = 378;
pub const COLOR_BYTES: usize = 420;
pub const FN_SLOT: usize = 59;

pub fn packet(command: u8, layer: u8, count: usize) -> Result<[u8; REPORT_BYTES]> {
    ensure!((1..=512).contains(&count), "無效的封包長度。");
    let mut p = [0; REPORT_BYTES];
    p[..8].copy_from_slice(&[6, command, layer, 0, 1, 0, count as u8, (count >> 8) as u8]);
    Ok(p)
}

pub fn read_request(command: u8, layer: u8, count: usize) -> Result<[u8; REPORT_BYTES]> {
    ensure!(
        matches!(
            (command, layer, count),
            (0x83, 0..=3, MAP_BYTES) | (0x84, 0, LIGHT_BYTES) | (0x87, 0, 2)
        ),
        "未允許的讀取指令。"
    );
    packet(command, layer, count)
}

pub fn write_request(command: u8, layer: u8, data: &[u8]) -> Result<[u8; REPORT_BYTES]> {
    ensure!(
        matches!(
            (command, layer, data.len()),
            (3, 0..=3, MAP_BYTES)
                | (4, 0, LIGHT_BYTES)
                | (5, 0, 4..=512)
                | (8, 0, FRAME_BYTES)
                | (10, 0, COLOR_BYTES)
        ),
        "未允許的設定寫入。"
    );
    let mut p = packet(command, layer, data.len())?;
    p[8..8 + data.len()].copy_from_slice(data);
    Ok(p)
}

pub fn reply(request: &[u8; REPORT_BYTES], response: &[u8]) -> Result<Vec<u8>> {
    let n = u16::from_le_bytes([request[6], request[7]]) as usize;
    ensure!(
        response.len() == REPORT_BYTES && response[..8] == request[..8],
        "裝置回應不符合查詢；未寫入設定。"
    );
    Ok(response[8..8 + n].to_vec())
}

pub trait Transport: Send {
    fn read(&mut self, command: u8, layer: u8, count: usize) -> Result<Vec<u8>>;
    fn write(&mut self, command: u8, layer: u8, data: &[u8]) -> Result<()>;
}
