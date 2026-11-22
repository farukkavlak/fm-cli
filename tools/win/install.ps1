# Run from the unpacked Windows package: puts fm in %LOCALAPPDATA%\fm-cli and on the user's PATH, then lets
# fm doctor --install place BepInEx and the plugin in the game's folder (found in the Steam libraries, or FM_HOME).
$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

$dest = Join-Path $env:LOCALAPPDATA 'fm-cli'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
# The package as a whole goes with fm: doctor --install takes BepInEx and the plugin from beside fm.exe.
Copy-Item -Recurse -Force -Path (Join-Path $PSScriptRoot '*') -Destination $dest
# A downloaded zip marks every file as from the internet; Windows then warns on each start.
Get-ChildItem -Recurse -Path $dest | Unblock-File

$path = [Environment]::GetEnvironmentVariable('Path', 'User')
if (-not ($path -split ';' | Where-Object { $_ -eq $dest })) {
	[Environment]::SetEnvironmentVariable('Path', ($path.TrimEnd(';') + ';' + $dest), 'User')
	Write-Host "fm added to your PATH; open a new terminal for it to take effect."
}
Write-Host "checking the install..."
& (Join-Path $dest 'fm.exe') doctor --install
