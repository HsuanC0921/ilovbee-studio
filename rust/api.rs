use crate::{
    actions,
    live::{self, Config},
    model::*,
    protocol::*,
    service::Service,
    store::{BackupSettings, Store},
};
use anyhow::{bail, ensure, Context, Result};
use serde_json::{json, Value};
use std::{
    io::Read,
    sync::{Arc, Mutex},
};
use tiny_http::{Header, Method, Request, Response, Server};

pub struct App {
    pub service: Service,
    pub macros: Vec<Macro>,
    pub schemes: Vec<Scheme>,
    pub layouts: Vec<Layout>,
    pub config: Config,
    pub status: String,
    pub active: bool,
    pub revision: u64,
}
impl App {
    pub fn new(store: Store, demo: bool) -> Result<Self> {
        let macros: Vec<Macro> = store.load("macros.json")?;
        encode_macros(&macros)?;
        let schemes: Vec<Scheme> = store.load("schemes.json")?;
        ensure!(schemes.len() <= 20, "方案超過 20 組。");
        for s in &schemes {
            validate_name(&s.name)?;
            validate_maps(&s.layers, false)?;
        }
        let layouts: Vec<Layout> = store.load("customlights.json")?;
        ensure!(layouts.len() <= 20, "燈光配置超過 20 組。");
        for l in &layouts {
            validate_layout(l)?;
        }
        let config = Config::load(&store)?;
        Ok(Self {
            service: Service::new(store, demo),
            macros,
            schemes,
            layouts,
            config,
            status: "未執行".into(),
            active: false,
            revision: 0,
        })
    }
    pub fn set_config(&mut self, c: Config) -> Result<()> {
        c.validate()?;
        if c.mode == "off" {
            live::recover(&mut self.service)?;
            self.active = false;
            self.status = "未執行".into();
        }
        c.save(&self.service.store)?;
        self.config = c;
        self.revision += 1;
        Ok(())
    }
    fn stop_live(&mut self) -> Result<()> {
        if self.config.mode != "off" || self.active {
            let mut c = self.config.clone();
            c.mode = "off".into();
            self.set_config(c)?;
        }
        Ok(())
    }
    pub fn state(&mut self) -> Result<Value> {
        let (connected, mut error) = match self.service.scan() {
            Ok(c) => (c, None),
            Err(e) => (false, Some(format!("{e:#}"))),
        };
        let profile = if connected {
            match self.service.capture() {
                Ok(p) => Some(p),
                Err(e) => {
                    error = Some(format!("{e:#}"));
                    None
                }
            }
        } else {
            None
        };
        let mut v = json!({"connected":connected,"demo":self.service.demo,"backend":"rust","error":error,"actions":actions::json_actions(&self.macros),"macros":self.macros,"schemes":self.schemes.iter().map(|s|&s.name).collect::<Vec<_>>(),"layouts":self.layouts,"live":self.config.json(&self.status)});
        if let Some(p) = profile {
            let mut names = serde_json::Map::new();
            for (&l, m) in &p.keymaps {
                let codes = bytes(m, MAP_BYTES)?;
                names.insert(
                    l.to_string(),
                    json!(codes
                        .as_chunks::<4>()
                        .0
                        .iter()
                        .map(|c| actions::decode(c, &self.macros))
                        .collect::<Vec<_>>()),
                );
            }
            v["lights"] = json!(p.lighting);
            v["maps"] = json!(p.keymaps);
            v["names"] = json!(names);
            v["colortable"] = json!(hex::encode_upper(self.service.color_table()?));
        }
        #[cfg(windows)]
        if !self.service.demo {
            match crate::windows::battery() {
                Ok(Some(b)) => {
                    v["battery"] = json!(b.percent);
                    v["batteryReportedAt"] = json!(b.reported_at);
                }
                Ok(None) => (),
                Err(e) => v["batteryError"] = json!(format!("{e:#}")),
            }
        }
        Ok(v)
    }
    pub fn route(&mut self, method: &str, path: &str, query: &str, body: &[u8]) -> Result<Value> {
        let doc = || -> Result<Value> { Ok(serde_json::from_slice(body)?) };
        let ok = || json!({"ok":true});
        match (method, path) {
            ("GET", "/api/state") => self.state(),
            ("GET", "/api/ping") => Ok(json!({"connected":self.service.scan()?})),
            ("GET", "/api/live") => Ok(self.config.json(&self.status)),
            ("POST", "/api/live") => {
                let d = doc()?;
                let mut c = self.config.clone();
                let mode = string(&d, "mode")?;
                if mode != "keep" {
                    c.mode = mode.into();
                }
                if d.get("style").is_some() {
                    c.style = number(&d, "style")?;
                }
                if d.get("gain").is_some() {
                    c.gain = d["gain"].as_f64().context("無效的靈敏度。")?;
                }
                if d.get("colormode").is_some() {
                    c.color_mode = number(&d, "colormode")?;
                }
                if d.get("colordir").is_some() {
                    c.color_dir = number(&d, "colordir")?;
                }
                if d.get("colors").is_some() {
                    c.colors = serde_json::from_value(d["colors"].clone())?;
                }
                if d.get("frame").is_some() {
                    c.frame = bytes(string(&d, "frame")?, FRAME_BYTES)?;
                }
                if d.get("fx").is_some() {
                    c.fx = bytes(string(&d, "fx")?, 126)?;
                }
                if d.get("fxspeed").is_some() {
                    c.fx_speed = number(&d, "fxspeed")?;
                }
                self.set_config(c)?;
                Ok(ok())
            }
            ("POST", "/api/lights") => {
                let d = doc()?;
                let expected = bytes(string(&d, "expected")?, LIGHT_BYTES)?;
                let after = bytes(string(&d, "after")?, LIGHT_BYTES)?;
                self.stop_live()?;
                self.service.set_lights(&expected, &after)?;
                Ok(ok())
            }
            ("POST", "/api/color") => {
                let d = doc()?;
                self.service
                    .set_color(number(&d, "mode")?, &bytes(string(&d, "rgb")?, 3)?)?;
                Ok(ok())
            }
            ("POST", "/api/keymap") => {
                let d = doc()?;
                let expected: Maps = serde_json::from_value(d["expected"].clone())?;
                let desired: Maps = serde_json::from_value(d["desired"].clone())?;
                self.service.apply_maps(&expected, &desired, &self.macros)?;
                Ok(ok())
            }
            ("POST", "/api/decode") => {
                let d = doc()?;
                let codes: Vec<String> = serde_json::from_value(d["codes"].clone())?;
                ensure!(codes.len() <= 416, "一次最多解碼 416 個按鍵。");
                Ok(json!(codes
                    .iter()
                    .map(|h| Ok(actions::decode(&bytes(h, 4)?, &self.macros)))
                    .collect::<Result<Vec<_>>>()?))
            }
            ("POST", "/api/macros/save") => {
                let items: Vec<Macro> = serde_json::from_slice(body)?;
                encode_macros(&items)?;
                self.service.store.save("macros.json", &items)?;
                self.macros = items;
                Ok(ok())
            }
            ("POST", "/api/macros/write") => {
                self.service.write_macros(&self.macros)?;
                Ok(ok())
            }
            ("POST", "/api/schemes/create") => {
                let d = doc()?;
                let name = string(&d, "name")?;
                let name = if name.trim().is_empty() {
                    format!("方案{}", self.schemes.len() + 1)
                } else {
                    name.into()
                };
                let name = self.scheme_name(&name, None)?;
                ensure!(self.schemes.len() < 20, "方案最多 20 組。");
                let s = Scheme {
                    name,
                    layers: self.service.capture()?.keymaps,
                };
                let mut items = self.schemes.clone();
                items.push(s);
                self.save_schemes(items)?;
                Ok(ok())
            }
            ("POST", "/api/schemes/import") => {
                let mut s: Scheme = serde_json::from_slice(body)?;
                s.name = self.scheme_name(&s.name, None)?;
                validate_maps(&s.layers, false)?;
                ensure!(self.schemes.len() < 20, "方案最多 20 組。");
                let mut items = self.schemes.clone();
                items.push(s);
                self.save_schemes(items)?;
                Ok(ok())
            }
            ("GET", "/api/schemes/export") => {
                let i = query_value(query, "index")
                    .context("缺少方案索引。")?
                    .parse::<usize>()?;
                Ok(serde_json::to_value(
                    self.schemes.get(i).context("沒有這個方案。")?,
                )?)
            }
            ("POST", "/api/schemes/rename") => {
                let d = doc()?;
                let i = index(&d, self.schemes.len())?;
                let name = self.scheme_name(string(&d, "name")?, Some(i))?;
                let mut items = self.schemes.clone();
                items[i].name = name;
                self.save_schemes(items)?;
                Ok(ok())
            }
            ("POST", "/api/schemes/delete") => {
                let d = doc()?;
                let i = index(&d, self.schemes.len())?;
                let mut items = self.schemes.clone();
                items.remove(i);
                self.save_schemes(items)?;
                Ok(ok())
            }
            ("POST", "/api/schemes/apply") => {
                let d = doc()?;
                let i = index(&d, self.schemes.len())?;
                let desired = self.schemes[i].layers.clone();
                let expected = self.service.capture()?.keymaps;
                self.service.apply_maps(&expected, &desired, &self.macros)?;
                Ok(ok())
            }
            ("POST", "/api/layouts/put") => {
                let d = doc()?;
                let name = validate_name(string(&d, "name")?)?;
                let l = Layout {
                    name,
                    frame: string(&d, "frame")?.to_uppercase(),
                    fx: d
                        .get("fx")
                        .map(|v| v.as_str().map(String::from).context("無效的逐鍵行為。"))
                        .transpose()?,
                };
                validate_layout(&l)?;
                let mut items = self.layouts.clone();
                if let Some(i) = items.iter().position(|x| x.name == l.name) {
                    items[i] = l;
                } else {
                    ensure!(items.len() < 20, "燈光配置最多 20 組。");
                    items.push(l);
                }
                self.service.store.save("customlights.json", &items)?;
                self.layouts = items;
                Ok(ok())
            }
            ("POST", "/api/layouts/delete") => {
                let d = doc()?;
                let name = string(&d, "name")?;
                let items: Vec<_> = self
                    .layouts
                    .iter()
                    .filter(|l| l.name != name)
                    .cloned()
                    .collect();
                self.service.store.save("customlights.json", &items)?;
                self.layouts = items;
                Ok(ok())
            }
            ("GET", "/api/backups") => {
                let s = self.service.store.backup_settings()?;
                Ok(
                    json!({"auto":s.auto,"keepAuto":s.keep_auto,"keepManual":s.keep_manual,"max":100,"files":self.service.store.backups()?}),
                )
            }
            ("POST", "/api/backups/settings") => {
                let d = doc()?;
                let s = BackupSettings {
                    auto: d["auto"].as_bool().context("無效的備份開關。")?,
                    keep_auto: number(&d, "keepAuto")?,
                    keep_manual: number(&d, "keepManual")?,
                };
                self.service.store.save_backup_settings(&s)?;
                self.service.store.prune(&s)?;
                Ok(ok())
            }
            ("POST", "/api/backup") => {
                let p = self.service.capture()?;
                Ok(json!({"file":self.service.store.backup("manual",&p)?}))
            }
            ("GET", "/api/profile") => Ok(serde_json::to_value(self.service.capture()?)?),
            ("POST", "/api/restore") => {
                let p: Profile = if let Some(name) = query_value(query, "name") {
                    self.service.store.load_backup(&name)?
                } else {
                    ensure!(body.len() <= 65536, "設定檔超過大小上限。");
                    serde_json::from_slice(body)?
                };
                p.validate()?;
                self.stop_live()?;
                self.service.restore(&p)?;
                Ok(ok())
            }
            _ => bail!("找不到此操作。"),
        }
    }
    fn scheme_name(&self, name: &str, except: Option<usize>) -> Result<String> {
        let name = validate_name(name)?;
        ensure!(
            !self
                .schemes
                .iter()
                .enumerate()
                .any(|(i, s)| Some(i) != except && s.name == name),
            "已有同名方案。"
        );
        Ok(name)
    }
    fn save_schemes(&mut self, items: Vec<Scheme>) -> Result<()> {
        self.service.store.save("schemes.json", &items)?;
        self.schemes = items;
        Ok(())
    }
}
fn validate_layout(l: &Layout) -> Result<()> {
    validate_name(&l.name)?;
    bytes(&l.frame, FRAME_BYTES)?;
    if let Some(h) = &l.fx {
        ensure!(bytes(h, 126)?.iter().all(|&b| b < 4), "無效的逐鍵行為。");
    }
    Ok(())
}
fn string<'a>(d: &'a Value, k: &str) -> Result<&'a str> {
    d[k].as_str()
        .with_context(|| format!("缺少或無效的欄位：{k}"))
}
fn number(d: &Value, k: &str) -> Result<usize> {
    let n = d[k].as_u64().with_context(|| format!("無效的整數：{k}"))?;
    Ok(usize::try_from(n)?)
}
fn index(d: &Value, count: usize) -> Result<usize> {
    let i = number(d, "index")?;
    ensure!(i < count, "沒有這個項目。");
    Ok(i)
}
fn query_value(query: &str, key: &str) -> Option<String> {
    url::form_urlencoded::parse(query.as_bytes())
        .find(|(k, _)| k == key)
        .map(|(_, v)| v.into_owned())
}

/// Authentication stays outside routing, so no endpoint can accidentally bypass it.
pub struct Session {
    pub port: u16,
    pub token: String,
}
impl Session {
    pub fn new(port: u16) -> Result<Self> {
        let mut b = [0; 32];
        getrandom::fill(&mut b).map_err(|e| anyhow::anyhow!("無法產生連線權杖：{e}"))?;
        Ok(Self {
            port,
            token: hex::encode_upper(b),
        })
    }
    pub fn authorize(
        &self,
        host: Option<&str>,
        origin: Option<&str>,
        cookie: Option<&str>,
        method: &str,
        path: &str,
        query: &str,
    ) -> Option<bool> {
        let authority = format!("127.0.0.1:{}", self.port);
        if host != Some(authority.as_str())
            || origin.is_some_and(|s| s != format!("http://{authority}"))
        {
            return None;
        }
        let valid = cookie.is_some_and(|c| {
            c.split(';')
                .any(|s| s.trim() == format!("ilv={}", self.token))
        });
        if valid {
            return Some(false);
        }
        if method == "GET"
            && path == "/"
            && query_value(query, "t").as_deref() == Some(self.token.as_str())
        {
            return Some(true);
        }
        None
    }
}
fn asset(path: &str) -> Option<(&'static str, &'static [u8])> {
    macro_rules! web {
        ($f:literal,$mime:literal) => {
            Some(($mime, include_bytes!(concat!("../app/web/", $f))))
        };
    }
    match path {
        "/" => web!("index.html", "text/html; charset=utf-8"),
        "/style.css" => web!("style.css", "text/css; charset=utf-8"),
        "/core.js" => web!("core.js", "text/javascript; charset=utf-8"),
        "/remap.js" => web!("remap.js", "text/javascript; charset=utf-8"),
        "/macros.js" => web!("macros.js", "text/javascript; charset=utf-8"),
        "/lighting.js" => web!("lighting.js", "text/javascript; charset=utf-8"),
        "/custom.js" => web!("custom.js", "text/javascript; charset=utf-8"),
        "/misc.js" => web!("misc.js", "text/javascript; charset=utf-8"),
        "/manual.js" => web!("manual.js", "text/javascript; charset=utf-8"),
        "/main.js" => web!("main.js", "text/javascript; charset=utf-8"),
        "/effects.js" => web!("effects.js", "text/javascript; charset=utf-8"),
        "/keyboard.json" => Some((
            "application/json; charset=utf-8",
            include_bytes!("../src/keyboard.json"),
        )),
        "/app.ico" => web!("app.ico", "image/x-icon"),
        _ => None,
    }
}
fn respond(r: Request, status: u16, mime: &str, data: Vec<u8>, cookie: Option<String>) {
    let mut res = Response::from_data(data).with_status_code(status);
    for (k, v) in [
        ("Content-Type", mime),
        ("Cache-Control", "no-store"),
        ("X-Content-Type-Options", "nosniff"),
        ("Referrer-Policy", "no-referrer"),
        ("X-Frame-Options", "DENY"),
    ] {
        res.add_header(Header::from_bytes(k, v).unwrap());
    }
    if let Some(c) = cookie {
        res.add_header(Header::from_bytes("Set-Cookie", c).unwrap());
    }
    if let Err(e) = r.respond(res) {
        eprintln!("HTTP 回應失敗：{e}");
    }
}
pub fn serve_one(mut r: Request, app: &Arc<Mutex<App>>, session: &Session) {
    let target = r.url().to_string();
    let (path, query) = target.split_once('?').unwrap_or((&target, ""));
    let header = |n: &'static str| {
        r.headers()
            .iter()
            .find(|h| h.field.equiv(n))
            .map(|h| h.value.as_str())
    };
    let method = r.method().as_str().to_string();
    let auth = session.authorize(
        header("Host"),
        header("Origin"),
        header("Cookie"),
        &method,
        path,
        query,
    );
    let Some(bootstrap) = auth else {
        respond(r, 403, "text/plain", b"forbidden".to_vec(), None);
        return;
    };
    let cookie =
        bootstrap.then(|| format!("ilv={}; Path=/; HttpOnly; SameSite=Strict", session.token));
    if r.method() == &Method::Get {
        if let Some((mime, data)) = asset(path) {
            respond(r, 200, mime, data.to_vec(), cookie);
            return;
        }
    }
    if !path.starts_with("/api/") {
        respond(r, 404, "text/plain", b"not found".to_vec(), None);
        return;
    }
    const LIMIT: usize = 2 * 1024 * 1024;
    if r.body_length().is_some_and(|n| n > LIMIT) {
        respond(r, 413, "text/plain", b"request too large".to_vec(), None);
        return;
    }
    let mut body = vec![];
    if let Err(e) = r
        .as_reader()
        .take((LIMIT + 1) as u64)
        .read_to_end(&mut body)
    {
        respond(r, 400, "text/plain", e.to_string().into_bytes(), None);
        return;
    }
    if body.len() > LIMIT {
        respond(r, 413, "text/plain", b"request too large".to_vec(), None);
        return;
    }
    let result = app.lock().unwrap().route(&method, path, query, &body);
    let (status, data) = match result {
        Ok(v) => (200, serde_json::to_vec(&v).unwrap()),
        Err(e) => (400, serde_json::to_vec(&format!("{e:#}")).unwrap()),
    };
    respond(r, status, "application/json; charset=utf-8", data, None);
}
pub fn listen() -> Result<Server> {
    Server::http("127.0.0.1:0").map_err(|e| anyhow::anyhow!("無法啟動本機服務：{e}"))
}
