param(
  [Parameter(Mandatory)][string]$Name
)

$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root "mods\$Name"
$infoPath = Join-Path $source "Info.json"
if (-not (Test-Path $infoPath)) { throw "No asset pack at $source" }

$project = Join-Path $root "src\$Name"
if (Test-Path $project) {
  dotnet build $project -c Release --nologo -v quiet
  if ($LASTEXITCODE) { throw "Build failed, nothing packaged" }
}

& (Join-Path $PSScriptRoot "check-relay.ps1") -Path $source
if ($LASTEXITCODE) { throw "Relay check failed, nothing packaged" }

$info = Get-Content -Raw $infoPath | ConvertFrom-Json
if ($info.version -notmatch '^\d+\.\d{1,2}\.\d{1,2}[a-z]?$') { throw "Info.json version '$($info.version)' isn't major.minor.patch" }
foreach ($file in "README.md", "LICENSE") {
  if (-not (Test-Path (Join-Path $source $file))) { throw "Missing $file in $source" }
}
if ((Test-Path (Join-Path $source "0Harmony.dll")) -and -not (Test-Path (Join-Path $source "THIRD-PARTY-NOTICES.md"))) { throw "0Harmony.dll ships without THIRD-PARTY-NOTICES.md" }
foreach ($assembly in @($info.assemblies | Where-Object { $_ })) {
  $dll = Join-Path $source $assembly
  if (-not (Test-Path $dll)) { throw "Missing $assembly listed in Info.json" }
  $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
  if ($fileVersion -ne "$($info.version).0") { throw "$assembly is version $fileVersion, Info.json says $($info.version)" }
}

$folder = "$($info.identifier.Replace('/', '-'))-$($info.version)"
$dist = Join-Path $root "dist"
$staging = Join-Path $dist $folder
$zip = Join-Path $dist "$folder.zip"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null
Copy-Item (Join-Path $source "*") $staging -Recurse -Force
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $staging -DestinationPath $zip
Remove-Item $staging -Recurse -Force

Write-Host "Packaged $($info.name) $($info.version) to $zip"
(Get-Item $zip).Length / 1KB | ForEach-Object { Write-Host ("{0:N0} KB" -f $_) }
