[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SetupPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{40C52ECA-8F30-4596-9B1B-C254017743DE}_is1'
foreach ($hive in @('HKCU:', 'HKLM:')) {
    if (Test-Path -LiteralPath ($hive + '\' + $uninstallKey)) {
        throw 'KAZUKO is already installed. Use a separate Windows test account for setup smoke tests.'
    }
}
$root = Join-Path (Split-Path -Parent $PSScriptRoot) ('artifacts/setup/smoke-' + [guid]::NewGuid().ToString('N'))
$appDir = Join-Path $root 'application'
$dataDir = Join-Path $root 'data'
New-Item -ItemType Directory -Force $dataDir | Out-Null
$marker = Join-Path $dataDir 'preserve-on-uninstall.txt'
Set-Content -LiteralPath $marker -Value 'independent data'
$installed = $false
$hostProcess = $null
try {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', ('/DIR="' + $appDir + '"'), ('/LOG="' + (Join-Path $root 'install.log') + '"'))
    $install = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($install.ExitCode -ne 0) { throw "Installation failed: $($install.ExitCode). See $root" }
    $installed = $true
    foreach ($relative in @('KAZUKO_ERP_JEWELRY.exe','coreclr.dll','hostfxr.dll','firebird/gbak.exe','firebird/plugins/engine13.dll','local-first-run.enabled')) {
        if (!(Test-Path -LiteralPath (Join-Path $appDir $relative))) { throw "Missing installed file: $relative" }
    }
    if (Test-Path -LiteralPath (Join-Path $appDir 'data/DATA.FDB')) { throw 'Setup shipped a business DB.' }
    $runtime = Get-Content -LiteralPath (Join-Path $appDir 'KAZUKO_ERP_JEWELRY.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (!$runtime.runtimeOptions.includedFrameworks) { throw 'App is not self-contained.' }
    Write-Output 'PASS: installed app includes .NET/Firebird and no business database.'
    # Host startup never connects to a DB until receiving an authenticated IPC command.
    $scope = [guid]::NewGuid().ToString('N') + '01234567'
    $exe = Join-Path $appDir 'KAZUKO_ERP_JEWELRY.exe'
    $hostProcess = Start-Process -FilePath $exe -ArgumentList @('--price-board-host', $scope) -WorkingDirectory $appDir -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 2
    if ($hostProcess.HasExited) { throw "Installed host exited during startup: $($hostProcess.ExitCode)" }
    Write-Output 'PASS: installed self-contained WPF executable starts independently.'
} finally {
    if ($hostProcess -and !$hostProcess.HasExited) {
        if ($hostProcess.MainModule.FileName -ne (Join-Path $appDir 'KAZUKO_ERP_JEWELRY.exe')) { throw 'Unexpected test host path; refusing to stop it.' }
        Stop-Process -Id $hostProcess.Id
        $hostProcess.WaitForExit()
    }
    if ($installed) {
        $uninstall = Start-Process -FilePath (Join-Path $appDir 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $root 'uninstall.log') + '"')) -WindowStyle Hidden -Wait -PassThru
        if ($uninstall.ExitCode -ne 0) { throw "Uninstall failed: $($uninstall.ExitCode)" }
        if (!(Test-Path -LiteralPath $marker)) { throw 'Uninstall removed independent data.' }
        if (Test-Path -LiteralPath (Join-Path $appDir 'KAZUKO_ERP_JEWELRY.exe')) { throw 'App executable remains after uninstall.' }
        Write-Output 'PASS: uninstall removes the app and preserves data outside its directory.'
    }
}
