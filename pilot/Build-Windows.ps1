param(
    [Parameter(Mandatory=$true)][string]$PostgresRoot,
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\Dado-Home-Pilot'),
    [string]$Dotnet = 'dotnet',
    [string]$Node = 'node',
    [string]$VcRedist
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination=[IO.Path]::GetFullPath($Output)
if ((Test-Path -LiteralPath $destination) -and (Get-ChildItem -LiteralPath $destination -Force | Select-Object -First 1)) { throw 'Output must be a new or empty directory. Existing pilot data is never overwritten.' }
if (!(Test-Path -LiteralPath (Join-Path $PostgresRoot 'bin\postgres.exe'))) { throw 'PostgresRoot must point to extracted PostgreSQL Windows x64 binaries.' }
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$oldApi=[Environment]::GetEnvironmentVariable('VITE_API_URL','Process')
try {
    # An explicit slash is normalized to same-origin by api.ts; works in Windows PowerShell 5.1 too.
    $env:VITE_API_URL='/'
    Push-Location (Join-Path $repo 'admin')
    try {
        & $Node node_modules/typescript/bin/tsc -p tsconfig.json
        if($LASTEXITCODE -ne 0){throw 'TypeScript check failed.'}
        & $Node node_modules/vite/bin/vite.js build
        if($LASTEXITCODE -ne 0){throw 'Admin build failed.'}
    } finally { Pop-Location }
} finally { [Environment]::SetEnvironmentVariable('VITE_API_URL',$oldApi,'Process') }
& $Dotnet publish (Join-Path $repo 'backend\DadoHome.Api') -c Release -r win-x64 --self-contained true -o (Join-Path $destination 'app') --nologo
if($LASTEXITCODE -ne 0){throw 'Backend publish failed.'}
Copy-Item -Path (Join-Path $repo 'admin\dist') -Destination (Join-Path $destination 'app\wwwroot') -Recurse
Copy-Item -Path (Join-Path $PSScriptRoot 'windows\*') -Destination $destination -Recurse
New-Item -ItemType Directory -Force -Path (Join-Path $destination 'postgres') | Out-Null
foreach($folder in @('bin','lib','share')){Copy-Item -LiteralPath (Join-Path $PostgresRoot $folder) -Destination (Join-Path $destination "postgres\$folder") -Recurse}
New-Item -ItemType Directory -Force -Path (Join-Path $destination 'docs') | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'docs\POS.md') -Destination (Join-Path $destination 'docs\POS.md')
foreach($name in @('SECURITY.md','EXPENSES.md')){Copy-Item -LiteralPath (Join-Path $repo "docs\$name") -Destination (Join-Path $destination "docs\$name")}
Copy-Item -LiteralPath (Join-Path $PostgresRoot 'doc\postgresql\html\legalnotice.html') -Destination (Join-Path $destination 'docs\PostgreSQL-license.html')
if($VcRedist){
    $signature=Get-AuthenticodeSignature -LiteralPath $VcRedist
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation'){throw 'VC Runtime installer must have a valid Microsoft signature.'}
    New-Item -ItemType Directory -Force -Path (Join-Path $destination 'prerequisites') | Out-Null
    Copy-Item -LiteralPath $VcRedist -Destination (Join-Path $destination 'prerequisites\VC_redist.x64.exe')
}
# BOM is needed for Russian text under Windows PowerShell 5.1.
foreach($file in Get-ChildItem -LiteralPath (Join-Path $destination 'scripts') -Filter '*.ps1'){
    $text=[IO.File]::ReadAllText($file.FullName)
    [IO.File]::WriteAllText($file.FullName,$text,(New-Object Text.UTF8Encoding $true))
}
@{
    Name='Dado Home Windows Pilot'; BuiltAtUtc=[DateTime]::UtcNow.ToString('o')
    Platform='Windows x64'; Runtime='Self-contained .NET 8'
    PostgreSQL=(& (Join-Path $PostgresRoot 'bin\postgres.exe') --version)
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'build-info.json') -Encoding UTF8
Write-Host "Built: $destination"
