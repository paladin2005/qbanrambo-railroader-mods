$script:GameDir = if ($env:RAILROADER_DIR) { $env:RAILROADER_DIR } else { "F:\SteamLibrary\steamapps\common\Railroader" }
$script:ManagedDir = Join-Path $script:GameDir "Railroader_Data\Managed"
$script:RelayAssembly = $null

function Import-Relay {
  if ($script:RelayAssembly) { return $script:RelayAssembly }
  $managed = $script:ManagedDir
  $relayPath = Join-Path $managed "Relay.dll"
  if (-not (Test-Path $relayPath)) { throw "Relay.dll not found in $managed. Set RAILROADER_DIR to the Railroader install folder." }
  [AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($sender, $eventArgs)
    $file = Join-Path $managed ((New-Object Reflection.AssemblyName($eventArgs.Name)).Name + ".dll")
    if (Test-Path $file) { [Reflection.Assembly]::LoadFile($file) } else { $null }
  }.GetNewClosure())
  $script:RelayAssembly = [Reflection.Assembly]::LoadFile($relayPath)
  $script:RelayAssembly
}

function New-RelayInterpreter([hashtable]$Modules = @{}) {
  $relay = Import-Relay
  $interpreter = [Activator]::CreateInstance($relay.GetType("Relay.Interpreter"))
  foreach ($name in "CoreModule", "Vector3Module") {
    $interpreter.RegisterModule($relay.GetType("Relay.StandardLibrary.$name").GetProperty("Instance").GetValue($null))
  }
  foreach ($name in "MathModule", "LogModule", "RegexModule") {
    $interpreter.RegisterNamedModule($relay.GetType("Relay.StandardLibrary.$name").GetProperty("Instance").GetValue($null))
  }
  if ($Modules.Count) {
    $loader = [Activator]::CreateInstance($relay.GetType("Relay.StringModuleLoader"))
    foreach ($entry in $Modules.GetEnumerator()) { $loader.RegisterModule($entry.Key, $entry.Value) }
    $interpreter.RegisterModuleLoader($loader)
  }
  , $interpreter
}

function Get-RelayError($ErrorRecord) {
  $exception = $ErrorRecord.Exception
  while ($exception.InnerException) { $exception = $exception.InnerException }
  $exception.Message
}
