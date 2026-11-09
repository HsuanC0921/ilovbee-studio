use crate::model::Profile;
use anyhow::{ensure, Context, Result};
use serde::{de::DeserializeOwned, Serialize};
use std::{
    fs,
    io::{Read, Write},
    path::{Path, PathBuf},
};

#[derive(Clone)]
pub struct Store {
    pub root: PathBuf,
}
#[derive(Clone)]
pub struct BackupSettings {
    pub auto: bool,
    pub keep_auto: usize,
    pub keep_manual: usize,
}
impl Default for BackupSettings {
    fn default() -> Self {
        Self {
            auto: true,
            keep_auto: 5,
            keep_manual: 5,
        }
    }
}
impl Store {
    pub fn new(root: PathBuf) -> Result<Self> {
        fs::create_dir_all(&root)?;
        Ok(Self { root })
    }
    pub fn read_text(&self, name: &str, limit: u64) -> Result<Option<String>> {
        let path = self.root.join(name);
        let f = match fs::File::open(&path) {
            Ok(f) => f,
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(None),
            Err(e) => return Err(e.into()),
        };
        let mut data = String::new();
        f.take(limit + 1).read_to_string(&mut data)?;
        ensure!(
            data.len() as u64 <= limit,
            "檔案超過大小上限：{}",
            path.display()
        );
        Ok(Some(data.trim_start_matches('\u{feff}').to_string()))
    }
    pub fn load<T: DeserializeOwned + Default>(&self, name: &str) -> Result<T> {
        match self.read_text(name, 2 * 1024 * 1024)? {
            Some(t) => serde_json::from_str(&t)
                .with_context(|| format!("無法讀取 {name}；原始檔案仍保留。")),
            None => Ok(T::default()),
        }
    }
    pub fn write_text(&self, name: &str, data: &str) -> Result<()> {
        let path = self.root.join(name);
        let parent = path.parent().context("缺少資料夾")?;
        fs::create_dir_all(parent)?;
        let mut tmp = tempfile::NamedTempFile::new_in(parent)?;
        tmp.write_all(data.as_bytes())?;
        tmp.as_file().sync_all()?;
        tmp.persist(path)?;
        Ok(())
    }
    pub fn save<T: Serialize + ?Sized>(&self, name: &str, data: &T) -> Result<()> {
        self.write_text(name, &serde_json::to_string_pretty(data)?)
    }
    pub fn backup_settings(&self) -> Result<BackupSettings> {
        let mut s = BackupSettings::default();
        for line in self
            .read_text("backup.ini", 4096)?
            .unwrap_or_default()
            .lines()
        {
            if let Some((k, v)) = line.split_once('=') {
                match k.trim() {
                    "auto" => s.auto = v.trim() != "0",
                    "keepauto" => s.keep_auto = v.trim().parse::<usize>()?.clamp(1, 100),
                    "keepmanual" => s.keep_manual = v.trim().parse::<usize>()?.clamp(1, 100),
                    _ => (),
                }
            }
        }
        Ok(s)
    }
    pub fn save_backup_settings(&self, s: &BackupSettings) -> Result<()> {
        ensure!(
            (1..=100).contains(&s.keep_auto) && (1..=100).contains(&s.keep_manual),
            "備份保留份數需為 1 到 100。"
        );
        self.write_text(
            "backup.ini",
            &format!(
                "auto={}\nkeepauto={}\nkeepmanual={}\n",
                u8::from(s.auto),
                s.keep_auto,
                s.keep_manual
            ),
        )
    }
    pub fn backups(&self) -> Result<Vec<String>> {
        let dir = self.root.join("backups");
        if !dir.exists() {
            return Ok(vec![]);
        }
        let mut names = Vec::new();
        for entry in fs::read_dir(dir)? {
            let e = entry?;
            if e.file_type()?.is_file() && e.path().extension().is_some_and(|s| s == "json") {
                names.push(e.file_name().to_string_lossy().into_owned());
            }
        }
        names.sort_by(|a, b| b.cmp(a));
        Ok(names)
    }
    pub fn prune(&self, s: &BackupSettings) -> Result<()> {
        let (mut manual, mut auto) = (0, 0);
        for name in self.backups()? {
            let (count, keep) = if name.ends_with("-manual.json") {
                (&mut manual, s.keep_manual)
            } else {
                (&mut auto, s.keep_auto)
            };
            *count += 1;
            if *count > keep {
                fs::remove_file(self.root.join("backups").join(name))?;
            }
        }
        Ok(())
    }
    pub fn backup(&self, purpose: &str, p: &Profile) -> Result<Option<String>> {
        let s = self.backup_settings()?;
        if purpose != "manual" && !s.auto {
            return Ok(None);
        }
        let name = format!(
            "{}-{purpose}.json",
            chrono::Local::now().format("%Y%m%d-%H%M%S-%f")
        );
        self.save(&format!("backups/{name}"), p)?;
        self.prune(&s)?;
        Ok(Some(name))
    }
    pub fn load_backup(&self, name: &str) -> Result<Profile> {
        ensure!(
            Path::new(name).file_name().and_then(|s| s.to_str()) == Some(name)
                && !name.contains(['/', '\\', ':'])
                && name.ends_with(".json"),
            "無效的備份檔名。"
        );
        let text = self
            .read_text(&format!("backups/{name}"), 65536)?
            .context("找不到備份。")?;
        let p: Profile = serde_json::from_str(&text)?;
        p.validate()?;
        Ok(p)
    }
}
