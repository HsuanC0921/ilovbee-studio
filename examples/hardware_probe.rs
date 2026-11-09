use anyhow::{ensure, Context, Result};
use ilovbee_studio::{service::Service, store::Store};
use std::{fs, path::PathBuf};

fn main() -> Result<()> {
    let root = PathBuf::from(
        std::env::args()
            .nth(1)
            .context("output directory required")?,
    );
    let mut service = Service::new(Store::new(root.clone())?, false);
    ensure!(service.scan()?, "Compatible B87 not found");
    let profile = service.capture()?;
    profile.validate()?;
    let path = root.join("initial-profile.json");
    ensure!(
        !path.exists(),
        "Initial snapshot already exists; refusing to overwrite"
    );
    fs::write(&path, serde_json::to_string_pretty(&profile)?)?;
    println!(
        "PASS real B87 capture: {} layers, {} lighting bytes; snapshot {}",
        profile.keymaps.len(),
        profile.lighting.len() / 2,
        path.display()
    );
    println!("Lighting mode {}", hex::decode(&profile.lighting)?[10]);
    #[cfg(windows)]
    println!(
        "Windows battery: {}",
        serde_json::to_string(&ilovbee_studio::windows::battery()?)?
    );
    Ok(())
}
