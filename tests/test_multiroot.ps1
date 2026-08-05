# test_multiroot.ps1 - tests multi-root aggregation and filter logic
# Usage: powershell -ExecutionPolicy Bypass -File tests\test_multiroot.ps1 [-ExePath <path>]
param(
    [string]$ExePath = ''
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$testsDir = Join-Path $root 'tests'
$csproj = Join-Path $root 'VideoTime.csproj'
$exeOut = Join-Path $root 'bin\Debug\VideoTime.exe'

# ---------- 1. 定位 MSBuild ----------
. (Join-Path $testsDir 'lib.ps1')
$tools = Get-VsBuildTools
$msbuild = $tools.MsBuild
Write-Host "[1/3] MSBuild: $msbuild"

# ---------- 2. Build if needed ----------
if (-not $ExePath) { $ExePath = $exeOut }
if (-not (Test-Path $ExePath)) {
    Write-Host '[2/3] Building VideoTime.exe ...'
    & $msbuild $csproj /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU /v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} else {
    Write-Host "[2/3] Using existing exe: $ExePath"
}

# ---------- 3. Compile test helper ----------
$csc = $tools.Csc
$refAsm = $tools.RefAsm
$helperExe = Join-Path $testsDir 'MultiRootFilterTest.exe'
$refArgs = @(
    (Join-Path $refAsm 'mscorlib.dll'),
    (Join-Path $refAsm 'System.dll'),
    (Join-Path $refAsm 'System.Core.dll'),
    (Join-Path $refAsm 'System.Drawing.dll'),
    (Join-Path $refAsm 'System.Windows.Forms.dll')
) | ForEach-Object { "/r:$_" }
Write-Host '[3/3] Compiling MultiRootFilterTest.dll ...'
& $csc /target:exe /out:$helperExe @refArgs "/r:$ExePath" (Join-Path $testsDir 'MultiRootFilterTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test helper compilation failed' }

# ---------- 4. Run tests ----------
Write-Host ''
Write-Host 'Running MultiRoot + Filter Tests ...'
$exeDir = Split-Path $ExePath -Parent
$copiedExe = Join-Path $testsDir 'VideoTime.exe'
Copy-Item $ExePath $copiedExe -Force -ErrorAction SilentlyContinue
Push-Location $testsDir
try {
    $out = & $helperExe 2>&1
}
finally {
    Pop-Location
    Remove-Item $copiedExe -Force -ErrorAction SilentlyContinue
    Remove-Item $helperExe -Force -ErrorAction SilentlyContinue
}
foreach ($line in $out) { Write-Host ([string]$line) }

$pass = 0; $fail = 0
foreach ($line in $out) {
    $s = [string]$line
    if ($s -match 'Total:\s*Passed\s*(\d+),\s*Failed\s*(\d+)') {
        $pass = [int]$Matches[1]; $fail = [int]$Matches[2]; break
    }
}
Write-Host ''
Write-Host ("Result: Passed {0}, Failed {1}" -f $pass, $fail)
if ($fail -gt 0) { Write-Host 'TESTS FAILED' -ForegroundColor Red; exit 1 }
Write-Host 'ALL TESTS PASSED' -ForegroundColor Green
exit 0
