$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project tests/Checks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    dotnet publish src/iLovbeeStudio.csproj -c Release -o dist --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    foreach ($resource in @('App.xbf', 'iLovbeeStudio.WinUI.pri')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot "dist\$resource") -PathType Leaf)) {
            throw "Published WinUI resource is missing: $resource"
        }
    }
    dotnet publish widget/iLovbeeWidget.csproj -c Release -o dist --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Widget publish failed.' }
    Copy-Item -LiteralPath docs -Destination dist -Recurse -Force
} finally { Pop-Location }
