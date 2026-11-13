param([string]$Tag, [switch]$VerifyRemoteArtwork)

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

if ($VerifyRemoteArtwork) {
    $countryUrls = foreach ($name in 'country_art.json','country_art_en.json') {
        $manifest = Get-Content -Raw -LiteralPath (Join-Path $projectRoot "data/$name") | ConvertFrom-Json
        $manifest.PSObject.Properties.Value
    }
    $coachRoot = [regex]::Match($identity, 'CoachArtworkRoot = "([^"]+)"').Groups[1].Value
    $genericTruck = [regex]::Match($identity, 'ActivityImage = "([^"]+)"').Groups[1].Value
    $checks = @($genericTruck, ($coachRoot + 'coach.jpg')) + @($countryUrls) + @(
        $countryUrls | ForEach-Object { $coachRoot + [System.IO.Path]::GetFileName($_) }
    )
    $checks | Sort-Object -Unique | ForEach-Object -ThrottleLimit 12 -Parallel {
        $result = Invoke-WebRequest -Uri $_ -Method Head -TimeoutSec 20
        if ($result.StatusCode -ne 200 -or $result.Headers['Content-Type'] -notmatch 'image/jpeg') {
            throw "Portada no disponible: $_"
        }
    }
    Write-Host "$($checks.Count) portadas verificadas."
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
$reportPath = Join-Path ([System.IO.Path]::GetTempPath()) ("ets2-diagnostics-{0}.json" -f [guid]::NewGuid().ToString('N'))
try {
    $smokeArgs = @('--self-test', '--diagnostics-json', '--output', ('"{0}"' -f $reportPath))
    $smoke = Start-Process -FilePath $exePath -ArgumentList $smokeArgs -PassThru -WindowStyle Hidden
    if (-not $smoke.WaitForExit(30000)) {
        Stop-Process -Id $smoke.Id -ErrorAction SilentlyContinue
        throw 'El ejecutable superó el tiempo de comprobación.'
    }
    if ($smoke.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath)) { throw 'Falló la comprobación del ejecutable empaquetado.' }
    $diagnostics = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    if ($diagnostics.version -cne $version[0] -or -not $diagnostics.dataAvailable) { throw 'El diagnóstico del ejecutable es incoherente.' }
}
finally {
    if (Test-Path -LiteralPath $reportPath) { Remove-Item -LiteralPath $reportPath }
}
$hash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$exePath.sha256" -Value "$hash  ETS2DiscordPresence.exe" -Encoding ascii
Write-Host "Creado: $exePath"
