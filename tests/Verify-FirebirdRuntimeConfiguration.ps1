$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $projectRoot 'artifacts/firebird-config-check'
$vendorRoot = Join-Path $fixtureRoot 'vendor'
$outputPath = Join-Path $fixtureRoot 'generated/firebird.conf'
New-Item -ItemType Directory -Force $vendorRoot | Out-Null
# A marker is sufficient: this test only invokes the configuration build target.
Set-Content -LiteralPath (Join-Path $vendorRoot 'fbclient.dll') -Value 'fixture'
$vendorText = @'
# Vendor's comments, semicolons; percent %3B and $(literal) must survive.
DatabaseAccess = Restrict D:\Data;D:\Mirror
#ServerMode = Super
ServerMode = Super
servermode = Classic
'@
$vendorPath = Join-Path $vendorRoot 'firebird.conf'
[IO.File]::WriteAllText($vendorPath, $vendorText)
& dotnet msbuild (Join-Path $projectRoot 'src/Kazuko.App/Kazuko.App.csproj') `
    -nologo -v:minimal -t:PrepareSharedEmbeddedFirebirdConfiguration `
    "-p:FirebirdHome=$vendorRoot" "-p:FirebirdConfigurationOutput=$outputPath"
if ($LASTEXITCODE -ne 0) { throw 'Runtime configuration generation failed.' }
$actual = [IO.File]::ReadAllText($outputPath)
$settings = [regex]::Matches($actual, '(?im)^\s*ServerMode\s*=\s*([^\r\n]+)')
if ($settings.Count -ne 1 -or $settings[0].Groups[1].Value -ne 'SuperClassic') {
    throw 'Generated runtime must contain exactly one active ServerMode = SuperClassic.'
}
$expectedPrefix = [regex]::Replace($vendorText, '(?im)^[ \t]*ServerMode[ \t]*=[^\r\n]*', '')
if (!$actual.StartsWith($expectedPrefix.TrimEnd())) { throw 'Other vendor settings were changed.' }
if ([IO.File]::ReadAllText($vendorPath) -cne $vendorText) { throw 'Original vendor file was changed.' }
Write-Output 'PASS: shared embedded mode replaces active settings; other vendor settings and original file are preserved.'
