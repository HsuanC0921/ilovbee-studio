use crate::protocol::*;
use anyhow::{ensure, Result};
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

pub type Maps = BTreeMap<u8, String>;

/// Fields identified by physical Fn shortcuts on USB 258A:0199 release 2010.
/// Preserves unspecified configuration bytes; does not access the onboard recording buffer.
#[derive(Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct KeyboardSettings {
    pub system_mode: Option<SystemMode>,
    pub win_lock: Option<bool>,
    pub side_color: Option<u8>,
}
#[derive(Debug, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum SystemMode {
    Windows,
    Mac,
}
impl KeyboardSettings {
    pub fn patch(&self, expected: &[u8]) -> Result<Vec<u8>> {
        ensure!(expected.len() == LIGHT_BYTES, "無效的鍵盤設定。");
        let mut after = expected.to_vec();
        if let Some(mode) = &self.system_mode {
            after[0] = match mode {
                SystemMode::Windows => 0,
                SystemMode::Mac => 1,
            };
        }
        if let Some(locked) = self.win_lock {
            after[15] = u8::from(locked);
        }
        if let Some(color) = self.side_color {
            ensure!(color <= 10, "側燈色彩必須在 1–11 之間。");
            after[18] = color;
        }
        Ok(after)
    }
}

pub fn bytes(hex: &str, count: usize) -> Result<Vec<u8>> {
    ensure!(hex.len() == count * 2, "資料長度不正確。");
    Ok(hex::decode(hex)?)
}

pub fn validate_maps(maps: &Maps, legacy: bool) -> Result<()> {
    ensure!(
        maps.len() == 4 || (legacy && maps.len() == 3),
        "必須包含完整四層按鍵設定。"
    );
    for i in 0..maps.len() as u8 {
        bytes(
            maps.get(&i)
                .ok_or_else(|| anyhow::anyhow!("缺少按鍵層。"))?,
            MAP_BYTES,
        )?;
    }
    Ok(())
}

pub fn preserve_fn(before: &Maps, after: &Maps) -> Result<()> {
    for (&layer, map) in after {
        let old = bytes(&before[&layer], MAP_BYTES)?;
        let new = bytes(map, MAP_BYTES)?;
        ensure!(
            old[FN_SLOT * 4..FN_SLOT * 4 + 4] == new[FN_SLOT * 4..FN_SLOT * 4 + 4],
            "FN 鍵必須保留。"
        );
    }
    Ok(())
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Profile {
    pub schema: u8,
    pub device: String,
    pub release: String,
    pub created: String,
    pub keymaps: Maps,
    pub lighting: String,
}
impl Profile {
    pub fn validate(&self) -> Result<()> {
        ensure!(
            self.schema == 1 && self.device == "258A:0199" && self.release == "2010",
            "此設定檔不適用目前 B87 硬體版本。"
        );
        validate_maps(&self.keymaps, true)?;
        bytes(&self.lighting, LIGHT_BYTES)?;
        Ok(())
    }
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct MacroEvent {
    pub code: u8,
    pub up: bool,
    pub delay: i32,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Macro {
    pub name: String,
    pub events: Vec<MacroEvent>,
}

pub fn validate_name(name: &str) -> Result<String> {
    let n = name.trim();
    ensure!(
        (1..=20).contains(&n.encode_utf16().count()),
        "名稱需為 1 到 20 個字。"
    );
    Ok(n.to_string())
}

pub fn encode_macros(macros: &[Macro]) -> Result<Vec<u8>> {
    ensure!(macros.len() <= 8, "巨集最多 8 個。");
    let mut bodies = Vec::new();
    for m in macros {
        validate_name(&m.name)?;
        ensure!(
            (1..=60).contains(&m.events.len()),
            "巨集事件數需為 1 到 60。"
        );
        let name: Vec<_> = m.name.encode_utf16().flat_map(u16::to_le_bytes).collect();
        ensure!(name.len() <= 40, "巨集名稱過長。");
        let mut body = vec![name.len() as u8];
        body.extend(name);
        for e in &m.events {
            ensure!(
                e.code != 0 && (0..=0xfffff).contains(&e.delay),
                "巨集含無效按鍵或延遲。"
            );
            let d = e.delay as u32;
            body.extend([
                if e.code >= 0xe0 { 0x10 } else { 0 }
                    | if e.up { 0x80 } else { 0 }
                    | (d >> 16) as u8,
                (d >> 8) as u8,
                d as u8,
                e.code,
            ]);
        }
        bodies.push(body);
    }
    let mut out = Vec::new();
    let mut offset = macros.len() * 4;
    for b in &bodies {
        out.extend((offset as u16).to_le_bytes());
        out.extend((b.len() as u16).to_le_bytes());
        offset += b.len();
    }
    ensure!(offset <= 512, "巨集總容量超過鍵盤上限。");
    for body in bodies {
        out.extend(body);
    }
    Ok(out)
}

pub fn is_macro(c: &[u8]) -> bool {
    c.len() == 4 && c[..3] == [3, 1, 1]
}

#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Scheme {
    pub name: String,
    pub layers: Maps,
}
#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Layout {
    pub name: String,
    pub frame: String,
    #[serde(default)]
    pub fx: Option<String>,
}

#[derive(Clone, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct PhysicalKey {
    pub id: usize,
    pub name: String,
    pub x: f64,
    pub y: f64,
    pub width: f64,
    pub height: f64,
    pub slot: i32,
}
pub fn keys() -> Vec<PhysicalKey> {
    serde_json::from_str(include_str!("../src/keyboard.json")).expect("embedded keyboard layout")
}
