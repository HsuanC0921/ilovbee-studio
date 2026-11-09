param([switch]$WithUi)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    cargo fmt --all --check
    if ($LASTEXITCODE -ne 0) { throw 'Rust formatting failed.' }
    cargo test --locked --all-targets
    if ($LASTEXITCODE -ne 0) { throw 'Rust tests failed.' }
    cargo clippy --locked --all-targets -- -D warnings
    if ($LASTEXITCODE -ne 0) { throw 'Rust lint failed.' }
    cargo build --locked --release
    if ($LASTEXITCODE -ne 0) { throw 'Rust build failed.' }
    New-Item -ItemType Directory -Path dist -Force | Out-Null
    Copy-Item -LiteralPath 'target/release/iLovbeeStudio.exe' -Destination 'dist/iLovbeeStudio.exe' -Force
    if ($WithUi) {
        cargo build --manifest-path ui/Cargo.toml --release --locked
        if ($LASTEXITCODE -ne 0) { throw 'Tauri shell build failed.' }
        Copy-Item -LiteralPath 'ui/target/release/ilovbee-ui.exe' -Destination 'dist/iLovbeeUI.exe' -Force
        # GNU builds dynamically load the WebView2 loader; MSVC may link it statically.
        $loader = 'ui/target/release/WebView2Loader.dll'
        if (Test-Path -LiteralPath $loader) {
            Copy-Item -LiteralPath $loader -Destination 'dist/WebView2Loader.dll' -Force
        }
    }
    Copy-Item -LiteralPath docs -Destination dist -Recurse -Force
    Copy-Item -LiteralPath 'README.zh-TW.md' -Destination dist -Force
    Copy-Item -LiteralPath LICENSE -Destination dist -Force
} finally { Pop-Location }
