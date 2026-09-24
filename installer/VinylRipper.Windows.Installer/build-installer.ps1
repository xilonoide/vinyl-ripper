<#
.SYNOPSIS
    Publica VinylRipper.Windows (win-x64, self-contained) y genera el instalador con Inno Setup.

.DESCRIPTION
    1. Lee la versión del csproj.
    2. dotnet publish -c Release -r win-x64 --self-contained  →  installer/.../publish
    3. ISCC.exe VinylRipper.Windows.Installer.iss                →  installer/.../output/VinylRipper-Setup-<ver>-win-x64.exe

    Inno Setup 6 debe estar instalado (por usuario o por máquina); se busca en las rutas habituales
    o se puede indicar con -Iscc.

.EXAMPLE
    pwsh installer/VinylRipper.Windows.Installer/build-installer.ps1
    pwsh installer/VinylRipper.Windows.Installer/build-installer.ps1 -SkipPublish
#>
[CmdletBinding()]
param(
    [string]$Iscc,
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $here '..\..')
$csproj = Join-Path $repoRoot 'src\VinylRipper.Windows\VinylRipper.Windows.csproj'
$iss = Join-Path $here 'VinylRipper.Windows.Installer.iss'
$publishDir = Join-Path $here 'publish'
$outputDir = Join-Path $here 'output'

# --- ISCC ------------------------------------------------------------------------------------
if (-not $Iscc) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $Iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $Iscc) { $Iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
}
if (-not $Iscc -or -not (Test-Path $Iscc)) {
    throw "No encuentro ISCC.exe (Inno Setup 6). Instálalo con 'winget install JRSoftware.InnoSetup' o pasa -Iscc <ruta>."
}

# --- Versión ---------------------------------------------------------------------------------
[xml]$proj = Get-Content $csproj
$version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { throw "El csproj no tiene <Version>." }
$version = $version.Trim()

Write-Host "Vinyl Ripper $version  ·  $Runtime  ·  $Configuration" -ForegroundColor Cyan

# --- Publish ---------------------------------------------------------------------------------
if (-not $SkipPublish) {
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    Write-Host "→ dotnet publish" -ForegroundColor DarkGray
    dotnet publish $csproj -c $Configuration -r $Runtime --self-contained true -o $publishDir -nologo -v q `
        -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló ($LASTEXITCODE)." }
}
elseif (-not (Test-Path (Join-Path $publishDir 'VinylRipper.Windows.exe'))) {
    throw "No hay publicación previa en $publishDir; quita -SkipPublish."
}

# --- Inno Setup ------------------------------------------------------------------------------
New-Item -ItemType Directory -Force $outputDir | Out-Null
Write-Host "→ ISCC" -ForegroundColor DarkGray
& $Iscc /Qp "/DAppVersion=$version" "/DPublishDir=$publishDir" "/DOutputDir=$outputDir" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC falló ($LASTEXITCODE)." }

$setup = Get-ChildItem $outputDir -Filter "VinylRipper-Setup-$version-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host ("✔ {0}  ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB)) -ForegroundColor Green
