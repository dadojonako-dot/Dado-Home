param(
    [Parameter(Mandatory=$true)][string]$BuiltRoot,
    [Parameter(Mandatory=$true)][string]$OutputZip
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$built=[IO.Path]::GetFullPath($BuiltRoot)
$zipPath=[IO.Path]::GetFullPath($OutputZip)
if(Test-Path -LiteralPath $zipPath){throw 'Archive already exists; choose a new output path.'}
if(!(Test-Path -LiteralPath (Join-Path $built 'app\DadoHome.Api.exe'))){throw 'BuiltRoot is not a built Windows kit.'}
New-Item -ItemType Directory -Force -Path (Split-Path $zipPath) | Out-Null
$sourceZip=Join-Path (Split-Path $zipPath) ('source-'+[Guid]::NewGuid().ToString('N')+'.zip')
try{
    $source=[IO.Compression.ZipFile]::Open($sourceZip,'Create')
    try{
        foreach($file in Get-ChildItem -LiteralPath $repo -Recurse -File -Force){
            $relative=$file.FullName.Substring($repo.Length+1).Replace('\','/')
            if($relative -match '(^|/)(\.git|node_modules|bin|obj|dist|runtime|artifacts)(/|$)' -or $relative -match '(^|/)\.env($|\.)'){continue}
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($source,$file.FullName,"Dado-Home-source/$relative",'Optimal') | Out-Null
        }
    }finally{$source.Dispose()}
    $bundle=[IO.Compression.ZipFile]::Open($zipPath,'Create')
    try{
        foreach($file in Get-ChildItem -LiteralPath $built -Recurse -File){
            $relative=$file.FullName.Substring($built.Length+1).Replace('\','/')
            # Explicit whitelist: local credentials, test data and logs cannot enter the archive.
            if($relative -notmatch '^(app/|postgres/|scripts/|docs/|prerequisites/|[^/]+\.(cmd|txt|json)$)'){continue}
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($bundle,$file.FullName,"Dado-Home-Pilot/$relative",'Optimal') | Out-Null
        }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($bundle,$sourceZip,'Dado-Home-Pilot/Исходники.zip','Optimal') | Out-Null
    }finally{$bundle.Dispose()}
    $hash=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    "$hash  $([IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath ($zipPath+'.sha256') -Encoding ASCII
    Write-Host "Archive: $zipPath"
    Write-Host "SHA256: $hash"
}finally{if(Test-Path -LiteralPath $sourceZip){Remove-Item -LiteralPath $sourceZip}}
