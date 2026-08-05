param(
    [string]$ExePath = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'bin\Debug\VideoTime.exe'),
    [string]$UserConfigPath = ''
)
$ErrorActionPreference = 'Stop'
$script:Pass = 0
$script:Fail = 0

function Assert([bool]$cond, [string]$msg) {
    if ($cond) { $script:Pass++; Write-Host "  PASS  $msg" -ForegroundColor Green }
    else { $script:Fail++; Write-Host "  FAIL  $msg" -ForegroundColor Red }
}

if (-not (Test-Path $ExePath)) { Write-Error "找不到 exe: $ExePath"; exit 1 }
$exe = (Resolve-Path $ExePath).Path

# ---- 定位被测 exe 读取的 user.config ----
# 应用默认读取与其身份相关的 user.config：%LOCALAPPDATA%\<AppName>\<exe>_Url_<hash>\<version>\user.config。
# 一台机器可能同时存在多个不同身份的 user.config（Debug/Release/多次发布），
# 无法从文件系统枚举顺序可靠推断被测 exe 用哪一个。因此改为：对找到的所有 user.config
# 统一写入目标级别、并在结束后逐一恢复，从而确保被测 exe 读到的必然是该级别（确定性）。
if ($UserConfigPath) {
    $cfgs = @(Get-Item -LiteralPath $UserConfigPath -ErrorAction Stop)
} else {
    $cfgDir = Join-Path $env:LOCALAPPDATA 'VideoTime'
    $cfgs = @(Get-ChildItem $cfgDir -Filter 'user.config' -Recurse -ErrorAction SilentlyContinue)
}

# 完全没有现成配置时，创建一份最小模板（best-effort），至少保证脚本不因缺配置而中断
if ($cfgs.Count -eq 0) {
    $manualDir = Join-Path $env:LOCALAPPDATA 'VideoTime'
    $manual = Join-Path $manualDir 'user.config'
    if (-not (Test-Path $manualDir)) { New-Item -ItemType Directory -Path $manualDir -Force | Out-Null }
    if (-not (Test-Path $manual)) {
        $tmpl = @(
            '<?xml version="1.0" encoding="utf-8"?>',
            '<configuration>',
            '    <configSections>',
            '        <sectionGroup name="userSettings" type="System.Configuration.UserSettingsGroup, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089">',
            '            <section name="VideoTime.Properties.Settings" type="System.Configuration.ClientSettingsSection, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" allowExeDefinition="MachineToLocalUser" requirePermission="false" />',
            '        </sectionGroup>',
            '    </configSections>',
            '    <userSettings>',
            '        <VideoTime.Properties.Settings>',
            '        </VideoTime.Properties.Settings>',
            '    </userSettings>',
            '</configuration>'
        )
        [IO.File]::WriteAllLines($manual, $tmpl, (New-Object System.Text.UTF8Encoding($true)))
    }
    $cfgs = @(Get-Item $manual)
}

$script:ManagedPaths = @()
$script:CreatedPaths = @()
$baks = @()
foreach ($c in $cfgs) {
    $p = $c.FullName
    $script:ManagedPaths += $p
    if (-not (Test-Path $p)) { $script:CreatedPaths += $p }
    $bak = $p + '.vtbak'
    Copy-Item $p $bak -Force
    $baks += @{ Orig = $p; Bak = $bak }
}

# 对每个被管理的 user.config 重写 LogOutputLevel（先去重旧节点再插入目标值）
function Set-Level([string]$value) {
    foreach ($p in $script:ManagedPaths) {
        $lines = [IO.File]::ReadAllLines($p, [Text.Encoding]::UTF8)
        $out = New-Object System.Collections.Generic.List[string]
        $inSetting = $false
        foreach ($ln in $lines) {
            if ($ln -match '<setting name="LogOutputLevel"') { $inSetting = $true; continue }
            if ($inSetting -and $ln -match '</setting>') { $inSetting = $false; continue }
            if (-not $inSetting) { $out.Add($ln) }
        }
        $inserted = $false
        $tmp2 = New-Object System.Collections.Generic.List[string]
        foreach ($ln in $out) {
            if (-not $inserted -and $ln -match '</VideoTime.Properties.Settings>') {
                $tmp2.Add('            <setting name="LogOutputLevel" serializeAs="String">')
                $tmp2.Add('                <value>' + $value + '</value>')
                $tmp2.Add('            </setting>')
                $inserted = $true
            }
            $tmp2.Add($ln)
        }
        if (-not $inserted) { throw ('未找到 VideoTime.Properties.Settings 结束标记: ' + $p) }
        [IO.File]::WriteAllLines($p, $tmp2, [Text.Encoding]::UTF8)
    }
}

function RunCli([string[]]$argsList) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = ($argsList | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $proc = [Diagnostics.Process]::Start($psi)
    $null = $proc.StandardOutput.ReadToEnd()
    $null = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    return $proc.ExitCode
}

function BE32([long]$v) { $b = [BitConverter]::GetBytes([uint32]$v); [Array]::Reverse($b); return [byte[]]$b }
$logPath = Join-Path (Split-Path $exe) 'log.txt'
$sampleDir = Join-Path $env:TEMP ('vt_cfgdir_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sampleDir | Out-Null
$ftyp = [byte[]](BE32 20) + [byte[]][char[]]('ftyp') + [byte[]][char[]]('isom') + [byte[]](0,0,0,0) + [byte[]][char[]]('isom')
$mvhd = [byte[]](BE32 108) + [byte[]][char[]]('mvhd') + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + [byte[]](0,0,0,0) + (BE32 1000) + (BE32 60000) + [byte[]](New-Object byte[] 80)
$moov = [byte[]](BE32 (8 + $mvhd.Length)) + [byte[]][char[]]('moov') + $mvhd
[IO.File]::WriteAllBytes((Join-Path $sampleDir 'x.mp4'), ($ftyp + $moov))

try {
    $prevLen = if (Test-Path $logPath) { (Get-Item $logPath).Length } else { 0 }

    Set-Level 'Off'
    RunCli @('-d', $sampleDir) | Out-Null
    Start-Sleep -Milliseconds 300
    $lenOff = if (Test-Path $logPath) { (Get-Item $logPath).Length } else { 0 }
    Assert ($lenOff -eq $prevLen) "日志级别 Off 时 CLI 不写日志（$prevLen -> $lenOff）"

    Set-Level 'Info'
    RunCli @('-d', $sampleDir) | Out-Null
    Start-Sleep -Milliseconds 300
    $lenInfo = (Get-Item $logPath).Length
    Assert ($lenInfo -gt $prevLen) "日志级别 Info 时 CLI 写日志（$prevLen -> $lenInfo）"

    $logText = [IO.File]::ReadAllText($logPath)
    Assert ($logText -match '\[信息\]') "log.txt 含 [信息] 标签"

    Set-Level 'Warning'
    RunCli @('-d', $sampleDir) | Out-Null
    Start-Sleep -Milliseconds 300
    $lenWarn = (Get-Item $logPath).Length
    Assert ($lenWarn -eq $lenInfo) "日志级别 Warning 时 Info 级被过滤（$lenInfo -> $lenWarn）"

    Set-Level 'Error'
    RunCli @('-d', $sampleDir) | Out-Null
    Start-Sleep -Milliseconds 300
    $lenErr = (Get-Item $logPath).Length
    Assert ($lenErr -eq $lenInfo) "日志级别 Error 时 Info/Warning 级被过滤（$lenInfo -> $lenErr）"
}
finally {
    # 还原所有被管理的 user.config
    foreach ($bk in $baks) {
        if ($script:CreatedPaths -contains $bk.Orig) {
            Remove-Item -Force $bk.Orig -ErrorAction SilentlyContinue
        } else {
            Move-Item -Force $bk.Bak $bk.Orig -ErrorAction SilentlyContinue
        }
    }
    # 校验用户的配置还原后仍为合法 XML（防止备份/恢复过程损坏配置）
    foreach ($p in $script:ManagedPaths) {
        if (Test-Path $p) {
            try {
                [xml]$null = Get-Content $p
                Assert $true ("配置还原后仍为合法 XML: " + (Split-Path $p -Leaf))
            } catch {
                Assert $false ("配置还原后为非法 XML: " + (Split-Path $p -Leaf))
            }
        }
    }
    Remove-Item -Recurse -Force $sampleDir -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "通过 $script:Pass 项，失败 $script:Fail 项"
if ($script:Fail -gt 0) { exit 1 } else { exit 0 }