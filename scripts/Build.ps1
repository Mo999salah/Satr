param([string]$Dotnet = 'dotnet', [string]$Compiler = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/Satr/Satr.csproj'
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version
$artifacts = Join-Path $root 'artifacts'
foreach ($rid in @('linux-x64', 'win-x64')) {
    $destination = Join-Path $artifacts $rid
    & $Dotnet publish $project -c Release -r $rid --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $destination
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $rid" }
    Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'README.md'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') $destination
    New-Item -ItemType Directory -Force (Join-Path $destination 'licenses') | Out-Null
    Copy-Item (Join-Path $root 'licenses/*') (Join-Path $destination 'licenses') -Recurse -Force
    if ($rid -eq 'linux-x64') {
        Copy-Item (Join-Path $root 'packaging/install-linux.sh') $destination
        foreach ($native in @('Satr','libporta_pty.so','libSkiaSharp.so','libHarfBuzzSharp.so')) {
            if (!(Test-Path (Join-Path $destination $native))) { throw "Missing Linux artifact: $native" }
        }
        & tar -czf (Join-Path $artifacts "Satr-$version-$rid.tar.gz") -C $destination .
        if ($LASTEXITCODE -ne 0) { throw 'Linux archive failed' }
    } else {
        if (!(Test-Path (Join-Path $destination 'x64/OpenConsole.exe'))) { throw 'ConPTY host missing' }
        Compress-Archive -Path (Join-Path $destination '*') -DestinationPath (Join-Path $artifacts "Satr-$version-$rid.zip") -Force
    }
}
if ($Compiler) {
    & $Compiler "/DAppVersion=$version" (Join-Path $root 'packaging/Satr.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
}
Get-ChildItem $artifacts -File | Where-Object Extension -In '.exe','.zip','.gz' | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding utf8
