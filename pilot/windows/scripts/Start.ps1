param([switch]$NoBrowser)
. "$PSScriptRoot\Common.ps1"
if ($script:PilotRoot -match '[^\x00-\x7F]') {
    Write-Host 'Для PostgreSQL нужен путь без русских букв. Распакуйте весь комплект, например, в C:\Dado-Home-Pilot и повторите запуск.' -ForegroundColor Red
    exit 1
}
New-Item -ItemType Directory -Force -Path $script:Runtime | Out-Null
$launchLock = $null
$startedApi = $null
$startedDb = $false
$previousEnv = @{}
try {
    try { $launchLock = [IO.File]::Open((Join-Path $script:Runtime 'launch.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'Запуск или остановка уже выполняется. Подождите несколько секунд.' }
    if (!(Test-Path -LiteralPath $script:ApiExe) -or !(Test-Path -LiteralPath (Join-Path $script:PgBin 'pg_ctl.exe'))) {
        throw 'Комплект неполный. Распакуйте весь архив перед запуском.'
    }
    if (![Environment]::Is64BitProcess) { throw 'Запускайте комплект в 64-разрядной Windows.' }
    foreach ($dll in @('vcruntime140.dll','msvcp140.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $env:WINDIR "System32\$dll"))) {
            throw 'Нужен Microsoft Visual C++ Runtime. Запустите prerequisites\VC_redist.x64.exe, затем снова Запустить.cmd.'
        }
    }
    if (!(Test-Path -LiteralPath $script:SettingsFile)) {
        if (Test-Path -LiteralPath (Join-Path $script:DataDir 'PG_VERSION')) { throw 'Файл настроек утерян. Восстановите runtime/settings.json из вашей копии.' }
        $passwords = [ordered]@{}
        foreach ($role in @('Administrator','Cashier','Finance','OrderManager','Support')) { $passwords[$role] = New-RandomValue }
        $settings = [ordered]@{ WebPort=5088; DatabasePort=55432; DatabasePassword=(New-RandomValue); JwtKey=(New-RandomValue); Passwords=$passwords }
        $settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $script:SettingsFile -Encoding UTF8
        $names = @('Администратор','Кассир','Финансы','Менеджер заказов','Служба поддержки')
        $lines = @('DADO HOME — тестовые учётные записи', 'Адрес: http://127.0.0.1:5088', 'Пароли созданы на этом компьютере. Не публикуйте этот файл.', '')
        $i = 0
        foreach ($role in $passwords.Keys) {
            $i++
            $lines += "$($names[$i-1]) | +99290000000$i | $($passwords[$role])"
        }
        $lines += ''; $lines += 'После смены пароля через панель этот файл не обновляется автоматически.'
        $lines | Set-Content -LiteralPath (Join-Path $script:Runtime 'Access.txt') -Encoding UTF8
    }
    $settings = Read-Settings
    $url = "http://127.0.0.1:$($settings.WebPort)"
    $running = Get-PilotProcess
    if ($running) {
        if (!(Get-PilotHealth $settings)) { throw 'Приложение запущено, но пока не отвечает. Проверьте runtime/logs или выполните Остановить.cmd.' }
        Write-Host "Комплект уже работает: $url"
        if (!$NoBrowser) { Start-Process $url }
        return
    }
    Assert-FreePort $settings.WebPort
    New-Item -ItemType Directory -Force -Path (Join-Path $script:Runtime 'logs') | Out-Null
    if (!(Test-Path -LiteralPath (Join-Path $script:DataDir 'PG_VERSION'))) {
        Write-Host 'Первый запуск: создаём локальную тестовую базу...'
        $passwordFile = Join-Path $script:Runtime 'init-password.tmp'
        try {
            [IO.File]::WriteAllText($passwordFile, $settings.DatabasePassword, (New-Object Text.UTF8Encoding $false))
            Invoke-Pg 'initdb.exe' @('-D',$script:DataDir,'-U','dado_pilot','-A','scram-sha-256',"--pwfile=$passwordFile",'--encoding=UTF8','--locale=C')
        } finally { if (Test-Path -LiteralPath $passwordFile) { Remove-Item -LiteralPath $passwordFile } }
    }
    & (Join-Path $script:PgBin 'pg_ctl.exe') -D $script:DataDir status *> $null
    if ($LASTEXITCODE -ne 0) {
        Assert-FreePort $settings.DatabasePort
        Invoke-Pg 'pg_ctl.exe' @('-D',$script:DataDir,'-l',(Join-Path $script:Runtime 'logs\postgres.log'),'-o',"-h 127.0.0.1 -p $($settings.DatabasePort)",'-w','start')
        $startedDb = $true
    }
    $environment = @{
        PGPASSWORD=$settings.DatabasePassword
        ASPNETCORE_ENVIRONMENT='Pilot'
        ASPNETCORE_URLS=$url
        ConnectionStrings__DadoDb="Host=127.0.0.1;Port=$($settings.DatabasePort);Database=dado_pilot;Username=dado_pilot;Password=$($settings.DatabasePassword)"
        Jwt__Key=$settings.JwtKey
        Jwt__Issuer='DadoHome.Pilot'
        Jwt__Audience='DadoHome.Pilot'
        Pilot__Enabled='true'
        Logging__LogLevel__Default='Warning'
    }
    foreach ($role in $settings.Passwords.PSObject.Properties) { $environment["Pilot__Passwords__$($role.Name)"] = $role.Value }
    foreach ($key in $environment.Keys) {
        $previousEnv[$key] = [Environment]::GetEnvironmentVariable($key,'Process')
        [Environment]::SetEnvironmentVariable($key,$environment[$key],'Process')
    }
    $exists = & (Join-Path $script:PgBin 'psql.exe') -h 127.0.0.1 -p $settings.DatabasePort -U dado_pilot -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='dado_pilot'"
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось подключиться к базе комплекта. Смотрите runtime/logs/postgres.log.' }
    if ($exists -ne '1') { Invoke-Pg 'createdb.exe' @('-h','127.0.0.1','-p',"$($settings.DatabasePort)",'-U','dado_pilot','dado_pilot') }
    Write-Host 'Запускаем DADO HOME...'
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $startedApi = Start-Process -FilePath $script:ApiExe -WorkingDirectory (Join-Path $script:PilotRoot 'app') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $script:Runtime "logs\app-$stamp.log") -RedirectStandardError (Join-Path $script:Runtime "logs\app-$stamp-error.log")
    @{ProcessId=$startedApi.Id;StartTicks=$startedApi.StartTime.ToUniversalTime().Ticks.ToString()} | ConvertTo-Json | Set-Content -LiteralPath $script:StateFile -Encoding UTF8
    $ready = $false
    for ($attempt=0; $attempt -lt 60; $attempt++) {
        if ($startedApi.HasExited) { throw 'Приложение завершилось при запуске. Смотрите runtime/logs.' }
        $health = Get-PilotHealth $settings
        if ($health -and $health.status -eq 'ready' -and $health.pilot) { $ready=$true; break }
        Start-Sleep -Milliseconds 500
    }
    if (!$ready) { throw 'Запуск занял слишком много времени. Смотрите runtime/logs.' }
    Write-Host "Готово: $url" -ForegroundColor Green
    Write-Host 'Логины и пароли: runtime\Access.txt'
    Write-Host 'Чтобы закончить работу, нажмите Остановить.cmd. Данные сохранятся.'
    if (!$NoBrowser) { Start-Process $url }
} catch {
    if ($startedApi -and !$startedApi.HasExited) { Stop-Process -Id $startedApi.Id -ErrorAction SilentlyContinue }
    if ($startedDb) { & (Join-Path $script:PgBin 'pg_ctl.exe') -D $script:DataDir -m fast -w stop *> $null }
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
} finally {
    foreach ($key in $previousEnv.Keys) { [Environment]::SetEnvironmentVariable($key,$previousEnv[$key],'Process') }
    if ($launchLock) { $launchLock.Dispose() }
}

