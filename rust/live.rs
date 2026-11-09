use crate::{
    api::App,
    lighting::{self, Palette, Visualizer, BANDS},
    model::{bytes, keys},
    protocol::FRAME_BYTES,
    service::Service,
    store::Store,
};
use anyhow::{ensure, Result};
use serde_json::{json, Value};
use std::{
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc, Mutex,
    },
    thread,
    time::{Duration, Instant},
};

#[derive(Clone)]
pub struct Config {
    pub mode: String,
    pub style: usize,
    pub gain: f64,
    pub color_mode: usize,
    pub color_dir: usize,
    pub colors: Vec<String>,
    pub frame: Vec<u8>,
    pub fx: Vec<u8>,
    pub fx_speed: usize,
}
impl Default for Config {
    fn default() -> Self {
        Self {
            mode: "off".into(),
            style: 0,
            gain: 40.,
            color_mode: 0,
            color_dir: 0,
            colors: vec!["FF2060".into(), "20C0FF".into()],
            frame: vec![0; FRAME_BYTES],
            fx: vec![0; 126],
            fx_speed: 2,
        }
    }
}
impl Config {
    pub fn stamp(store: &Store) -> Result<Option<std::time::SystemTime>> {
        match std::fs::metadata(store.root.join("live.ini")) {
            Ok(m) => Ok(Some(m.modified()?)),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(e) => Err(e.into()),
        }
    }
    pub fn load(store: &Store) -> Result<Self> {
        let mut c = Self::default();
        for line in store
            .read_text("live.ini", 8192)?
            .unwrap_or_default()
            .lines()
        {
            if let Some((k, v)) = line.split_once('=') {
                let v = v.trim();
                match k.trim() {
                    "mode" => c.mode = v.into(),
                    "style" => c.style = v.parse()?,
                    "gain" => c.gain = v.parse()?,
                    "colormode" => c.color_mode = v.parse()?,
                    "colordir" => c.color_dir = v.parse()?,
                    "colors" => c.colors = v.split(',').map(|s| s.trim().to_uppercase()).collect(),
                    "frame" => c.frame = bytes(v, FRAME_BYTES)?,
                    "fx" => c.fx = bytes(v, 126)?,
                    "fxspeed" => c.fx_speed = v.parse()?,
                    _ => (),
                }
            }
        }
        c.validate()?;
        Ok(c)
    }
    pub fn validate(&self) -> Result<()> {
        ensure!(
            ["off", "music", "custom"].contains(&self.mode.as_str()),
            "無效的模式。"
        );
        ensure!(
            self.style < 12
                && self.gain.is_finite()
                && (5.0..=200.).contains(&self.gain)
                && self.color_mode <= 1
                && self.color_dir <= 3,
            "無效的律動設定。"
        );
        ensure!((1..=6).contains(&self.colors.len()), "配色需為 1 到 6 色。");
        for c in &self.colors {
            bytes(c, 3)?;
        }
        ensure!(
            self.frame.len() == FRAME_BYTES
                && self.fx.len() == 126
                && self.fx.iter().all(|&x| x < 4)
                && (1..=4).contains(&self.fx_speed),
            "無效的逐鍵燈效資料。"
        );
        Ok(())
    }
    pub fn save(&self, store: &Store) -> Result<()> {
        self.validate()?;
        store.write_text("live.ini",&format!("mode={}\nstyle={}\ngain={}\ncolormode={}\ncolordir={}\ncolors={}\nframe={}\nfx={}\nfxspeed={}\n",self.mode,self.style,self.gain,self.color_mode,self.color_dir,self.colors.join(","),hex::encode_upper(&self.frame),hex::encode_upper(&self.fx),self.fx_speed))
    }
    pub fn json(&self, status: &str) -> Value {
        json!({"mode":self.mode,"style":self.style,"gain":self.gain,"colormode":self.color_mode,"colordir":self.color_dir,"colors":self.colors,"frame":hex::encode_upper(&self.frame),"fx":hex::encode_upper(&self.fx),"fxspeed":self.fx_speed,"effects":lighting::EFFECTS,"styles":lighting::STYLES,"status":status})
    }
    pub fn palette(&self) -> Palette {
        Palette {
            mode: self.color_mode,
            stops: self
                .colors
                .iter()
                .map(|h| {
                    hex::decode(h)
                        .expect("validated palette")
                        .try_into()
                        .unwrap()
                })
                .collect(),
            direction: self.color_dir,
        }
    }
}

/// Restore marker is durable before entering mode 18, and removed only after verified recovery.
pub fn recover(service: &mut Service) -> Result<()> {
    let Some(text) = service.store.read_text("live-restore.txt", 32)? else {
        return Ok(());
    };
    let mode: u8 = text.trim().parse()?;
    ensure!(mode < 20, "無效的燈效還原記錄。");
    let now = service.read_lights()?;
    if now[10] == 18 {
        let mut after = now.clone();
        after[10] = mode;
        service.set_lights(&now, &after)?;
    }
    std::fs::remove_file(service.store.root.join("live-restore.txt"))?;
    Ok(())
}
pub fn enter(service: &mut Service) -> Result<()> {
    recover(service)?;
    let now = service.read_lights()?;
    if now[10] != 18 {
        service
            .store
            .write_text("live-restore.txt", &now[10].to_string())?;
        let mut after = now.clone();
        after[10] = 18;
        service.set_lights(&now, &after)?;
    }
    Ok(())
}

pub fn spawn(app: Arc<Mutex<App>>, stop: Arc<AtomicBool>) -> thread::JoinHandle<()> {
    thread::spawn(move || {
        let start = Instant::now();
        let mut viz = Visualizer::new(keys());
        let mut levels = [0.; BANDS];
        #[cfg(windows)]
        let mut audio: Option<crate::audio::Audio> = None;
        let mut sent = Vec::new();
        let mut revision = 0;
        let mut last = Instant::now() - Duration::from_secs(2);
        let mut config_check = Instant::now() - Duration::from_secs(1);
        while !stop.load(Ordering::Relaxed) {
            let mut a = app.lock().unwrap();
            if config_check.elapsed() >= Duration::from_millis(500) {
                config_check = Instant::now();
                if let Err(e) = a.reload_config() {
                    a.status = format!("錯誤：設定檔無法載入：{e:#}");
                    drop(a);
                    thread::sleep(Duration::from_millis(500));
                    continue;
                }
            }
            if revision != a.revision {
                revision = a.revision;
                sent.clear();
            }
            let c = a.config.clone();
            if c.mode == "off" {
                #[cfg(windows)]
                {
                    audio = None;
                }
                // Also recovers an interrupted previous session when no live mode is requested.
                if a.service.store.root.join("live-restore.txt").exists() {
                    if let Err(e) = recover(&mut a.service) {
                        a.status = format!("錯誤：{e:#}");
                        drop(a);
                        thread::sleep(Duration::from_secs(1));
                        continue;
                    }
                }
                a.active = false;
                a.status = "未執行".into();
                drop(a);
                thread::sleep(Duration::from_millis(100));
                continue;
            }
            let result = (|| -> Result<()> {
                if !a.active {
                    if !a.service.scan()? {
                        anyhow::bail!("找不到相容 B87，請連接 USB。");
                    }
                    enter(&mut a.service)?;
                    a.active = true;
                }
                let frame = if c.mode == "music" {
                    if a.service.demo {
                        // Demo is a complete device/audio isolation boundary.
                        for (i, level) in levels.iter_mut().enumerate() {
                            *level = (start.elapsed().as_secs_f64() * 1.8 + i as f64 * 0.6)
                                .sin()
                                .abs()
                                * 0.6;
                        }
                    } else {
                        #[cfg(windows)]
                        {
                            if audio.is_none() {
                                audio = Some(crate::audio::Audio::new()?);
                            }
                            let raw = lighting::spectrum(
                                &audio.as_mut().unwrap().samples()?,
                                48000.,
                                c.gain,
                            );
                            for i in 0..BANDS {
                                levels[i] = raw[i].max(levels[i] * 0.82);
                            }
                        }
                        #[cfg(not(windows))]
                        {
                            anyhow::bail!("音樂律動需要 Windows WASAPI。");
                        }
                    }
                    viz.render(
                        c.style,
                        &levels,
                        start.elapsed().as_secs_f64(),
                        &c.palette(),
                    )
                } else {
                    #[cfg(windows)]
                    {
                        audio = None;
                    }
                    lighting::animate(&c.frame, &c.fx, start.elapsed().as_secs_f64(), c.fx_speed)
                };
                if frame != sent || last.elapsed() > Duration::from_secs(1) {
                    a.service.send_frame(&frame)?;
                    sent = frame;
                    last = Instant::now();
                }
                a.status = if c.mode == "music" {
                    format!("音樂律動 · {}", lighting::STYLES[c.style])
                } else {
                    "自訂燈光".into()
                };
                Ok(())
            })();
            let delay = if let Err(e) = result {
                a.status = format!("錯誤：{e:#}");
                a.active = false;
                #[cfg(windows)]
                {
                    audio = None;
                }
                Duration::from_secs(1)
            } else {
                Duration::from_millis(33)
            };
            drop(a);
            thread::sleep(delay);
        }
        let mut a = app.lock().unwrap();
        if let Err(e) = recover(&mut a.service) {
            a.status = format!("燈效還原失敗：{e:#}");
            eprintln!("{}", a.status);
        }
    })
}
