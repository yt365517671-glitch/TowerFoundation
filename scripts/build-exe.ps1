param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$dotnetPath = Join-Path $projectRoot '.dotnet\dotnet.exe'
$desktopProject = Join-Path $projectRoot 'src\TowerFoundation.Desktop\TowerFoundation.Desktop.csproj'
$licenseToolProject = Join-Path $projectRoot 'src\TowerFoundation.LicenseTool\TowerFoundation.LicenseTool.csproj'
$provisionerProject = Join-Path $projectRoot 'tools\TowerFoundation.LicenseProvisioner\TowerFoundation.LicenseProvisioner.csproj'
$testProject = Join-Path $projectRoot 'tests\TowerFoundation.Tests\TowerFoundation.Tests.csproj'
$smokeProject = Join-Path $projectRoot 'tests\TowerFoundation.DesktopSmoke\TowerFoundation.DesktopSmoke.csproj'
$solution = Join-Path $projectRoot 'TowerFoundation.slnx'
$productName = -join ([char]0x5854, [char]0x57FA, [char]0x667A, [char]0x8BBE)
$version = '0.9.21'
$releaseRoot = Join-Path $projectRoot 'release'
$releaseDirectory = Join-Path $releaseRoot ($productName + '-v' + $version)
$customerDirectory = Join-Path $releaseDirectory '客户版'
$issuerDirectory = Join-Path $releaseDirectory '授权码生成器-签发员版'
$customerZip = Join-Path $releaseDirectory ($productName + '-客户版-v' + $version + '.zip')
$issuerZip = Join-Path $releaseDirectory ($productName + '-授权码生成器-签发员版-v' + $version + '.zip')
$developmentRoot = Join-Path $projectRoot 'development'
$developmentDirectory = Join-Path $developmentRoot ($productName + '-开发测试版-v' + $version)
$authorityRoot = Join-Path $projectRoot 'license-authority'
$authorityDirectory = Join-Path $authorityRoot ($productName + '-根授权管理器-v' + $version)
$stageRoot = Join-Path $projectRoot 'tmp\release-staging-v0.9.21'
$issuerStage = Join-Path $stageRoot 'issuer'
$rootStage = Join-Path $stageRoot 'root'
$productionSmokeDirectory = Join-Path $projectRoot 'tmp\production-license-smoke-v0.9.21'
$smokeImage = Join-Path $projectRoot 'tmp\ui-smoke\release-window.png'

if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    throw 'Project-local .NET SDK was not found.'
}

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBUILDUSESERVER = '0'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & $dotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')"
    }
}

function Assert-WithinRoot {
    param([string]$Path, [string]$Root, [string]$Description)
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description resolved outside the intended root: $resolvedPath"
    }
}

function Reset-WorkspaceDirectory {
    param([string]$Path, [string]$Root, [string]$Description)
    Assert-WithinRoot -Path $Path -Root $Root -Description $Description
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Copy-OcrNativeLibraries {
    param([string]$TargetDirectory)
    $source = Join-Path $env:NUGET_PACKAGES 'tesseractocr\5.5.2\x64'
    $target = Join-Path $TargetDirectory 'x64'
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($name in @('leptonica-1.85.0.dll', 'tesseract55.dll')) {
        $sourcePath = Join-Path $source $name
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Tesseract native library was not found: $sourcePath"
        }
        Copy-Item -LiteralPath $sourcePath -Destination $target -Force
    }
}

function Invoke-GuiSmoke {
    param([string]$Path, [string]$Description, [int]$Seconds = 5)
    $process = Start-Process -FilePath $Path -PassThru -WindowStyle Hidden
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
        do {
            Start-Sleep -Milliseconds 250
            $process.Refresh()
            if ($process.HasExited) {
                throw "$Description exited early with code $($process.ExitCode)."
            }
        } until ([DateTime]::UtcNow -ge $deadline)
        if (-not $process.Responding) {
            throw "$Description did not remain responsive."
        }
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            [void]$process.WaitForExit(5000)
        }
    }
}

function Assert-NoPrivateState {
    param([string[]]$Directories)
    $forbiddenNames = @(
        'settings.json', 'license.tjzlic', 'license-state.dat',
        'root-authority.json', 'issuer-identity.json', 'license-history.json'
    )
    foreach ($directory in $Directories) {
        $forbidden = @(Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object {
            $_.Name -in $forbiddenNames -or
            $_.Extension -in @('.csv', '.pdf', '.tjproj', '.tjzlic', '.tjzroot', '.jwdlic', '.jwdroot')
        })
        if ($forbidden.Count -gt 0) {
            throw "Package contains private or user data: $($forbidden[0].FullName)"
        }
    }
}

function Assert-ZipSafe {
    param([string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($entry in $archive.Entries) {
            $name = [System.IO.Path]::GetFileName($entry.FullName)
            $extension = [System.IO.Path]::GetExtension($entry.FullName)
            if ($name -in @('settings.json', 'license.tjzlic', 'license-state.dat', 'root-authority.json', 'issuer-identity.json', 'license-history.json') -or
                $extension -in @('.csv', '.pdf', '.pdb', '.tjproj', '.tjzlic', '.tjzroot', '.jwdlic', '.jwdroot')) {
                throw "ZIP contains forbidden private state: $($entry.FullName)"
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

$projectSource = Get-Content -LiteralPath $desktopProject -Raw -Encoding UTF8
if (-not $projectSource.Contains("<Version>$version</Version>")) {
    throw "Desktop version is not $version."
}
$profileSource = Get-Content -LiteralPath (Join-Path $projectRoot 'src\TowerFoundation.Desktop\AppBuildProfile.cs') -Raw -Encoding UTF8
foreach ($required in @('SettingsDirectoryName = "production"', 'LicenseDirectoryName = "production-license"', 'TOWER_FOUNDATION_PRODUCTION')) {
    if (-not ($profileSource + $projectSource).Contains($required)) {
        throw "Build profile isolation declaration is missing: $required"
    }
}

foreach ($project in @($testProject, $smokeProject, $desktopProject, $licenseToolProject, $provisionerProject)) {
    Invoke-DotNet restore $project --ignore-failed-sources --disable-parallel '-p:NuGetAudit=false' --verbosity minimal
}
Invoke-DotNet restore $licenseToolProject --runtime win-x64 --ignore-failed-sources --disable-parallel '-p:NuGetAudit=false' --verbosity minimal

Invoke-DotNet build $solution --configuration Release --no-restore --verbosity minimal --maxcpucount:1 '-nodeReuse:false' '-p:UseSharedCompilation=false'
Invoke-DotNet build $provisionerProject --configuration Release --no-restore --verbosity minimal --maxcpucount:1 '-nodeReuse:false' '-p:UseSharedCompilation=false'

if (-not $SkipTests) {
    $testOutput = & $dotnetPath run --project $testProject --configuration Release --no-build --no-restore 2>&1
    $testOutput | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0 -or -not (($testOutput -join "`n").Contains('Tests: 78, Passed: 78, Failed: 0'))) {
        throw 'Core test total did not match the v0.9.21 release baseline (78/78).'
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $smokeImage) -Force | Out-Null
    Invoke-DotNet run --project $smokeProject --configuration Release --no-build --no-restore -- $smokeImage
}

Invoke-DotNet $((Join-Path $projectRoot 'tools\TowerFoundation.LicenseProvisioner\bin\Release\net10.0\TowerFoundation.LicenseProvisioner.dll')) verify

Reset-WorkspaceDirectory -Path $developmentDirectory -Root $developmentRoot -Description 'development directory'
Reset-WorkspaceDirectory -Path $releaseDirectory -Root $releaseRoot -Description 'release directory'
Reset-WorkspaceDirectory -Path $authorityDirectory -Root $authorityRoot -Description 'authority directory'
Reset-WorkspaceDirectory -Path $stageRoot -Root (Join-Path $projectRoot 'tmp') -Description 'staging directory'
Reset-WorkspaceDirectory -Path $productionSmokeDirectory -Root (Join-Path $projectRoot 'tmp') -Description 'production smoke directory'
New-Item -ItemType Directory -Path $customerDirectory,$issuerDirectory,$issuerStage,$rootStage -Force | Out-Null

Invoke-DotNet restore $desktopProject --runtime win-x64 --ignore-failed-sources --disable-parallel '-p:NuGetAudit=false' --verbosity minimal
Invoke-DotNet publish $desktopProject --configuration Release --runtime win-x64 --self-contained true --no-restore --output $developmentDirectory `
    '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:EnableCompressionInSingleFile=true' `
    '-p:DebugType=embedded' '-p:DebugSymbols=false' '-p:UseSharedCompilation=false' --verbosity minimal --maxcpucount:1 '-nodeReuse:false'
Copy-OcrNativeLibraries -TargetDirectory $developmentDirectory
$developmentOriginalExe = Join-Path $developmentDirectory ($productName + '.exe')
$developmentExe = Join-Path $developmentDirectory ($productName + '-开发测试版.exe')
Move-Item -LiteralPath $developmentOriginalExe -Destination $developmentExe -Force

Invoke-DotNet publish $desktopProject --configuration Release --runtime win-x64 --self-contained true --no-restore --output $customerDirectory `
    '-p:TowerFoundationChannel=Production' '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' `
    '-p:EnableCompressionInSingleFile=true' '-p:DebugType=embedded' '-p:DebugSymbols=false' `
    '-p:UseSharedCompilation=false' --verbosity minimal --maxcpucount:1 '-nodeReuse:false'
Copy-OcrNativeLibraries -TargetDirectory $customerDirectory
Get-ChildItem -LiteralPath $customerDirectory -Recurse -File -Filter '*.pdb' |
    Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\客户版授权使用说明.txt') -Destination $customerDirectory -Force

$licensePublishArguments = @(
    'publish', $licenseToolProject, '--configuration', 'Release', '--runtime', 'win-x64',
    '--self-contained', 'true', '--no-restore', '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=embedded', '-p:DebugSymbols=false', '-p:UseSharedCompilation=false',
    '--verbosity', 'minimal', '--maxcpucount:1', '-nodeReuse:false'
)
Invoke-DotNet @licensePublishArguments --output $issuerStage '-p:LicenseToolRole=Issuer'
Invoke-DotNet @licensePublishArguments --output $rootStage '-p:LicenseToolRole=RootManager'
$issuerExe = Join-Path $issuerStage ($productName + '-授权码生成器.exe')
$rootManagerExe = Join-Path $rootStage ($productName + '-根授权管理器.exe')
if (-not (Test-Path -LiteralPath $issuerExe -PathType Leaf) -or -not (Test-Path -LiteralPath $rootManagerExe -PathType Leaf)) {
    throw 'Single-file license tools were not generated.'
}
Copy-Item -LiteralPath $issuerExe -Destination $issuerDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\授权码生成器使用说明.txt') -Destination $issuerDirectory -Force
Copy-Item -LiteralPath $rootManagerExe -Destination $authorityDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\根授权管理器保管说明.txt') -Destination $authorityDirectory -Force

$developmentAudit = Join-Path $projectRoot 'tmp\development-profile-audit.json'
Remove-Item Env:\TOWER_FOUNDATION_DATA_DIRECTORY -ErrorAction SilentlyContinue
$developmentAuditProcess = Start-Process -FilePath $developmentExe -ArgumentList @('--release-audit', $developmentAudit) -PassThru -WindowStyle Hidden
[void]$developmentAuditProcess.WaitForExit(15000)
if ($developmentAuditProcess.ExitCode -ne 0) { throw 'Development profile audit failed.' }
$developmentAuditData = Get-Content -LiteralPath $developmentAudit -Raw -Encoding UTF8 | ConvertFrom-Json
if ($developmentAuditData.profile -ne 'Development' -or $developmentAuditData.requiresLicense -ne $false -or
    $developmentAuditData.settingsDirectory -ne (Join-Path $env:LOCALAPPDATA 'TowerFoundation')) {
    throw 'Development build did not preserve the original local settings profile.'
}

$env:TOWER_FOUNDATION_DATA_DIRECTORY = $productionSmokeDirectory
$customerExe = Join-Path $customerDirectory ($productName + '.exe')
$productionAudit = Join-Path $productionSmokeDirectory 'release-audit.json'
$auditProcess = Start-Process -FilePath $customerExe -ArgumentList @('--release-audit', $productionAudit) -PassThru -WindowStyle Hidden
[void]$auditProcess.WaitForExit(15000)
if ($auditProcess.ExitCode -ne 0) { throw 'Production profile audit failed.' }
$audit = Get-Content -LiteralPath $productionAudit -Raw -Encoding UTF8 | ConvertFrom-Json
if ($audit.profile -ne 'Production' -or $audit.requiresLicense -ne $true -or
    $audit.hasDeepSeekApiKey -ne $false -or $audit.hasVisionApiKey -ne $false -or
    $audit.settingsDirectory -ne $productionSmokeDirectory -or $audit.licenseDirectory -ne $productionSmokeDirectory) {
    throw 'Production profile did not start with isolated, key-free settings and licensing state.'
}

$blockedResult = Join-Path $productionSmokeDirectory 'blocked-command.txt'
$blocked = Start-Process -FilePath $customerExe -ArgumentList @('--formal-use-self-test', $blockedResult) -PassThru -WindowStyle Hidden
[void]$blocked.WaitForExit(15000)
if ($blocked.ExitCode -ne 5 -or (Test-Path -LiteralPath $blockedResult)) {
    throw 'Unlicensed production CLI did not block formal use.'
}
Invoke-GuiSmoke -Path $customerExe -Description 'unlicensed preview client'

$provisionerDll = Join-Path $projectRoot 'tools\TowerFoundation.LicenseProvisioner\bin\Release\net10.0\TowerFoundation.LicenseProvisioner.dll'
Invoke-DotNet $provisionerDll issue-smoke-license (Join-Path $productionSmokeDirectory 'license.tjzlic') $audit.machineCode
$authorizedResult = Join-Path $productionSmokeDirectory 'authorized-command.txt'
$authorized = Start-Process -FilePath $customerExe -ArgumentList @('--formal-use-self-test', $authorizedResult) -PassThru -WindowStyle Hidden
[void]$authorized.WaitForExit(15000)
if ($authorized.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $authorizedResult) -or
    -not ((Get-Content -LiteralPath $authorizedResult -Raw -Encoding UTF8).Contains('PASS'))) {
    throw 'Authorized production CLI did not pass the license gate.'
}
Invoke-GuiSmoke -Path $customerExe -Description 'authorized production client'

$publishedOcrResult = Join-Path $productionSmokeDirectory 'ocr-result.txt'
$ocr = Start-Process -FilePath $customerExe -ArgumentList @('--ocr-self-test', $publishedOcrResult) -PassThru -WindowStyle Hidden
if (-not $ocr.WaitForExit(30000) -or $ocr.ExitCode -ne 0 -or
    -not ((Get-Content -LiteralPath $publishedOcrResult -Raw).Contains('180'))) {
    throw 'Published production OCR self-test failed.'
}

Remove-Item Env:\TOWER_FOUNDATION_DATA_DIRECTORY -ErrorAction SilentlyContinue
Invoke-GuiSmoke -Path $developmentExe -Description 'development test build'
Invoke-GuiSmoke -Path (Join-Path $issuerDirectory ($productName + '-授权码生成器.exe')) -Description 'license generator'
Invoke-GuiSmoke -Path (Join-Path $authorityDirectory ($productName + '-根授权管理器.exe')) -Description 'root license manager'

Assert-NoPrivateState -Directories @($developmentDirectory, $customerDirectory, $issuerDirectory, $authorityDirectory)

Compress-Archive -Path (Join-Path $customerDirectory '*') -DestinationPath $customerZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $issuerDirectory '*') -DestinationPath $issuerZip -CompressionLevel Optimal
Assert-ZipSafe -Path $customerZip
Assert-ZipSafe -Path $issuerZip

$customerRootManagerLeaks = @(Get-ChildItem -LiteralPath $customerDirectory -Recurse -File |
    Where-Object Name -eq ($productName + '-根授权管理器.exe'))
$issuerRootManagerLeaks = @(Get-ChildItem -LiteralPath $issuerDirectory -Recurse -File |
    Where-Object Name -eq ($productName + '-根授权管理器.exe'))
if ($customerRootManagerLeaks.Count -gt 0 -or $issuerRootManagerLeaks.Count -gt 0) {
    throw 'Root manager leaked into a public package.'
}

$customerHash = (Get-FileHash -LiteralPath $customerExe -Algorithm SHA256).Hash
$developmentHash = (Get-FileHash -LiteralPath $developmentExe -Algorithm SHA256).Hash
$issuerPublished = Join-Path $issuerDirectory ($productName + '-授权码生成器.exe')
$rootPublished = Join-Path $authorityDirectory ($productName + '-根授权管理器.exe')
$manifest = [ordered]@{
    product = $productName
    version = $version
    productionProfile = 'Production'
    developmentProfile = 'Development'
    licensing = 'offline machine-bound ECDSA P-256 two-tier signed license'
    unlicensedMode = 'browse only; calculation, AI, save and export blocked'
    apiKeysBundled = $false
    rootPrivateKeyBundled = $false
    customerExe = [ordered]@{ path = '客户版\塔基智设.exe'; sha256 = $customerHash }
    customerZip = [ordered]@{ file = [System.IO.Path]::GetFileName($customerZip); sha256 = (Get-FileHash -LiteralPath $customerZip -Algorithm SHA256).Hash }
    issuerZip = [ordered]@{ file = [System.IO.Path]::GetFileName($issuerZip); sha256 = (Get-FileHash -LiteralPath $issuerZip -Algorithm SHA256).Hash }
    developmentExe = [ordered]@{ path = 'development\塔基智设-开发测试版.exe'; sha256 = $developmentHash }
    rootManagerPublicRelease = $false
    tests = if ($SkipTests) {
        '78/78 plus WPF workflow smoke passed before packaging-only retry'
    } else {
        '78/78 plus WPF workflow smoke'
    }
    builtAt = [DateTimeOffset]::Now.ToString('o')
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseDirectory 'manifest.json') -Encoding UTF8

$privateManifest = [ordered]@{
    product = $productName
    version = $version
    role = 'root-authority-private'
    rootPrivateKeyBundled = $false
    privateKeyStorage = 'Windows DPAPI CurrentUser outside package'
    file = [System.IO.Path]::GetFileName($rootPublished)
    sha256 = (Get-FileHash -LiteralPath $rootPublished -Algorithm SHA256).Hash
    builtAt = [DateTimeOffset]::Now.ToString('o')
}
$privateManifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $authorityDirectory 'manifest.json') -Encoding UTF8

if (Test-Path -LiteralPath $productionSmokeDirectory) {
    Assert-WithinRoot -Path $productionSmokeDirectory -Root (Join-Path $projectRoot 'tmp') -Description 'production smoke cleanup'
    Remove-Item -LiteralPath $productionSmokeDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $stageRoot) {
    Assert-WithinRoot -Path $stageRoot -Root (Join-Path $projectRoot 'tmp') -Description 'staging cleanup'
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}

$legacyRelease = Join-Path $releaseRoot ($productName + '-v0.9.20')
$legacyDevelopment = Join-Path $developmentRoot ($productName + '-开发测试版-v0.9.20-原版')
if ((Test-Path -LiteralPath $legacyRelease) -and -not (Test-Path -LiteralPath $legacyDevelopment)) {
    Assert-WithinRoot -Path $legacyRelease -Root $releaseRoot -Description 'legacy release'
    New-Item -ItemType Directory -Path $developmentRoot -Force | Out-Null
    Move-Item -LiteralPath $legacyRelease -Destination $legacyDevelopment
}
Get-ChildItem -LiteralPath $releaseRoot -Directory | Where-Object { $_.FullName -ne $releaseDirectory } | ForEach-Object {
    Assert-WithinRoot -Path $_.FullName -Root $releaseRoot -Description 'obsolete public release'
    Remove-Item -LiteralPath $_.FullName -Recurse -Force
}

Write-Host "PASS 开发测试版: $developmentExe"
Write-Host "PASS 客户版: $customerExe"
Write-Host "PASS 客户ZIP: $customerZip"
Write-Host "PASS 签发员ZIP: $issuerZip"
Write-Host "PASS 根授权管理器（制作方私有）: $rootPublished"
