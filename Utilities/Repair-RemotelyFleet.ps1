<#
.SYNOPSIS
    Массово восстанавливает агентов Remoton без обхода машин вручную.

.DESCRIPTION
    На каждой машине через удалённый планировщик заданий (schtasks, /ru SYSTEM)
    запускается команда, которая:
      1) если файлы агента целы, а пропала только служба — пересоздаёт и запускает службу
         (мгновенно, без скачивания пакета);
      2) если файлы битые — запускает Install-Remotely.ps1 с сервера; он берёт DeviceID
         из ConnectionInfo.json (включая резервную копию) и ставит свежий агент.

    Нужны права администратора на целевых машинах и доступ по SMB/RPC (в домене — обычное дело).
    WinRM и PsExec не требуются.

.PARAMETER ComputerName
    Машины (имена или IP). Проверьте сначала на одной.

.PARAMETER ComputerListPath
    Файл со списком машин, по одной в строке.

.PARAMETER AllDomainComputers
    Все включённые компьютеры домена (кроме контроллеров домена).

.EXAMPLE
    .\Repair-RemotelyFleet.ps1 -ComputerName PEROVA-1
.EXAMPLE
    .\Repair-RemotelyFleet.ps1 -AllDomainComputers
.EXAMPLE
    .\Repair-RemotelyFleet.ps1 -ComputerListPath .\machines.txt
#>
param(
    [string[]]$ComputerName,
    [string]$ComputerListPath,
    [switch]$AllDomainComputers,
    [int]$BatchPauseSeconds = 0,
    [string]$ServerUrl = "https://help.urldot.ru",
    [string]$OutLog = "$env:TEMP\Repair-Remotely.log"
)

$ErrorActionPreference = "Continue"

if ($AllDomainComputers) {
    Import-Module ActiveDirectory -ErrorAction Stop
    $ComputerName = @(
        Get-ADComputer -Filter { Enabled -eq $true } |
            Where-Object { $_.Name -notmatch 'DC$' -and $_.Name -notmatch '^DC' } |
            Select-Object -ExpandProperty Name
    )
}
elseif ($ComputerListPath) {
    $ComputerName = @(Get-Content -Path $ComputerListPath -ErrorAction Stop |
        Where-Object { $_ -and $_ -notmatch '^\s*#' })
}

if (!$ComputerName -or $ComputerName.Count -eq 0) {
    Write-Error "Не задан список машин: -ComputerName, -ComputerListPath или -AllDomainComputers."
    exit 1
}

function Write-LogLine([string]$Text) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $Text"
    Write-Host $line
    Add-Content -Path $OutLog -Value $line -Encoding UTF8
}

# Выполняется НА целевой машине под SYSTEM.
$remoteScript = @'
$agent = "$env:ProgramFiles\Remotely\Remotely_Agent.exe"
$deps  = "$env:ProgramFiles\Remotely\Remotely_Agent.deps.json"
$svc   = "Remotely_Service"
$log   = "$env:TEMP\RemotonRepair.txt"

"$(Get-Date -Format o) start" | Out-File $log -Append

$healthy = (Test-Path $agent) -and (Test-Path $deps) -and ((Get-Item $deps).Length -gt 100)

if ($healthy) {
    if (-not (Get-Service $svc -ErrorAction SilentlyContinue)) {
        New-Service -Name $svc -BinaryPathName "`"$agent`"" -DisplayName "Remotely Service" -StartupType Automatic -Description "Background service that maintains a connection to the Remotely server." | Out-Null
        Start-Process cmd.exe -ArgumentList "/c sc.exe failure $svc reset=5 actions=restart/5000" -Wait -WindowStyle Hidden
        "service recreated" | Out-File $log -Append
    }
    Start-Service $svc -ErrorAction SilentlyContinue
    "service start attempted: $((Get-Service $svc -ErrorAction SilentlyContinue).Status)" | Out-File $log -Append
}
else {
    try {
        $tmp = "$env:TEMP\Install-Remotely.ps1"
        Invoke-WebRequest -Uri "__SERVER__/Content/Install-Remotely.ps1" -OutFile $tmp -UseBasicParsing
        & $tmp -Quiet
        "reinstall attempted: $((Get-Service $svc -ErrorAction SilentlyContinue).Status)" | Out-File $log -Append
    }
    catch {
        "reinstall error: $($_.Exception.Message)" | Out-File $log -Append
    }
}

"$(Get-Date -Format o) done" | Out-File $log -Append
'@.Replace("__SERVER__", $ServerUrl)

$taskName = "RemotonRepair"
$remoteScriptPath = "$env:TEMP\RemotonRepair-remote.ps1"
$remoteTargetPath = "C:\Windows\Temp\RemotonRepair.ps1"

# Скрипт доставляется файлом через админ-шару (C$): schtasks /tr не принимает
# длинную команду, а base64 от этого скрипта в лимит 261 символ не влезает.
Set-Content -Path $remoteScriptPath -Value $remoteScript -Encoding UTF8

Write-LogLine "Машин: $($ComputerName.Count). Лог на машинах: %TEMP%\RemotonRepair.txt"

$results = foreach ($pc in $ComputerName) {
    $tn = $taskName
    $target = $remoteTargetPath
    $sharePath = "\\$pc\C$\Windows\Temp\RemotonRepair.ps1"

    try {
        Copy-Item -Path $remoteScriptPath -Destination $sharePath -Force -ErrorAction Stop
    }
    catch {
        [pscustomobject]@{ Computer = $pc; Result = "COPY_FAILED: $($_.Exception.Message)" }
        continue
    }

    try {
        & schtasks.exe /s $pc /create /tn $tn /tr "powershell.exe -NoProfile -ExecutionPolicy Bypass -File $target" /sc once /st 00:00 /ru SYSTEM /f 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            [pscustomobject]@{ Computer = $pc; Result = "CREATE_FAILED (code $LASTEXITCODE)" }
            continue
        }
        & schtasks.exe /s $pc /run /tn $tn 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            [pscustomobject]@{ Computer = $pc; Result = "RUN_FAILED (code $LASTEXITCODE)" }
            continue
        }
        Start-Sleep -Seconds 1
        & schtasks.exe /s $pc /delete /tn $tn /f 2>&1 | Out-Null

        # Забираем журналы установки/ремонта для разбора, если машина не ожила.
        $logDir = "$env:TEMP\RemotonRepairLogs"
        if (!(Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
        foreach ($remoteLog in @(
            "C:\Windows\Temp\RemotonRepair.txt",
            "C:\Windows\Temp\Remotely_Install.txt"
        )) {
            Copy-Item -Path "\\$pc\C$\Windows\Temp\$(Split-Path $remoteLog -Leaf)" `
                -Destination "$logDir\$pc-$(Split-Path $remoteLog -Leaf)" -Force -ErrorAction SilentlyContinue
        }

        [pscustomobject]@{ Computer = $pc; Result = "STARTED" }
    }
    catch {
        [pscustomobject]@{ Computer = $pc; Result = "ERROR: $($_.Exception.Message)" }
    }
}

$results | ForEach-Object {
    Write-LogLine ("{0,-24} {1}" -f $_.Computer, $_.Result)
}

$ok = @($results | Where-Object { $_.Result -eq "STARTED" }).Count
$failed = @($results | Where-Object { $_.Result -ne "STARTED" }).Count
Write-LogLine "Итог: запущено $ok, проблем $failed. Лог: $OutLog"
