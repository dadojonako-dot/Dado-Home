$ErrorActionPreference = 'Stop'
$script:PilotRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script:Runtime = Join-Path $script:PilotRoot 'runtime'
$script:DataDir = Join-Path $script:Runtime 'pgdata'
$script:PgBin = Join-Path $script:PilotRoot 'postgres\bin'
$script:ApiExe = Join-Path $script:PilotRoot 'app\DadoHome.Api.exe'
$script:SettingsFile = Join-Path $script:Runtime 'settings.json'
$script:StateFile = Join-Path $script:Runtime 'process.json'

function New-RandomValue {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return [Convert]::ToBase64String($bytes).Replace('+','-').Replace('/','_').TrimEnd('=')
}
function Read-Settings {
    if (!(Test-Path -LiteralPath $script:SettingsFile)) { throw 'Настройки не найдены. Сначала запустите Запустить.cmd.' }
    return Get-Content -LiteralPath $script:SettingsFile -Raw -Encoding UTF8 | ConvertFrom-Json
}
function Get-PilotProcess {
    if (!(Test-Path -LiteralPath $script:StateFile)) { return $null }
    $state = Get-Content -LiteralPath $script:StateFile -Raw | ConvertFrom-Json
    $process = Get-Process -Id $state.ProcessId -ErrorAction SilentlyContinue
    if ($process -and $process.Path -eq $script:ApiExe -and $process.StartTime.ToUniversalTime().Ticks.ToString() -eq $state.StartTicks) { return $process }
    return $null
}
function Assert-FreePort([int]$Port) {
    $listener = New-Object Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), $Port
    try { $listener.Start() } catch { throw "Порт $Port занят. Остановите другой экземпляр комплекта или измените порт в runtime/settings.json." }
    finally { $listener.Stop() }
}
function Invoke-Pg([string]$Name, [string[]]$Arguments) {
    & (Join-Path $script:PgBin $Name) @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Ошибка $Name (код $LASTEXITCODE). Подробности в runtime/logs." }
}
function Get-PilotHealth($Settings) {
    try { return Invoke-RestMethod -Uri "http://127.0.0.1:$($Settings.WebPort)/health/ready" -TimeoutSec 2 }
    catch { return $null }
}
