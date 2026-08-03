param(
    [string]$ExePath = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'bin\Debug\VideoTime.exe'),
    [string]$HelperDir = (Split-Path -Parent $MyInvocation.MyCommand.Path)
)
$ErrorActionPreference = 'Stop'
$script:Pass = 0
$script:Fail = 0

function Assert([bool]$cond, [string]$msg) {
    if ($cond) { $script:Pass++; Write-Host "  PASS  $msg" -ForegroundColor Green }
    else { $script:Fail++; Write-Host "  FAIL  $msg" -ForegroundColor Red }
}

function Approx([double]$a, [double]$b, [double]$tol) { [Math]::Abs($a - $b) -le $tol }

function BE32([long]$v) {
    $b = [BitConverter]::GetBytes([uint32]$v); [Array]::Reverse($b); return [byte[]]$b
}
function LE32([long]$v) {
    return [byte[]]([BitConverter]::GetBytes([uint32]$v))
}
function BE64Double([double]$v) {
    $b = [BitConverter]::GetBytes([double]$v); [Array]::Reverse($b); return [byte[]]$b
}
function LE64([long]$v) {
    return [byte[]]([BitConverter]::GetBytes([int64]$v))
}

if (-not (Test-Path $ExePath)) { Write-Error "找不到 exe: $ExePath"; exit 1 }
$exe = (Resolve-Path $ExePath).Path
$hdir = Join-Path $env:TEMP ('vt_helper_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $hdir | Out-Null
Copy-Item $exe (Join-Path $hdir 'VideoTime.exe') -Force
Copy-Item (Join-Path $HelperDir 'CollectProgress.dll') (Join-Path $hdir 'CollectProgress.dll') -Force
$asm = [Reflection.Assembly]::LoadFrom((Join-Path $hdir 'VideoTime.exe'))
[void][VideoTime.DurationParser]

$tmp = Join-Path $env:TEMP ('vt_parser_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
Write-Host "测试目录: $tmp"

# ---------- synthetic MP4: timescale=1000 duration=60000 => 60s ----------
$ftyp = [byte[]](BE32 20) + [byte[]][char[]]('ftyp') + [byte[]][char[]]('isom') + [byte[]](0,0,0,0) + [byte[]][char[]]('isom')
$free = [byte[]](BE32 8) + [byte[]][char[]]('free')
$mvhd = [byte[]](BE32 108) + [byte[]][char[]]('mvhd') + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + (BE32 1000) + (BE32 60000) + [byte[]](New-Object byte[] 80)
$moov = [byte[]](BE32 (8 + $mvhd.Length)) + [byte[]][char[]]('moov') + $mvhd
$mp4 = $ftyp + $free + $moov
$mp4Path = Join-Path $tmp 'sample.mp4'
[IO.File]::WriteAllBytes($mp4Path, $mp4)

# ---------- synthetic MKV: timescale=1e6 duration=120000 => 120s ----------
$docType = [byte[]](0x42,0x82,0x88) + [byte[]][char[]]('matroska')
$ebmlHeader = [byte[]](0x1A,0x45,0xDF,0xA3,0x8B) + $docType
$timecode = [byte[]](0x2A,0xD7,0xB1,0x84) + (BE32 1000000)
$duration = [byte[]](0x44,0x89,0x88) + (BE64Double 120000.0)
$info = [byte[]](0x15,0x49,0xA9,0x66,0x93) + $timecode + $duration
$segment = [byte[]](0x18,0x53,0x80,0x67,0x01,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF) + $info
$mkv = $ebmlHeader + $segment
$mkvPath = Join-Path $tmp 'sample.mkv'
[IO.File]::WriteAllBytes($mkvPath, $mkv)

# ---------- synthetic AVI: micro=40000 frames=5000 => 200s ----------
$aviData = New-Object byte[] 56
$micro = LE32 40000; for ($i=0;$i -lt 4;$i++){ $aviData[$i] = $micro[$i] }
$frames = LE32 5000; for ($i=0;$i -lt 4;$i++){ $aviData[16+$i] = $frames[$i] }
$avihChunk = (LE32 56) + [byte[]][char[]]('avih') + $aviData
$hdrl = [byte[]][char[]]('LIST') + (LE32 68) + [byte[]][char[]]('hdrl') + $avihChunk
$avi = [byte[]][char[]]('RIFF') + (LE32 80) + [byte[]][char[]]('AVI ') + $hdrl
$aviPath = Join-Path $tmp 'sample.avi'
[IO.File]::WriteAllBytes($aviPath, $avi)

# ---------- synthetic WMV: play=2000000000 preroll=0 => 200s ----------
$asfGuid = [byte[]](0x30,0x26,0xB2,0x75,0x8E,0x66,0xCF,0x11,0xA6,0xD9,0x00,0xAA,0x00,0x62,0xCE,0x6C)
$filePropsGuid = [byte[]](0xA1,0xDC,0xAB,0x8C,0x47,0xA9,0xCF,0x11,0x8E,0xE4,0x00,0xC0,0x0C,0x20,0x53,0x65)
$fileProps = $filePropsGuid + (LE64 92) + [byte[]](New-Object byte[] 16) + (LE64 0) + (LE64 0) + (LE64 0) + (LE64 2000000000) + (LE64 0) + (LE64 0) + [byte[]](0,0,0,0)
$headerObj = $asfGuid + (LE64 (16+8+4+1+1+$fileProps.Length)) + (LE32 1) + [byte[]](1,2) + $fileProps
$wmv = $headerObj
$wmvPath = Join-Path $tmp 'sample.wmv'
[IO.File]::WriteAllBytes($wmvPath, $wmv)

# ---------- failure & non-video ----------
$badPath = Join-Path $tmp 'broken.mp4'
[IO.File]::WriteAllBytes($badPath, [byte[]](0x67,0x61,0x72,0x62,0x61,0x67,0x65))
$txtPath = Join-Path $tmp 'note.txt'
[IO.File]::WriteAllText($txtPath, 'hello')

Write-Host '== 各格式时长解析 =='
$d1 = [VideoTime.DurationParser]::ParseFile($mp4Path); Assert (Approx $d1 60 0.001)   "MP4  解析 = $d1 秒（期望 60）"
$d2 = [VideoTime.DurationParser]::ParseFile($mkvPath); Assert (Approx $d2 120 0.001)  "MKV  解析 = $d2 秒（期望 120）"
$d3 = [VideoTime.DurationParser]::ParseFile($aviPath); Assert (Approx $d3 200 0.001)  "AVI  解析 = $d3 秒（期望 200）"
$d4 = [VideoTime.DurationParser]::ParseFile($wmvPath); Assert (Approx $d4 200 0.001)  "WMV  解析 = $d4 秒（期望 200）"
$d5 = [VideoTime.DurationParser]::ParseFile($badPath); Assert ($d5 -lt 0)             "坏MP4解析 = $d5（期望 -1 失败）"
$d6 = [VideoTime.DurationParser]::ParseFile($txtPath); Assert ($d6 -lt 0)             "TXT  解析 = $d6（期望 -1 不支持）"

Write-Host '== 扩展名过滤 =='
$listed = [VideoTime.DurationParser]::GetVideoFiles($tmp)
Assert ($listed.Count -eq 5) "GetVideoFiles 只返回视频扩展名（$($listed.Count) 个，期望 5）"

Write-Host '== 混合格式整目录扫描 =='
$sub = Join-Path $tmp 'mix'
New-Item -ItemType Directory -Path $sub | Out-Null
Copy-Item $mp4Path $sub; Copy-Item $mkvPath $sub; Copy-Item $aviPath $sub; Copy-Item $wmvPath $sub

$spType = $asm.GetType('VideoTime.ScanProgress')
$helperAsm = [Reflection.Assembly]::LoadFrom((Join-Path $hdir 'CollectProgress.dll'))
$cpType = $helperAsm.GetType('CollectProgress')
$prog = [Activator]::CreateInstance($cpType)

$result = [VideoTime.VideoScanner]::Run($sub, $true, [Threading.CancellationToken]::None, $prog)

Assert (Approx $result.TotalSeconds 580 0.5)  "混合目录总时长 = $($result.TotalSeconds) 秒（期望 580）"
Assert ($result.FolderResults[0].FileCount -eq 4) "视频数 = $($result.FolderResults[0].FileCount)（期望 4）"
Assert ($result.FailCount -eq 0)             "失败文件 = $($result.FailCount)（期望 0）"
$items = @($prog.Lines)
$parsePhase = @($items | Where-Object { $_ -like 'parse:*' })
Assert ($parsePhase.Count -gt 0)             "进度事件含解析阶段（$($parsePhase.Count) 条）"
$lastParts = $parsePhase[-1].Split(':')[1].Split('/')
Assert ($lastParts[0] -eq '4' -and $lastParts[1] -eq '4') "最后一条进度 4/4（$($parsePhase[-1])）"
$collectPhase = @($items | Where-Object { $_ -like 'collect:*' })
Assert ($collectPhase.Count -eq 1)           "进度事件含收集阶段（$($collectPhase.Count) 条）"

Write-Host '== 工具方法 =='
Assert (([VideoTime.VideoScanner]::Format(8382)) -eq '2时19分42秒') "Format(8382) = 2时19分42秒"
Assert (([VideoTime.VideoScanner]::Format(3599)) -eq '0时59分59秒') "Format(3599) = 0时59分59秒"
Assert (([VideoTime.VideoScanner]::Format(0)) -eq '0时0分0秒') "Format(0) = 0时0分0秒"
Assert (([VideoTime.VideoScanner]::Format(-1)) -eq '0时0分-1秒') "Format(-1) = 0时0分-1秒"

$edir = Join-Path $tmp 'empty'
New-Item -ItemType Directory -Path $edir | Out-Null
$el = @([VideoTime.DurationParser]::GetVideoFiles($edir))
Assert ($el.Count -eq 0) "空目录 GetVideoFiles = 0"

Write-Host '== 扩展名大小写 =='
$cdir = Join-Path $tmp 'case'
New-Item -ItemType Directory -Path $cdir | Out-Null
Copy-Item $mp4Path (Join-Path $cdir 'UP.MP4')
Copy-Item $mkvPath (Join-Path $cdir 'Mix.MkV')
$cl = @([VideoTime.DurationParser]::GetVideoFiles($cdir))
Assert ($cl.Count -eq 2) "大小写扩展名回显 Count = $($cl.Count)（期望 2）"
Assert (Approx ([VideoTime.DurationParser]::ParseFile((Join-Path $cdir 'UP.MP4'))) 60 0.001) "解析 .MP4 = 60"
Assert (Approx ([VideoTime.DurationParser]::ParseFile((Join-Path $cdir 'Mix.MkV'))) 120 0.001) "解析 .MkV = 120"
Copy-Item $mp4Path (Join-Path $cdir 'ext.mov')
Assert (Approx ([VideoTime.DurationParser]::ParseFile((Join-Path $cdir 'ext.mov'))) 60 0.001) "解析 .mov = 60"

Write-Host ''
Write-Host "通过 $script:Pass 项，失败 $script:Fail 项"
try { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -Recurse -Force $hdir -ErrorAction SilentlyContinue } catch { }
if ($script:Fail -gt 0) { exit 1 } else { exit 0 }
