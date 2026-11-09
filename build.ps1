param([switch]$WithUi, [switch]$ReleaseChecks,
      [string]$PlaywrightCliPath = '.test-tools/node_modules/@playwright/cli/playwright-cli.js')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if ($ReleaseChecks -and -not $WithUi) { throw 'Release checks require -WithUi.' }
    if ($ReleaseChecks) {
        Get-ChildItem -LiteralPath 'app/web' -Filter '*.js' | ForEach-Object {
            node --check $_.FullName
            if ($LASTEXITCODE -ne 0) { throw ('JavaScript syntax failed: ' + $_.Name) }
        }
    }
    cargo fmt --all --check
    if ($LASTEXITCODE -ne 0) { throw 'Rust formatting failed.' }
    cargo test --locked --all-targets
    if ($LASTEXITCODE -ne 0) { throw 'Rust tests failed.' }
    cargo clippy --locked --all-targets -- -D warnings
    if ($LASTEXITCODE -ne 0) { throw 'Rust lint failed.' }
    cargo build --locked --release
    if ($LASTEXITCODE -ne 0) { throw 'Rust build failed.' }
    if ($WithUi) {
        cargo fmt --manifest-path ui/Cargo.toml --all --check
        if ($LASTEXITCODE -ne 0) { throw 'Tauri formatting failed.' }
        cargo test --manifest-path ui/Cargo.toml --locked
        if ($LASTEXITCODE -ne 0) { throw 'Tauri tests failed.' }
        cargo clippy --manifest-path ui/Cargo.toml --locked -- -D warnings
        if ($LASTEXITCODE -ne 0) { throw 'Tauri lint failed.' }
        cargo build --manifest-path ui/Cargo.toml --release --locked
        if ($LASTEXITCODE -ne 0) { throw 'Tauri shell build failed.' }
    }
    if ($ReleaseChecks) {
        $checks = 'target/release-checks'
        New-Item -ItemType Directory -Path $checks -Force | Out-Null
        python scripts/verify-release.py --binary target/release/iLovbeeStudio.exe --output "$checks/security-stress.json"
        if ($LASTEXITCODE -ne 0) { throw 'Security/stress release gate failed.' }
        python scripts/run-ui-checks.py --binary target/release/iLovbeeStudio.exe --cli-path $PlaywrightCliPath --output "$checks/ui.json"
        if ($LASTEXITCODE -ne 0) { throw 'Browser release gate failed.' }
        $metadata = @()
        foreach ($target in @('x86_64-pc-windows-msvc','x86_64-pc-windows-gnu')) {
            foreach ($manifest in @('Cargo.toml','ui/Cargo.toml')) {
                $prefix = if ($manifest -eq 'Cargo.toml') { 'host' } else { 'ui' }
                $file = "$checks/$prefix-$target.json"
                cargo metadata --manifest-path $manifest --locked --format-version 1 --filter-platform $target | Set-Content -LiteralPath $file -Encoding utf8
                if ($LASTEXITCODE -ne 0) { throw 'Dependency graph generation failed.' }
                $metadata += $file
            }
        }
        python scripts/audit-dependencies.py --metadata @metadata --output "$checks/dependencies.json"
        if ($LASTEXITCODE -ne 0) { throw 'Dependency advisory release gate failed.' }
    }
    # Package staging is reached only after every requested gate succeeds.
    New-Item -ItemType Directory -Path dist -Force | Out-Null
    Copy-Item -LiteralPath 'target/release/iLovbeeStudio.exe' -Destination 'dist/iLovbeeStudio.exe' -Force
    if ($WithUi) {
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
    if ($ReleaseChecks) {
        New-Item -ItemType Directory -Path 'dist/validation' -Force | Out-Null
        foreach ($name in @('security-stress.json','ui.json','dependencies.json')) {
            Copy-Item -LiteralPath "$checks/$name" -Destination 'dist/validation' -Force
        }
    }
} finally { Pop-Location }
