param(
    [string]$ExePath = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'bin\Debug\VideoTime.exe'),
    [string]$UserConfigPath = ''
)
if (-not $UserConfigPath) {
    $cfgDir = Join-Path $env:LOCALAPPDATA 'VideoTime'
    $found = Get-ChildItem $cfgDir -Filter 'user.config' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $UserConfigPath = $found.FullName }
}
$ErrorActionPreference = 'Stop'
$script:Pass = 0
$script:Fail = 0

function Assert([bool]$cond, [string]$msg) {
    if ($cond) { $script:Pass++; Write-Host "  PASS  $msg" -ForegroundColor Green }
    else { $script:Fail++; Write-Host "  FAIL  $msg" -ForegroundColor Red }
}

if (-not (Test-Path $ExePath)) { Write-Error "找不到 exe: $ExePath"; exit 1 }
$exe = (Resolve-Path $ExePath).Path
function BE32([long]$v) { $b = [BitConverter]::GetBytes([uint32]$v); [Array]::Reverse($b); return [byte[]]$b }
$cfg = $UserConfigPath
$bak = $cfg + '.vtbak'
$createdConfig = -not (Test-Path $cfg)

# 若目标配置文件尚不存在（新机器未运行过 GUI），先按最小模板创建，保证可备份/恢复
if ($createdConfig) {
    $dir = Split-Path $cfg
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
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
    [IO.File]::WriteAllLines($cfg, $tmpl, [Text.Encoding]::UTF8)
}
Copy-Item $cfg $bak -Force

# 先删除可能已存在的 LogOutputLevel 节点（避免重复节点导致配置加载异常），再插入目标值
function Set-Level([string]$value) {
    $lines = [IO.File]::ReadAllLines($cfg, [Text.Encoding]::UTF8)
    $out = New-Object System.Collections.Generic.List[string]
    $inSetting = $false
    foreach ($ln in $lines) {
        if ($ln -match '<setting name="LogOutputLevel"') { $inSetting = $true; continue }
        if ($inSetting -and $ln -match '</setting>') { $inSetting = $false; continue }
        if (-not $inSetting) { $out.Add($ln) }
    }
    # 插到 </VideoTime.Properties.Settings> 之前
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
    if (-not $inserted) { throw '未找到 VideoTime.Properties.Settings 结束标记' }
    [IO.File]::WriteAllLines($cfg, $tmp2, [Text.Encoding]::UTF8)
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

    # Verify [信息] tag text in log
    $logText = [IO.File]::ReadAllText($logPath)
    Assert ($logText -match '\[信息\]') "log.txt 含 [信息] 标签"

    # Warning level: Info-level logs filtered out
    Set-Level 'Warning'
    RunCli @('-d', $sampleDir) | Out-Null
    Start-Sleep -Milliseconds 300
    $lenWarn = (Get-Item $logPath).Length
    Assert ($lenWarn -eq $lenInfo) "日志级别 Warning 时 Info 级被过滤（$lenInfo -> $lenWarn）"

    # Error level: Info/Warning-level logs filtered out
    Set-Level 'Error'
    RunCli @('-d', $sampleDir) | Out-Null
    Start-Sleep -Milliseconds 300
    $lenErr = (Get-Item $logPath).Length
    Assert ($lenErr -eq $lenInfo) "日志级别 Error 时 Info/Warning 级被过滤（$lenInfo -> $lenErr）"
}
finally {
    if ($createdConfig) {
        Remove-Item -Force $cfg -ErrorAction SilentlyContinue
    } else {
        Move-Item -Force $bak $cfg -ErrorAction SilentlyContinue
    }
    Remove-Item -Recurse -Force $sampleDir -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "通过 $script:Pass 项，失败 $script:Fail 项"
if ($script:Fail -gt 0) { exit 1 } else { exit 0 }
