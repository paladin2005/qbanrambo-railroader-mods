param(
  [Parameter(Mandatory)][string]$Name
)

. (Join-Path $PSScriptRoot "RelayHost.ps1")

$root = Split-Path $PSScriptRoot -Parent
$main = Join-Path $root "mods\$Name\Scripts\Main.rl"
$test = Join-Path $root "tests\$Name.rl"
foreach ($file in $main, $test) { if (-not (Test-Path $file)) { throw "Missing $file" } }

$modules = @{}
Get-ChildItem (Join-Path $PSScriptRoot "relay-mocks") -Filter *.rl | ForEach-Object { $modules[$_.BaseName] = Get-Content -Raw $_.FullName }

$mainSource = Get-Content -Raw $main
$mainLines = ($mainSource -split "`n").Count
try {
  $result = (New-RelayInterpreter $modules).Interpret($mainSource + "`n" + (Get-Content -Raw $test), $Name)
  Write-Host "$result"
} catch {
  Write-Host "FAILED  $(Get-RelayError $_)" -ForegroundColor Red
  Write-Host "        Main.rl has $mainLines lines; later line numbers are in tests\$Name.rl (subtract $mainLines)" -ForegroundColor Red
  exit 1
}
