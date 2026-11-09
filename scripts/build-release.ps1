param([string]$Tag)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $projectRoot 'dist/standalone'
$exePath = Join-Path $publishDir 'ETS2DiscordPresence.exe'
$project = Join-Path $projectRoot 'src/Ets2DiscordPresence/Ets2DiscordPresence.csproj'
$xml = [xml](Get-Content -Raw -LiteralPath $project)
$version = @($xml.Project.PropertyGroup.Version | Where-Object { $_ })
if ($version.Count -ne 1 -or $version[0] -notmatch '^\d+\.\d+\.\d+$') { throw 'La versión del proyecto no es válida.' }
$expectedTag = "v$($version[0])"
if ($Tag -and $Tag -cne $expectedTag) { throw "El tag '$Tag' no coincide con '$expectedTag'." }
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -cne $expectedTag) { throw 'El tag de GitHub no coincide con la versión.' }
$readme = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'README.md')
if ($readme -notmatch "(?m)^## Última versión · $([regex]::Escape($expectedTag))\s*$") { throw 'Actualiza la sección Última versión del README.' }

$identity = Get-Content -Raw (Join-Path $projectRoot 'src/Ets2DiscordPresence/DiscordIdentity.cs')
if ($identity -notmatch 'ApplicationId = "[0-9]{17,20}"') { throw 'Falta registrar la aplicación de Discord antes de crear una versión para jugadores.' }

$assetTags = [regex]::Matches($identity, '(?:AssetsTag\s*=\s*"|/)(v\d+\.\d+\.\d+)(?:"|/assets/)') |
    ForEach-Object { $_.Groups[1].Value }
if (@($assetTags).Count -eq 0 -or @($assetTags | Where-Object { $_ -cne $expectedTag }).Count -gt 0) {
    throw "Los assets de Discord deben usar $expectedTag."
}

dotnet test (Join-Path $projectRoot 'tests/Ets2DiscordPresence.Tests/Ets2DiscordPresence.Tests.csproj') `
    -c Release --artifacts-path (Join-Path $projectRoot 'dist/verification')
if ($LASTEXITCODE -ne 0) { throw 'Falló la verificación previa a la publicación.' }

dotnet publish (Join-Path $projectRoot 'src/Ets2DiscordPresence/Ets2DiscordPresence.csproj') `
    -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true /p:DebugType=embedded `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'La publicación falló.' }

if (-not (Test-Path -LiteralPath $exePath)) { throw "Falta $exePath" }
$hash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$exePath.sha256" -Value "$hash  ETS2DiscordPresence.exe" -Encoding ascii
Write-Host "Creado: $exePath"
