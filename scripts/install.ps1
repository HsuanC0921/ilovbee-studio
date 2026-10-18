$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'dist'
$mainExeName = 'iLovbeeStudio.WinUI.exe'
$widgetExeName = 'iLovbeeWidget.exe'
$mainExe = Join-Path $source $mainExeName
$widgetExe = Join-Path $source $widgetExeName
if (-not (Test-Path -LiteralPath $mainExe -PathType Leaf) -or -not (Test-Path -LiteralPath $widgetExe -PathType Leaf)) {
    throw '發佈檔不完整。請先執行 build.ps1，再執行安裝。'
}

$running = Get-Process -Name 'iLovbeeStudio.WinUI', 'iLovbeeWidget' -ErrorAction SilentlyContinue
if ($running) { throw '請先關閉 iLovbee Studio 與系統匣小工具，再重新安裝。' }

$programs = Join-Path $env:LOCALAPPDATA 'Programs'
$installRoot = Join-Path $programs 'iLovbee Studio'
$appRoot = Join-Path $installRoot 'app'
$markerName = '.ilovbee-studio-install.json'
$productId = 'HsuanC0921.iLovbeeStudio'
$stagingRoot = Join-Path $programs ('iLovbee Studio.installing-' + [guid]::NewGuid().ToString('N'))
$oldRoot = Join-Path $programs ('iLovbee Studio.previous-' + [guid]::NewGuid().ToString('N'))
$version = '0.3.4'
$projectFile = Join-Path $projectRoot 'src\iLovbeeStudio.csproj'
if (Test-Path -LiteralPath $projectFile) {
    $match = [regex]::Match((Get-Content -LiteralPath $projectFile -Raw), '<Version>([^<]+)</Version>')
    if ($match.Success) { $version = $match.Groups[1].Value }
}

New-Item -ItemType Directory -Path $programs -Force | Out-Null
$movedOld = $false
try {
    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
    $stagingApp = Join-Path $stagingRoot 'app'
    New-Item -ItemType Directory -Path $stagingApp | Out-Null
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $stagingApp -Recurse
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $stagingRoot
    $marker = [ordered]@{ ProductId = $productId; Version = $version; InstalledAt = [DateTimeOffset]::Now.ToString('O') }
    Set-Content -LiteralPath (Join-Path $stagingRoot $markerName) -Value ($marker | ConvertTo-Json) -Encoding utf8

    if (Test-Path -LiteralPath $installRoot) {
        $existingMarker = Join-Path $installRoot $markerName
        if (-not (Test-Path -LiteralPath $existingMarker -PathType Leaf) -or (Get-Content -LiteralPath $existingMarker -Raw | ConvertFrom-Json).ProductId -ne $productId) {
            throw "目標資料夾已存在且不屬於 iLovbee Studio：$installRoot"
        }
        Move-Item -LiteralPath $installRoot -Destination $oldRoot
        $movedOld = $true
    }
    Move-Item -LiteralPath $stagingRoot -Destination $installRoot
    $stagingRoot = $null

    $shortcutFolder = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\iLovbee Studio'
    New-Item -ItemType Directory -Path $shortcutFolder -Force | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    foreach ($shortcut in @(
        @{ Name = 'iLovbee Studio'; Target = (Join-Path $appRoot $mainExeName); Description = '設定與管理 iLovbee B87 鍵盤' },
        @{ Name = 'iLovbee 電量小工具'; Target = (Join-Path $appRoot $widgetExeName); Description = '在系統匣查看 B87 電量' }
    )) {
        $link = $shell.CreateShortcut((Join-Path $shortcutFolder ($shortcut.Name + '.lnk')))
        $link.TargetPath = $shortcut.Target
        $link.WorkingDirectory = $appRoot
        $link.IconLocation = $shortcut.Target + ',0'
        $link.Description = $shortcut.Description
        $link.Save()
    }

    $uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$productId"
    $sizeKb = [int]([math]::Ceiling(((Get-ChildItem -LiteralPath $appRoot -File -Recurse | Measure-Object -Property Length -Sum).Sum) / 1KB))
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $uninstaller = Join-Path $installRoot 'uninstall.ps1'
    New-Item -Path $uninstallKey -Force | Out-Null
    Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayName -Value 'iLovbee Studio'
    Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion -Value $version
    Set-ItemProperty -LiteralPath $uninstallKey -Name Publisher -Value 'HsuanC0921'
    Set-ItemProperty -LiteralPath $uninstallKey -Name InstallLocation -Value $installRoot
    Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayIcon -Value (Join-Path $appRoot $mainExeName)
    Set-ItemProperty -LiteralPath $uninstallKey -Name UninstallString -Value ('"{0}" -NoProfile -ExecutionPolicy Bypass -File "{1}"' -f $powershell, $uninstaller)
    New-ItemProperty -LiteralPath $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name EstimatedSize -Value $sizeKb -PropertyType DWord -Force | Out-Null

    if ($movedOld) {
        $oldMarkerPath = Join-Path $oldRoot $markerName
        if ((Test-Path -LiteralPath $oldMarkerPath -PathType Leaf) -and (Get-Content -LiteralPath $oldMarkerPath -Raw | ConvertFrom-Json).ProductId -eq $productId) {
            Remove-Item -LiteralPath $oldRoot -Recurse -Force
        }
    }
    Write-Output "已安裝 iLovbee Studio $version：$installRoot"
    Write-Output '開始功能表已新增「iLovbee Studio」與「iLovbee 電量小工具」。'
} catch {
    if ($movedOld -and -not (Test-Path -LiteralPath $installRoot) -and (Test-Path -LiteralPath $oldRoot)) {
        Move-Item -LiteralPath $oldRoot -Destination $installRoot
    }
    throw
} finally {
    if ($stagingRoot -and (Test-Path -LiteralPath $stagingRoot)) {
        $resolvedPrograms = [IO.Path]::GetFullPath($programs).TrimEnd('\') + '\'
        $resolvedStaging = [IO.Path]::GetFullPath($stagingRoot)
        if ($resolvedStaging.StartsWith($resolvedPrograms, [StringComparison]::OrdinalIgnoreCase) -and $resolvedStaging -like '*iLovbee Studio.installing-*') {
            Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
        }
    }
}
