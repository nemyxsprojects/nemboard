[CmdletBinding()]
param(
    [switch] $SkipTests,
    [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $projectRoot 'Source'
$solution = Join-Path $sourceRoot 'Soundboard.sln'
$appProject = Join-Path $sourceRoot 'Soundboard.App\Soundboard.App.csproj'
$publishDirectory = Join-Path $projectRoot 'artifacts\publish\win-x64'
$installerScript = Join-Path $projectRoot 'installer\Soundboard.iss'
$distDirectory = Join-Path $projectRoot 'dist'

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Write-Host 'Restauration des dépendances...'
dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw 'La restauration a échoué.' }

if (-not $SkipTests) {
    Write-Host 'Exécution des tests...'
    dotnet test $solution -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Les tests ont échoué.' }
}

Write-Host 'Publication autonome Windows x64...'
dotnet publish $appProject -c Release -r win-x64 --self-contained true --no-restore -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'La publication a échoué.' }

if ($SkipInstaller) {
    Write-Host "Application publiée dans $publishDirectory"
    exit 0
}

$compilerCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique

if (-not $compilerCandidates) {
    throw 'ISCC.exe est introuvable. Installez Inno Setup 6 ou 7 depuis https://jrsoftware.org/isdl.php puis relancez ce script.'
}

New-Item -ItemType Directory -Force -Path $distDirectory | Out-Null
Write-Host "Compilation de l’installateur Inno Setup..."
$compiler = @($compilerCandidates)[0]
$innoOutputDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'NemBoard-Inno-Output'
New-Item -ItemType Directory -Force -Path $innoOutputDirectory | Out-Null
$temporarySetup = Join-Path $innoOutputDirectory 'NemBoard-Setup-x64.exe'
if (Test-Path -LiteralPath $temporarySetup) {
    Remove-Item -LiteralPath $temporarySetup -Force
}
& $compiler "/O$innoOutputDirectory" $installerScript
if ($LASTEXITCODE -ne 0) { throw 'La compilation Inno Setup a échoué.' }

$setup = Join-Path $distDirectory 'NemBoard-Setup-x64.exe'
Copy-Item -LiteralPath $temporarySetup -Destination $setup -Force
$hash = Get-FileHash -LiteralPath $setup -Algorithm SHA256
Write-Host "Installateur : $setup"
Write-Host "SHA-256      : $($hash.Hash)"
