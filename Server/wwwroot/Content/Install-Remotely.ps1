<#
.SYNOPSIS
   Installs the Remotely Client.
.DESCRIPTION
   Do not modify this script.  It was generated specifically for your account.
.EXAMPLE
   powershell.exe -f Install-Remotely.ps1
   powershell.exe -f Install-Remotely.ps1 -DeviceAlias "My Super Computer" -DeviceGroup "My Stuff"
#>

param (
	[string]$DeviceAlias,
	[string]$DeviceGroup,
	[string]$Path,
	[string]$OrganizationId,
	[string]$ServerUrl,
	[switch]$Uninstall,
	[switch]$Quiet
)

#region Set SecurityProtocol
# This will include all security protocols greater than or equal to TLS 1.2.
[System.Net.SecurityProtocolType]$SecurityProtocols = 0;
[System.Enum]::GetValues([System.Net.SecurityProtocolType]) | Where-Object {
	$_ -ge [System.Net.SecurityProtocolType]::Tls12
} | ForEach-Object {
	$SecurityProtocols = $SecurityProtocols -bor $_
}
[System.Net.ServicePointManager]::SecurityProtocol = $SecurityProtocols
#endregion

#region Set Variables
$LogPath = "$env:TEMP\Remotely_Install.txt"

# Критично: агент запускает установщик со своим рабочим каталогом
# (C:\Program Files\Remotely). Windows не позволяет переименовать каталог,
# который является текущим для процесса, из-за чего замена каталога падала.
Set-Location -Path $env:TEMP

[string]$HostName = $null
if ($ServerUrl) {
	$HostName = $ServerUrl
}

[string]$Organization = $null
if ($OrganizationId) {
	$Organization = $OrganizationId
}

$ConnectionInfo = $null

if ([System.Environment]::Is64BitOperatingSystem) {
	$Platform = "x64"
}
else {
	$Platform = "x86"
}

$InstallPath = "$env:ProgramFiles\Remotely"
#endregion

#region Functions
function Write-Log($Message) {
	if (!$Quiet) {
		Write-Host $Message
	}
	"$((Get-Date).ToString()) - $Message" | Out-File -FilePath $LogPath -Append
}
function Do-Exit() {
	Write-Log "Exiting..."
	Start-Sleep -Seconds 3
	exit
}
function Is-Administrator() {
	$Identity = [Security.Principal.WindowsIdentity]::GetCurrent()
	$Principal = New-Object System.Security.Principal.WindowsPrincipal -ArgumentList $Identity
	return $Principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Run-StartupChecks {

	# Позволяем повторную установку/восстановление без параметров:
	# ищем ConnectionInfo.json в рабочем каталоге, в каталоге от прерванного
	# обновления (.old) и в резервной копии в TEMP.
	$ConnectionInfoCandidates = @(
		"$InstallPath\ConnectionInfo.json",
		"$InstallPath.old\ConnectionInfo.json",
		"$env:TEMP\Remotely-ConnectionInfo.json"
	)

	if (!$HostName -or !$Organization) {
		foreach ($Candidate in $ConnectionInfoCandidates) {
			if (($HostName -and $Organization) -or !(Test-Path -Path $Candidate)) {
				continue
			}
			try {
				$ExistingConnectionInfo = Get-Content -Path $Candidate -Raw | ConvertFrom-Json
				if (!$HostName -and $ExistingConnectionInfo.Host) {
					$script:HostName = $ExistingConnectionInfo.Host
				}
				if (!$Organization -and $ExistingConnectionInfo.OrganizationID) {
					$script:Organization = $ExistingConnectionInfo.OrganizationID
				}
				Write-Log "Using ConnectionInfo from `"$Candidate`" for server URL and organization."
			}
			catch {
				Write-Log "Failed to read `"$Candidate`".  Continuing with supplied parameters."
			}
		}
	}

	if (!$HostName -or !$Organization) {
		Write-Log "Required parameters are missing.  Please try downloading the installer again."
		Do-Exit
	}

	if (!(Is-Administrator)) {
		Write-Log -Message "Install script requires elevation.  Attempting to self-elevate..."
		Start-Sleep -Seconds 3

		$Params = "-File `"$($MyInvocation.ScriptName)`"";
		if ($OrganizationId) {
			$Params += " -OrganizationId $OrganizationId"
		}
		if ($ServerUrl) {
			$Params += " -ServerUrl $ServerUrl"
		}
		if ($DeviceAlias) {
			$Params += " -DeviceAlias $DeviceAlias"
		}
		if ($DeviceGroup) {
			$Params += " -DeviceGroup $DeviceGroup"
		}
		if ($Path) {
			$Params += " -Path `"$Path`""
		}
		if ($Uninstall) {
			$Params += " -Uninstall"
		}
		if ($Quiet) {
			$Params += " -Quiet"
		}
		Start-Process -FilePath powershell.exe -ArgumentList $Params -Verb RunAs
		exit
	}
}

function Stop-Remotely {
	# На время обновления отодвигаем автоперезапуск на 60 секунд: SCM не должен
	# поднимать убитого агента через 5 секунд, пока идёт распаковка и замена.
	# Пустой actions= ("") в разных оболочках срабатывает ненадёжно, поэтому
	# используем явное значение с большой задержкой. Вернём restart/5000 в Start-Remotely.
	Start-Process cmd.exe -ArgumentList '/c sc failure Remotely_Service reset= 0 actions= restart/60000' -Wait -WindowStyle Hidden

	# Останавливаем службу и ждём именно состояния STOPPED.
	Start-Process -FilePath "cmd.exe" -ArgumentList "/c sc stop Remotely_Service" -Wait -WindowStyle Hidden

	for ($i = 0; $i -lt 60; $i++) {
		$status = (Get-Service -Name "Remotely_Service" -ErrorAction SilentlyContinue).Status
		if (!$status -or $status -eq "Stopped") { break }
		Start-Sleep -Milliseconds 500
	}

	# Добиваем процессы во всех сессиях (SYSTEM видит все), включая Desktop.
	Start-Process -FilePath "cmd.exe" -ArgumentList "/c taskkill /F /IM Remotely_Agent.exe & taskkill /F /IM Remotely_Desktop.exe" -Wait -WindowStyle Hidden

	for ($i = 0; $i -lt 40; $i++) {
		if (!(Get-Process -Name Remotely_Agent -ErrorAction SilentlyContinue) -and
			!(Get-Process -Name Remotely_Desktop -ErrorAction SilentlyContinue)) {
			break
		}
		Start-Sleep -Milliseconds 500
	}
	Start-Sleep -Milliseconds 500
}

function Start-Remotely {
	# Возвращает систему в рабочее состояние: служба должна существовать и быть запущенной.
	try {
		# Автоперезапуск возвращаем ВСЕГДА, даже если запуск ниже не удастся:
		# иначе машина останется без защиты от падений до переустановки.
		if (!(Get-Service -Name "Remotely_Service" -ErrorAction SilentlyContinue)) {
			Write-Log "Remotely_Service is missing.  Recreating it."
			New-Service -Name "Remotely_Service" -BinaryPathName "`"$InstallPath\Remotely_Agent.exe`"" -DisplayName "Remotely Service" -StartupType Automatic -Description "Background service that maintains a connection to the Remotely server.  The service is used for remote support and maintenance by this computer's administrators."
		}

		# ВАЖНО: sc.exe требует пробел после знака равенства (reset= 5),
		# без него команда падает с Invalid parameter и автоперезапуск не
		# восстанавливается. Вызываем через cmd ради надёжных аргументов.
		Start-Process cmd.exe -ArgumentList '/c sc failure Remotely_Service reset= 5 actions= restart/5000' -Wait -WindowStyle Hidden

		Start-Service -Name Remotely_Service -ErrorAction Stop
		Write-Log "Remotely_Service started."
	}
	catch {
		Write-Log "Failed to start Remotely_Service: $($Error[0].Exception.Message)"
	}
}

function Uninstall-Remotely {
	Stop-Remotely
	# При удалении службу убираем явно: Stop-Remotely её намеренно оставляет,
	# чтобы неудачное обновление не убивало агента.
	Start-Process -FilePath "cmd.exe" -ArgumentList "/c sc delete Remotely_Service" -Wait -WindowStyle Hidden
	Remove-Item -Path $InstallPath -Force -Recurse -ErrorAction SilentlyContinue
	Remove-NetFirewallRule -Name "Remotely Desktop Unattended" -ErrorAction SilentlyContinue
}

function Install-Remotely {
	# ETag не обязателен для установки: при офлайн-установке из локального файла
	# сервер недоступен, и это не должно обрывать установку.
	# Берём ETag из CheckPackage (агент проверяет обновление именно там), запасной
	# вариант — статический файл, последний — «unknown».
	# ВАЖНО: платформа для CheckPackage — «win-x64»/«win-x86» (как называет её агент),
	# а не «x64»: иначе сервер ответит 304 без ETag, и сходимость etag.txt сломается.
	$ETag = $null
	try {
		$HeadResponse = Invoke-WebRequest -Uri "$HostName/api/AgentUpdate/CheckPackage/win-$Platform" -Method Head -UseBasicParsing -ErrorAction Stop
		$ETag = $HeadResponse.Headers["ETag"]
	}
	catch {
		Write-Log "CheckPackage HEAD failed.  Falling back to the static package file."
	}
	if (!$ETag) {
		try {
			$HeadResponse = Invoke-WebRequest -Uri "$HostName/Content/Remotely-Win-$Platform.zip" -Method Head -UseBasicParsing -ErrorAction Stop
			$ETag = $HeadResponse.Headers["ETag"]
		}
		catch {
			Write-Log "Failed to get ETag from server (offline install?).  Continuing."
		}
	}
	if (!$ETag) {
		Write-Log "No ETag available.  Agent will check for updates on its own schedule."
		$ETag = "unknown"
	}

	if ((Test-Path -Path "$InstallPath") -and (Test-Path -Path "$InstallPath\ConnectionInfo.json")) {
		$ConnectionInfo = Get-Content -Path "$InstallPath\ConnectionInfo.json" | ConvertFrom-Json
		if ($ConnectionInfo) {
			$ConnectionInfo.Host = $HostName
			$ConnectionInfo.OrganizationID = $Organization
			$ConnectionInfo.ServerVerificationToken = ""
		}
	}
	elseif (Test-Path -Path "$InstallPath.old\ConnectionInfo.json") {
		# Каталог остался переименованным после прерванного обновления.
		$ConnectionInfo = Get-Content -Path "$InstallPath.old\ConnectionInfo.json" | ConvertFrom-Json
		if ($ConnectionInfo) {
			$ConnectionInfo.Host = $HostName
			$ConnectionInfo.OrganizationID = $Organization
			$ConnectionInfo.ServerVerificationToken = ""
		}
	}
	elseif (Test-Path -Path "$env:TEMP\Remotely-ConnectionInfo.json") {
		# Резервная копия, которую мы сохраняем при каждом обновлении,
		# чтобы DeviceID не терялся, если рабочий каталог пропал.
		$ConnectionInfo = Get-Content -Path "$env:TEMP\Remotely-ConnectionInfo.json" | ConvertFrom-Json
		if ($ConnectionInfo) {
			$ConnectionInfo.Host = $HostName
			$ConnectionInfo.OrganizationID = $Organization
			$ConnectionInfo.ServerVerificationToken = ""
		}
	}
	else {
		New-Item -ItemType Directory -Path "$InstallPath" -Force
	}

	if (!$ConnectionInfo) {
		$NewDeviceId = [System.Guid]::NewGuid().ToString();
		$ConnectionInfo = @{
			DeviceID                = $NewDeviceId;
			Host                    = $HostName;
			OrganizationID          = $Organization;
			ServerVerificationToken = "";
		}
	}

	# Резервная копия до любых изменений каталога: если обновление прервётся
	# и рабочий каталог пропадёт, DeviceID и адрес сервера можно будет взять отсюда.
	New-Item -ItemType File -Path "$env:TEMP\Remotely-ConnectionInfo.json" -Value (ConvertTo-Json -InputObject $ConnectionInfo) -Force

	if ($HostName.EndsWith("/")) {
		$HostName = $HostName.Substring(0, $HostName.LastIndexOf("/"))
	}

	if ($Path) {
		Write-Log "Copying install files..."
		Copy-Item -Path $Path -Destination "$env:TEMP\Remotely-Win-$Platform.zip"

	}
	else {
		$ProgressPreference = 'SilentlyContinue'
		Write-Log "Downloading client..."
		Invoke-WebRequest -Uri "$HostName/Content/Remotely-Win-$Platform.zip" -OutFile "$env:TEMP\Remotely-Win-$Platform.zip" -UseBasicParsing
		$ProgressPreference = 'Continue'
	}

	if (!(Test-Path -Path "$env:TEMP\Remotely-Win-$Platform.zip")) {
		Write-Log "Client files failed to download."
		Do-Exit
	}

	Stop-Remotely

	# Распаковка поверх существующего каталога. Замена каталога через
	# переименование отказала на всех реальных машинах: любой открытый хендлер
	# (антивирус, индексатор, остаточный процесс) блокирует rename, и обновление
	# никогда не завершалось. После Stop-Remotely все процессы агента
	# остановлены и дожиты, поэтому запись поверх безопасна. Целостность
	# проверяем после распаковки; при неудаче повторяем, затем поднимаем
	# службу — следующий цикл обновления дочинит оставшееся.
	$unpackOk = $false
	for ($unpackAttempt = 1; $unpackAttempt -le 3; $unpackAttempt++) {
		Expand-Archive -Path "$env:TEMP\Remotely-Win-$Platform.zip" -DestinationPath "$InstallPath" -Force
		$depsPath = "$InstallPath\Remotely_Agent.deps.json"
		if ((Test-Path -Path "$InstallPath\Remotely_Agent.exe") -and
			(Test-Path -Path $depsPath) -and
			((Get-Item -Path $depsPath).Length -gt 100)) {
			$unpackOk = $true
			break
		}
		Write-Log "Unpack attempt $unpackAttempt produced an incomplete install.  Retrying."
		Start-Sleep -Seconds 3
	}

	if (!$unpackOk) {
		Write-Log "Failed to unpack the package after 3 attempts.  Restarting the current agent."
		Start-Remotely
		Do-Exit
	}

	New-Item -ItemType File -Path "$InstallPath\ConnectionInfo.json" -Value (ConvertTo-Json -InputObject $ConnectionInfo) -Force

	New-Item -ItemType File -Path "$InstallPath\etag.txt" -Value $ETag -Force

	if ($DeviceAlias -or $DeviceGroup) {
		$DeviceSetupOptions = @{
			DeviceAlias     = $DeviceAlias;
			DeviceGroupName = $DeviceGroup;
			OrganizationID  = $Organization;
			DeviceID        = $ConnectionInfo.DeviceID;
		}

		$Body = $DeviceSetupOptions | ConvertTo-Json
		Invoke-RestMethod -Method Post -ContentType "application/json" -Uri "$HostName/api/devices" -Body $Body
	}

	# Stop-Remotely больше не удаляет службу: если она уже есть, достаточно
	# запустить её с новым каталогом. Если её нет (чистая установка или старый
	# неудачный апдейт) — создаём.
	Start-Remotely
}

#endregion

#region Main

try {
	Run-StartupChecks

	Write-Log "Install/uninstall logs are being written to `"$LogPath`""

	if ($Uninstall) {
		Write-Log "Uninstall started."
		Uninstall-Remotely
		Write-Log "Uninstall completed."
		exit
	}
	else {
		Write-Log "Install started."
		Install-Remotely
		Write-Log "Install completed."
		exit
	}
}
catch {
	Write-Log -Message "Error occurred: $($Error[0].InvocationInfo.PositionMessage)"

	# Страховка: что бы ни случилось, машина не должна остаться без запущенной
	# службы (мы могли её останавливать на время обновления).
	try {
		$svc = Get-Service -Name "Remotely_Service" -ErrorAction SilentlyContinue
		if (!$svc -or $svc.Status -ne "Running") {
			Write-Log "Attempting to start Remotely_Service after the error."
			Start-Remotely
		}
	}
	catch {
		Write-Log "Failed to restart Remotely_Service after the error."
	}

	throw $Error[0]
	Do-Exit
}
#endregion