use crate::{model::*, protocol::*, store::Store};
use anyhow::{bail, ensure, Context, Result};

pub struct Service {
    pub store: Store,
    pub demo: bool,
    transport: Option<Box<dyn Transport>>,
}
impl Service {
    pub fn new(store: Store, demo: bool) -> Self {
        Self {
            store,
            demo,
            transport: if demo {
                Some(Box::new(DemoTransport::default()))
            } else {
                None
            },
        }
    }
    /// Inject a transport for protocol/transaction tests without a USB device.
    pub fn with_transport(store: Store, transport: Box<dyn Transport>) -> Self {
        Self {
            store,
            demo: true,
            transport: Some(transport),
        }
    }
    pub fn scan(&mut self) -> Result<bool> {
        if self.demo {
            return Ok(true);
        }
        #[cfg(windows)]
        {
            self.transport = crate::windows::connect()?;
        }
        Ok(self.transport.is_some())
    }
    fn device(&mut self) -> Result<&mut (dyn Transport + 'static)> {
        self.transport
            .as_deref_mut()
            .context("未找到相容的 B87 USB 設定介面。請切至有線模式。")
    }
    fn prepare(&mut self) -> Result<()> {
        if !self.demo {
            #[cfg(windows)]
            {
                crate::windows::check_conflict()?;
                crate::windows::wait_keys_idle()?;
            }
        }
        self.device()?;
        Ok(())
    }
    pub fn read_map(&mut self, layer: u8) -> Result<Vec<u8>> {
        self.device()?.read(0x83, layer, MAP_BYTES)
    }
    pub fn read_lights(&mut self) -> Result<Vec<u8>> {
        self.device()?.read(0x84, 0, LIGHT_BYTES)
    }
    pub fn capture(&mut self) -> Result<Profile> {
        let mut keymaps = Maps::new();
        for i in 0..4 {
            keymaps.insert(i, hex::encode_upper(self.read_map(i)?));
        }
        Ok(Profile {
            schema: 1,
            device: "258A:0199".into(),
            release: "2010".into(),
            created: chrono::Utc::now().to_rfc3339(),
            keymaps,
            lighting: hex::encode_upper(self.read_lights()?),
        })
    }
    fn write_verified(&mut self, command: u8, layer: u8, data: &[u8]) -> Result<()> {
        let d = self.device()?;
        d.write(command, layer, data)?;
        ensure!(
            d.read(command | 0x80, layer, data.len())? == data,
            "設定讀回不符。"
        );
        Ok(())
    }
    fn apply_profile(&mut self, p: &Profile) -> Result<()> {
        for (&i, h) in &p.keymaps {
            self.write_verified(3, i, &bytes(h, MAP_BYTES)?)?;
        }
        self.write_verified(4, 0, &bytes(&p.lighting, LIGHT_BYTES)?)
    }
    fn rollback_error(
        original: anyhow::Error,
        rollback: Result<()>,
        non_readable: bool,
    ) -> anyhow::Error {
        let suffix = if non_readable {
            " 巨集緩衝區無法讀回，可能已更新；請重新寫入所需巨集。"
        } else {
            ""
        };
        match rollback {
            Ok(()) => anyhow::anyhow!("套用失敗，已回復原映射與燈效：{original:#}{suffix}"),
            Err(e) => anyhow::anyhow!("套用失敗且回復未能確認，請保留備份並重新連接鍵盤。原錯誤：{original:#}；回復：{e:#}{suffix}"),
        }
    }
    pub fn apply_maps(&mut self, expected: &Maps, desired: &Maps, macros: &[Macro]) -> Result<()> {
        validate_maps(expected, false)?;
        validate_maps(desired, false)?;
        preserve_fn(expected, desired)?;
        // Validate every reference before any hardware mutation, including a macro upload.
        let mut upload = false;
        for (&layer, map) in desired {
            let new = bytes(map, MAP_BYTES)?;
            let old = bytes(&expected[&layer], MAP_BYTES)?;
            for (a, b) in new.as_chunks::<4>().0.iter().zip(old.as_chunks::<4>().0) {
                if is_macro(a) && a != b {
                    ensure!((a[3] as usize) < macros.len(), "按鍵指向不存在的巨集。");
                    upload = true;
                }
            }
        }
        let macro_data = if upload {
            Some(encode_macros(macros)?)
        } else {
            None
        };
        self.prepare()?;
        let before = self.capture()?;
        for (&i, h) in expected {
            ensure!(
                bytes(&before.keymaps[&i], MAP_BYTES)? == bytes(h, MAP_BYTES)?,
                "鍵盤設定已變更。請先匯出草稿，再重新讀取鍵盤。"
            );
        }
        let changed: Vec<_> = desired
            .iter()
            .filter(|(i, h)| !h.eq_ignore_ascii_case(&expected[i]))
            .map(|(&i, _)| i)
            .collect();
        if changed.is_empty() {
            return Ok(());
        }
        self.store.backup("before-keymaps", &before)?;
        if let Some(data) = macro_data {
            self.device()?
                .write(5, 0, &data)
                .context("巨集寫入失敗，映射尚未寫入；巨集緩衝區無法讀回。")?;
        }
        let mut attempted = Vec::new();
        let result = (|| {
            for &layer in &changed {
                attempted.push(layer);
                self.write_verified(3, layer, &bytes(&desired[&layer], MAP_BYTES)?)?;
            }
            for (&layer, h) in desired {
                ensure!(
                    self.read_map(layer)? == bytes(h, MAP_BYTES)?,
                    "完整映射核對失敗。"
                );
            }
            Ok(())
        })();
        if let Err(e) = result {
            let rollback = (|| {
                for &layer in attempted.iter().rev() {
                    self.write_verified(3, layer, &bytes(&before.keymaps[&layer], MAP_BYTES)?)?;
                }
                Ok(())
            })();
            return Err(Self::rollback_error(e, rollback, upload));
        }
        Ok(())
    }
    pub fn set_lights(&mut self, expected: &[u8], after: &[u8]) -> Result<()> {
        ensure!(
            expected.len() == LIGHT_BYTES && after.len() == LIGHT_BYTES,
            "無效的燈效設定。"
        );
        self.prepare()?;
        let before = self.capture()?;
        ensure!(
            bytes(&before.lighting, LIGHT_BYTES)? == expected,
            "燈效設定已變更，請重新讀取。"
        );
        if expected == after {
            return Ok(());
        }
        self.store.backup("before-lighting", &before)?;
        if let Err(e) = self.write_verified(4, 0, after) {
            let rb = self.write_verified(4, 0, expected);
            return Err(Self::rollback_error(e, rb, false));
        }
        Ok(())
    }
    pub fn restore(&mut self, p: &Profile) -> Result<()> {
        p.validate()?;
        self.prepare()?;
        let before = self.capture()?;
        preserve_fn(&before.keymaps, &p.keymaps)?;
        self.store.backup("before-restore", &before)?;
        if let Err(e) = self.apply_profile(p) {
            let rb = self.apply_profile(&before);
            return Err(Self::rollback_error(e, rb, false));
        }
        Ok(())
    }
    pub fn write_macros(&mut self, macros: &[Macro]) -> Result<()> {
        ensure!(!macros.is_empty(), "尚無巨集。");
        let data = encode_macros(macros)?;
        self.prepare()?;
        let p = self.capture()?;
        self.store.backup("before-macro", &p)?;
        self.device()?
            .write(5, 0, &data)
            .context("巨集寫入失敗；巨集緩衝區無法讀回，請重試寫入。")
    }
    pub fn set_color(&mut self, mode: usize, rgb: &[u8]) -> Result<()> {
        ensure!(
            (1..20).contains(&mode) && rgb.len() == 3,
            "無效的自訂顏色。"
        );
        self.prepare()?;
        let mut table = self.color_table()?;
        // Save the known local table before sending this write-only command.
        self.store
            .write_text("colortable-before.hex", &hex::encode_upper(&table))?;
        table[mode * 21..mode * 21 + 3].copy_from_slice(rgb);
        self.device()?.write(10, 0, &table)?;
        self.store
            .write_text("colortable.hex", &hex::encode_upper(table))
    }
    pub fn color_table(&self) -> Result<Vec<u8>> {
        if let Some(t) = self.store.read_text("colortable.hex", 1024)? {
            return bytes(t.trim(), COLOR_BYTES);
        }
        let standard = hex::decode("FF000000FF000000FFFFFF00FF00FF00FFFFFFFFFF")?;
        let mut table = vec![0; COLOR_BYTES];
        for m in 1..20 {
            table[m * 21..m * 21 + 21].copy_from_slice(&standard);
        }
        Ok(table)
    }
    pub fn send_frame(&mut self, frame: &[u8]) -> Result<()> {
        self.device()?.write(8, 0, frame)
    }
}

/// Explicit demo device; never used as a fallback after an actual USB failure.
pub struct DemoTransport {
    pub maps: [Vec<u8>; 4],
    pub lights: Vec<u8>,
}
impl Default for DemoTransport {
    fn default() -> Self {
        let maps = std::array::from_fn(|_| {
            let mut m = vec![0; MAP_BYTES];
            m[FN_SLOT * 4] = 0x0d;
            m[78 * 4..78 * 4 + 4].copy_from_slice(&[2, 0, 1, 0x92]);
            m
        });
        let mut lights = vec![0; LIGHT_BYTES];
        lights[10] = 1;
        lights[126] = 0x5a;
        lights[127] = 0xa5;
        for mode in 1..20 {
            lights[56 + mode * 2] = 4;
            lights[57 + mode * 2] = 0x27;
        }
        Self { maps, lights }
    }
}
impl Transport for DemoTransport {
    fn read(&mut self, c: u8, l: u8, n: usize) -> Result<Vec<u8>> {
        read_request(c, l, n)?;
        Ok(match c {
            0x83 => self.maps[l as usize].clone(),
            0x84 => self.lights.clone(),
            0x87 => vec![0, 0],
            _ => bail!("未允許的指令"),
        })
    }
    fn write(&mut self, c: u8, l: u8, data: &[u8]) -> Result<()> {
        write_request(c, l, data)?;
        match c {
            3 => self.maps[l as usize] = data.to_vec(),
            4 => self.lights = data.to_vec(),
            _ => (),
        }
        Ok(())
    }
}
