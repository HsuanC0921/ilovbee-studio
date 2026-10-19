$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    # Native AOT 連結需要 vswhere；VS Installer 目錄不在 PATH 時補上。
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if ((Test-Path -LiteralPath $installer) -and ($env:PATH -notlike "*$installer*")) { $env:PATH += ";$installer" }
    dotnet run --project tests/Checks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    if (Test-Path -LiteralPath dist) { Remove-Item -LiteralPath dist -Recurse -Force }
    dotnet publish app/iLovbeeStudio.csproj -c Release -o dist --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Remove-Item -Path (Join-Path dist '*.pdb') -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath docs -Destination dist -Recurse -Force
} finally { Pop-Location }
