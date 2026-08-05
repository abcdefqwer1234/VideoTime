param(
    [string]$ExePath = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'bin\Debug\VideoTime.exe')
)
$ErrorActionPreference = 'Stop'
$script:Pass = 0
$script:Fail = 0

function Assert([bool]$cond, [string]$msg) {
    if ($cond) { $script:Pass++; Write-Host "  PASS  $msg" -ForegroundColor Green }
    else { $script:Fail++; Write-Host "  FAIL  $msg" -ForegroundColor Red }
}
function BE32([long]$v) { $b = [BitConverter]::GetBytes([uint32]$v); [Array]::Reverse($b); return [byte[]]$b }
function LE32([long]$v) { return [byte[]]([BitConverter]::GetBytes([uint32]$v)) }
function BE64Double([double]$v) { $b = [BitConverter]::GetBytes([double]$v); [Array]::Reverse($b); return [byte[]]$b }
function LE64([long]$v) { return [byte[]]([BitConverter]::GetBytes([int64]$v)) }

if (-not (Test-Path $ExePath)) { Write-Error "找不到 exe: $ExePath"; exit 1 }
$exe = (Resolve-Path $ExePath).Path
$tmp = Join-Path $env:TEMP ('vt_cli_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
Write-Host "测试目录: $tmp"

# log.txt 初始长度（用于判断本次 CLI 是否产生日志，避免依赖全局日志级别）
$logPath = Join-Path (Split-Path $exe) 'log.txt'
$logPre = if (Test-Path $logPath) { (Get-Item $logPath).Length } else { 0 }

# ---------- samples ----------
$ftyp = [byte[]](BE32 20) + [byte[]][char[]]('ftyp') + [byte[]][char[]]('isom') + [byte[]](0,0,0,0) + [byte[]][char[]]('isom')
$free = [byte[]](BE32 8) + [byte[]][char[]]('free')
$mvhd = [byte[]](BE32 108) + [byte[]][char[]]('mvhd') + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + (BE32 1000) + (BE32 60000) + [byte[]](New-Object byte[] 80)
$moov = [byte[]](BE32 (8 + $mvhd.Length)) + [byte[]][char[]]('moov') + $mvhd
$mp4 = $ftyp + $free + $moov
$mp4Path = Join-Path $tmp 'sample.mp4'; [IO.File]::WriteAllBytes($mp4Path, $mp4)

$docType = [byte[]](0x42,0x82,0x88) + [byte[]][char[]]('matroska')
$ebmlHeader = [byte[]](0x1A,0x45,0xDF,0xA3,0x8B) + $docType
$timecode = [byte[]](0x2A,0xD7,0xB1,0x84) + (BE32 1000000)
$duration = [byte[]](0x44,0x89,0x88) + (BE64Double 120000.0)
$info = [byte[]](0x15,0x49,0xA9,0x66,0x93) + $timecode + $duration
$segment = [byte[]](0x18,0x53,0x80,0x67,0x01,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF) + $info
$mkv = $ebmlHeader + $segment
$mkvPath = Join-Path $tmp 'sample.mkv'; [IO.File]::WriteAllBytes($mkvPath, $mkv)

$aviData = New-Object byte[] 56
$micro = LE32 40000; for ($i=0;$i -lt 4;$i++){ $aviData[$i] = $micro[$i] }
$frames = LE32 5000; for ($i=0;$i -lt 4;$i++){ $aviData[16+$i] = $frames[$i] }
$avihChunk = [byte[]][char[]]('avih') + (LE32 56) + $aviData
$hdrl = [byte[]][char[]]('LIST') + (LE32 68) + [byte[]][char[]]('hdrl') + $avihChunk
$avi = [byte[]][char[]]('RIFF') + (LE32 80) + [byte[]][char[]]('AVI ') + $hdrl
$aviPath = Join-Path $tmp 'sample.avi'; [IO.File]::WriteAllBytes($aviPath, $avi)

$asfGuid = [byte[]](0x30,0x26,0xB2,0x75,0x8E,0x66,0xCF,0x11,0xA6,0xD9,0x00,0xAA,0x00,0x62,0xCE,0x6C)
$filePropsGuid = [byte[]](0xA1,0xDC,0xAB,0x8C,0x47,0xA9,0xCF,0x11,0x8E,0xE4,0x00,0xC0,0x0C,0x20,0x53,0x65)
$fileProps = $filePropsGuid + (LE64 92) + [byte[]](New-Object byte[] 16) + (LE64 0) + (LE64 0) + (LE64 0) + (LE64 2000000000) + (LE64 0) + (LE64 0) + [byte[]](0,0,0,0)
$headerObj = $asfGuid + (LE64 (16+8+4+1+1+$fileProps.Length)) + (LE32 1) + [byte[]](1,2) + $fileProps
[IO.File]::WriteAllBytes((Join-Path $tmp 'sample.wmv'), $headerObj)

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
Assert ($csvText -match '580') "CSV 含总时长 580（不递归）"
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
Assert ($csvFwdText -match '580') "正斜杠路径 CSV 含总时长 580（分隔符等价）"

# ---------- recursive CSV ----------
$csv2 = Join-Path $tmp 'out2.csv'
$r = RunCli @('-d', $tmp, '-r', '-o', $csv2)
Assert ($r.Code -eq 0) "递归 CSV 退出码 = $($r.Code)（期望 0）"
$csv2Text = [IO.File]::ReadAllText($csv2)
Assert ($csv2Text -match '640') "递归 CSV 总时长 640 秒"
Assert ($csv2Text -match 'deep') "递归 CSV 含子目录 deep"

# ---------- recursive HTML ----------
$html = Join-Path $tmp 'out.html'
$r = RunCli @('-d', $tmp, '-r', '-o', $html)
Assert ($r.Code -eq 0) "递归 HTML 退出码 = $($r.Code)（期望 0）"
$htmlText = [IO.File]::ReadAllText($html)
Assert ($htmlText -match '<html') "HTML 以 <html> 开头"
Assert ($htmlText -match '视频时长统计报表') "HTML 含标题"
Assert ($htmlText -match '640') "HTML 含总时长 640"
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
