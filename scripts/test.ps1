param([switch]$UnitOnly)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    if (-not $UnitOnly -and (-not $env:TEST_DATABASE_CONNECTION -or -not $env:TEST_COMMAND_CONNECTION -or -not $env:TEST_QUERY_CONNECTION)) {
        throw 'Set TEST_DATABASE_CONNECTION, TEST_COMMAND_CONNECTION and TEST_QUERY_CONNECTION to a dedicated test database, or use -UnitOnly for a partial check without a coverage gate.'
    }
    $results = Join-Path $repo 'artifacts/backend'
    if (Test-Path $results) {
        $resolved = (Resolve-Path $results).Path
        if (-not $resolved.StartsWith($repo + [IO.Path]::DirectorySeparatorChar)) { throw 'Results path outside project.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    $testArgs = @('test', 'backend/Booking.Tests/Booking.Tests.csproj', '--configuration', 'Release', '--collect:Code Coverage', '--settings', 'backend/Booking.Tests/coverage.runsettings', '--results-directory', $results)
    if ($UnitOnly) { $testArgs += @('--filter', 'Category!=Integration') }
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
    if (-not $UnitOnly) {
        & python scripts/check-coverage.py $results
        if ($LASTEXITCODE -ne 0) { throw 'Backend coverage gate failed.' }
    }
    Push-Location frontend
    try {
        & npm ci
        if ($LASTEXITCODE -ne 0) { throw 'Frontend installation failed.' }
        & npm run test:coverage
        if ($LASTEXITCODE -ne 0) { throw 'Frontend tests or coverage gate failed.' }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    } finally { Pop-Location }
} finally { Pop-Location }
