$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project tests/Checks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    dotnet publish src/iLovbeeStudio.csproj -c Release -o dist --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    dotnet publish widget/iLovbeeWidget.csproj -c Release -o dist --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Widget publish failed.' }
    Copy-Item -LiteralPath docs -Destination dist -Recurse -Force
} finally { Pop-Location }
