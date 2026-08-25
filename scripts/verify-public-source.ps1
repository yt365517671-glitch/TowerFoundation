param([string]$Root = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$rootPath = [IO.Path]::GetFullPath($Root)
$forbiddenNames = @(
    'settings.json', 'license-state.dat', 'root-authority.json',
    'issuer-identity.json', 'license-history.json',
    'enterprise-tower-load-library-v2.json',
    'enterprise-tower-load-library-legacy.json'
)
$forbiddenExtensions = @(
    '.csv', '.dll', '.dwg', '.dxf', '.exe', '.jwdlic', '.key', '.p12',
    '.pdf', '.pdb', '.pfx', '.shx', '.snk',
    '.tjzlic', '.tjzroot', '.traineddata', '.xls', '.xlsx', '.zip'
)

$files = @(Get-ChildItem -LiteralPath $rootPath -Recurse -File -Force |
    Where-Object {
        $_.FullName -notmatch '\\.git\\' -and
        $_.FullName -notmatch '\\(?:bin|obj|artifacts)\\'
    })
$blocked = @($files | Where-Object {
    $candidate = $_
    $candidate.Name -in $forbiddenNames -or
    $candidate.Extension.ToLowerInvariant() -in $forbiddenExtensions
})
if ($blocked.Count -gt 0) {
    throw "Forbidden public file: $($blocked[0].FullName)"
}

$secretPattern = '(?i)(sk-[a-z0-9_-]{16,}|ghp_[a-z0-9]{20,}|github_pat_[a-z0-9_]{20,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|C:\\Users\\yt|G:\\AI\\tjRJKF|yt365517671@gmail\.com)'
foreach ($file in $files | Where-Object {
    $_.Extension -in @('.cs', '.csproj', '.json', '.md', '.ps1', '.py', '.txt', '.xaml', '.xml', '.yml', '.yaml')
}) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    if ($content -match $secretPattern) {
        throw "Possible secret or local-machine path: $($file.FullName)"
    }
}

Write-Host "PASS public-source audit: $($files.Count) files; no forbidden assets or known secret patterns found."
