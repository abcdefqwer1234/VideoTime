# run_probes.ps1 - compile and run GUI-regression probes (#2 cancel race, #3 stale selection, #4 CSV invariant)
# Usage: powershell -ExecutionPolicy Bypass -File tests\run_probes.ps1 [-ExePath <path>]
param(
    [string]$ExePath = ''
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$testsDir = Join-Path $root 'tests'
$csproj = Join-Path $root 'VideoTime.csproj'
$exeOut = Join-Path $root 'bin\Debug\VideoTime.exe'

# ---------- 1. locate MSBuild ----------
. (Join-Path $testsDir 'lib.ps1')
$tools = Get-VsBuildTools
$msbuild = $tools.MsBuild
Write-Host "MSBuild: $msbuild"

# ---------- 2. build if needed ----------
if (-not $ExePath) { $ExePath = $exeOut }
if (-not (Test-Path $ExePath)) {
    Write-Host 'Building VideoTime.exe (Debug) ...'
    & $msbuild $csproj /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU /v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} else {
    Write-Host "Using existing exe: $ExePath"
}

$csc = $tools.Csc
$refAsm = $tools.RefAsm
$refArgs = @(
    (Join-Path $refAsm 'mscorlib.dll'),
    (Join-Path $refAsm 'System.dll'),
    (Join-Path $refAsm 'System.Core.dll'),
    (Join-Path $refAsm 'System.Drawing.dll'),
    (Join-Path $refAsm 'System.Windows.Forms.dll')
) | ForEach-Object { "/r:$_" }

$probes = @('ProbeCancelRace', 'ProbeStaleSelection', 'ProbeCsvInvariant')
$overallFail = 0

foreach ($probe in $probes) {
    Write-Host ''
    Write-Host "=== Probe: $probe ==="
    $src = Join-Path $testsDir "$probe.cs"
    $helperExe = Join-Path $testsDir "$probe.exe"

    Write-Host "Compiling $probe.cs ..."
    & $csc /nologo /target:exe "/out:$helperExe" @refArgs "/r:$ExePath" $src
    if ($LASTEXITCODE -ne 0) { Write-Host "Compile failed: $probe" -ForegroundColor Red; $overallFail = 1; continue }

    $runDir = Join-Path $root 'bin\Debug'
    $copied = Join-Path $runDir "$probe.exe"
    Copy-Item $helperExe $copied -Force

    $out = @()
    Push-Location $runDir
    try {
        $out = & $copied 2>&1
    }
    finally {
        Pop-Location
        Remove-Item $helperExe -Force -ErrorAction SilentlyContinue
        Remove-Item $copied -Force -ErrorAction SilentlyContinue
    }
    foreach ($line in $out) { Write-Host ([string]$line) }

    $fail = 0
    foreach ($line in $out) {
        $s = [string]$line
        if ($s -match 'Total:\s*Passed\s*(\d+),\s*Failed\s*(\d+)') { $fail = [int]$Matches[2]; break }
    }
    if ($fail -gt 0) { Write-Host "PROBE FAILED: $probe" -ForegroundColor Red; $overallFail = 1 }
    else { Write-Host "PROBE PASSED: $probe" -ForegroundColor Green }
}

Write-Host ''
if ($overallFail -gt 0) { Write-Host 'PROBES FAILED' -ForegroundColor Red; exit 1 }
Write-Host 'ALL PROBES PASSED' -ForegroundColor Green
exit 0
