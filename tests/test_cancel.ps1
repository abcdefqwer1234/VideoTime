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

function Is-Oce([Exception]$ex) {
    $e = $ex
    while ($e -ne $null) {
        if ($e -is [OperationCanceledException]) { return $true }
        $e = $e.InnerException
    }
    return $false
}

if (-not (Test-Path $ExePath)) { Write-Error "exe not found: $ExePath"; exit 1 }
$exe = (Resolve-Path $ExePath).Path
$hdir = Join-Path $env:TEMP ('vt_helper_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $hdir | Out-Null
Copy-Item $exe (Join-Path $hdir 'VideoTime.exe') -Force
Copy-Item (Join-Path $HelperDir 'CollectProgress.dll') (Join-Path $hdir 'CollectProgress.dll') -Force
[void][Reflection.Assembly]::LoadFrom((Join-Path $hdir 'VideoTime.exe'))
[void][VideoTime.DurationParser]

# progress helper: records progress lines + cancels on a chosen phase
$helperAsm = [Reflection.Assembly]::LoadFrom((Join-Path $hdir 'CollectProgress.dll'))
$recorderType = $helperAsm.GetType('CancelRecorder')
if (-not $recorderType) { Write-Error 'CollectProgress.dll 缺少 CancelRecorder 类型'; exit 1 }

$tmp = Join-Path $env:TEMP ('vt_cancel_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
Write-Host "test dir: $tmp"

function New-ValidMp4([uint32]$durMs = 60000) {
    $ftyp = [byte[]](BE32 20) + [byte[]][char[]]('ftyp') + [byte[]][char[]]('isom') + [byte[]](0,0,0,0) + [byte[]][char[]]('isom')
    $mvhd = [byte[]](BE32 108) + [byte[]][char[]]('mvhd') + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + (BE32 1000) + (BE32 $durMs) + [byte[]](New-Object byte[] 80)
    $moov = [byte[]](BE32 (8 + $mvhd.Length)) + [byte[]][char[]]('moov') + $mvhd
    return $ftyp + $moov
}

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

Write-Host ''
Write-Host "Passed $script:Pass, Failed $script:Fail"
try { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -Recurse -Force $hdir -ErrorAction SilentlyContinue } catch { }
if ($script:Fail -gt 0) { exit 1 } else { exit 0 }
