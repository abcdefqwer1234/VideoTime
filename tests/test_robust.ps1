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

function BE32([long]$v) { $b = [BitConverter]::GetBytes([uint32]$v); [Array]::Reverse($b); return [byte[]]$b }
function LE32([long]$v) { return [byte[]]([BitConverter]::GetBytes([uint32]$v)) }
function BE64([long]$v) { $b = [BitConverter]::GetBytes([int64]$v); [Array]::Reverse($b); return [byte[]]$b }
function LE64([long]$v) { return [byte[]]([BitConverter]::GetBytes([int64]$v)) }
function BE64Double([double]$v) { $b = [BitConverter]::GetBytes([double]$v); [Array]::Reverse($b); return [byte[]]$b }

if (-not (Test-Path $ExePath)) { Write-Error "exe not found: $ExePath"; exit 1 }
$exe = (Resolve-Path $ExePath).Path
$hdir = Join-Path $env:TEMP ('vt_helper_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $hdir | Out-Null
Copy-Item $exe (Join-Path $hdir 'VideoTime.exe') -Force
Copy-Item (Join-Path $HelperDir 'CollectProgress.dll') (Join-Path $hdir 'CollectProgress.dll') -Force
[void][Reflection.Assembly]::LoadFrom((Join-Path $hdir 'VideoTime.exe'))
[void][VideoTime.DurationParser]

function PF([string]$path) { return [VideoTime.DurationParser]::ParseFile($path) }

$tmp = Join-Path $env:TEMP ('vt_robust_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
Write-Host "test dir: $tmp"

function New-ValidMp4 {
    $ftyp = [byte[]](BE32 20) + [byte[]][char[]]('ftyp') + [byte[]][char[]]('isom') + [byte[]](0,0,0,0) + [byte[]][char[]]('isom')
    $mvhd = [byte[]](BE32 108) + [byte[]][char[]]('mvhd') + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + (BE32 1000) + (BE32 60000) + [byte[]](New-Object byte[] 80)
    $moov = [byte[]](BE32 (8 + $mvhd.Length)) + [byte[]][char[]]('moov') + $mvhd
    return $ftyp + $moov
}
function New-ValidMkv([int]$durMs = 120000) {
    $docType = [byte[]](0x42,0x82,0x88) + [byte[]][char[]]('matroska')
    $ebmlHeader = [byte[]](0x1A,0x45,0xDF,0xA3,0x8B) + $docType
    $timecode = [byte[]](0x2A,0xD7,0xB1,0x84) + (BE32 1000000)
    $duration = [byte[]](0x44,0x89,0x88) + (BE64Double ([double]$durMs))
    $info = [byte[]](0x15,0x49,0xA9,0x66,0x93) + $timecode + $duration
    $segment = [byte[]](0x18,0x53,0x80,0x67,0x01,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF) + $info
    return $ebmlHeader + $segment
}
function New-ValidAvi([uint32]$micro = 40000, [uint32]$frames = 5000) {
    $aviData = New-Object byte[] 56
    $m = LE32 $micro; for ($i=0;$i -lt 4;$i++){ $aviData[$i] = $m[$i] }
    $f = LE32 $frames; for ($i=0;$i -lt 4;$i++){ $aviData[16+$i] = $f[$i] }
    $avihChunk = [byte[]][char[]]('avih') + (LE32 56) + $aviData
    $hdrl = [byte[]][char[]]('LIST') + (LE32 68) + [byte[]][char[]]('hdrl') + $avihChunk
    return [byte[]][char[]]('RIFF') + (LE32 80) + [byte[]][char[]]('AVI ') + $hdrl
}
function New-ValidWmv([int64]$play100ns = 2000000000, [int64]$preroll100ns = 0) {
    $asfGuid = [byte[]](0x30,0x26,0xB2,0x75,0x8E,0x66,0xCF,0x11,0xA6,0xD9,0x00,0xAA,0x00,0x62,0xCE,0x6C)
    $filePropsGuid = [byte[]](0xA1,0xDC,0xAB,0x8C,0x47,0xA9,0xCF,0x11,0x8E,0xE4,0x00,0xC0,0x0C,0x20,0x53,0x65)
    $fileProps = $filePropsGuid + (LE64 92) + [byte[]](New-Object byte[] 16) + (LE64 0) + (LE64 0) + (LE64 0) + (LE64 $play100ns) + (LE64 0) + (LE64 $preroll100ns) + [byte[]](0,0,0,0)
    return $asfGuid + (LE64 (16+8+4+1+1+$fileProps.Length)) + (LE32 1) + [byte[]](1,2) + $fileProps
}
function Write-File([string]$name, [byte[]]$bytes) {
    $p = Join-Path $tmp $name
    [IO.File]::WriteAllBytes($p, $bytes)
    return $p
}

# ============ 1. MP4 boundary ============
Write-Host '== MP4 boundary =='
$p1 = Write-File 'v1.mp4' (New-ValidMp4)
Assert (Approx (PF $p1) 60 0.001) "MP4 = 60s"

$mvhd1 = [byte[]](BE32 108) + [byte[]][char[]]('mvhd') + [byte[]](1,0,0,0) + [byte[]](BE64 0) + [byte[]](BE64 0) + (BE32 1000) + (BE64 90000000) + [byte[]](New-Object byte[] 64)
$moov1 = [byte[]](BE32 (8 + $mvhd1.Length)) + [byte[]][char[]]('moov') + $mvhd1
$ftyp = [byte[]](BE32 20) + [byte[]][char[]]('ftyp') + [byte[]][char[]]('isom') + [byte[]](0,0,0,0) + [byte[]][char[]]('isom')
$p2 = Write-File 'v1.mov' ($ftyp + $moov1)
Assert (Approx (PF $p2) 90000 1) "MP4 v1(64bit) duration 90000s"

$mvhd0 = [byte[]](BE32 108) + [byte[]][char[]]('mvhd') + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + (BE32 0) + (BE32 60000) + [byte[]](New-Object byte[] 80)
$moov0 = [byte[]](BE32 (8 + $mvhd0.Length)) + [byte[]][char[]]('moov') + $mvhd0
$p3 = Write-File 'ts0.mp4' ($ftyp + $moov0)
$d3 = PF $p3; Assert ($d3 -lt 0) "timescale=0 fail ($d3)"

$moovHuge = [byte[]](BE32 0x7FFFFFF0) + [byte[]][char[]]('moov') + [byte[]][char[]]('mvhd')
$p4 = Write-File 'hugebox.mp4' ($ftyp + $moovHuge)
$d4 = PF $p4; Assert ($d4 -lt 0) "moov boxSize huge fail, no crash ($d4)"

$pad = New-Object byte[] 204800
(New-Object Random 42).NextBytes($pad)
$moovT = [byte[]](BE32 (8 + 108)) + [byte[]][char[]]('moov') + [byte[]](BE32 108) + [byte[]][char[]]('mvhd') + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + (BE32 1000) + (BE32 45000) + [byte[]](New-Object byte[] 80)
$p5 = Write-File 'tailmoov.mp4' ($ftyp + $pad + $moovT)
Assert (Approx (PF $p5) 45 0.001) "moov at tail (200KB pad) = 45s"

$moov8 = [byte[]](BE32 8) + [byte[]][char[]]('moov')
$p6 = Write-File 'emptymoov.mp4' ($ftyp + $moov8)
$d6 = PF $p6; Assert ($d6 -lt 0) "empty moov fail ($d6)"

# moov 恰跨 1MB 分块边界（验证流式重叠逻辑）：boxStart=1MB-6，'moov' 四字节落在 1MB-2..1MB+1
$mvhdX = [byte[]](BE32 108) + [byte[]][char[]]('mvhd') + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + (BE32 1000) + (BE32 30000) + [byte[]](New-Object byte[] 80)
$moovBox = [byte[]](BE32 (8 + $mvhdX.Length)) + [byte[]][char[]]('moov') + $mvhdX
$boxStart8 = (1MB) - 6
$content = New-Object byte[] ($boxStart8 + $moovBox.Length + 20)
for ($i=0;$i -lt $ftyp.Length;$i++){ $content[$i] = $ftyp[$i] }
for ($i=$ftyp.Length; $i -lt $boxStart8; $i++){ $content[$i] = 0x11 }
for ($i=0;$i -lt $moovBox.Length;$i++){ $content[$boxStart8 + $i] = $moovBox[$i] }
for ($i=$boxStart8 + $moovBox.Length; $i -lt $content.Length; $i++){ $content[$i] = 0x22 }
$pX = Write-File 'cross.mov' $content
Assert (Approx (PF $pX) 30 0.001) "moov crossing 1MB chunk boundary -> 30s"

# moov 盒头恰从 1MB 分块边界起始（验证第二块内 boxStart 定位）
$moovAtMb = [byte[]](BE32 (8 + $mvhdX.Length)) + [byte[]][char[]]('moov') + $mvhdX
$boxStartA = 1MB
$contentA = New-Object byte[] ($boxStartA + $moovAtMb.Length + 20)
for ($i=0;$i -lt $ftyp.Length;$i++){ $contentA[$i] = $ftyp[$i] }
for ($i=$ftyp.Length; $i -lt $boxStartA; $i++){ $contentA[$i] = 0x11 }
for ($i=0;$i -lt $moovAtMb.Length;$i++){ $contentA[$boxStartA + $i] = $moovAtMb[$i] }
for ($i=$boxStartA + $moovAtMb.Length; $i -lt $contentA.Length; $i++){ $contentA[$i] = 0x22 }
$pA = Write-File 'at1mb.mov' $contentA
Assert (Approx (PF $pA) 30 0.001) "moov starting exactly at 1MB -> 30s"

# moov 跨更靠后的 2MB 分块边界
$boxStartB = 2MB - 6
$contentB = New-Object byte[] ($boxStartB + $moovBox.Length + 20)
for ($i=0;$i -lt $ftyp.Length;$i++){ $contentB[$i] = $ftyp[$i] }
for ($i=$ftyp.Length; $i -lt $boxStartB; $i++){ $contentB[$i] = 0x33 }
for ($i=0;$i -lt $moovBox.Length;$i++){ $contentB[$boxStartB + $i] = $moovBox[$i] }
for ($i=$boxStartB + $moovBox.Length; $i -lt $contentB.Length; $i++){ $contentB[$i] = 0x44 }
$pB = Write-File 'cross2mb.mov' $contentB
Assert (Approx (PF $pB) 30 0.001) "moov crossing 2MB chunk boundary -> 30s"

# mvhd duration=0：合法元数据，应成功解析为 0 秒
$mvhd0d = [byte[]](BE32 108) + [byte[]][char[]]('mvhd') + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + (BE32 1000) + (BE32 0) + [byte[]](New-Object byte[] 80)
$moov0d = [byte[]](BE32 (8 + $mvhd0d.Length)) + [byte[]][char[]]('moov') + $mvhd0d
$p0d = Write-File 'dur0.mp4' ($ftyp + $moov0d)
$d0d = PF $p0d; Assert ($d0d -eq 0) "mvhd duration=0 valid -> 0s ($d0d)"

# ============ 2. MKV boundary ============
Write-Host '== MKV boundary =='
$p7 = Write-File 'ok.mkv' (New-ValidMkv 120000)
Assert (Approx (PF $p7) 120 0.001) "MKV = 120s"

$timecodeF = [byte[]](0x2A,0xD7,0xB1,0x84) + (BE32 1000000)
$durF = [byte[]](0x44,0x89,0x84) + [byte[]](BE32 ([BitConverter]::ToUInt32([BitConverter]::GetBytes([single]1000.0),0)))
$infoF = [byte[]](0x15,0x49,0xA9,0x66,0x91) + $timecodeF + $durF
$segF = [byte[]](0x18,0x53,0x80,0x67,0x01,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF) + $infoF
$docType = [byte[]](0x42,0x82,0x88) + [byte[]][char[]]('matroska')
$ebmlHeader = [byte[]](0x1A,0x45,0xDF,0xA3,0x8B) + $docType
$p8 = Write-File 'float.mkv' ($ebmlHeader + $segF)
Assert (Approx (PF $p8) 1 0.001) "MKV float(4B) duration = 1s"

$p9 = Write-File 'trunc.mkv' ([byte[]](0x1A,0x45,0xDF,0xA3,0x8B,0x42,0x82))
$d9 = PF $p9; Assert ($d9 -lt 0) "truncated MKV fail ($d9)"

$p10 = Write-File 'noseg.mkv' ($ebmlHeader + $docType)
$d10 = PF $p10; Assert ($d10 -lt 0) "no Segment MKV fail ($d10)"

# TimeScale=0：合法结构，时长按公式得 0 秒（成功返回 0）
$timecode0 = [byte[]](0x2A,0xD7,0xB1,0x84) + (BE32 0)
$dur120 = [byte[]](0x44,0x89,0x88) + (BE64Double 120000.0)
$info0 = [byte[]](0x15,0x49,0xA9,0x66,0x93) + $timecode0 + $dur120
$seg0 = [byte[]](0x18,0x53,0x80,0x67,0x01,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF) + $info0
$pTS0 = Write-File 'ts0.mkv' ($ebmlHeader + $seg0)
$dTS0 = PF $pTS0; Assert ($dTS0 -eq 0) "MKV TimeScale=0 -> 0s ($dTS0)"

# MKV Info 缺少 Duration 元素 → 失败
$timecodeNd = [byte[]](0x2A,0xD7,0xB1,0x84) + (BE32 1000000)
$infoNd = [byte[]](0x15,0x49,0xA9,0x66,0x88) + $timecodeNd
$segNd = [byte[]](0x18,0x53,0x80,0x67,0x01,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF) + $infoNd
$pNd = Write-File 'nondur.mkv' ($ebmlHeader + $segNd)
$dNd = PF $pNd; Assert ($dNd -lt 0) "MKV no Duration fail ($dNd)"

# ============ 3. AVI / ASF boundary ============
Write-Host '== AVI / ASF boundary =='
$p11 = Write-File 'ok.avi' (New-ValidAvi)
Assert (Approx (PF $p11) 200 0.001) "AVI = 200s"

$p12 = Write-File 'micro0.avi' (New-ValidAvi 0 5000)
$d12 = PF $p12; Assert ($d12 -lt 0) "avi micro=0 fail ($d12)"

$p12b = Write-File 'frames0.avi' (New-ValidAvi 40000 0)
$d12b = PF $p12b; Assert ($d12b -lt 0) "avi frames=0 fail ($d12b)"

# AVI 无 avih 块 → 失败
$aviNoAvih = [byte[]][char[]]('RIFF') + (LE32 16) + [byte[]][char[]]('AVI ') + [byte[]][char[]]('LIST') + (LE32 8) + [byte[]][char[]]('hdrl')
$pNoAvih = Write-File 'noavih.avi' $aviNoAvih
$dNoAvih = PF $pNoAvih; Assert ($dNoAvih -lt 0) "AVI no avih fail ($dNoAvih)"

$p13 = Write-File 'ok.wmv' (New-ValidWmv)
Assert (Approx (PF $p13) 200 0.001) "WMV = 200s"

$big = New-ValidWmv
$bigHeader = [byte[]]($big[0..15]) + (LE64 0x7FFFFFFF0) + [byte[]]($big[24..($big.Length-1)])
$p14 = Write-File 'hugehdr.wmv' $bigHeader
$d14 = PF $p14; Assert ($d14 -ge -1) "ASF huge headerSize no crash ($d14)"

# preroll >= play：diff<=0，应返回"无有效播放时长"失败
$p14b = Write-File 'preroll.wmv' (New-ValidWmv 2000000000 2000000000)
$d14b = PF $p14b; Assert ($d14b -lt 0) "ASF preroll>=play fail ($d14b)"

# headerSize=0：应无对象可扫，失败不崩溃
$big2 = New-ValidWmv
$hs0 = [byte[]]($big2[0..15]) + (LE64 0) + [byte[]]($big2[24..($big2.Length-1)])
$p14c = Write-File 'hdr0.wmv' $hs0
$d14c = PF $p14c; Assert ($d14c -lt 0) "ASF headerSize=0 fail ($d14c)"

# ASF 无 File Properties 对象 → 失败
$dummyGuid = [byte[]](0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0)
$dummyObj = $dummyGuid + (LE64 24)
$hdrSize = [long](30 + $dummyObj.Length)
$asfNoFp = $asfGuid + (LE64 $hdrSize) + (LE32 1) + [byte[]](0,0) + $dummyObj
$pNoFp = Write-File 'nofp.wmv' $asfNoFp
$dNoFp = PF $pNoFp; Assert ($dNoFp -lt 0) "ASF no FileProps fail ($dNoFp)"

# ASF sub-object size<24 → break → 失败
$smallObj = $dummyGuid + (LE64 20)
$hdrSm = [long](30 + $smallObj.Length)
$asfSm = $asfGuid + (LE64 $hdrSm) + (LE32 1) + [byte[]](0,0) + $smallObj
$pSm = Write-File 'smallobj.wmv' $asfSm
$dSm = PF $pSm; Assert ($dSm -lt 0) "ASF objSize<24 break ($dSm)"

# ============ 4. degenerate input ============
Write-Host '== degenerate input =='
$p15 = Write-File 'empty.mp4' ([byte[]](New-Object byte[] 0))
$d15 = PF $p15; Assert ($d15 -lt 0) "empty file fail ($d15)"

$p16 = Write-File 'tiny.mp4' ([byte[]](0,0,0,0,0,0,0,0,0,0,0,0,0,0,0))
$d16 = PF $p16; Assert ($d16 -lt 0) "15-byte file fail ($d16)"

$p17 = Write-File 'fakemp4.mp4' ([byte[]][char[]]'garbagegarbagegarbagegarbage')
$d17 = PF $p17; Assert ($d17 -lt 0) "fake mp4 (non-container) fail ($d17)"

$ff = New-Object byte[] 4096
for ($i=0; $i -lt $ff.Length; $i++){ $ff[$i] = 0xFF }
$p18 = Write-File 'ff.mp4' $ff
$d18 = PF $p18; Assert ($d18 -lt 0) "all-0xFF MP4 fail no crash ($d18)"

# ============ 5. Scan level ============
Write-Host '== Scan level =='

$edir = Join-Path $tmp 'empty_dir'
New-Item -ItemType Directory -Path $edir | Out-Null
$r = [VideoTime.VideoScanner]::Run($edir, $true, [Threading.CancellationToken]::None)
Assert ($r.TotalSeconds -eq 0) "empty dir TotalSeconds=0"
Assert ($r.FolderResults.Count -eq 1) "empty dir FolderResults=1"
Assert ($r.FailCount -eq 0) "empty dir no fail"

$mix1 = Join-Path $tmp 'mix_bad'
New-Item -ItemType Directory -Path $mix1 | Out-Null
Copy-Item $p1 (Join-Path $mix1 'good.mp4')
$badp = Join-Path $mix1 'bad.mp4'
[IO.File]::WriteAllBytes($badp, [byte[]](0x67,0x61,0x72,0x62,0x61,0x67,0x65))
$r = [VideoTime.VideoScanner]::Run($mix1, $false, [Threading.CancellationToken]::None)
Assert (Approx $r.TotalSeconds 60 0.001) "mixed dir total = 60s"
Assert ($r.FailCount -eq 1) "mixed dir fail = 1"
Assert ($r.FolderResults[0].FileCount -eq 2) "FileCount includes failed file (=2, matches doc)"
Assert ($r.FolderResults[0].FolderPath -eq $mix1) "FolderResults[0] 是根目录"

$dt2 = Join-Path $tmp 'deeptest'
New-Item -ItemType Directory -Path $dt2 | Out-Null
$deep = $dt2
for ($i=0; $i -lt 61; $i++) { $deep = Join-Path $deep ("d" + $i); New-Item -ItemType Directory -Path $deep | Out-Null }
Copy-Item $p1 (Join-Path $deep 'leaf.mp4')
$r = [VideoTime.VideoScanner]::Run($dt2, $true, [Threading.CancellationToken]::None)
Assert ($r.DepthSkipped -ge 1) "61-level nesting -> skipped $($r.DepthSkipped)"
Assert (Approx $r.TotalSeconds 0 0.001) "61-level leaf(depth61>50) omitted, total=0"
Assert ($r.FailCount -eq 0) "61-level no file fail"
Assert ($r.FolderResults.Count -eq 51) "recorded depth 0..50 (root+50) = $($r.FolderResults.Count)"
Assert ($r.SkippedDirs.Count -ge 1) "SkippedDirs 非空（61级）"
Assert ($r.SkippedDirs[0] -ne '') "SkippedDirs 路径非空"

$nr = Join-Path $tmp 'nonrec'
$nrSub = Join-Path $nr 'sub'
New-Item -ItemType Directory -Path $nrSub -Force | Out-Null
Copy-Item $p1 (Join-Path $nr 'top.mp4')
Copy-Item $p7 (Join-Path $nrSub 'inner.mkv')
$r = [VideoTime.VideoScanner]::Run($nr, $false, [Threading.CancellationToken]::None)
Assert (Approx $r.TotalSeconds 60 0.001) "non-recursive top only 60s"

# 不存在的目录 -> VideoScanner 防御性返回 DirFail=1（不抛异常，CLI/GUI 已预检）
$r = [VideoTime.VideoScanner]::Run((Join-Path $tmp 'nope'), $false, [Threading.CancellationToken]::None)
Assert ($r.DirFail -eq 1) "nonexistent dir -> DirFail=1 (defensive, no throw)"

# Reparse point (junction) 不递归
$rpBase = Join-Path $tmp 'juncycle'
New-Item -ItemType Directory -Path $rpBase -Force | Out-Null
Copy-Item $p1 (Join-Path $rpBase 'a.mp4')
cmd /c mklink /J (Join-Path $rpBase 'self') $rpBase 2>$null
$r = [VideoTime.VideoScanner]::Run($rpBase, $true, [Threading.CancellationToken]::None)
Assert ($r.TotalSeconds -ge 0) "junction cycle terminates (no hang)"
Assert ($r.FailCount -eq 0) "junction cycle no crash"

# ============ 6. Report escaping ============
Write-Host '== Report escaping =='
$weird = Join-Path $tmp 'we&ird,dir'
New-Item -ItemType Directory -Path $weird | Out-Null
Copy-Item $p1 (Join-Path $weird 'a.mp4')
$r = [VideoTime.VideoScanner]::Run($weird, $false, [Threading.CancellationToken]::None)
$csv = [VideoTime.ReportExporter]::BuildCsv($r)
$html = [VideoTime.ReportExporter]::BuildHtml($r)
Assert ($csv.Contains('we&ird,dir",')) "CSV quotes comma-containing path"
Assert ($html.Contains('we&amp;ird,dir')) 'HTML escapes & comma'

# ============ 7. Unicode path ============
Write-Host '== Unicode path =='
$zh = Join-Path $tmp '视频 目录'
New-Item -ItemType Directory -Path $zh | Out-Null
Copy-Item $p1 (Join-Path $zh '课程 🎬 视频.mp4')
$r = [VideoTime.VideoScanner]::Run($zh, $false, [Threading.CancellationToken]::None)
Assert (Approx $r.TotalSeconds 60 0.001) "unicode path scan total = 60s"
$csv = [VideoTime.ReportExporter]::BuildCsv($r)
$html = [VideoTime.ReportExporter]::BuildHtml($r)
Assert ($csv.Contains('视频 目录')) "CSV keeps unicode folder name"
Assert ($html.Contains('视频 目录')) "HTML keeps unicode folder name"

$zh2 = Join-Path $tmp '带,逗号目录'
New-Item -ItemType Directory -Path $zh2 | Out-Null
Copy-Item $p1 (Join-Path $zh2 'a.mp4')
$r = [VideoTime.VideoScanner]::Run($zh2, $false, [Threading.CancellationToken]::None)
$csv = [VideoTime.ReportExporter]::BuildCsv($r)
Assert ($csv.Contains('带,逗号目录",')) "CSV quotes unicode+comma folder"

# ============ 8. Fuzz: seeded random bytes ============
Write-Host '== Fuzz =='
$rng = New-Object Random 12345
$exts = @('.mp4','.mov','.m4v','.3gp','.mkv','.webm','.avi','.wmv','.asf','.txt')
$fuzzOk = $true
for ($i = 0; $i -lt 12; $i++) {
    $size = $rng.Next(0, 4097)
    $data = New-Object byte[] $size
    $rng.NextBytes($data)
    $ext = $exts[$rng.Next(0, $exts.Length)]
    $fp = Write-File ('fuzz' + $i + $ext) $data
    $d = PF $fp
    if ($d -lt -1) { $fuzzOk = $false; Write-Host "    [note] fuzz$i$ext returned $d" }
}
Assert ($fuzzOk) "fuzz: 12 random files return -1..n (no crash/hang)"

Write-Host ''
Write-Host "Passed $script:Pass, Failed $script:Fail"
try { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -Recurse -Force $hdir -ErrorAction SilentlyContinue } catch { }
if ($script:Fail -gt 0) { exit 1 } else { exit 0 }