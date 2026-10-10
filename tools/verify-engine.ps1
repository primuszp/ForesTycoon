param([switch]$Native, [switch]$SkipBuild)

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
        '--logger', 'trx', '--results-directory', 'artifacts/engine-validation/tests')
    if ($Native) {
        $gameAssembly = 'ForesTycoon/bin/Release/net8.0/ForesTycoon.dll'
        foreach ($smoke in @('--render-environment-smoke-test', '--checkpoint-smoke-test', '--forest-smoke-test', '--graphics-smoke-test',
            '--tree-growth-smoke-test', '--material-alpha-smoke-test', '--truck-smoke-test', '--wildlife-smoke-test', '--ui-font-smoke-test', '--smoke-test')) {
            Invoke-DotNet @($gameAssembly, $smoke)
        }
        Invoke-DotNet @('ForesTycoon.Editor/bin/Release/net8.0/ForesTycoon.Editor.dll', '--smoke-test')
        Invoke-DotNet @('ForesTycoon.Editor/bin/Release/net8.0/ForesTycoon.Editor.dll', '--window-lifecycle-smoke-test')
    }
}
finally { Pop-Location }
