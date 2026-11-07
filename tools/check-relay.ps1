param(
  [string[]]$Path = @("$PSScriptRoot\..\mods")
)

. (Join-Path $PSScriptRoot "RelayHost.ps1")

$files = foreach ($item in $Path) {
  if (Test-Path $item -PathType Container) { Get-ChildItem $item -Recurse -Filter *.rl } else { Get-Item $item }
}

$failed = 0
foreach ($file in $files) {
  try {
    [void](New-RelayInterpreter).Compile((Get-Content -Raw $file.FullName), $file.BaseName)
    Write-Host "OK     $($file.FullName)"
  } catch {
    Write-Host "ERROR  $($file.FullName)" -ForegroundColor Red
    Write-Host "       $(Get-RelayError $_)" -ForegroundColor Red
    $failed++
  }
}

if (-not $files) { Write-Host "No .rl files found" }
if ($failed) { exit 1 }
