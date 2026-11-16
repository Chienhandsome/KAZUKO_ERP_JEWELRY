[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$Version = '1.0.3',
    [string]$IsccPath,
    [string]$WebView2Installer,
    [ValidatePattern('^8\.0\.\d+$')][string]$RuntimeVersion,
    [switch]$BootstrapCompiler
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$installerRoot = Join-Path $projectRoot 'installer'
$buildRoot = Join-Path $projectRoot ('artifacts/setup/' + $Version + '/' + [guid]::NewGuid().ToString('N'))
$publishRoot = Join-Path $buildRoot 'publish'
$outputRoot = Join-Path $projectRoot 'artifacts/setup/output'
New-Item -ItemType Directory -Force $publishRoot, $outputRoot | Out-Null

function Assert-Signature([string]$Path, [string]$PublisherPattern) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch $PublisherPattern) {
        throw "Invalid installer signature: $Path"
    }
}
function Get-VerifiedDownload([string]$Url, [string]$Destination, [string]$PublisherPattern) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Write-Host "Downloading prerequisite: $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing -TimeoutSec 120
    Assert-Signature $Destination $PublisherPattern
}
if (!$IsccPath) {
    $candidates = @(
        (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Inno Setup 6/ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'),
        (Join-Path $projectRoot 'artifacts/setup/tools/InnoSetup/ISCC.exe')
    )
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (!$IsccPath -and $BootstrapCompiler) {
    $compilerRoot = Join-Path $projectRoot 'artifacts/setup/tools/InnoSetup'
    $compilerSetup = Join-Path $buildRoot 'innosetup.exe'
    Get-VerifiedDownload 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe' $compilerSetup 'Pyrsys B\.V\.'
    if ((Get-FileHash -LiteralPath $compilerSetup -Algorithm SHA256).Hash -ne 'A549929319F9120716677C685535CA4671E4B937B5FDC87E2585F32D8EB30E8C') {
        throw 'Inno Setup 6.7.4 does not match the official release SHA256.'
    }
    $process = Start-Process -FilePath $compilerSetup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/NOICONS', ('/DIR="' + $compilerRoot + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed: $($process.ExitCode)" }
    $IsccPath = Join-Path $compilerRoot 'ISCC.exe'
}
if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'Install Inno Setup 6 (https://jrsoftware.org/isdl.php), use -IsccPath, or run with -BootstrapCompiler.'
}
$IsccPath = (Resolve-Path -LiteralPath $IsccPath).Path
$webViewPath = Join-Path $buildRoot 'WebView2Setup.exe'
if ($WebView2Installer) {
    Copy-Item -LiteralPath $WebView2Installer -Destination $webViewPath
    Assert-Signature $webViewPath 'Microsoft Corporation'
} else {
    Get-VerifiedDownload 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' $webViewPath 'Microsoft Corporation'
}
if (!$RuntimeVersion) {
    $releaseMetadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json' -TimeoutSec 60
    $RuntimeVersion = $releaseMetadata.'latest-runtime'
    if ($RuntimeVersion -notmatch '^8\.0\.\d+$') { throw 'Cannot resolve .NET 8 runtime. Supply -RuntimeVersion explicitly.' }
}
Write-Host "Publishing with .NET runtime $RuntimeVersion"
Push-Location $projectRoot
try {
    # A fresh staging directory prevents stale files and workspace DB inclusion.
    $publishArgs = @('publish', 'src/Kazuko.App/Kazuko.App.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', "-p:RuntimeFrameworkVersion=$RuntimeVersion", '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:DebugType=None', '-p:DebugSymbols=false', "-p:Version=$Version", '-o', $publishRoot, '-m:1', '-nr:false')
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
} finally { Pop-Location }
foreach ($required in @('KAZUKO_ERP_JEWELRY.exe', 'coreclr.dll', 'hostfxr.dll',
    'PresentationFramework.dll', 'firebird/fbclient.dll', 'firebird/plugins/engine13.dll',
    'firebird/gbak.exe', 'firebird/intl/fbintl.dll', 'firebird/vcruntime140.dll', 'firebird/msvcp140.dll',
    'runtimes/win-x64/native/WebView2Loader.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $publishRoot $required))) { throw "Missing runtime file: $required" }
}
$firebirdConfig = Get-Content -LiteralPath (Join-Path $publishRoot 'firebird/firebird.conf') -Raw
if ($firebirdConfig -notmatch '(?im)^\s*ServerMode\s*=\s*SuperClassic\s*$') { throw 'Published Firebird must use SuperClassic for ERP + TV.' }
Get-ChildItem -LiteralPath $publishRoot -File -Recurse | ForEach-Object {
    $relative = $_.FullName.Substring($publishRoot.Length + 1)
    # Supplied Firebird security/example databases are part of the runtime bundle.
    if ($relative -notlike 'firebird\*' -and ($_.Extension -in '.fdb', '.fbk' -or $_.Name -eq 'connection.json')) {
        throw "Business database/profile must not be shipped: $relative"
    }
}
$latestMigration = Get-ChildItem (Join-Path $projectRoot 'database/migrations') -File | Where-Object Name -Match '^\d{4}__[^.]+\.sql$' | ForEach-Object { [int]$_.Name.Substring(0,4) } | Measure-Object -Maximum
if ($latestMigration.Maximum -ne 93) { throw 'Refresh the clean first-run schema baseline before publishing a newer schema.' }
Set-Content -LiteralPath (Join-Path $publishRoot 'local-first-run.enabled') -Value '93' -Encoding ASCII
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/installation.md') -Destination (Join-Path $publishRoot 'INSTALLATION.md')
$manifest = Get-ChildItem -LiteralPath $publishRoot -File -Recurse | ForEach-Object {
    [pscustomobject]@{ Path = $_.FullName.Substring($publishRoot.Length + 1); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $buildRoot 'payload-manifest.json') -Encoding UTF8
& $IsccPath "/DAppVersion=$Version" "/DPublishDir=$publishRoot" "/DWebViewInstaller=$webViewPath" "/DOutputDir=$outputRoot" (Join-Path $installerRoot 'Kazuko.iss')
if ($LASTEXITCODE -ne 0) { throw "Setup compilation failed: $LASTEXITCODE" }
$setupPath = Join-Path $outputRoot "KazukoERP-Setup-$Version-win-x64.exe"
if (!(Test-Path -LiteralPath $setupPath)) { throw 'Compiler did not produce the expected setup file.' }
$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash
"$hash  $([IO.Path]::GetFileName($setupPath))" | Set-Content -LiteralPath ($setupPath + '.sha256') -Encoding ASCII
Write-Host "Setup: $setupPath"
Write-Host "SHA256: $hash"
