# Shared test helpers: build-tool discovery + common assertion/byte-builder functions.
# Usage: . (Join-Path $PSScriptRoot 'lib.ps1'); $tools = Get-VsBuildTools
#
# 约定：
#   - Assert 使用 $script:Pass / $script:Fail 计数，调用脚本需在顶部初始化两个变量。
#   - New-Valid* 与 Write-File 供需要构造合成媒体样本的脚本使用（Write-File 写入调用方作用域的 $tmp 目录）。

function Get-VsBuildTools {
    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = ''
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null | Select-Object -First 1
    }
    if (-not $msbuild -and $env:VSINSTALLDIR) {
        $cand = Join-Path $env:VSINSTALLDIR 'MSBuild\Current\Bin\MSBuild.exe'
        if (Test-Path $cand) { $msbuild = $cand }
    }
    if (-not $msbuild) { throw 'MSBuild not found (install VS or set VSINSTALLDIR)' }

    $csc = Join-Path (Split-Path $msbuild) 'Roslyn\csc.exe'
    if (-not (Test-Path $csc)) { throw "csc not found: $csc" }

    $refAsm = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
    if (-not (Test-Path $refAsm)) { $refAsm = 'C:\Program Files\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8' }
    if (-not (Test-Path $refAsm)) { throw "Reference assemblies (.NET Framework 4.8) not found: $refAsm" }

    return @{ MsBuild = $msbuild; Csc = $csc; RefAsm = $refAsm }
}

# ---- common assertion / byte helpers (shared by multiple test scripts) ----

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

function Is-Oce([Exception]$ex) {
    $e = $ex
    while ($e -ne $null) {
        if ($e -is [OperationCanceledException]) { return $true }
        $e = $e.InnerException
    }
    return $false
}

function PF([string]$path) { return [VideoTime.DurationParser]::ParseFile($path) }

function New-ValidMp4([uint32]$durMs = 60000) {
    $ftyp = [byte[]](BE32 20) + [byte[]][char[]]('ftyp') + [byte[]][char[]]('isom') + [byte[]](0,0,0,0) + [byte[]][char[]]('isom')
    $mvhd = [byte[]](BE32 108) + [byte[]][char[]]('mvhd') + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + (BE32 1000) + (BE32 $durMs) + [byte[]](New-Object byte[] 80)
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
