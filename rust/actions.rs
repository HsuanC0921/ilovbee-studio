use crate::model::{is_macro, Macro};
use serde_json::{json, Value};

pub fn code(usage: u8, modifier: u8) -> [u8; 4] {
    [0, modifier, 0, usage]
}
pub fn all(macros: &[Macro]) -> Vec<(String, [u8; 4])> {
    let mut a: Vec<(String, [u8; 4])> = vec![
        ("顯示桌面／回復視窗（Win＋D）".into(), code(7, 8)),
        ("F13（供巨集／腳本使用）".into(), code(0x68, 0)),
        ("區域截圖（Win＋Shift＋S）".into(), code(0x16, 10)),
        ("停用按鍵".into(), [0; 4]),
        ("小算盤".into(), [2, 0, 1, 0x92]),
        ("播放／暫停".into(), [2, 0, 0, 0xcd]),
        ("靜音".into(), [2, 0, 0, 0xe2]),
        ("上一首".into(), [2, 0, 0, 0xb6]),
        ("下一首".into(), [2, 0, 0, 0xb5]),
        ("音量降低".into(), [2, 0, 0, 0xea]),
        ("音量提高".into(), [2, 0, 0, 0xe9]),
        ("停止播放".into(), [2, 0, 0, 0xb7]),
    ];
    // Captured from this B87's Fn layer, not generic HID usages. Assigning one
    // changes a physical key; it does not invoke the function over USB.
    for (name, value) in [
        ("側燈切色（鍵盤內建）", [8, 0, 0, 1]),
        ("機內背光錄製／儲存（鍵盤內建）", [8, 7, 0, 0]),
        ("機內背光 1（可錄製）", [8, 9, 0x20, 0]),
        ("機內背光 2（固定）", [8, 9, 0x21, 0]),
        ("機內背光 3（固定）", [8, 9, 0x22, 0]),
        ("Win Lock 切換（鍵盤內建）", [7, 0, 0, 1]),
        ("切換 Windows 模式（鍵盤內建）", [7, 0, 0, 0x0e]),
        ("切換 Mac 模式（鍵盤內建）", [7, 0, 0, 0x0f]),
    ] {
        a.push((name.into(), value));
    }
    for (name, key, modifier) in [
        ("複製（Ctrl＋C）", 6, 1),
        ("貼上（Ctrl＋V）", 0x19, 1),
        ("復原（Ctrl＋Z）", 0x1d, 1),
        ("儲存（Ctrl＋S）", 0x16, 1),
        ("鎖定電腦（Win＋L）", 0x0f, 8),
    ] {
        a.push((name.into(), code(key, modifier)));
    }
    for i in 0..26 {
        a.push((((b'A' + i) as char).to_string(), code(4 + i, 0)));
    }
    for i in 1..=24 {
        a.push((
            format!("F{i}"),
            code(if i <= 12 { 0x39 + i } else { 0x68 + i - 13 }, 0),
        ));
    }
    for i in 1..=9 {
        a.push((i.to_string(), code(0x1d + i, 0)));
    }
    a.push(("0".into(), code(0x27, 0)));
    for (name, m) in [("Ctrl", 1), ("Shift", 2), ("Alt", 4), ("Win", 8)] {
        a.push((name.into(), code(0, m)));
    }
    for (name, key) in [
        ("-", 0x2d),
        ("=", 0x2e),
        ("[", 0x2f),
        ("]", 0x30),
        ("\\", 0x31),
        (";", 0x33),
        ("'", 0x34),
        ("`", 0x35),
        (",", 0x36),
        (".", 0x37),
        ("/", 0x38),
        ("Caps Lock", 0x39),
        ("Print Screen", 0x46),
        ("Scroll Lock", 0x47),
        ("Pause", 0x48),
        ("Insert", 0x49),
        ("選單鍵", 0x65),
        ("Enter", 0x28),
        ("Escape", 0x29),
        ("Backspace", 0x2a),
        ("Tab", 0x2b),
        ("空白鍵", 0x2c),
        ("Delete", 0x4c),
        ("Home", 0x4a),
        ("End", 0x4d),
        ("Page Up", 0x4b),
        ("Page Down", 0x4e),
        ("向右", 0x4f),
        ("向左", 0x50),
        ("向下", 0x51),
        ("向上", 0x52),
    ] {
        a.push((name.into(), code(key, 0)));
    }
    for (i, m) in macros.iter().enumerate() {
        a.push((format!("巨集：{}", m.name), [3, 1, 1, i as u8]));
    }
    a
}
pub fn json_actions(macros: &[Macro]) -> Vec<Value> {
    all(macros)
        .into_iter()
        .map(|(n, h)| json!({"n":n,"h":hex::encode_upper(h)}))
        .collect()
}
pub fn decode(c: &[u8], macros: &[Macro]) -> String {
    if c.len() != 4 {
        return "未知".into();
    }
    if is_macro(c) {
        return macros.get(c[3] as usize).map_or_else(
            || format!("巨集 #{}", c[3] as usize + 1),
            |m| format!("巨集：{}", m.name),
        );
    }
    let actions = all(&[]);
    if let Some((n, _)) = actions.iter().find(|(_, b)| b == c) {
        return n.clone();
    }
    if c[0] == 0 && c[2] == 0 {
        let key = actions
            .iter()
            .find(|(_, b)| *b == code(c[3], 0))
            .map_or_else(|| format!("HID {:02X}", c[3]), |(n, _)| n.clone());
        let mut mods = Vec::new();
        for (i, n) in ["Ctrl", "Shift", "Alt", "Win", "Ctrl", "Shift", "Alt", "Win"]
            .iter()
            .enumerate()
        {
            if c[1] & (1 << i) != 0 && !mods.contains(n) {
                mods.push(*n);
            }
        }
        if mods.is_empty() {
            return key;
        }
        let prefix = mods.join("＋");
        return if c[3] == 0 {
            prefix
        } else {
            format!("{prefix}＋{key}")
        };
    }
    if c[0] == 0x0d {
        "Fn 功能層".into()
    } else {
        "鍵盤內建功能".into()
    }
}
