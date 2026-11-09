//! Read-only, operator-paced snapshots. Continuous HID polling caused keyboard lag
//! and repeating input on the real B87; never poll this interface in the background.
use anyhow::{ensure, Context, Result};
use ilovbee_studio::{service::Service, store::Store};
use std::{
    fs,
    io::{self, BufRead, Write},
    path::PathBuf,
};
fn main() -> Result<()> {
    let root = PathBuf::from(
        std::env::args()
            .nth(1)
            .context("output directory required")?,
    );
    ensure!(!root.exists(), "output directory already exists");
    let mut service = Service::new(Store::new(root.clone())?, false);
    ensure!(service.scan()?, "B87 not found");
    let mut before = service.read_lights()?;
    fs::write(root.join("before.hex"), hex::encode_upper(&before))?;
    let mut log = fs::File::create(root.join("changes.jsonl"))?;
    println!("READY. Perform one physical shortcut, then enter a label to capture once. Enter quit to exit. No background polling.");
    for line in io::stdin().lock().lines() {
        let label = line?;
        if label.trim() == "quit" {
            break;
        }
        let after = service.read_lights()?;
        if after != before {
            let changes: Vec<_> = before
                .iter()
                .zip(&after)
                .enumerate()
                .filter(|(_, (a, b))| a != b)
                .map(|(offset, (&a, &b))| serde_json::json!({"offset":offset,"before":a,"after":b}))
                .collect();
            let entry = serde_json::json!({"label":label,"at":chrono::Utc::now().to_rfc3339(),"changes":changes,"payload":hex::encode_upper(&after)});
            writeln!(log, "{entry}")?;
            log.flush()?;
            println!("{entry}");
            before = after;
        } else {
            println!("No change in the readable lighting payload.");
        }
    }
    fs::write(root.join("after.hex"), hex::encode_upper(&before))?;
    Ok(())
}
