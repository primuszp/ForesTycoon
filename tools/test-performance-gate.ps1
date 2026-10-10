param(
    [string]$Profile = 'docs/performance-profiles/rtx5060-i78700.json',
    [string]$FixturesDirectory = 'artifacts/performance-validation'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repositoryRoot
try {
    $fixtureRoot = (Resolve-Path -LiteralPath $FixturesDirectory).Path
    $profilePath = (Resolve-Path -LiteralPath $Profile).Path
    $cases = @(
        @{ Name = 'valid'; ExpectedError = $null },
        @{ Name = 'hardware'; ExpectedError = 'Hardware mismatch' },
        @{ Name = 'incomplete'; ExpectedError = 'Benchmark protocol mismatch' },
        @{ Name = 'summary'; ExpectedError = 'Summary disagrees with raw frames' },
        @{ Name = 'negative-frame'; ExpectedError = 'Invalid frame measurement' },
        @{ Name = 'nonfinite-frame'; ExpectedError = 'Invalid frame measurement' },
        @{ Name = 'missing-limit'; ExpectedError = 'Invalid measurement or threshold' },
        @{ Name = 'busy-gpu'; ExpectedError = 'Reference GPU is busy' },
        @{ Name = 'compute-client'; ExpectedError = 'Another compute process owns' },
        @{ Name = 'threshold'; ExpectedError = 'StationaryP95Ms measured' }
    )
    # Exercise the real gate while replacing only the expensive benchmark process with recorded data.
    function dotnet {
        param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
        if ($Arguments.Count -ne 5 -or $Arguments[1] -ne '--large-world-benchmark') { throw 'Unexpected benchmark invocation.' }
        $fixture = Get-Content -LiteralPath (Join-Path $fixtureRoot "$($Arguments[2])-$($Arguments[3]).json") -Raw | ConvertFrom-Json
        switch ($scenario.Name) {
            'hardware' { $fixture.Hardware.Renderer = 'Another GPU' }
            'incomplete' { $fixture.Samples = $fixture.Samples[0..598] }
            'summary' { $fixture.StationaryP95Ms = 0.001 }
            'negative-frame' { $fixture.Samples[0].UpdateMs = -1 }
            'nonfinite-frame' { $fixture.Samples[0].UpdateMs = 'NaN' }
        }
        $fixture | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Arguments[4] -Encoding utf8
        $global:LASTEXITCODE = 0
    }
    function nvidia-smi {
        $global:LASTEXITCODE = 0
        if ($args -contains '--query-compute-apps=pid,process_name') {
            if ($scenario.Name -eq 'compute-client') { '123, C:\work\python.exe' }
        }
        elseif ($scenario.Name -eq 'busy-gpu') { '76' } else { '0' }
    }
    foreach ($scenario in $cases) {
        $output = Join-Path 'artifacts/performance-gate-contract-tests' $scenario.Name
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        $selectedProfile = $profilePath
        if ($scenario.Name -in @('threshold', 'missing-limit')) {
            $modified = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
            if ($scenario.Name -eq 'threshold') { $modified.Cases[0].Limits.StationaryP95Ms = 0.001 }
            else { $modified.Cases[0].Limits.StationaryP95Ms = $null }
            $selectedProfile = Join-Path $output 'profile.json'
            $modified | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $selectedProfile -Encoding utf8
        }
        # A prior green manifest must never survive a subsequent failed run.
        @{ Status = 'Passed' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'validation.json') -Encoding utf8
        $caught = $null
        try { & (Join-Path $PSScriptRoot 'verify-performance.ps1') -Profile $selectedProfile -ResultsDirectory $output -SkipBuild }
        catch { $caught = $_.Exception.Message }
        $manifest = Get-Content -LiteralPath (Join-Path $output 'validation.json') -Raw | ConvertFrom-Json
        if ($null -eq $scenario.ExpectedError) {
            if ($caught -or $manifest.Status -ne 'Passed') { throw "Valid fixture rejected: $caught" }
        }
        elseif (-not $caught -or -not $caught.Contains($scenario.ExpectedError) -or $manifest.Status -ne 'Failed') {
            throw "Gate failed to reject $($scenario.Name) correctly: $caught; status $($manifest.Status)"
        }
        Write-Host "Performance gate contract: $($scenario.Name) passed."
    }
}
finally { Pop-Location }
