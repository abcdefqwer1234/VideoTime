# 一键回归：编译 + 编译测试 helper + 运行全部测试
# 用法: powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1 [-ExePath <path>] [-UserConfig <path>]
param(
    [string]$ExePath = '',
    [string]$UserConfig = ''
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
Write-Host "[1/4] MSBuild: $msbuild"

# ---------- 2. 编译 Debug ----------
Write-Host '[2/4] 编译 VideoTime.csproj (Debug/AnyCPU) ...'
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
Write-Host '[3/4] 编译 CollectProgress.dll ...'
& $csc /target:library /out:$helperDll @refArgs "/r:$exeOut" (Join-Path $testsDir 'CollectProgress.cs') (Join-Path $testsDir 'CancelRecorder.cs')
if ($LASTEXITCODE -ne 0) { throw 'CollectProgress.dll 编译失败' }

# ---------- 4. 运行测试 ----------
if (-not $ExePath) { $ExePath = $exeOut }
Write-Host "[4/4] 运行回归测试（exe: $ExePath）"
$ps = (Get-Process -Id $PID).Path
$overallFail = 0
$totalPass = 0
$totalFail = 0

function Get-TestSummary($output) {
    $pass = 0; $fail = 0
    foreach ($line in $output) {
        $s = [string]$line
        if ($s -match '通过\s*(\d+)\s*项[，,]\s*失败\s*(\d+)\s*项') { $pass = [int]$Matches[1]; $fail = [int]$Matches[2]; break }
        if ($s -match 'Passed\s*(\d+)[,，]\s*Failed\s*(\d+)') { $pass = [int]$Matches[1]; $fail = [int]$Matches[2]; break }
    }
    return @{ Pass = $pass; Fail = $fail }
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

foreach ($c in $cases) {
    $scriptPath = Join-Path $testsDir ($c.Name + '.ps1')
    $out = & $ps -NoProfile -ExecutionPolicy Bypass -File $scriptPath @($c.Args) 2>&1
    foreach ($line in $out) { Write-Host ([string]$line) }
    $summary = Get-TestSummary $out
    $totalPass += $summary.Pass
    $totalFail += $summary.Fail
    Write-Host ("  [{0}] 通过 {1} 项，失败 {2} 项" -f $c.Name, $summary.Pass, $summary.Fail) -ForegroundColor Cyan
    if ($LASTEXITCODE -ne 0) {
        $overallFail++
        Write-Host "  ^^ 该脚本未全部通过" -ForegroundColor Yellow
    }
    Write-Host ''
}

Write-Host "合计断言: 通过 $totalPass 项，失败 $totalFail 项"
if ($overallFail -gt 0) { Write-Host "有 $overallFail 个测试脚本未全部通过" -ForegroundColor Red; exit 1 }
Write-Host '全部测试通过' -ForegroundColor Green
exit 0
