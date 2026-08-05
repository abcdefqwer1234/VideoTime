# Shared helper: locate VS MSBuild / csc / reference-assembly dir.
# Usage: . (Join-Path $PSScriptRoot 'lib.ps1'); $tools = Get-VsBuildTools

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