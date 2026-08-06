# run_probes.ps1 - compile and run GUI-regression probes (cancel race / stale selection / CSV invariant / filter scalability / scanner throughput)
# 用法: powershell -ExecutionPolicy Bypass -File tests\run_probes.ps1 [-ExePath <path>] [-IncludeE2E]
#   -IncludeE2E 额外运行 ProbeFilterClearFreeze（真实 E:\ 扫描 + 过滤/清除卡死回归，重，依赖 E:\ 存在，默认跳过）
param(
    [string]$ExePath = '',
    [switch]$IncludeE2E
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

$probes = @('ProbeCancelRace', 'ProbeStaleSelection', 'ProbeCsvInvariant', 'ProbeFilterScalability', 'ProbeScannerThroughput')
if ($IncludeE2E) { $probes += 'ProbeFilterClearFreeze' }
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
    $exitCode = -1
    Push-Location $runDir
    try {
        $out = @(& $copied 2>&1)
        $exitCode = $LASTEXITCODE
    }
    catch {
        # 探针崩溃时 CLR 把异常堆栈写向 stderr，PowerShell 会包装成 NativeCommandError（且因
        # $ErrorActionPreference=Stop 变成终止错误）。捕获并保留错误文本，判 FAIL 后继续下一个探针，
        # 避免一个探针崩溃掩盖其余探针的回归结果。
        $out += '[crash] ' + ([string]$_).Trim()
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq -1) { $exitCode = 255 }
    }
    finally {
        Pop-Location
        Remove-Item $helperExe -Force -ErrorAction SilentlyContinue
        Remove-Item $copied -Force -ErrorAction SilentlyContinue
    }
    foreach ($line in $out) { Write-Host ([string]$line) }

    # 判定规则（堵住"崩溃误报 PASS"）：
    #   - 输出含 SKIP:  -> PROBE SKIPPED（如 ProbeFilterClearFreeze 无 E:\ 时）
    #   - 其余情况：退出码非 0、缺少 Total 汇总行（疑似崩溃/异常退出）、或 Failed>0，任一命中即判 FAIL
    $totalLine = $null
    $failCount = 0
    foreach ($line in $out) {
        $s = [string]$line
        if ($s -match 'Total:\s*Passed\s*(\d+),\s*Failed\s*(\d+)') { $totalLine = $s; $failCount = [int]$Matches[2]; break }
    }
    $isSkip = @($out | Where-Object { ([string]$_) -match 'SKIP:' }).Count -gt 0
    if ($isSkip) {
        Write-Host "PROBE SKIPPED: $probe" -ForegroundColor Yellow
        continue
    }
    $probeFailed = $false
    $reason = ''
    if ($failCount -gt 0) { $probeFailed = $true; $reason = "断言失败 $failCount 项" }
    elseif ($null -eq $totalLine) { $probeFailed = $true; $reason = '未输出 Total 汇总行，疑似崩溃/异常退出' }
    elseif ($exitCode -ne 0) { $probeFailed = $true; $reason = "退出码非 0（$exitCode）" }
    if ($probeFailed) { Write-Host "PROBE FAILED: $probe（$reason）" -ForegroundColor Red; $overallFail = 1 }
    else { Write-Host "PROBE PASSED: $probe（exit=$exitCode）" -ForegroundColor Green }
}

Write-Host ''
if ($overallFail -gt 0) { Write-Host 'PROBES FAILED' -ForegroundColor Red; exit 1 }
Write-Host 'ALL PROBES PASSED' -ForegroundColor Green
exit 0
