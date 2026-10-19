$ErrorActionPreference = 'Stop'
$productId = 'HsuanC0921.iLovbeeStudio'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\iLovbee Studio'
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\iLovbee Studio'))
$resolvedRoot = [IO.Path]::GetFullPath($installRoot)
if (-not $resolvedRoot.Equals($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw '解除安裝路徑驗證失敗。' }
$markerPath = Join-Path $installRoot '.ilovbee-studio-install.json'
if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf) -or (Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json).ProductId -ne $productId) {
    throw '安裝識別檔不存在，為保護其他檔案，已停止解除安裝。'
}
if (Get-Process -Name 'iLovbeeStudio', 'iLovbeeStudio.WinUI', 'iLovbeeWidget' -ErrorAction SilentlyContinue) {
    throw '請先從系統匣結束 iLovbee Studio，再解除安裝。'
}

$shortcutFolder = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\iLovbee Studio'
if (Test-Path -LiteralPath $shortcutFolder) { Remove-Item -LiteralPath $shortcutFolder -Recurse -Force }
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$productId"
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
foreach ($n in 'iLovbeeStudio', 'iLovbeeWidget') { Remove-ItemProperty -LiteralPath $run -Name $n -ErrorAction SilentlyContinue }
if (Test-Path -LiteralPath $uninstallKey) { Remove-Item -LiteralPath $uninstallKey -Recurse -Force }
Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
Write-Output 'iLovbee Studio 已解除安裝。使用者設定與鍵盤備份保留在本機。'
