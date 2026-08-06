# 一键回归：编译 + 编译测试 helper + 运行全部测试
# 用法: powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1 [-ExePath <path>] [-UserConfig <path>] [-IncludeProbes] [-IncludeE2E]
#   -IncludeProbes  额外运行 GUI 探针回归（run_probes.ps1），把探针纳入一键回归
#   -IncludeE2E     连同真实 E:\ 卡死回归探针一并运行（重、依赖 E:\ 存在，需配合 -IncludeProbes）
param(
    [string]$ExePath = '',
    [string]$UserConfig = '',
    [switch]$IncludeProbes,
    [switch]$IncludeE2E
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root   # tests\.. -> 项目根
$testsDir = Join-Path $root 'tests'
$csproj = Join-Path $root 'VideoTime.csproj'
$exeOut = Join-Path $root 'bin\Debug\VideoTime.exe'

# ---------- 1. 定位 MSBuild ----------
. (Join-Path $testsDir 'lib.ps1')
$tools = Get-VsBuildTools
$msbuild = $tools.MsBuild
Write-Host "[1/5] MSBuild: $msbuild"

# ---------- 2. 编译 Debug ----------
Write-Host '[2/5] 编译 VideoTime.csproj (Debug/AnyCPU) ...'
& $msbuild $csproj /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU /v:minimal
if ($LASTEXITCODE -ne 0) { throw '编译失败' }

# ---------- 3. 编译测试 helper ----------
$csc = $tools.Csc
$refAsm = $tools.RefAsm
$helperDll = Join-Path $testsDir 'CollectProgress.dll'
$refArgs = @(
    (Join-Path $refAsm 'mscorlib.dll'),
    (Join-Path $refAsm 'System.dll'),
    (Join-Path $refAsm 'System.Core.dll')
) | ForEach-Object { "/r:$_" }
Write-Host '[3/5] 编译 CollectProgress.dll ...'
& $csc /target:library /out:$helperDll @refArgs "/r:$exeOut" (Join-Path $testsDir 'CollectProgress.cs') (Join-Path $testsDir 'CancelRecorder.cs')
if ($LASTEXITCODE -ne 0) { throw 'CollectProgress.dll 编译失败' }

# ---------- 4. 运行测试 ----------
if (-not $ExePath) { $ExePath = $exeOut }
Write-Host "[4/5] 运行回归测试（exe: $ExePath）"
$ps = (Get-Process -Id $PID).Path
$overallFail = 0
$totalPass = 0
$totalFail = 0

function Get-TestSummary($output) {
    $pass = 0; $fail = 0; $found = $false
    foreach ($line in $output) {
        $s = [string]$line
        if ($s -match '通过\s*(\d+)\s*项[，,]\s*失败\s*(\d+)\s*项') { $pass = [int]$Matches[1]; $fail = [int]$Matches[2]; $found = $true; break }
        if ($s -match 'Passed\s*(\d+)[,，]\s*Failed\s*(\d+)') { $pass = [int]$Matches[1]; $fail = [int]$Matches[2]; $found = $true; break }
    }
    return @{ Pass = $pass; Fail = $fail; Found = $found }
}

$cases = @(
    @{ Name = 'test_parsers';   Args = @('-ExePath', $ExePath, '-HelperDir', $testsDir) },
    @{ Name = 'test_robust';    Args = @('-ExePath', $ExePath, '-HelperDir', $testsDir) },
    @{ Name = 'test_cli';       Args = @('-ExePath', $ExePath) },
    @{ Name = 'test_cancel';    Args = @('-ExePath', $ExePath, '-HelperDir', $testsDir) },
    @{ Name = 'test_multiroot'; Args = @('-ExePath', $ExePath) }
)

# test_settings2 自会定位被测 exe 实际读取的 user.config（对所有找到的配置统一处理后恢复），
# 仅当用户显式指定 -UserConfig 时才定向到某一个。
if ($UserConfig) { $cases += @{ Name = 'test_settings2'; Args = @('-ExePath', $ExePath, '-UserConfigPath', $UserConfig) } }
else { $cases += @{ Name = 'test_settings2'; Args = @('-ExePath', $ExePath) } }

# 带超时与输出收集的子进程运行器：避免测试脚本挂死时 run-tests 无限等待。
# 子进程（powershell.exe）向管道写出的中文按系统代码页 GBK(936) 编码，读取时必须匹配，
# 否则测试汇总行（通过/失败）会乱码导致汇总为 0。
function Invoke-TestScript([string]$scriptPath, [string[]]$argList) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $ps
    $psi.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $scriptPath + '" ' + (($argList | ForEach-Object { '"' + $_ + '"' }) -join ' ')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::GetEncoding(936)
    $psi.StandardErrorEncoding = [Text.Encoding]::GetEncoding(936)
    $proc = [Diagnostics.Process]::Start($psi)
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $lines = New-Object System.Collections.Generic.List[string]
    $timeoutMs = 300000
    if (-not $proc.WaitForExit($timeoutMs)) {
        try { $proc.Kill() } catch { }
        $proc.WaitForExit()
        $lines.Add('  [超时] 测试脚本超过 ' + ($timeoutMs / 1000) + ' 秒未完成，已强制终止')
        return @{ Out = $lines; Code = 1 }
    }
    $proc.WaitForExit()
    foreach ($ln in (($stdoutTask.Result) -split "`r?`n")) { if ($ln -ne '') { $lines.Add($ln) } }
    foreach ($ln in (($stderrTask.Result) -split "`r?`n")) { if ($ln -ne '') { $lines.Add('  [stderr] ' + $ln) } }
    return @{ Out = $lines; Code = $proc.ExitCode }
}

foreach ($c in $cases) {
    $scriptPath = Join-Path $testsDir ($c.Name + '.ps1')
    $res = Invoke-TestScript $scriptPath $c.Args
    foreach ($line in $res.Out) { Write-Host ([string]$line) }
    $summary = Get-TestSummary $res.Out
    $totalPass += $summary.Pass
    $totalFail += $summary.Fail
    Write-Host ("  [{0}] 通过 {1} 项，失败 {2} 项" -f $c.Name, $summary.Pass, $summary.Fail) -ForegroundColor Cyan
    if ($res.Code -ne 0) {
        $overallFail++
        Write-Host "  ^^ 该脚本未全部通过" -ForegroundColor Yellow
    } elseif (-not $summary.Found) {
        # 脚本以 0 退出但找不到汇总行：可能是编码问题导致乱码，或脚本根本没执行到汇总（静默零断言）
        $overallFail++
        Write-Host "  ^^ 脚本退出码为 0 但未匹配到汇总行（疑似编码乱码或断言未执行），不能视为通过" -ForegroundColor Yellow
    }
    Write-Host ''
}

# ---------- 4b. 可选: GUI 探针回归 ----------
if ($IncludeProbes) {
    Write-Host '[4b] 运行 GUI 探针回归（run_probes.ps1）...'
    $probeArgs = @('-ExePath', $ExePath)
    if ($IncludeE2E) { $probeArgs += '-IncludeE2E' }
    $res = Invoke-TestScript (Join-Path $testsDir 'run_probes.ps1') $probeArgs
    foreach ($line in $res.Out) { Write-Host ([string]$line) }
    $allPassed = @($res.Out | Where-Object { ([string]$_) -match 'ALL PROBES PASSED' }).Count -gt 0
    $anyFailed = @($res.Out | Where-Object { ([string]$_) -match 'PROBES FAILED' }).Count -gt 0
    if ($res.Code -ne 0 -or -not $allPassed -or $anyFailed) {
        $overallFail++
        Write-Host '  ^^ GUI 探针未全部通过' -ForegroundColor Yellow
    } else {
        Write-Host '  探针全部通过' -ForegroundColor Cyan
    }
    Write-Host ''
}

Write-Host "合计断言: 通过 $totalPass 项，失败 $totalFail 项"
if ($overallFail -gt 0) { Write-Host "有 $overallFail 个测试脚本未全部通过" -ForegroundColor Red; exit 1 }

# ---------- 5. 清理测试构建产物（CollectProgress.dll / log.txt） ----------
Write-Host '[5/5] 清理测试构建产物 ...'
Remove-Item (Join-Path $testsDir 'CollectProgress.dll') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $root 'bin\Debug\log.txt') -Force -ErrorAction SilentlyContinue

Write-Host '全部测试通过' -ForegroundColor Green
exit 0
