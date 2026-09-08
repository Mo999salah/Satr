param([string]$Dotnet = 'dotnet', [string]$Compiler = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/Satr/Satr.csproj'
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version
$artifacts = Join-Path $root 'artifacts'
foreach ($path in @(
    (Join-Path $artifacts 'linux-x64'),
    (Join-Path $artifacts 'win-x64'),
    (Join-Path $artifacts "Satr-$version-linux-x64.tar.gz"),
    (Join-Path $artifacts "Satr-$version-win-x64.zip"),
    (Join-Path $artifacts "Satr-Setup-$version-win-x64.exe"),
    (Join-Path $artifacts "satr_${version}_amd64.deb"),
    (Join-Path $artifacts "satr-$version-1-x86_64.pkg.tar.zst"),
    (Join-Path $artifacts 'satr.db'),
    (Join-Path $artifacts 'satr.db.tar.zst'),
    (Join-Path $artifacts 'satr.files'),
    (Join-Path $artifacts 'satr.files.tar.zst'),
    (Join-Path $artifacts 'SHA256SUMS.txt')
)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
foreach ($rid in @('linux-x64', 'win-x64')) {
    $destination = Join-Path $artifacts $rid
    & $Dotnet publish $project -c Release -r $rid --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $destination
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $rid" }
    Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'README.md'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') $destination
    New-Item -ItemType Directory -Force (Join-Path $destination 'licenses') | Out-Null
    Copy-Item (Join-Path $root 'licenses/*') (Join-Path $destination 'licenses') -Recurse -Force
    if ($rid -eq 'linux-x64') {
        Copy-Item (Join-Path $root 'packaging/install-linux.sh'), (Join-Path $root 'packaging/uninstall-linux.sh') $destination
        foreach ($native in @('Satr','libporta_pty.so','libSkiaSharp.so','libHarfBuzzSharp.so')) {
            if (!(Test-Path (Join-Path $destination $native))) { throw "Missing Linux artifact: $native" }
        }
        & tar -czf (Join-Path $artifacts "Satr-$version-$rid.tar.gz") -C $destination .
        if ($LASTEXITCODE -ne 0) { throw 'Linux archive failed' }
        $bash = Get-Command bash -ErrorAction SilentlyContinue
        if ($bash) {
            & $bash.Source (Join-Path $root 'packaging/linux-packages.sh') $version $destination $artifacts
            if ($LASTEXITCODE -ne 0) { throw 'Linux packages failed' }
        }
    } else {
        if (!(Test-Path (Join-Path $destination 'x64/OpenConsole.exe'))) { throw 'ConPTY host missing' }
        Compress-Archive -Path (Join-Path $destination '*') -DestinationPath (Join-Path $artifacts "Satr-$version-$rid.zip") -Force
    }
}
if ($Compiler) {
    & $Compiler "/DAppVersion=$version" (Join-Path $root 'packaging/Satr.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
}
$releaseFiles = @(
    (Join-Path $artifacts "Satr-$version-linux-x64.tar.gz"),
    (Join-Path $artifacts "Satr-$version-win-x64.zip")
)
if (Test-Path (Join-Path $artifacts "Satr-Setup-$version-win-x64.exe")) {
    $releaseFiles += Join-Path $artifacts "Satr-Setup-$version-win-x64.exe"
}
foreach ($linuxPackage in @(
    (Join-Path $artifacts "satr_${version}_amd64.deb"),
    (Join-Path $artifacts "satr-$version-1-x86_64.pkg.tar.zst"),
    (Join-Path $artifacts 'satr.db'),
    (Join-Path $artifacts 'satr.db.tar.zst'),
    (Join-Path $artifacts 'satr.files'),
    (Join-Path $artifacts 'satr.files.tar.zst')
)) {
    if (Test-Path -LiteralPath $linuxPackage) { $releaseFiles += $linuxPackage }
}
$releaseFiles | Get-Item | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding utf8
