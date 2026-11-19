$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Uruchom PowerShell jako administrator.'
}

$root = Join-Path $env:ProgramFiles 'ParentalControl'
$serviceSource = Join-Path $PSScriptRoot 'ParentalControl.Service\bin\Release\net10.0-windows\win-x64\publish'
$agentSource = Join-Path $PSScriptRoot 'ParentalControl.Agent\bin\Release\net10.0-windows\win-x64\publish'
$serviceDestination = Join-Path $root 'service'
$agentDestination = Join-Path $root 'agent'

if (-not (Test-Path (Join-Path $serviceSource 'ParentalControl.Service.exe')) -or
    -not (Test-Path (Join-Path $agentSource 'ParentalControl.Agent.exe'))) {
    throw 'Najpierw opublikuj oba projekty poleceniami opisanymi w README.md.'
}

New-Item -ItemType Directory -Force -Path $serviceDestination, $agentDestination, 'C:\DD' | Out-Null
Copy-Item (Join-Path $serviceSource '*') $serviceDestination -Recurse -Force
Copy-Item (Join-Path $agentSource '*') $agentDestination -Recurse -Force

$configPath = 'C:\DD\config.txt'
if (-not (Test-Path $configPath)) {
    Copy-Item (Join-Path $PSScriptRoot 'config.example.txt') $configPath
}
icacls 'C:\DD' /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' /T /C | Out-Null

$serviceExe = Join-Path $serviceDestination 'ParentalControl.Service.exe'
if (Get-Service -Name 'ParentalControl' -ErrorAction SilentlyContinue) {
    Stop-Service -Name 'ParentalControl' -Force -ErrorAction SilentlyContinue
    sc.exe delete ParentalControl | Out-Null
    Start-Sleep -Seconds 2
}
New-Service -Name 'ParentalControl' -DisplayName 'Parental Control' -BinaryPathName "`"$serviceExe`"" -StartupType Automatic | Out-Null
Start-Service -Name 'ParentalControl'

$agentExe = Join-Path $agentDestination 'ParentalControl.Agent.exe'
New-Item -Path 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Force | Out-Null
Set-ItemProperty -Path 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ParentalControlAgent' -Value "`"$agentExe`""

Write-Host 'Instalacja zakończona. Ustaw hasło i dzienne limity w C:\DD\config.txt, a następnie zaloguj się ponownie.'
