param(
  [Parameter(Mandatory)][string]$Name,
  [switch]$Mods
)

$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root "mods\$Name"
if (-not (Test-Path (Join-Path $source "Info.json"))) { throw "No asset pack at $source" }

$project = Join-Path $root "src\$Name"
if (Test-Path $project) {
  dotnet build $project -c Release --nologo -v quiet
  if ($LASTEXITCODE) { throw "Build failed, nothing deployed" }
}

& (Join-Path $PSScriptRoot "check-relay.ps1") -Path $source
if ($LASTEXITCODE) { throw "Relay check failed, nothing deployed" }

$folder = if ($Mods) { "Mods" } else { "ModDev" }
$target = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Railroader\$folder\$Name"
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $source "*") $target -Recurse -Force
Write-Host "Deployed $Name to $target"
Write-Host "Restart Railroader to load it"
