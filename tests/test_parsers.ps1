param(
    [string]$ExePath = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'bin\Debug\VideoTime.exe'),
    [string]$HelperDir = (Split-Path -Parent $MyInvocation.MyCommand.Path)
)
$ErrorActionPreference = 'Stop'
$script:Pass = 0
$script:Fail = 0

. (Join-Path $PSScriptRoot 'lib.ps1')

if (-not (Test-Path $ExePath)) { Write-Error "找不到 exe: $ExePath"; exit 1 }
$exe = (Resolve-Path $ExePath).Path
$hdir = Join-Path $env:TEMP ('vt_helper_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $hdir | Out-Null
Copy-Item $exe (Join-Path $hdir 'VideoTime.exe') -Force
Copy-Item (Join-Path $HelperDir 'CollectProgress.dll') (Join-Path $hdir 'CollectProgress.dll') -Force
# 按字节加载（而非 LoadFrom），避免进程内锁住 $hdir 下的 DLL/EXE，导致结尾清理失败残留临时目录
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $hdir 'VideoTime.exe')))
[void][VideoTime.DurationParser]

$tmp = Join-Path $env:TEMP ('vt_parser_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
Write-Host "测试目录: $tmp"

# ---------- synthetic media（经 lib.ps1 统一构造器，与其它测试同源） ----------
$mp4Path = Join-Path $tmp 'sample.mp4'
[IO.File]::WriteAllBytes($mp4Path, (New-ValidMp4))

$mkvPath = Join-Path $tmp 'sample.mkv'
[IO.File]::WriteAllBytes($mkvPath, (New-ValidMkv))

$aviPath = Join-Path $tmp 'sample.avi'
[IO.File]::WriteAllBytes($aviPath, (New-ValidAvi))

$wmvPath = Join-Path $tmp 'sample.wmv'
[IO.File]::WriteAllBytes($wmvPath, (New-ValidWmv))

# ---------- failure & non-video ----------
$badPath = Join-Path $tmp 'broken.mp4'
[IO.File]::WriteAllBytes($badPath, [byte[]](0x67,0x61,0x72,0x62,0x61,0x67,0x65))
$txtPath = Join-Path $tmp 'note.txt'
[IO.File]::WriteAllText($txtPath, 'hello')
# >16B 的 .txt：走"不支持的文件格式"分支（<16B 会先落到"文件过小"分支）
$bigTxtPath = Join-Path $tmp 'note_big.txt'
[IO.File]::WriteAllText($bigTxtPath, ('x' * 64))

Write-Host '== 各格式时长解析 =='
$d1 = [VideoTime.DurationParser]::ParseFile($mp4Path); Assert (Approx $d1 60 0.001)   "MP4  解析 = $d1 秒（期望 60）"
$d2 = [VideoTime.DurationParser]::ParseFile($mkvPath); Assert (Approx $d2 120 0.001)  "MKV  解析 = $d2 秒（期望 120）"
$d3 = [VideoTime.DurationParser]::ParseFile($aviPath); Assert (Approx $d3 200 0.001)  "AVI  解析 = $d3 秒（期望 200）"
$d4 = [VideoTime.DurationParser]::ParseFile($wmvPath); Assert (Approx $d4 200 0.001)  "WMV  解析 = $d4 秒（期望 200）"
$d5 = [VideoTime.DurationParser]::ParseFile($badPath); Assert ($d5 -eq -1)              "坏MP4解析 = $d5（期望 -1 失败）"
$d6 = [VideoTime.DurationParser]::ParseFile($txtPath); Assert ($d6 -eq -1)              "TXT  解析 = $d6（期望 -1 不支持）"
$d6b = [VideoTime.DurationParser]::ParseFile($bigTxtPath); Assert ($d6b -eq -1)        ">16B TXT 解析 = $d6b（期望 -1 不支持格式分支）"

Write-Host '== 扩展名过滤 =='
$listed = [VideoTime.DurationParser]::GetVideoFiles($tmp)
Assert ($listed.Count -eq 5) "GetVideoFiles 只返回视频扩展名（$($listed.Count) 个，期望 5）"
Assert ($listed -contains $mp4Path) "GetVideoFiles 含 sample.mp4"
Assert ($listed -notcontains $txtPath) "GetVideoFiles 排除 note.txt"
$videoExts = @('.mp4','.mov','.m4v','.3gp','.mkv','.webm','.avi','.wmv','.asf')
Assert (@($listed | Where-Object { [IO.Path]::GetExtension($_) -notin $videoExts }).Count -eq 0) "GetVideoFiles 全部为视频扩展名"

Write-Host '== 混合格式整目录扫描 =='
$sub = Join-Path $tmp 'mix'
New-Item -ItemType Directory -Path $sub | Out-Null
Copy-Item $mp4Path $sub; Copy-Item $mkvPath $sub; Copy-Item $aviPath $sub; Copy-Item $wmvPath $sub

$helperAsm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $hdir 'CollectProgress.dll')))
$cpType = $helperAsm.GetType('CollectProgress')
$prog = [Activator]::CreateInstance($cpType)

$result = [VideoTime.VideoScanner]::Run($sub, $true, [Threading.CancellationToken]::None, $prog)

Assert (Approx $result.TotalSeconds 580 0.5)  "混合目录总时长 = $($result.TotalSeconds) 秒（期望 580）"
Assert ($result.TotalFileCount -eq 4)         "TotalFileCount = $($result.TotalFileCount)（期望 4）"
Assert ($result.FolderResults[0].FileCount -eq 4) "视频数 = $($result.FolderResults[0].FileCount)（期望 4）"
Assert ($result.FailCount -eq 0)             "失败文件 = $($result.FailCount)（期望 0）"
$items = @($prog.Lines)
$parsePhase = @($items | Where-Object { $_ -like 'parse:*' })
Assert ($parsePhase.Count -gt 0)             "进度事件含解析阶段（$($parsePhase.Count) 条）"
if ($parsePhase.Count -gt 0) {
    $lastParts = $parsePhase[-1].Split(':')[1].Split('/')
    Assert ($lastParts[0] -eq '4' -and $lastParts[1] -eq '4') "最后一条进度 4/4（$($parsePhase[-1])）"
} else {
    Assert $false "最后一条进度 4/4（无 parse 进度记录）"
}
$collectPhase = @($items | Where-Object { $_ -like 'collect:*' })
Assert ($collectPhase.Count -eq 1)           "进度事件含收集阶段（$($collectPhase.Count) 条）"

Write-Host '== 失败原因 =='
$fdir = Join-Path $tmp 'fail'
New-Item -ItemType Directory -Path $fdir | Out-Null
Copy-Item $badPath (Join-Path $fdir 'bad.mp4')
$fr = [VideoTime.VideoScanner]::Run($fdir, $false, [Threading.CancellationToken]::None)
Assert ($fr.TotalFileCount -eq 1) "TotalFileCount 计入失败文件 = $($fr.TotalFileCount)（期望 1）"
Assert ($fr.FailCount -eq 1) "失败数 = $($fr.FailCount)（期望 1）"
Assert ($fr.FailedFiles.Count -eq 1) "FailedFiles 记录 1 条"
Assert ($fr.FailedFiles[0].Reason -match '文件过小') "失败原因 = 文件过小（$($fr.FailedFiles[0].Reason)）"
Assert ($fr.TotalSeconds -eq 0) "失败文件不计入总时长（$($fr.TotalSeconds)）"

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

Write-Host '== 扩展名表覆盖 =='
$extdir = Join-Path $tmp 'exts'
New-Item -ItemType Directory -Path $extdir | Out-Null
foreach ($ext in $videoExts) {
    Copy-Item $mp4Path (Join-Path $extdir ('v' + ($ext.TrimStart('.')) + $ext))
}
[IO.File]::WriteAllText((Join-Path $extdir 'skip.txt'), ('y' * 64))
$el = @([VideoTime.DurationParser]::GetVideoFiles($extdir))
Assert ($el.Count -eq 9) "9 种视频扩展名全识别（$($el.Count) 个）"
Assert (@($el | Where-Object { [IO.Path]::GetExtension($_) -notin $videoExts }).Count -eq 0) "扩展名覆盖目录不含非视频文件"

Write-Host '== TextImageRenderer =='
$img = [VideoTime.TextImageRenderer]::Render('测试文本 abc 123')
Assert ($null -ne $img) "TextImageRenderer.Render 返回非空 Image"
Assert ($img.Width -gt 0 -and $img.Height -gt 0) "TextImageRenderer 尺寸为正 ($($img.Width)x$($img.Height))"
$img.Dispose()
$imgEmpty = [VideoTime.TextImageRenderer]::Render('')
Assert ($null -ne $imgEmpty -and $imgEmpty.Width -gt 0 -and $imgEmpty.Height -gt 0) "空文本渲染不崩溃 ($($imgEmpty.Width)x$($imgEmpty.Height))"
$imgEmpty.Dispose()
$multi = '行1' + "`n" + '行2' + "`n" + '行3'
$imgMulti = [VideoTime.TextImageRenderer]::Render($multi)
Assert ($null -ne $imgMulti -and $imgMulti.Height -gt 0) "多行文本渲染不崩溃"
$imgMulti.Dispose()

Write-Host ''
Write-Host "通过 $script:Pass 项，失败 $script:Fail 项"
try { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -Recurse -Force $hdir -ErrorAction SilentlyContinue } catch { }
if ($script:Fail -gt 0) { exit 1 } else { exit 0 }
