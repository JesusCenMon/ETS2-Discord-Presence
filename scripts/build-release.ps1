$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $projectRoot 'dist/publish'
$archivePath = Join-Path $projectRoot 'dist/ETS2-Discord-Presence-win-x64.zip'
$identity = Get-Content -Raw (Join-Path $projectRoot 'src/Ets2DiscordPresence/DiscordIdentity.cs')
if ($identity -notmatch 'ApplicationId = "[0-9]{17,20}"') { throw 'Falta registrar la aplicación de Discord antes de crear una versión para jugadores.' }

dotnet publish (Join-Path $projectRoot 'src/Ets2DiscordPresence/Ets2DiscordPresence.csproj') `
    -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'La publicación falló.' }

$files = @(
    (Join-Path $publishDir 'ETS2DiscordPresence.exe'),
    (Join-Path $publishDir 'cities_default.json'),
    (Join-Path $publishDir 'hero-truck.png'),
    (Join-Path $projectRoot 'data/CITY-DATA-LICENSE.txt')
)
foreach ($file in $files) { if (-not (Test-Path -LiteralPath $file)) { throw "Falta $file" } }

$distDir = Split-Path -Parent $archivePath
New-Item -ItemType Directory -Force $distDir | Out-Null
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath }
Compress-Archive -LiteralPath $files -DestinationPath $archivePath
Write-Host "Creado: $archivePath"
