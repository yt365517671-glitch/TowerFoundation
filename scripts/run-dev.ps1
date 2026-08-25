$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $projectRoot '.dotnet\dotnet.exe'
$applicationPath = Join-Path $projectRoot 'src\TowerFoundation.Desktop\bin\Debug\net10.0-windows\塔基智设.dll'

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    throw 'Project-local .NET runtime was not found. Restore the .dotnet directory first.'
}

if (-not (Test-Path -LiteralPath $applicationPath)) {
    throw 'Application build output was not found. Build the desktop project first.'
}

& $dotnetPath $applicationPath
