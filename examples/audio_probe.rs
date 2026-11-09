//! Read-only WASAPI diagnostic. Emits levels, never records audio samples.
use anyhow::Result;
use ilovbee_studio::{audio::Audio, lighting};
use std::{
    thread,
    time::{Duration, Instant},
};
fn main() -> Result<()> {
    let mut audio = Audio::new()?;
    let start = Instant::now();
    while start.elapsed() < Duration::from_secs(8) {
        let samples = audio.samples()?;
        let rms = (samples.iter().map(|x| x * x).sum::<f64>() / samples.len() as f64).sqrt();
        let bands = lighting::spectrum(&samples, 48000., 40.);
        let peak = bands
            .iter()
            .enumerate()
            .max_by(|a, b| a.1.total_cmp(b.1))
            .unwrap();
        println!(
            "{}",
            serde_json::json!({"ms":start.elapsed().as_millis(),"rms":rms,"peak_band":peak.0,"peak_level":peak.1})
        );
        thread::sleep(Duration::from_millis(50));
    }
    Ok(())
}
