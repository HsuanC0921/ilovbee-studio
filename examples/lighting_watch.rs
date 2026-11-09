//! Read-only monitor for correlating physical Fn shortcuts with lighting fields.
use anyhow::{ensure, Context, Result};
use ilovbee_studio::{service::Service, store::Store};
use std::{
    fs,
    io::Write,
    path::PathBuf,
    thread,
    time::{Duration, Instant},
};
fn main() -> Result<()> {
    let root = PathBuf::from(
        std::env::args()
            .nth(1)
            .context("output directory required")?,
    );
    let mut service = Service::new(Store::new(root.clone())?, false);
    ensure!(service.scan()?, "B87 not found");
    let mut before = service.read_lights()?;
    fs::write(root.join("before.hex"), hex::encode_upper(&before))?;
    let mut log = fs::File::create(root.join("changes.jsonl"))?;
    println!(
        "READY physical lighting monitor; mode {} switch {}",
        before[10], before[26]
    );
    let start = Instant::now();
    while start.elapsed() < Duration::from_secs(150) && !root.join("stop").exists() {
        let after = service.read_lights()?;
        if after != before {
            let changes: Vec<_> = before
                .iter()
                .zip(&after)
                .enumerate()
                .filter(|(_, (a, b))| a != b)
                .map(|(offset, (&a, &b))| serde_json::json!({"offset":offset,"before":a,"after":b}))
                .collect();
            let entry = serde_json::json!({"ms":start.elapsed().as_millis(),"changes":changes,"payload":hex::encode_upper(&after)});
            writeln!(log, "{entry}")?;
            log.flush()?;
            println!("{entry}");
            before = after;
        }
        thread::sleep(Duration::from_millis(300));
    }
    fs::write(root.join("after.hex"), hex::encode_upper(&before))?;
    Ok(())
}
