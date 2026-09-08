param(
    [string]$Dotnet = 'dotnet',
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/Satr/Satr.csproj'
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'win-dev'
$target = Join-Path $env:LOCALAPPDATA 'Satr-Dev'
$state = Join-Path $env:LOCALAPPDATA 'Satr-Dev-Data'
$launcher = Join-Path $target 'satr-dev.cmd'

function Invoke-Dotnet([string[]]$Arguments) {
    & $Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($Arguments -join ' ')" }
}

Invoke-Dotnet -Arguments @('run', '--project', (Join-Path $root 'tests/Satr.BufferTests/Satr.BufferTests.csproj'), '-c', 'Release', '--nologo')
Invoke-Dotnet -Arguments @('run', '--project', (Join-Path $root 'tests/Satr.PortChecks/Satr.PortChecks.csproj'), '-c', 'Release', '--nologo')
Invoke-Dotnet -Arguments @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $publish, '--nologo')

$running = @(Get-Process -Name Satr -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $target 'Satr.exe') })
if ($running.Count -gt 0) { throw 'Close the running Satr Dev window before updating it.' }

$stage = "$target.next-$PID"
$previous = "$target.previous"
Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -Path (Join-Path $publish '*') -Destination $stage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'packaging/satr-dev.cmd') -Destination (Join-Path $stage 'satr-dev.cmd') -Force

if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $previous -Recurse -Force -ErrorAction SilentlyContinue
    Move-Item -LiteralPath $target -Destination $previous
}
Move-Item -LiteralPath $stage -Destination $target

$shell = New-Object -ComObject WScript.Shell
foreach ($shortcutPath in @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Satr Dev.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'Satr Dev.lnk')
)) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $launcher
    $shortcut.WorkingDirectory = $target
    $shortcut.IconLocation = "$(Join-Path $target 'Satr.exe'),0"
    $shortcut.Save()
}

Write-Host "Installed Satr Dev: $target"
Write-Host "State: $state"
if (!$NoLaunch) { Start-Process -FilePath $launcher -WorkingDirectory $target }
