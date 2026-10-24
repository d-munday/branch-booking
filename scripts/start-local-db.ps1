$ErrorActionPreference = 'Stop'
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
    throw 'This optional portable harness supports Windows x64. Use Docker or a local PostgreSQL instance elsewhere.'
}
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    $env:npm_config_cache = Join-Path $repo '.local/npm-cache'
    $native = Join-Path $repo '.local/node_modules/@embedded-postgres/windows-x64/native/bin'
    if (-not (Test-Path (Join-Path $native 'postgres.exe'))) {
        & npm install --prefix .local --no-save --ignore-scripts @embedded-postgres/windows-x64@18.4.0-beta.17 pg@8.16.3
        if ($LASTEXITCODE -ne 0) { throw 'Portable PostgreSQL installation failed.' }
    }
    $data = Join-Path $repo '.local/pgdata'
    if (-not (Test-Path (Join-Path $data 'PG_VERSION'))) {
        & (Join-Path $native 'initdb.exe') -D $data -U booking_owner -A trust --encoding=UTF8 --locale=C
        if ($LASTEXITCODE -ne 0) { throw 'Database initialization failed.' }
    }
    & (Join-Path $native 'pg_ctl.exe') -D $data status 2>$null
    if ($LASTEXITCODE -ne 0) {
        & (Join-Path $native 'pg_ctl.exe') -D $data -l (Join-Path $repo '.local/postgres.log') -o '-p 55432 -h 127.0.0.1' -w start
        if ($LASTEXITCODE -ne 0) { throw 'Database start failed.' }
    }
    & node scripts/init-local-db.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Schema initialization failed.' }
    $settings = Get-Content '.local/db-env.json' -Raw | ConvertFrom-Json
    foreach ($property in $settings.PSObject.Properties) {
        [Environment]::SetEnvironmentVariable($property.Name, $property.Value, 'Process')
    }
    Write-Output 'PostgreSQL is ready on 127.0.0.1:55432. Test connection variables are set for this shell.'
    Write-Output 'This isolated development database uses localhost trust authentication. Never expose it or use it in deployment.'
} finally { Pop-Location }
