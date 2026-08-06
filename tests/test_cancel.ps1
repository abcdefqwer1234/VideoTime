param(
    [string]$ExePath = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'bin\Debug\VideoTime.exe'),
    [string]$HelperDir = (Split-Path -Parent $MyInvocation.MyCommand.Path)
)
$ErrorActionPreference = 'Stop'
$script:Pass = 0
$script:Fail = 0

. (Join-Path $PSScriptRoot 'lib.ps1')

if (-not (Test-Path $ExePath)) { Write-Error "exe not found: $ExePath"; exit 1 }
$exe = (Resolve-Path $ExePath).Path
$hdir = Join-Path $env:TEMP ('vt_helper_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $hdir | Out-Null
Copy-Item $exe (Join-Path $hdir 'VideoTime.exe') -Force
Copy-Item (Join-Path $HelperDir 'CollectProgress.dll') (Join-Path $hdir 'CollectProgress.dll') -Force
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $hdir 'VideoTime.exe')))
[void][VideoTime.DurationParser]

# progress helper: records progress lines + cancels on a chosen phase
$helperAsm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $hdir 'CollectProgress.dll')))
$recorderType = $helperAsm.GetType('CancelRecorder')
if (-not $recorderType) { Write-Error 'CollectProgress.dll 缺少 CancelRecorder 类型'; exit 1 }

$tmp = Join-Path $env:TEMP ('vt_cancel_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
Write-Host "test dir: $tmp"

Write-Host '== Cancel =='

# baseline: no cancellation, scan completes normally
$dirA = Join-Path $tmp 'base'
New-Item -ItemType Directory -Path $dirA | Out-Null
[IO.File]::WriteAllBytes((Join-Path $dirA 'a.mp4'), (New-ValidMp4))
$r = [VideoTime.VideoScanner]::Run($dirA, $false, [Threading.CancellationToken]::None)
Assert (Approx $r.TotalSeconds 60 0.001) "baseline: no cancel -> total = 60s"

# pre-cancelled token: should not hang, should throw OperationCanceledException
$cts0 = New-Object Threading.CancellationTokenSource
$cts0.Cancel()
$oce0 = $false
try { $null = [VideoTime.VideoScanner]::Run($dirA, $false, $cts0.Token) } catch { $oce0 = Is-Oce $_.Exception }
Assert ($oce0) "pre-cancelled token -> OperationCanceledException"

# cancel on collect phase: only the collect report should have fired
$dirB = Join-Path $tmp 'many'
New-Item -ItemType Directory -Path $dirB | Out-Null
for ($i = 0; $i -lt 24; $i++) {
    [IO.File]::WriteAllBytes((Join-Path $dirB ('f{0:D2}.mp4' -f $i)), (New-ValidMp4))
}
$cts1 = New-Object Threading.CancellationTokenSource
$cr1 = [Activator]::CreateInstance($recorderType)
$cr1.CancelPhase = 'collect'
$cr1.OnHit = [Action]{ $cts1.Cancel() }
$oce1 = $false
try { $null = [VideoTime.VideoScanner]::Run($dirB, $true, $cts1.Token, $cr1) } catch { $oce1 = Is-Oce $_.Exception }
Assert ($oce1) "cancel on collect -> OperationCanceledException"
Assert ($cr1.Lines.Count -eq 1) "cancel on collect -> only collect reported (got $($cr1.Lines.Count))"
Assert ($cr1.Lines[0].StartsWith('collect')) "cancel on collect -> first line is collect"

# cancel on parse phase: parse reported but never reaches N/N
$cts2 = New-Object Threading.CancellationTokenSource
$cr2 = [Activator]::CreateInstance($recorderType)
$cr2.CancelPhase = 'parse'
$cr2.OnHit = [Action]{ $cts2.Cancel() }
$oce2 = $false
try { $null = [VideoTime.VideoScanner]::Run($dirB, $true, $cts2.Token, $cr2) } catch { $oce2 = Is-Oce $_.Exception }
Assert ($oce2) "cancel on parse -> OperationCanceledException"
$parseLines = @($cr2.Lines | Where-Object { $_ -like 'parse:*' })
Assert ($parseLines.Count -ge 1) "cancel on parse -> parse progress reported ($($parseLines.Count))"
$lastParse = $parseLines[-1]
Assert ($lastParse -ne 'parse:24/24') "cancel on parse -> last report not N/N ($lastParse)"
Assert ($cr2.Lines[-1] -like 'parse:*') "cancel on parse -> no progress after last parse report"

# multi-root RunMultiple: collect reported as initial + per-root (regression for collect progress)
$dirM1 = Join-Path $tmp 'm1'
$dirM2 = Join-Path $tmp 'm2'
New-Item -ItemType Directory -Path $dirM1 | Out-Null
New-Item -ItemType Directory -Path $dirM2 | Out-Null
[IO.File]::WriteAllBytes((Join-Path $dirM1 'a.mp4'), (New-ValidMp4))
[IO.File]::WriteAllBytes((Join-Path $dirM2 'b.mp4'), (New-ValidMp4))
$cr4 = [Activator]::CreateInstance($recorderType)
$r4 = [VideoTime.VideoScanner]::RunMultiple(@($dirM1, $dirM2), $true, [Threading.CancellationToken]::None, $cr4)
Assert ($r4.TotalSeconds -eq 120) "RunMultiple 双根合计 = 120s（60+60）"
$cLines = @($cr4.Lines | Where-Object { $_ -like 'collect:*' })
Assert ($cLines.Count -eq 3) "RunMultiple collect 上报 0/2,1/2,2/2 三条（got $($cLines.Count)）"
Assert ($cLines[0] -eq 'collect:0/2') "RunMultiple 首条 collect:0/2（got $($cLines[0])）"
Assert ($cLines[2] -eq 'collect:2/2') "RunMultiple 末条 collect:2/2（got $($cLines[2])）"

# multi-root cancel on parse phase still works
$cts5 = New-Object Threading.CancellationTokenSource
$cr5 = [Activator]::CreateInstance($recorderType)
$cr5.CancelPhase = 'parse'
$cr5.OnHit = [Action]{ $cts5.Cancel() }
$oce5 = $false
try { $null = [VideoTime.VideoScanner]::RunMultiple(@($dirM1, $dirM2), $true, $cts5.Token, $cr5) } catch { $oce5 = Is-Oce $_.Exception }
Assert ($oce5) "RunMultiple cancel on parse -> OperationCanceledException"

Write-Host ''
Write-Host "Passed $script:Pass, Failed $script:Fail"
try { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -Recurse -Force $hdir -ErrorAction SilentlyContinue } catch { }
if ($script:Fail -gt 0) { exit 1 } else { exit 0 }
