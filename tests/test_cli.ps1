param(
    [string]$ExePath = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'bin\Debug\VideoTime.exe')
)
$ErrorActionPreference = 'Stop'
$script:Pass = 0
$script:Fail = 0

. (Join-Path $PSScriptRoot 'lib.ps1')

if (-not (Test-Path $ExePath)) { Write-Error "找不到 exe: $ExePath"; exit 1 }
$exe = (Resolve-Path $ExePath).Path
$tmp = Join-Path $env:TEMP ('vt_cli_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
Write-Host "测试目录: $tmp"

# log.txt 初始长度（用于判断本次 CLI 是否产生日志，避免依赖全局日志级别）
$logPath = Join-Path (Split-Path $exe) 'log.txt'
$logPre = if (Test-Path $logPath) { (Get-Item $logPath).Length } else { 0 }

# ---------- samples（经 lib.ps1 统一构造器） ----------
$mp4Path = Join-Path $tmp 'sample.mp4'; [IO.File]::WriteAllBytes($mp4Path, (New-ValidMp4))
$mkvPath = Join-Path $tmp 'sample.mkv'; [IO.File]::WriteAllBytes($mkvPath, (New-ValidMkv))
$aviPath = Join-Path $tmp 'sample.avi'; [IO.File]::WriteAllBytes($aviPath, (New-ValidAvi))
[IO.File]::WriteAllBytes((Join-Path $tmp 'sample.wmv'), (New-ValidWmv))

$sub = Join-Path $tmp 'deep'
New-Item -ItemType Directory -Path $sub | Out-Null
Copy-Item $mp4Path $sub
[IO.File]::WriteAllBytes((Join-Path $tmp 'broken.mp4'), [byte[]](0x67,0x61,0x72,0x62,0x61,0x67,0x65))

function RunCli([string[]]$argsList) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = ($argsList | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::GetEncoding(936)
    $psi.StandardErrorEncoding = [Text.Encoding]::GetEncoding(936)
    $proc = [Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    return @{ Code = $proc.ExitCode; Out = $stdout; Err = $stderr }
}

# ---------- non-recursive CSV ----------
$csv = Join-Path $tmp 'out.csv'
$r = RunCli @('-d', $tmp, '-o', $csv)
Assert ($r.Code -eq 0) "非递归 CSV 退出码 = $($r.Code)（期望 0）"
Assert (Test-Path $csv) "CSV 文件已生成"
$csvText = if (Test-Path $csv) { [IO.File]::ReadAllText($csv) } else { '' }
Assert ($csvText -match '文件夹,总时长') "CSV 含表头（文件夹,总时长）"
Assert ($csvText -match ',580,') "CSV 含总时长 580（不递归）"
Assert ($csvText -match '失败明细') "CSV 含失败明细区"
Assert ($csvText -match '文件读取失败') "CSV 含失败文件行（broken.mp4）"
Assert ($csvText -notmatch 'deep') "CSV 不含子目录 deep（不递归）"

# CSV 应以 UTF-8 BOM (EF BB BF) 开头
$bomBytes = [IO.File]::ReadAllBytes($csv)
Assert (($bomBytes.Length -ge 3) -and ($bomBytes[0] -eq 0xEF) -and ($bomBytes[1] -eq 0xBB) -and ($bomBytes[2] -eq 0xBF)) "CSV 含 UTF-8 BOM"

# ---------- 正斜杠路径等价 ----------
$csvFwd = Join-Path $tmp 'out_fwd.csv'
$tmpFwd = $tmp.Replace('\', '/')
$r = RunCli @('-d', $tmpFwd, '-o', $csvFwd)
Assert ($r.Code -eq 0) "正斜杠路径退出码 = $($r.Code)（期望 0）"
$csvFwdText = if (Test-Path $csvFwd) { [IO.File]::ReadAllText($csvFwd) } else { '' }
Assert ($csvFwdText -match ',580,') "正斜杠路径 CSV 含总时长 580（分隔符等价）"

# ---------- recursive CSV ----------
$csv2 = Join-Path $tmp 'out2.csv'
$r = RunCli @('-d', $tmp, '-r', '-o', $csv2)
Assert ($r.Code -eq 0) "递归 CSV 退出码 = $($r.Code)（期望 0）"
$csv2Text = [IO.File]::ReadAllText($csv2)
Assert ($csv2Text -match ',640,') "递归 CSV 总时长 640 秒"
Assert ($csv2Text -match 'deep') "递归 CSV 含子目录 deep"

# ---------- recursive HTML ----------
$html = Join-Path $tmp 'out.html'
$r = RunCli @('-d', $tmp, '-r', '-o', $html)
Assert ($r.Code -eq 0) "递归 HTML 退出码 = $($r.Code)（期望 0）"
$htmlText = [IO.File]::ReadAllText($html)
Assert ($htmlText -match '<html') "HTML 以 <html> 开头"
Assert ($htmlText -match '视频时长统计报表') "HTML 含标题"
Assert ($htmlText -match '>640</td>') "HTML 含总时长 640"
Assert ($htmlText -match '失败明细') "HTML 含失败明细标题"
Assert ($htmlText -match '文件读取失败') "HTML 含文件读取失败行"

# ---------- no -o: print tree ----------
$r = RunCli @('-d', $tmp, '-r')
Assert ($r.Code -eq 0) "树形输出退出码 = $($r.Code)（期望 0）"
Assert ($r.Out -match '总时间') "树形输出含'总时间'"
Assert ($r.Out -match '视频') "树形输出含'视频'"

# ---------- errors ----------
$r = RunCli @('-d', 'Z:\no_such_dir_xyz')
Assert ($r.Code -eq 1) "目录不存在退出码 = $($r.Code)（期望 1）"
Assert ($r.Err -match '不存在') "目录不存在提示输出到 stderr"
$r = RunCli @('-x')
Assert ($r.Code -eq 1) "未知参数退出码 = $($r.Code)（期望 1）"
$r = RunCli @('-h')
Assert ($r.Code -eq 0) "帮助退出码 = $($r.Code)（期望 0）"
Assert ($r.Out -match '用法') "帮助含'用法'"
$r = RunCli @('-d')
Assert ($r.Code -eq 1) "缺少 -d 参数退出码 = $($r.Code)（期望 1）"
Assert ($r.Err -match '缺少') "缺少 -d 参数提示输出到 stderr"
$r = RunCli @('-d', $tmp, '-o')
Assert ($r.Code -eq 1) "缺少 -o 参数退出码 = $($r.Code)（期望 1）"
Assert ($r.Err -match '缺少') "缺少 -o 参数提示输出到 stderr"

# ---------- long-form args ----------
$csvL = Join-Path $tmp 'out_long.csv'
$r = RunCli @('--folder', $tmp, '--out', $csvL)
Assert ($r.Code -eq 0) "长参数 --folder/--out 退出码 = $($r.Code)（期望 0）"
Assert (Test-Path $csvL) "长参数 --out CSV 已生成"
$csvLText = if (Test-Path $csvL) { [IO.File]::ReadAllText($csvL) } else { '' }
Assert ($csvLText -match ',580,') "长参数 CSV 含总时长 580"

$csvL2 = Join-Path $tmp 'out_long2.csv'
$r = RunCli @('--folder', $tmp, '--recursive', '--out', $csvL2)
Assert ($r.Code -eq 0) "长参数 --recursive 退出码 = $($r.Code)（期望 0）"
Assert (([IO.File]::ReadAllText($csvL2)) -match ',640,') "长参数递归 CSV 含总时长 640"

$r = RunCli @('--help')
Assert ($r.Code -eq 0) "--help 退出码 = $($r.Code)（期望 0）"
Assert ($r.Out -match '用法') "--help 含'用法'"

$r = RunCli @('--folder', $tmp, '--recursive')
Assert ($r.Code -eq 0) "长参数树形输出退出码 = $($r.Code)（期望 0）"
Assert ($r.Out -match '总时间') "长参数树形输出含'总时间'"

# ---------- no args -> GUI stays open ----------
$psi = New-Object Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$guiProc = [Diagnostics.Process]::Start($psi)
Start-Sleep -Milliseconds 1500
$guiAlive = -not $guiProc.HasExited
if ($guiAlive) { try { $guiProc.Kill() } catch { } }
Assert ($guiAlive) "无参数时启动 GUI（进程保持运行）"

# ---------- depth skip (MaxDepth=50) ----------
$dBase = Join-Path $env:TEMP ('vtq_' + [guid]::NewGuid().ToString('N').Substring(0,4))
New-Item -ItemType Directory -Path $dBase | Out-Null
$deepRoot = Join-Path $dBase 'r'
New-Item -ItemType Directory -Path $deepRoot | Out-Null
$cur = $deepRoot
for ($i = 0; $i -lt 55; $i++) {
    $cur = Join-Path $cur 'a'
    New-Item -ItemType Directory -Path $cur | Out-Null
}
[IO.File]::WriteAllBytes((Join-Path $cur 'x.mp4'), [byte[]](0x00))
$csv3 = Join-Path $dBase 'out3.csv'
$r = RunCli @('-d', $deepRoot, '-r', '-o', $csv3)
Assert ($r.Code -eq 0) "深度测试退出码 = $($r.Code)（期望 0）"
$csv3Text = [IO.File]::ReadAllText($csv3)
Assert ($csv3Text -match '超过50层') "CSV 含'超过50层目录已省略'"
Assert ($csv3Text -match '失败明细') "CSV 含失败明细区（深度省略）"
Remove-Item -Recurse -Force $dBase

# ---------- log ----------
# 仅当本次 CLI 确实写了日志时才断言内容，避免依赖 user.config 的日志级别
$logPost = if (Test-Path $logPath) { (Get-Item $logPath).Length } else { 0 }
if ($logPost -gt $logPre) {
    $logText = [IO.File]::ReadAllText($logPath)
    Assert ($logText -match '命令行模式') "log.txt 记录命令行模式条目"
} else {
    Write-Host "  SKIP  log 断言（本次未产生日志，当前日志级别可能为 Off/Warning/Error）" -ForegroundColor Yellow
}

Write-Host ''
Write-Host "通过 $script:Pass 项，失败 $script:Fail 项"
try { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -Recurse -Force $dBase -ErrorAction SilentlyContinue } catch { }
if ($script:Fail -gt 0) { exit 1 } else { exit 0 }
