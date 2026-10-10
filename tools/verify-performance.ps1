param(
    [Parameter(Mandatory = $true)][string]$Profile,
    [string]$ResultsDirectory = 'artifacts/performance-validation',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repositoryRoot
try {
    $evidence = $null
    $directory = $ResultsDirectory
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $validationPath = Join-Path $directory 'validation.json'
    @{ Status = 'Running'; StartedUtc = [DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath $validationPath -Encoding utf8
    $limits = Get-Content -LiteralPath $Profile -Raw | ConvertFrom-Json
    if ($limits.Protocol -ne 'large-world-v1' -or $limits.Cases.Count -ne 4) {
        throw 'The profile must specify large-world-v1 and all four cases.'
    }
    if (-not $SkipBuild) {
        & dotnet build ForesTycoon.sln -c Release -warnaserror --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Performance build failed.' }
    }
    $assembly = 'ForesTycoon/bin/Release/net8.0/ForesTycoon.dll'
    $previousCpu = $env:FORESTYCOON_BENCHMARK_CPU
    try {
        if ($IsWindows) {
            $env:FORESTYCOON_BENCHMARK_CPU = (Get-CimInstance Win32_Processor | Select-Object -ExpandProperty Name) -join ', '
        }
        elseif (Test-Path -LiteralPath '/proc/cpuinfo') {
            $cpuLine = Get-Content -LiteralPath '/proc/cpuinfo' | Where-Object { $_ -match '^model name\s*:' } | Select-Object -First 1
            $env:FORESTYCOON_BENCHMARK_CPU = ($cpuLine -split ':', 2)[1].Trim()
        }
        $failures = [System.Collections.Generic.List[string]]::new()
        $seenCases = [System.Collections.Generic.HashSet[string]]::new()
        $idleGpuSamples = [System.Collections.Generic.List[int]]::new()
        foreach ($case in $limits.Cases) {
            $key = '{0}-{1}' -f $case.Tiles, $case.Weather
            if ($case.Tiles -notin @(64, 128) -or $case.Weather -notin @('Sunny', 'Storm') -or -not $seenCases.Add($key)) {
                throw "Invalid or duplicate profile case: $key"
            }
            $output = Join-Path $directory "$key.json"
            if ($limits.Hardware.Vendor -eq 'NVIDIA Corporation') {
                # Allow the previous benchmark's utilization averaging window to drain.
                Start-Sleep -Seconds 2
                if (-not (Get-Command nvidia-smi -ErrorAction SilentlyContinue)) { throw 'nvidia-smi is required to verify that the reference GPU is idle.' }
                $gpuLoad = @(& nvidia-smi '--query-gpu=utilization.gpu' '--format=csv,noheader,nounits')
                if ($LASTEXITCODE -ne 0 -or $gpuLoad.Count -ne 1 -or $gpuLoad[0] -notmatch '^\s*\d+\s*$') {
                    throw 'Unable to verify utilization of the single reference GPU.'
                }
                $idleGpuSamples.Add([int]$gpuLoad[0])
                if ([int]$gpuLoad[0] -gt 10) { throw "Reference GPU is busy ($($gpuLoad[0])% utilization); finish other GPU work before benchmarking." }
                $gpuProcesses = @(& nvidia-smi '--query-compute-apps=pid,process_name' '--format=csv,noheader')
                if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect other GPU processes.' }
                foreach ($gpuProcess in $gpuProcesses) {
                    $parts = $gpuProcess -split ',', 2
                    if ($parts.Count -eq 2 -and (Split-Path -Leaf $parts[1].Trim()) -in $limits.CompetingGpuProcessNames) {
                        throw 'Another compute process owns the reference GPU; finish that workload before benchmarking.'
                    }
                }
            }
            # Each invocation is a fresh process; no previous map contaminates its process high water.
            & dotnet $assembly --large-world-benchmark $case.Tiles $case.Weather $output
            if ($LASTEXITCODE -ne 0) { throw "Benchmark $key failed with exit code $LASTEXITCODE" }
            $result = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
            foreach ($field in @('Vendor', 'Renderer', 'Driver', 'OS', 'Runtime', 'Architecture', 'LogicalProcessors', 'Cpu')) {
                if ($null -eq $limits.Hardware.$field -or $result.Hardware.$field -cne $limits.Hardware.$field) {
                    throw "Hardware mismatch for $field; measured '$($result.Hardware.$field)', required '$($limits.Hardware.$field)'. Review a new baseline on this machine."
                }
            }
            if ($result.Protocol -ne $limits.Protocol -or $result.Tiles -ne $case.Tiles -or $result.Weather -cne $case.Weather -or
                $result.MeasuredFrames -ne 600 -or $result.WarmupFrames -ne 180 -or $result.Samples.Count -ne 600 -or
                $result.Width -ne 1280 -or $result.Height -ne 720 -or $result.Seed -ne 42 -or $result.Quality -cne 'High' -or
                $result.Msaa -ne 4 -or $result.TickRate -ne 30 -or $result.ForestYearSeconds -ne 900) {
                throw "Benchmark protocol mismatch: $key"
            }
            $boundaryCount = @($result.Samples | Where-Object MonthlyBoundary).Count
            $movingCount = @($result.Samples | Where-Object MovingCamera).Count
            if ($boundaryCount -ne 1 -or $movingCount -ne 300) { throw "Incomplete workload: $key" }
            for ($index = 0; $index -lt 600; $index++) {
                $sample = $result.Samples[$index]
                if ($sample.Frame -ne $index -or $sample.MovingCamera -ne ($index -ge 300) -or $sample.VisibleChunks -le 0) {
                    throw "Invalid frame or camera workload: $key/$index"
                }
                foreach ($field in @('UpdateMs', 'RenderMs', 'TotalMs')) {
                    if ($null -eq $sample.$field -or -not [double]::IsFinite([double]$sample.$field) -or $sample.$field -lt 0) {
                        throw "Invalid frame measurement: $key/$index/$field"
                    }
                }
                if ($sample.TotalMs + 0.000001 -lt $sample.UpdateMs + $sample.RenderMs) { throw "Inconsistent frame duration: $key/$index" }
            }
            $stationary = @($result.Samples[0..299].TotalMs | Sort-Object)
            $moving = @($result.Samples[300..599].TotalMs | Sort-Object)
            $expected = @{
                StationaryP95Ms = $stationary[284]
                MovingP95Ms = $moving[284]
                MaxFrameMs = ($result.Samples.TotalMs | Measure-Object -Maximum).Maximum
                MonthBoundaryMs = ($result.Samples | Where-Object MonthlyBoundary).TotalMs
            }
            foreach ($field in $expected.Keys) {
                if ($null -eq $result.$field -or [Math]::Abs([double]$result.$field - [double]$expected[$field]) -gt 0.000001) {
                    throw "Summary disagrees with raw frames: $key/$field"
                }
            }
            foreach ($field in @('StationaryP95Ms', 'MovingP95Ms', 'MaxFrameMs', 'MonthBoundaryMs',
                'ConstructorMs', 'FirstDrawMs', 'TransactionLoadMs', 'LoadedFirstDrawMs', 'ProcessPeakWorkingSetBytes')) {
                $value = $result.$field
                $limit = $case.Limits.$field
                if ($null -eq $value -or $null -eq $limit -or -not [double]::IsFinite([double]$value) -or
                    -not [double]::IsFinite([double]$limit) -or $value -le 0 -or $limit -le 0) {
                    throw "Invalid measurement or threshold: $key/$field"
                }
                if ($value -gt $limit) { $failures.Add("$key/$field measured $value, limit $limit") }
            }
            $value = $result.LoadMemory.PeakWorkingSetBytes
            $limit = $case.Limits.LoadPeakWorkingSetBytes
            if ($null -eq $value -or $null -eq $limit -or $value -le 0 -or $limit -le 0 -or
                -not [double]::IsFinite([double]$value) -or -not [double]::IsFinite([double]$limit)) {
                throw "Invalid load memory measurement or threshold: $key"
            }
            if ($value -gt $limit) { $failures.Add("$key/load peak measured $value bytes, limit $limit") }
        }
        $evidence = [ordered]@{
            Status = $(if ($failures.Count -eq 0) { 'Passed' } else { 'Failed' })
            Protocol = $limits.Protocol
            Profile = (Resolve-Path -LiteralPath $Profile).Path
            ProfileSha256 = (Get-FileHash -LiteralPath $Profile -Algorithm SHA256).Hash
            AssemblySha256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
            # Rendering/Engine/Map can change without changing the game's assembly bytes.
            AssemblyHashes = @(Get-ChildItem -LiteralPath (Split-Path -Parent $assembly) -Filter '*.dll' -File |
                Sort-Object Name | ForEach-Object {
                    [ordered]@{ Name = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
                })
            GitRevision = (& git rev-parse HEAD)
            GitStatus = @(& git status --short)
            IdleGpuPercentBeforeCases = @($idleGpuSamples)
            Failures = @($failures)
        }
        $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $validationPath -Encoding utf8
        if ($failures.Count -gt 0) { throw ($failures -join "`n") }
        Write-Host 'All four large-world performance cases passed on the required hardware.'
    }
    finally { $env:FORESTYCOON_BENCHMARK_CPU = $previousCpu }
}
catch {
    if ($validationPath) {
        if ($null -eq $evidence) { $evidence = [ordered]@{} }
        $evidence['Status'] = 'Failed'
        $evidence['Error'] = $_.Exception.Message
        $evidence['FinishedUtc'] = [DateTime]::UtcNow.ToString('o')
        $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $validationPath -Encoding utf8
    }
    throw
}
finally { Pop-Location }
