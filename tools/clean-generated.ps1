# Removes old diagnostic outputs; build directories and the local runner stay intact.
[CmdletBinding(SupportsShouldProcess = $true)]
param([ValidateRange(1, 8760)][int]$OlderThanHours = 2)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
if (-not (Test-Path -LiteralPath $artifactRoot)) { return }
if ((Get-Item -LiteralPath $artifactRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'The artifacts directory must not be a filesystem link.'
}
$protectedNames = @(
    'github-runner', 'seasonal-256x', 'engine-validation',
    'performance-validation', 'large-world-benchmark',
    'solid-crowns-build.log', 'solid-crowns-native.log', 'solid-crowns-tests.log',
    'seasonal-256x-viewport.log'
)
$cutoff = (Get-Date).AddHours(-$OlderThanHours)
$deleted = 0
$bytes = 0L
foreach ($item in Get-ChildItem -LiteralPath $artifactRoot -Force) {
    if ($item.Name -in $protectedNames) { continue }
    $target = [IO.Path]::GetFullPath($item.FullName)
    if (-not $target.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe cleanup path: $target" }
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
    $contents = if ($item.PSIsContainer) {
        @(Get-ChildItem -LiteralPath $target -Recurse -Force)
    } else { @() }
    if (@($contents | Where-Object {
        $_.Attributes -band [IO.FileAttributes]::ReparsePoint
    }).Count -gt 0) { continue }
    if ($item.LastWriteTime -gt $cutoff -or @($contents | Where-Object {
        $_.LastWriteTime -gt $cutoff
    }).Count -gt 0) { continue }
    $relative = 'artifacts/' + $item.Name
    $tracked = @(& git -C $repositoryRoot ls-files -- $relative)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot check tracked files; cleanup stopped.' }
    if ($tracked.Count -gt 0) { throw "Refusing to delete tracked files: $relative" }
    $files = if ($item.PSIsContainer) {
        @($contents | Where-Object { -not $_.PSIsContainer })
    } else { @($item) }
    $size = ($files | Measure-Object Length -Sum).Sum
    if ($PSCmdlet.ShouldProcess($target, 'Delete old generated output')) {
        Remove-Item -LiteralPath $target -Recurse -Force
        $deleted++
        $bytes += $size
    }
}
[pscustomobject]@{ DeletedEntries = $deleted; FreedMiB = [math]::Round($bytes / 1MB, 1) }
