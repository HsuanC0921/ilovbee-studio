use crate::{model::PhysicalKey, protocol::FRAME_BYTES};
use std::f64::consts::PI;
pub const EFFECTS: [&str; 4] = ["恆亮", "閃爍", "呼吸", "彩虹"];
pub const STYLES: [&str; 12] = [
    "頻譜柱",
    "鏡像頻譜",
    "瀑布",
    "低頻脈衝",
    "漣漪",
    "能量彩虹",
    "音量計",
    "峰值保持",
    "星空",
    "火焰",
    "對角掃描",
    "橫向電平",
];
pub const BANDS: usize = 18;
fn byte(v: f64) -> u8 {
    v.round().clamp(0., 255.) as u8
}
pub fn hsv(h: f64, s: f64, v: f64) -> [u8; 3] {
    let h = h.rem_euclid(360.) / 60.;
    let c = v * s;
    let x = c * (1. - (h % 2. - 1.).abs());
    let m = v - c;
    let (r, g, b) = match h as u32 {
        0 => (c, x, 0.),
        1 => (x, c, 0.),
        2 => (0., c, x),
        3 => (0., x, c),
        4 => (x, 0., c),
        _ => (c, 0., x),
    };
    [
        byte((r + m) * 255.),
        byte((g + m) * 255.),
        byte((b + m) * 255.),
    ]
}
pub fn animate(frame: &[u8], fx: &[u8], t: f64, speed: usize) -> Vec<u8> {
    let mut f = frame.to_vec();
    let w = t * [0., 0.4, 0.7, 1.1, 1.6][speed.clamp(1, 4)];
    let breath = 0.5 - 0.5 * (2. * PI * w).cos();
    let rainbow = hsv(w * 120., 1., 1.);
    for (slot, &effect) in fx.iter().take(126).enumerate() {
        let rgb = &mut f[slot * 3..slot * 3 + 3];
        match effect {
            1 if w.fract() >= 0.5 => rgb.fill(0),
            2 => rgb.iter_mut().for_each(|c| *c = byte(*c as f64 * breath)),
            3 => {
                let v = *rgb.iter().max().unwrap() as f64 / 255.;
                for i in 0..3 {
                    rgb[i] = byte(rainbow[i] as f64 * v);
                }
            }
            _ => (),
        }
    }
    f
}
#[derive(Clone)]
pub struct Palette {
    pub mode: usize,
    pub stops: Vec<[u8; 3]>,
    pub direction: usize,
}
impl Default for Palette {
    fn default() -> Self {
        Self {
            mode: 0,
            stops: vec![[255, 64, 0]],
            direction: 0,
        }
    }
}
impl Palette {
    pub fn pick(&self, hue: f64, sat: f64, v: f64, pos: f64) -> [u8; 3] {
        if self.mode == 0 {
            return hsv(hue, sat, v);
        }
        let n = self.stops.len();
        let f = pos.clamp(0., 1.) * n.saturating_sub(1) as f64;
        let i = (f as usize).min(n.saturating_sub(2));
        let t = f - i as f64;
        std::array::from_fn(|c| {
            let a = self.stops[i][c] as f64;
            let b = self.stops[(i + 1).min(n - 1)][c] as f64;
            let gradient = byte(a + (b - a) * t) as f64;
            byte(byte(255. + (gradient - 255.) * sat) as f64 * v.clamp(0., 1.))
        })
    }
}
fn hash(n: u32) -> f64 {
    let mut x = n.wrapping_mul(2654435761);
    x ^= x >> 15;
    x = x.wrapping_mul(2246822519);
    x ^= x >> 13;
    (x & 0xffffff) as f64 / 0x1000000 as f64
}
fn pingpong(x: f64) -> f64 {
    let x = x.rem_euclid(2.);
    if x > 1. {
        2. - x
    } else {
        x
    }
}
pub struct Visualizer {
    keys: Vec<PhysicalKey>,
    cells: Vec<(usize, usize, usize)>,
    history: [[f64; BANDS]; 6],
    ripples: Vec<(f64, f64)>,
    sweeps: Vec<(f64, f64)>,
    holds: [f64; BANDS],
    last_beat: f64,
    bass: f64,
    wf: f64,
    peak: f64,
}
impl Visualizer {
    pub fn new(keys: Vec<PhysicalKey>) -> Self {
        let col = |x: f64| ((x - 21.) / 41.2).floor().clamp(0., 17.) as usize;
        let cells = keys
            .iter()
            .map(|k| {
                let (a, b) = if k.width > 45. {
                    (col(k.x + 8.), col(k.x + k.width - 8.))
                } else {
                    let c = col(k.x + k.width / 2.);
                    (c, c)
                };
                (a, b, ((k.y - 38.) / 43.).round().clamp(0., 5.) as usize)
            })
            .collect();
        Self {
            keys,
            cells,
            history: [[0.; BANDS]; 6],
            ripples: vec![],
            sweeps: vec![],
            holds: [0.; BANDS],
            last_beat: 0.,
            bass: 0.,
            wf: 0.,
            peak: 0.,
        }
    }
    pub fn render(&mut self, style: usize, raw: &[f64; BANDS], t: f64, color: &Palette) -> Vec<u8> {
        let mut f = vec![0; FRAME_BYTES];
        self.peak = raw.iter().copied().fold(self.peak * 0.997, f64::max);
        let norm = self.peak.max(0.25);
        let bands: [f64; BANDS] = std::array::from_fn(|i| (raw[i] / norm).clamp(0., 1.).powi(2));
        let low = (bands[0] + bands[1] + bands[2]) / 3.;
        self.bass = low.max(self.bass * 0.9);
        let beat = low > 0.35 && low > self.bass * 0.92 && t - self.last_beat > 0.18;
        if beat {
            self.last_beat = t;
        }
        if style == 2 && t - self.wf > 0.07 {
            self.wf = t;
            self.history.rotate_right(1);
            self.history[0] = bands;
        }
        if style == 4 && beat {
            self.ripples.push((t, t * 40.));
            if self.ripples.len() > 4 {
                self.ripples.remove(0);
            }
        }
        if style == 10 && beat {
            self.sweeps.push((t, t * 50.));
            if self.sweeps.len() > 3 {
                self.sweeps.remove(0);
            }
        }
        self.ripples.retain(|r| t - r.0 <= 1.6);
        self.sweeps.retain(|r| t - r.0 <= 1.5);
        for (hold, &band) in self.holds.iter_mut().zip(&bands) {
            *hold = band.max(*hold * 0.97);
        }
        let energy = bands.iter().sum::<f64>() / BANDS as f64;
        for (ki, k) in self.keys.iter().enumerate() {
            if !(0..126).contains(&k.slot) {
                continue;
            }
            let (c0, c1, row) = self.cells[ki];
            let band = (c0 + c1) / 2;
            let up = (5 - row) as f64;
            let (cx, cy) = (k.x + k.width / 2., k.y + k.height / 2.);
            let span = |a: &[f64; BANDS]| a[c0..=c1].iter().copied().fold(0., f64::max);
            let mut sat = 1.;
            let (hue, v) = match style {
                0 => (t * 6. + band as f64 * 270. / 18., span(&bands) * 6. - up),
                1 => {
                    let m = (((c0 + c1) as f64 / 2. - 8.5).abs().floor() as usize).min(8);
                    (t * 6. + m as f64 * 270. / 18., bands[m * 2] * 6. - up)
                }
                2 => (
                    t * 10. + band as f64 * 270. / 18.,
                    span(&self.history[row]) * 1.4,
                ),
                3 => (
                    t * 15. + row as f64 * 18.,
                    (self.bass * 1.5).min(1.) * (1. - 0.12 * row as f64),
                ),
                4 => {
                    let (mut hue, mut v) = (0., 0.);
                    for r in &self.ripples {
                        let age = t - r.0;
                        let dist = ((cx - 420.).powi(2) + (cy - 150.).powi(2)).sqrt();
                        let w = (dist - age * 420.).abs();
                        if w < 60. {
                            let a = (1. - w / 60.) * (1. - age / 1.6);
                            if a > v {
                                (hue, v) = (r.1, a);
                            }
                        }
                    }
                    (hue, v)
                }
                5 => (
                    t * 30. + cx / 760. * 240. - energy * 60.,
                    0.12 + (energy * 2.4).min(1.) * (0.5 + 0.5 * span(&bands)),
                ),
                6 => (row as f64 * 24., (energy * 3.).min(1.) * 6. - up),
                7 => {
                    let mut v = span(&bands) * 6. - up;
                    let top = 5 - (span(&self.holds) * 6.).min(5.) as usize;
                    if row == top {
                        v = 1.;
                        sat = 0.;
                    }
                    (t * 6. + band as f64 * 270. / 18., v)
                }
                8 => {
                    let slot = k.slot as u32;
                    let tw = (t * (1.2 + 3. * hash(slot)) + hash(slot + 9) * std::f64::consts::TAU)
                        .sin()
                        .max(0.)
                        .powi(8);
                    (hash(slot + 31) * 360., tw * (0.25 + energy * 3.).min(1.))
                }
                9 => {
                    let b = span(&bands);
                    let flick = hash((t * 14.) as u32 * 131 + band as u32 * 7) * 0.9;
                    ((42. - 9. * up).max(0.), b * 6. + flick * b * 2. - up)
                }
                10 => {
                    let (mut hue, mut v) = (0., 0.);
                    for r in &self.sweeps {
                        let age = t - r.0;
                        let d = (cx + cy * 1.2 - age * 620. + 80.).abs();
                        if d < 110. {
                            let a = (1. - d / 110.) * (1. - age / 1.5);
                            if a > v {
                                (hue, v) = (r.1, a);
                            }
                        }
                    }
                    (hue, v)
                }
                _ => {
                    let g = (up as usize * 18 / 6).min(17);
                    let level = bands[g..(g + 3).min(18)].iter().copied().fold(0., f64::max);
                    (
                        up * 55.,
                        if (cx - 21.) / (41.2 * 18.) < level {
                            1.
                        } else {
                            0.
                        },
                    )
                }
            };
            if v <= 0.02 {
                continue;
            }
            let v = v.clamp(0., 1.);
            let colpos = ((cx - 21.) / (41.2 * 17.)).clamp(0., 1.);
            let pos = match color.direction {
                0 => up / 5.,
                1 => colpos,
                2 => pingpong(t * 0.12 + colpos * 0.5),
                _ => v,
            };
            let rgb = color.pick(hue, sat, v, pos);
            let offset = k.slot as usize * 3;
            f[offset..offset + 3].copy_from_slice(&rgb);
        }
        f
    }
}

/// Goertzel magnitude over a Hann window, matching the existing 60 Hz–12 kHz bands.
pub fn spectrum(samples: &[f64], sample_rate: f64, gain: f64) -> [f64; BANDS] {
    std::array::from_fn(|b| {
        let frequency = 60. * 200_f64.powf((b as f64 + 0.5) / BANDS as f64);
        let k = 2. * (2. * PI * frequency / sample_rate).cos();
        let (mut s1, mut s2) = (0., 0.);
        for (i, &sample) in samples.iter().enumerate() {
            let hann = 0.5 - 0.5 * (2. * PI * i as f64 / (samples.len() - 1) as f64).cos();
            let s = sample * hann + k * s1 - s2;
            s2 = s1;
            s1 = s;
        }
        let power = (s1 * s1 + s2 * s2 - k * s1 * s2).max(0.).sqrt() / (samples.len() as f64 / 4.);
        (power * gain * (1. + 2. * b as f64 / BANDS as f64))
            .sqrt()
            .clamp(0., 1.)
    })
}
