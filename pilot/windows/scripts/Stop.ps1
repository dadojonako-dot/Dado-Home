. "$PSScriptRoot\Common.ps1"
$launchLock=$null
try {
    if (!(Test-Path -LiteralPath $script:Runtime)) { Write-Host 'Комплект не запущен.'; return }
    $launchLock=[IO.File]::Open((Join-Path $script:Runtime 'launch.lock'),'OpenOrCreate','ReadWrite','None')
    $process=Get-PilotProcess
    if ($process) { Stop-Process -Id $process.Id; $process.WaitForExit(10000) | Out-Null }
    if (Test-Path -LiteralPath (Join-Path $script:DataDir 'PG_VERSION')) {
        & (Join-Path $script:PgBin 'pg_ctl.exe') -D $script:DataDir status *> $null
        if ($LASTEXITCODE -eq 0) { Invoke-Pg 'pg_ctl.exe' @('-D',$script:DataDir,'-m','fast','-w','stop') }
    }
    if (Test-Path -LiteralPath $script:StateFile) { Remove-Item -LiteralPath $script:StateFile }
    Write-Host 'DADO HOME остановлен. Чеки, остатки и учётные записи сохранены.' -ForegroundColor Green
} catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }
finally { if($launchLock){$launchLock.Dispose()} }
