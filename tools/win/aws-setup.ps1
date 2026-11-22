# A rented Windows test machine (AWS g4dn, Windows Server 2022) made ready for T81, run once as Administrator:
#   powershell -ExecutionPolicy Bypass -File aws-setup.ps1 -PublicKey "ssh-ed25519 AAAA... fm-cli"
# It turns on Windows' own SSH server for that key (the tests are driven from the Mac over SSH) and fetches the
# Steam installer. The NVIDIA driver is a step of its own: AWS's gaming driver, from its current instructions
# for G4dn ("Install NVIDIA drivers on Windows instances"), checked on the day rather than written here.
param([Parameter(Mandatory = $true)][string]$PublicKey)
$ErrorActionPreference = 'Stop'

# OpenSSH server, started now and with Windows.
if ((Get-WindowsCapability -Online -Name OpenSSH.Server*).State -ne 'Installed') {
	Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0 | Out-Null
}
Set-Service -Name sshd -StartupType Automatic
Start-Service sshd
if (-not (Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue)) {
	New-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 | Out-Null
}
# An administrator's keys live in one file of their own, readable only by Administrators and SYSTEM.
$keys = Join-Path $env:ProgramData 'ssh\administrators_authorized_keys'
Add-Content -Path $keys -Value $PublicKey
icacls $keys /inheritance:r /grant 'Administrators:F' /grant 'SYSTEM:F' | Out-Null
# PowerShell as the SSH shell: the commands sent from the Mac are PowerShell.
New-ItemProperty -Path 'HKLM:\SOFTWARE\OpenSSH' -Name DefaultShell -Value 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -PropertyType String -Force | Out-Null

# Steam's installer, on the desktop: signing in and installing the game are the player's.
$steam = Join-Path ([Environment]::GetFolderPath('Desktop')) 'SteamSetup.exe'
Invoke-WebRequest -Uri 'https://cdn.akamai.steamstatic.com/client/installer/SteamSetup.exe' -OutFile $steam

Write-Host "SSH is on (port 22, the given key). Next: the NVIDIA gaming driver, then $steam, sign in, install Football Manager 27."
