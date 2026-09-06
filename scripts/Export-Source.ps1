param([string]$Destination)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path $PSScriptRoot -Parent
if (!$Destination) { $Destination = Join-Path (Split-Path $root -Parent) 'Satr-source.zip' }
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw 'Choose a new archive path; existing archives are not overwritten.' }
$zip = [IO.Compression.ZipFile]::Open($Destination,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $root -Recurse -File -Force) {
        $relative = $file.FullName.Substring($root.Length+1).Replace('\','/')
        if ($relative -match '(^|/)(bin|obj|artifacts|\.git|\.vs|work)/' -or $relative -match '\.(pdb|log|tmp|user)$' -or $relative -match '(^|/)\.env') { continue }
        if ($file.FullName -eq $Destination) { continue }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,"Satr/$relative",[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }
Write-Host $Destination
