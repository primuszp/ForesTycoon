param([switch]$Native, [switch]$SkipBuild, [switch]$Stress, [ValidateRange(1,120)][int]$StressMonths = 24)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repositoryRoot
try {
    function Invoke-DotNet([string[]]$Arguments) {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
    }
    if (-not $SkipBuild) {
        Invoke-DotNet @('restore', 'ForesTycoon.sln')
        Invoke-DotNet @('build', 'ForesTycoon.sln', '-c', 'Release', '--no-restore', '-warnaserror', '--verbosity', 'minimal')
    }
    Invoke-DotNet @('test', 'ForesTycoon.Tests', '-c', 'Release', '--no-build', '--no-restore',
        '--logger', 'trx', '--logger', 'console;verbosity=normal',
        '--blame-hang-timeout', '180s', '--blame-hang-dump-type', 'none',
        '--results-directory', 'artifacts/engine-validation/tests')
    if ($Stress) {
        $gameAssembly = 'ForesTycoon/bin/Release/net8.0/ForesTycoon.dll'
        Invoke-DotNet @($gameAssembly, '--memory-performance-probe', 'artifacts/engine-validation/memory', '40')
        Invoke-DotNet @($gameAssembly, '--background-wait-stress', '60')
        Invoke-DotNet @($gameAssembly, '--simulation-soak', "$StressMonths", '16', '42', 'artifacts/engine-validation/soak-16')
        Invoke-DotNet @($gameAssembly, '--simulation-soak', "$StressMonths", '32', '7', 'artifacts/engine-validation/soak-32')
    }
    if ($Native) {
        $gameAssembly = 'ForesTycoon/bin/Release/net8.0/ForesTycoon.dll'
        foreach ($smoke in @('--render-environment-smoke-test', '--checkpoint-smoke-test', '--forest-smoke-test', '--graphics-smoke-test',
            '--seasonal-weather-smoke-test', '--tree-growth-smoke-test', '--dendro-tree-smoke-test', '--material-alpha-smoke-test', '--truck-smoke-test', '--wildlife-smoke-test', '--ui-font-smoke-test', '--smoke-test')) {
            Invoke-DotNet @($gameAssembly, $smoke)
        }
        Invoke-DotNet @('ForesTycoon.Editor/bin/Release/net8.0/ForesTycoon.Editor.dll', '--smoke-test')
        Invoke-DotNet @('ForesTycoon.Editor/bin/Release/net8.0/ForesTycoon.Editor.dll', '--window-lifecycle-smoke-test')
    }
}
finally { Pop-Location }
