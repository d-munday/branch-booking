$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
& (Join-Path $repo '.local/node_modules/@embedded-postgres/windows-x64/native/bin/pg_ctl.exe') -D (Join-Path $repo '.local/pgdata') -m fast -w stop
if ($LASTEXITCODE -ne 0) { throw 'Database stop failed.' }
